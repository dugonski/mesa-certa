using System.Globalization;
using System.Text.RegularExpressions;

namespace MesaCerta.Shared;

public static partial class Catalog
{
    public const int MaxWeightGrams = 100_000;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static bool NeedsPrice(SaleType saleType, int priceCents) =>
        saleType == SaleType.Weight && priceCents == 0;

    public static bool InCatalog(SaleType saleType, int priceCents, bool active) =>
        active || NeedsPrice(saleType, priceCents);

    /// <summary>Converte kg digitado (vírgula ou ponto, até 3 casas) em gramas exatas.</summary>
    public static int? KgToGrams(string? value)
    {
        var text = (value ?? string.Empty).Trim().Replace(',', '.');
        if (!KgPattern().IsMatch(text)) return null;
        var parts = text.Split('.');
        var whole = parts[0].Length == 0 ? "0" : parts[0];
        var decimals = parts.Length > 1 ? parts[1] : string.Empty;
        if (whole.TrimStart('0').Length > 6) return null;
        var grams = long.Parse(whole, CultureInfo.InvariantCulture) * 1000
            + long.Parse(decimals.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return grams is >= 1 and <= MaxWeightGrams ? (int)grams : null;
    }

    public static string FormatKg(int grams) =>
        (grams / 1000m).ToString("N3", PtBr);

    [GeneratedRegex(@"^(?:[0-9]+|[0-9]*\.[0-9]{1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex KgPattern();
}
