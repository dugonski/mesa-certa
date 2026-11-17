using System.Text.Json;
using System.Text.RegularExpressions;

namespace MesaCerta.Shared.Contracts;

/// <summary>Dados recebidos fora do contrato. O cliente recebe apenas "Confira os campos informados".</summary>
public sealed class ContractException(string field, Exception? inner = null)
    : Exception($"Campo inválido: {field}", inner)
{
    public string Field { get; } = field;
}

public interface IRequestData
{
    /// <summary>Valida e devolve uma cópia normalizada (por exemplo, textos sem espaços nas pontas).</summary>
    IRequestData Normalize();
}

public static partial class Rules
{
    public static bool IsUuid(string? value) =>
        value is not null && UuidPattern().IsMatch(value);

    public static string Text(string? value, string field, int min, int max, bool trim = true)
    {
        var text = trim ? value?.Trim() : value;
        if (text is null || text.Length < min || text.Length > max) throw new ContractException(field);
        return text;
    }

    public static int Range(int value, string field, int min, int max) =>
        value >= min && value <= max ? value : throw new ContractException(field);

    public static int? OptionalRange(int? value, string field, int min, int max) =>
        value is null ? null : Range(value.Value, field, min, max);

    public static Guid Id(Guid value, string field) =>
        value != Guid.Empty ? value : throw new ContractException(field);

    /// <summary>Converte e valida o campo <c>data</c> da requisição para o contrato da operação.</summary>
    public static T Parse<T>(JsonElement? data) where T : IRequestData
    {
        var element = data is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } value
            ? value
            : JsonSerializer.SerializeToElement(new { });
        if (element.ValueKind != JsonValueKind.Object) throw new ContractException("data");
        try
        {
            var parsed = element.Deserialize<T>(Json.MesaCertaJson.Options) ?? throw new ContractException("data");
            return (T)parsed.Normalize();
        }
        catch (JsonException error)
        {
            throw new ContractException(error.Path ?? "data", error);
        }
    }

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidPattern();

    [GeneratedRegex("^[a-zA-Z0-9._-]{3,60}$")]
    public static partial Regex UsernamePattern();
}

public sealed record LoginRequest : IRequestData
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string Terminal { get; init; }

    public IRequestData Normalize() => this with
    {
        Username = Rules.Text(Username, "username", 1, 60),
        Password = Rules.Text(Password, "password", 1, 128, trim: false),
        Terminal = Rules.Text(Terminal, "terminal", 1, 80),
    };
}

public sealed record OpenTabRequest : IRequestData
{
    public required int TableNumber { get; init; }

    public IRequestData Normalize() => this with { TableNumber = Rules.Range(TableNumber, "tableNumber", 1, 1000) };
}

public sealed record ConfigureTablesRequest : IRequestData
{
    public required int Count { get; init; }

    public IRequestData Normalize() => this with { Count = Rules.Range(Count, "count", 1, 1000) };
}

public sealed record AddItemRequest : IRequestData
{
    public required Guid TabId { get; init; }
    public required int Version { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public int? WeightGrams { get; init; }
    public string Note { get; init; } = "";

    public IRequestData Normalize() => this with
    {
        TabId = Rules.Id(TabId, "tabId"),
        Version = Rules.Range(Version, "version", 0, int.MaxValue),
        ProductId = Rules.Id(ProductId, "productId"),
        Quantity = Rules.Range(Quantity, "quantity", 1, 99),
        WeightGrams = Rules.OptionalRange(WeightGrams, "weightGrams", 1, Catalog.MaxWeightGrams),
        Note = Rules.Text(Note ?? "", "note", 0, 240),
    };
}

public sealed record UpdateItemRequest : IRequestData
{
    public required Guid TabId { get; init; }
    public required int Version { get; init; }
    public required Guid ItemId { get; init; }
    public required int Quantity { get; init; }
    public int? WeightGrams { get; init; }
    public string Note { get; init; } = "";

    public IRequestData Normalize() => this with
    {
        TabId = Rules.Id(TabId, "tabId"),
        Version = Rules.Range(Version, "version", 0, int.MaxValue),
        ItemId = Rules.Id(ItemId, "itemId"),
        Quantity = Rules.Range(Quantity, "quantity", 1, 99),
        WeightGrams = Rules.OptionalRange(WeightGrams, "weightGrams", 1, Catalog.MaxWeightGrams),
        Note = Rules.Text(Note ?? "", "note", 0, 240),
    };
}

public sealed record RemoveItemRequest : IRequestData
{
    public required Guid TabId { get; init; }
    public required int Version { get; init; }
    public required Guid ItemId { get; init; }

    public IRequestData Normalize() => this with
    {
        TabId = Rules.Id(TabId, "tabId"),
        Version = Rules.Range(Version, "version", 0, int.MaxValue),
        ItemId = Rules.Id(ItemId, "itemId"),
    };
}

public sealed record PayTabRequest : IRequestData
{
    public required Guid TabId { get; init; }
    public required int Version { get; init; }
    public required PaymentMethod Method { get; init; }

    public IRequestData Normalize() => this with
    {
        TabId = Rules.Id(TabId, "tabId"),
        Version = Rules.Range(Version, "version", 0, int.MaxValue),
    };
}

public sealed record CloseTabRequest : IRequestData
{
    public required Guid TabId { get; init; }
    public required int Version { get; init; }

    public IRequestData Normalize() => this with
    {
        TabId = Rules.Id(TabId, "tabId"),
        Version = Rules.Range(Version, "version", 0, int.MaxValue),
    };
}

public sealed record ExportXmlRequest : IRequestData
{
    public required Guid TabId { get; init; }

    public IRequestData Normalize() => this with { TabId = Rules.Id(TabId, "tabId") };
}

public sealed record SaveProductRequest : IRequestData
{
    public Guid? Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required int PriceCents { get; init; }
    public SaleType SaleType { get; init; } = SaleType.Unit;
    public required bool Active { get; init; }

    public IRequestData Normalize() => this with
    {
        Id = Id is { } id ? Rules.Id(id, "id") : null,
        Name = Rules.Text(Name, "name", 1, 100),
        Category = Rules.Text(Category, "category", 1, 60),
        PriceCents = Rules.Range(PriceCents, "priceCents", 1, 1_000_000),
    };
}

public sealed record CreateUserRequest : IRequestData
{
    public required string Name { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required Role Role { get; init; }

    public IRequestData Normalize()
    {
        var username = Username?.Trim();
        if (username is null || !Rules.UsernamePattern().IsMatch(username)) throw new ContractException("username");
        return this with
        {
            Name = Rules.Text(Name, "name", 2, 100),
            Username = username,
            Password = Rules.Text(Password, "password", 8, 72, trim: false),
        };
    }
}

public sealed record EmptyRequest : IRequestData
{
    public IRequestData Normalize() => this;
}
