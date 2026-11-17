using System.Globalization;
using System.Text;
using MesaCerta.Shared;

namespace MesaCerta.Server.Fiscal;

public sealed record NfceItem(
    string? ProductId,
    string ProductName,
    SaleType SaleType,
    int Quantity,
    int? WeightGrams,
    int UnitPriceCents,
    string? Note) : IPricedItem;

public sealed record NfceTab(int Number, string Label, IReadOnlyList<NfceItem> Items);

public sealed record NfcePayment(DateTime CreatedAt, PaymentMethod Method, int AmountCents);

/// <summary>
/// Gera um documento NFC-e 4.00 de pré-autorização, sem assinatura. A classificação
/// fiscal dos produtos continua provisória até NCM/CFOP/tributação serem configurados.
/// </summary>
public static class NfceXml
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string EscapeXml(string? value)
    {
        var builder = new StringBuilder();
        foreach (var c in value ?? "")
            builder.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => c.ToString(),
            });
        return builder.ToString();
    }

    private static string Value(long cents) => (cents / 100m).ToString("0.00", Invariant);

    private static string NfeDate(DateTime createdAt)
    {
        var utc = createdAt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(createdAt, DateTimeKind.Utc) : createdAt.ToUniversalTime();
        return utc.AddHours(-3).ToString("yyyy-MM-dd'T'HH:mm:ss", Invariant) + "-03:00";
    }

    public static int AccessDigit(string keyBase)
    {
        var weight = 2;
        var total = 0;
        for (var index = keyBase.Length - 1; index >= 0; index--)
        {
            total += (keyBase[index] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }
        var digit = 11 - total % 11;
        return digit is 10 or 11 ? 0 : digit;
    }

    private static string LastDigits(int number, int length)
    {
        var text = number.ToString(Invariant);
        if (text.Length > length) text = text[^length..];
        return text.PadLeft(length, '0');
    }

    public static string Build(NfceTab tab, NfcePayment payment, FiscalConfig? fiscalConfig = null)
    {
        var fiscal = fiscalConfig ?? FiscalConfig.Default;
        var issuedAt = NfeDate(payment.CreatedAt);
        var yearMonth = issuedAt.Substring(2, 2) + issuedAt.Substring(5, 2);
        var invoiceNumber = LastDigits(tab.Number, 9);
        var numericCode = LastDigits(tab.Number, 8);
        var keyBase = $"41{yearMonth}{fiscal.Cnpj}65001{invoiceNumber}1{numericCode}";
        var digit = AccessDigit(keyBase);
        var accessKey = keyBase + digit;
        var total = Value(payment.AmountCents);

        var lines = new List<string>();
        for (var index = 0; index < tab.Items.Count; index++)
        {
            var item = tab.Items[index];
            var weighted = item.SaleType == SaleType.Weight;
            var unit = weighted ? "KG" : "UN";
            var quantity = weighted
                ? ((item.WeightGrams ?? 0) / 1000m).ToString("0.0000", Invariant)
                : ((decimal)item.Quantity).ToString("0.0000", Invariant);
            var unitValue = (item.UnitPriceCents / 100m).ToString("0.0000000000", Invariant);
            var itemValue = Value(Money.ItemTotalCents(item));
            var additional = string.IsNullOrEmpty(item.Note) ? "" : $"\n      <infAdProd>{EscapeXml(item.Note)}</infAdProd>";
            lines.Add(string.Join("\n",
                $"    <det nItem=\"{index + 1}\">",
                "      <prod>",
                $"        <cProd>{EscapeXml(item.ProductId ?? (index + 1).ToString(Invariant))}</cProd>",
                "        <cEAN>SEM GTIN</cEAN>",
                $"        <xProd>{EscapeXml(item.ProductName)}</xProd>",
                "        <NCM>00000000</NCM>",
                "        <CFOP>5102</CFOP>",
                $"        <uCom>{unit}</uCom>",
                $"        <qCom>{quantity}</qCom>",
                $"        <vUnCom>{unitValue}</vUnCom>",
                $"        <vProd>{itemValue}</vProd>",
                "        <cEANTrib>SEM GTIN</cEANTrib>",
                $"        <uTrib>{unit}</uTrib>",
                $"        <qTrib>{quantity}</qTrib>",
                $"        <vUnTrib>{unitValue}</vUnTrib>",
                "        <indTot>1</indTot>",
                "      </prod>",
                "      <imposto>",
                "        <vTotTrib>0.00</vTotTrib>",
                "        <ICMS><ICMSSN102><orig>0</orig><CSOSN>102</CSOSN></ICMSSN102></ICMS>",
                "        <PIS><PISOutr><CST>99</CST><vBC>0.00</vBC><pPIS>0.0000</pPIS><vPIS>0.00</vPIS></PISOutr></PIS>",
                "        <COFINS><COFINSOutr><CST>99</CST><vBC>0.00</vBC><pCOFINS>0.0000</pCOFINS><vCOFINS>0.00</vCOFINS></COFINSOutr></COFINS>",
                $"      </imposto>{additional}",
                "    </det>"));
        }

        var paymentXml = payment.Method == PaymentMethod.Pix
            ? "<tPag>17</tPag>"
            : "<tPag>99</tPag><xPag>CARTAO</xPag>";

        return string.Join("\n",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">",
            $"  <infNFe Id=\"NFe{accessKey}\" versao=\"4.00\">",
            "    <ide>",
            $"      <cUF>41</cUF><cNF>{numericCode}</cNF><natOp>VENDA</natOp><mod>65</mod><serie>1</serie><nNF>{int.Parse(invoiceNumber, Invariant)}</nNF>",
            $"      <dhEmi>{issuedAt}</dhEmi><tpNF>1</tpNF><idDest>1</idDest><cMunFG>{fiscal.CityCode}</cMunFG><tpImp>4</tpImp>",
            $"      <tpEmis>1</tpEmis><cDV>{digit}</cDV><tpAmb>2</tpAmb><finNFe>1</finNFe><indFinal>1</indFinal><indPres>1</indPres>",
            "      <procEmi>0</procEmi><verProc>MesaCerta 0.6.0</verProc>",
            "    </ide>",
            "    <emit>",
            $"      <CNPJ>{fiscal.Cnpj}</CNPJ><xNome>{EscapeXml(fiscal.LegalName)}</xNome><xFant>{EscapeXml(fiscal.TradeName)}</xFant>",
            $"      <enderEmit><xLgr>{EscapeXml(fiscal.Street)}</xLgr><nro>{EscapeXml(fiscal.Number)}</nro><xBairro>{EscapeXml(fiscal.District)}</xBairro><cMun>{fiscal.CityCode}</cMun><xMun>{EscapeXml(fiscal.City)}</xMun><UF>{EscapeXml(fiscal.State)}</UF><CEP>{fiscal.ZipCode}</CEP><cPais>1058</cPais><xPais>BRASIL</xPais></enderEmit>",
            $"      <IE>{fiscal.Ie}</IE><CRT>1</CRT>",
            "    </emit>",
            string.Join("\n", lines),
            $"    <total><ICMSTot><vBC>0.00</vBC><vICMS>0.00</vICMS><vICMSDeson>0.00</vICMSDeson><vFCP>0.00</vFCP><vBCST>0.00</vBCST><vST>0.00</vST><vFCPST>0.00</vFCPST><vFCPSTRet>0.00</vFCPSTRet><vProd>{total}</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vII>0.00</vII><vIPI>0.00</vIPI><vIPIDevol>0.00</vIPIDevol><vPIS>0.00</vPIS><vCOFINS>0.00</vCOFINS><vOutro>0.00</vOutro><vNF>{total}</vNF><vTotTrib>0.00</vTotTrib></ICMSTot></total>",
            "    <transp><modFrete>9</modFrete></transp>",
            $"    <pag><detPag><indPag>0</indPag>{paymentXml}<vPag>{total}</vPag></detPag><vTroco>0.00</vTroco></pag>",
            $"    <infAdic><infCpl>Ficha {tab.Number} - {EscapeXml(tab.Label)}. XML de pre-autorizacao em homologacao; classificacao fiscal provisoria. Documento ainda sem assinatura digital, protocolo ou autorizacao da SEFAZ.</infCpl></infAdic>",
            "  </infNFe>",
            "</NFe>",
            "");
    }
}
