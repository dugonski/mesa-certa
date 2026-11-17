namespace MesaCerta.Shared;

public interface IPricedItem
{
    SaleType SaleType { get; }
    int Quantity { get; }
    int? WeightGrams { get; }
    int UnitPriceCents { get; }
}

public sealed record PricedItem(SaleType SaleType, int Quantity, int? WeightGrams, int UnitPriceCents) : IPricedItem;

/// <summary>
/// Dinheiro sempre em centavos inteiros e peso em gramas inteiras.
/// Cada item pesado é arredondado uma única vez, meio centavo para cima.
/// </summary>
public static class Money
{
    public static long ItemTotalCents(IPricedItem item) =>
        item.SaleType == SaleType.Weight
            ? ((long)item.UnitPriceCents * (item.WeightGrams ?? 0) + 500) / 1000
            : (long)item.Quantity * item.UnitPriceCents;

    public static long SumItems(IEnumerable<IPricedItem> items) =>
        items.Sum(ItemTotalCents);
}
