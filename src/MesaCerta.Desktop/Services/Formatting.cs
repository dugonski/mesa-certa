using System.Globalization;
using MesaCerta.Shared;

namespace MesaCerta.Desktop.Services;

/// <summary>Formatação e rótulos de apresentação, em pt-BR.</summary>
public static class Formatting
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Money(long cents) => (cents / 100m).ToString("C", PtBr);

    public static string Date(DateTime? value) =>
        value is { } date ? date.ToLocalTime().ToString("dd/MM, HH:mm", PtBr) : "";

    public static string Number(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    public static string Quantity(IPricedItem item) =>
        item.SaleType == SaleType.Weight ? $"{Catalog.FormatKg(item.WeightGrams ?? 0)} kg" : item.Quantity.ToString(PtBr);

    public static string Status(TabStatus status) => status switch
    {
        TabStatus.Open => "Aberta",
        TabStatus.Paid => "Paga",
        _ => "Encerrada",
    };

    public static readonly IReadOnlyDictionary<string, string> ActionLabels = new Dictionary<string, string>
    {
        ["LOGIN"] = "Entrada no sistema",
        ["LOGOUT"] = "Saída do sistema",
        ["TAB_OPENED"] = "Ficha aberta",
        ["ITEM_ADDED"] = "Produto lançado",
        ["ITEM_UPDATED"] = "Item editado",
        ["ITEM_REMOVED"] = "Item removido",
        ["PAYMENT_SIMULATED"] = "Pagamento registrado",
        ["TAB_CLOSED"] = "Ficha encerrada",
        ["PRODUCT_CREATED"] = "Produto cadastrado",
        ["PRODUCT_UPDATED"] = "Produto atualizado",
        ["USER_CREATED"] = "Usuário cadastrado",
        ["TABLES_CONFIGURED"] = "Quantidade de mesas alterada",
    };

    /// <summary>Preço digitado em reais (vírgula ou ponto) convertido para centavos.</summary>
    public static int? ParsePriceCents(string? text)
    {
        var normalized = (text ?? "").Trim().Replace("R$", "").Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        if (value < 0.01m || value > 10000m) return null;
        return (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);
    }
}
