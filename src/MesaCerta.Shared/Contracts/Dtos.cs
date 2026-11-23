using System.Text.Json;
using System.Text.Json.Serialization;

namespace MesaCerta.Shared.Contracts;

public sealed record UserDto(Guid Id, string Name, string Username, Role Role, bool Active);

public sealed record LoginResult(string Token, UserDto User);

public sealed record DiningTableDto(int Number, bool Active, Guid? ActiveTabId);

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Category,
    int PriceCents,
    SaleType SaleType,
    bool Active,
    DateTime CreatedAt)
{
    [JsonIgnore]
    public bool NeedsPrice => Catalog.NeedsPrice(SaleType, PriceCents);
    [JsonIgnore]
    public bool InCatalog => Catalog.InCatalog(SaleType, PriceCents, Active);
}

public sealed record TabItemDto(
    Guid Id,
    Guid TabId,
    Guid ProductId,
    string ProductName,
    int UnitPriceCents,
    int Quantity,
    SaleType SaleType,
    int? WeightGrams,
    string Note,
    Guid AddedBy,
    DateTime CreatedAt) : IPricedItem;

public sealed record PaymentDto(Guid Id, PaymentMethod Method, int AmountCents, DateTime CreatedAt);

public sealed record TabDto(
    Guid Id,
    int Number,
    string Label,
    TabStatus Status,
    int Version,
    Guid OpenedBy,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    int? TableNumber,
    IReadOnlyList<TabItemDto> Items,
    PaymentDto? Payment)
{
    [JsonIgnore]
    public long TotalCents => Money.SumItems(Items);
}

public sealed record AuditLogDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string Terminal,
    string Action,
    string EntityId,
    JsonElement Details,
    DateTime CreatedAt)
{
    /// <summary>Resumo exibido na tela de atividades.</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (Details.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "product", "label", "username" })
                    if (Details.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString()!;
            return EntityId;
        }
    }
}

public sealed record SnapshotDto(
    UserDto User,
    IReadOnlyList<DiningTableDto> Tables,
    IReadOnlyList<TabDto> Tabs,
    IReadOnlyList<ProductDto> Products,
    IReadOnlyList<TabDto> History,
    IReadOnlyList<UserDto> Users,
    IReadOnlyList<AuditLogDto> AuditLogs);

public sealed record XmlExportDto(string Xml, string Filename);

public sealed record ConfigureTablesResult(int Count);

public sealed record LogoutResult(bool Ok);
