namespace MesaCerta.Shared;

// Os nomes são transmitidos e gravados em maiúsculas (ADMIN, OPEN, PIX, WEIGHT...).
public enum Role
{
    Admin,
    Attendant,
    Cashier,
}

public enum TabStatus
{
    Open,
    Paid,
    Closed,
}

public enum PaymentMethod
{
    Pix,
    Card,
}

public enum SaleType
{
    Unit,
    Weight,
}

public static class EnumNames
{
    public static string ToWire<T>(T value) where T : struct, Enum =>
        value.ToString().ToUpperInvariant();

    public static bool TryParseWire<T>(string? text, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(ToWire(candidate), text, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    public static T ParseWire<T>(string text) where T : struct, Enum =>
        TryParseWire<T>(text, out var value)
            ? value
            : throw new FormatException($"Valor inválido para {typeof(T).Name}.");
}
