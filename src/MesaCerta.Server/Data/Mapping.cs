using System.Text.Json;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Data;

public static class Mapping
{
    public static UserDto ToDto(this User user) => new(user.Id, user.Name, user.Username, user.Role, user.Active);

    public static ProductDto ToDto(this Product product) =>
        new(product.Id, product.Name, product.Category, product.PriceCents, product.SaleType, product.Active, product.CreatedAt);

    public static DiningTableDto ToDto(this DiningTable table) => new(table.Number, table.Active, table.ActiveTabId);

    public static TabItemDto ToDto(this TabItem item) =>
        new(item.Id, item.TabId, item.ProductId, item.ProductName, item.UnitPriceCents, item.Quantity,
            item.SaleType, item.WeightGrams, item.Note, item.AddedBy, item.CreatedAt);

    public static TabDto ToDto(this Tab tab) =>
        new(tab.Id, tab.Number, tab.Label, tab.Status, tab.Version, tab.OpenedBy, tab.OpenedAt, tab.ClosedAt,
            tab.TableNumber,
            tab.Items.OrderBy(item => item.CreatedAt).Select(ToDto).ToList(),
            tab.Payment is { } payment ? new PaymentDto(payment.Id, payment.Method, payment.AmountCents, payment.CreatedAt) : null);

    public static AuditLogDto ToDto(this AuditLog log)
    {
        using var details = JsonDocument.Parse(string.IsNullOrWhiteSpace(log.Details) ? "{}" : log.Details);
        return new(log.Id, log.UserId, log.UserName, log.Terminal, log.Action, log.EntityId,
            details.RootElement.Clone(), log.CreatedAt);
    }

    /// <summary>Ficha com itens e pagamento, lida sem rastreamento para refletir o estado gravado.</summary>
    public static Task<Tab?> FindTabAsync(this MesaCertaDbContext db, Guid id) =>
        db.Tabs.AsNoTracking().Include(tab => tab.Items).Include(tab => tab.Payment)
            .AsSplitQuery()
            .FirstOrDefaultAsync(tab => tab.Id == id);

    /// <summary>Ficha rastreada, para alterações dentro da transação.</summary>
    public static Task<Tab?> FindTabForChangeAsync(this MesaCertaDbContext db, Guid id) =>
        db.Tabs.Include(tab => tab.Items).Include(tab => tab.Payment)
            .AsSplitQuery()
            .FirstOrDefaultAsync(tab => tab.Id == id);
}
