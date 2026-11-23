using MesaCerta.Server.Fiscal;
using MesaCerta.Shared;

namespace MesaCerta.Tests;

public class FiscalTests
{
    [Fact]
    public void Xml_usa_estrutura_NFCe_400_de_pre_autorizacao_sem_assinatura_ou_protocolo()
    {
        var xml = NfceXml.Build(
            new NfceTab(1, "Ana & João \"A\"", [new NfceItem(null, "Café <especial>", SaleType.Unit, 2, null, 650, "A&B")]),
            new NfcePayment(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), PaymentMethod.Pix, 1300));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">", xml);
        Assert.Matches("<infNFe Id=\"NFe\\d{44}\" versao=\"4.00\">", xml);
        Assert.Contains("<mod>65</mod>", xml);
        Assert.Contains("<tpAmb>2</tpAmb>", xml);
        Assert.Contains("<dhEmi>2026-01-01T09:00:00-03:00</dhEmi>", xml);
        Assert.Contains("Ficha 1 - Ana &amp; João &quot;A&quot;", xml);
        Assert.Contains("Café &lt;especial&gt;", xml);
        Assert.Contains("<infAdProd>A&amp;B</infAdProd>", xml);
        Assert.Contains("<vNF>13.00</vNF>", xml);
        Assert.Contains("<qCom>2.0000</qCom>", xml);
        Assert.Contains("<vUnCom>6.5000000000</vUnCom>", xml);
        Assert.Contains("<tPag>17</tPag>", xml);
        Assert.DoesNotMatch("<Signature|<protNFe|<nProt>", xml);
        Assert.DoesNotContain("\r", xml);
    }

    [Fact]
    public void Chave_de_acesso_tem_digito_verificador_modulo_11()
    {
        var xml = NfceXml.Build(
            new NfceTab(42, "Mesa 01", [new NfceItem("p1", "Buffet", SaleType.Weight, 1, 333, 6590, "")]),
            new NfcePayment(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc), PaymentMethod.Card, 2194));
        var key = System.Text.RegularExpressions.Regex.Match(xml, "NFe(\\d{44})").Groups[1].Value;
        Assert.Equal(NfceXml.AccessDigit(key[..43]), key[43] - '0');
        Assert.StartsWith("412603", key);
        Assert.Contains("<qCom>0.3330</qCom>", xml);
        Assert.Contains("<vProd>21.94</vProd>", xml);
        Assert.Contains("<tPag>99</tPag><xPag>CARTAO</xPag>", xml);
        Assert.DoesNotContain("infAdProd", xml);
    }

    [Fact]
    public void Configuracao_fiscal_aceita_somente_digitos_no_tamanho_certo()
    {
        var fiscal = FiscalConfig.FromEnv(new Dictionary<string, string?>
        {
            ["FISCAL_CNPJ"] = "12.345.678/0001-90",
            ["FISCAL_IE"] = "123",
            ["FISCAL_MUNICIPIO"] = "",
        });
        Assert.Equal("12345678000190", fiscal.Cnpj);
        Assert.Equal("0000000000", fiscal.Ie);
        Assert.Equal("CURITIBA", fiscal.City);
    }
}
