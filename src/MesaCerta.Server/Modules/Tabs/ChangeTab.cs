using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Server.Modules.Payments;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules.Tabs;

/// <summary>Lançar, editar, remover, pagar e encerrar: sempre com controle de versão da ficha.</summary>
public static class ChangeTab
{
    public static async Task<object> HandleAsync(CommandContext context)
    {
        var (db, user, terminal, action) = (context.Db, context.User, context.Terminal, context.Action);
        var (tabId, version) = context.Data switch
        {
            AddItemRequest r => (r.TabId, r.Version),
            UpdateItemRequest r => (r.TabId, r.Version),
            RemoveItemRequest r => (r.TabId, r.Version),
            PayTabRequest r => (r.TabId, r.Version),
            CloseTabRequest r => (r.TabId, r.Version),
            _ => throw new AppException(ErrorCodes.UnknownAction, "Operação desconhecida."),
        };

        var tab = await db.FindTabForChangeAsync(tabId);
        TabRules.AssertTab(tab, version, action == Commands.TabsClose ? TabStatus.Paid : TabStatus.Open);

        // Mudança condicional de versão: todas as gravações dependentes confirmam juntas.
        var status = tab!.Status;
        var updated = await db.Tabs
            .Where(t => t.Id == tab.Id && t.Version == version && t.Status == status)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Version, t => t.Version + 1));
        if (updated != 1)
            throw new AppException(ErrorCodes.Conflict, "A ficha mudou. Atualize e tente novamente.");

        switch (context.Data)
        {
            case AddItemRequest data:
                await AddItemAsync(db, user, terminal, tab, data);
                break;
            case UpdateItemRequest data:
                UpdateItem(db, user, terminal, tab, data);
                break;
            case RemoveItemRequest data:
                RemoveItem(db, user, terminal, tab, data);
                break;
            case PayTabRequest data:
                PayTab.Apply(context, tab, data);
                break;
            case CloseTabRequest:
                await CloseAsync(db, user, terminal, tab);
                break;
        }

        await db.SaveChangesAsync();
        var result = await db.FindTabAsync(tab.Id);
        return result!.ToDto();
    }

    private static async Task AddItemAsync(MesaCertaDbContext db, User user, string terminal, Tab tab, AddItemRequest data)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == data.ProductId);
        if (product is not { Active: true } || product.PriceCents <= 0)
            throw new AppException(ErrorCodes.NotFound, "Produto indisponível. Atualize a lista.");
        var (quantity, weightGrams) = TabRules.ItemMeasures(product.SaleType, data.Quantity, data.WeightGrams);
        TabRules.TotalCents(tab.Items.Cast<IPricedItem>()
            .Append(new PricedItem(product.SaleType, quantity, weightGrams, product.PriceCents)));

        db.TabItems.Add(new TabItem
        {
            Id = Guid.NewGuid(),
            TabId = tab.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            UnitPriceCents = product.PriceCents,
            Quantity = quantity,
            WeightGrams = weightGrams,
            SaleType = product.SaleType,
            Note = data.Note,
            AddedBy = user.Id,
            CreatedAt = Clock.Now(),
        });
        AuditTrail.Add(db, user, terminal, "ITEM_ADDED", tab.Id.ToString(), new
        {
            product = product.Name,
            quantity,
            unitPriceCents = product.PriceCents,
            note = data.Note,
            weightGrams,
        });
    }

    private static TabItem FindItem(Tab tab, Guid itemId) =>
        tab.Items.FirstOrDefault(item => item.Id == itemId)
        ?? throw new AppException(ErrorCodes.NotFound, "Item não encontrado nesta ficha.");

    private static void UpdateItem(MesaCertaDbContext db, User user, string terminal, Tab tab, UpdateItemRequest data)
    {
        var item = FindItem(tab, data.ItemId);
        var before = new { quantity = item.Quantity, weightGrams = item.WeightGrams, note = item.Note };
        var (quantity, weightGrams) = TabRules.ItemMeasures(item.SaleType, data.Quantity, data.WeightGrams);
        TabRules.TotalCents(tab.Items.Select(row => row.Id == item.Id
            ? new PricedItem(row.SaleType, quantity, weightGrams, row.UnitPriceCents)
            : (IPricedItem)row));

        item.Quantity = quantity;
        item.WeightGrams = weightGrams;
        item.Note = data.Note;
        AuditTrail.Add(db, user, terminal, "ITEM_UPDATED", tab.Id.ToString(), new
        {
            itemId = item.Id,
            product = item.ProductName,
            before,
            after = new { quantity, weightGrams, note = data.Note },
        });
    }

    private static void RemoveItem(MesaCertaDbContext db, User user, string terminal, Tab tab, RemoveItemRequest data)
    {
        var item = FindItem(tab, data.ItemId);
        db.TabItems.Remove(item);
        AuditTrail.Add(db, user, terminal, "ITEM_REMOVED", tab.Id.ToString(), new
        {
            itemId = item.Id,
            product = item.ProductName,
            quantity = item.Quantity,
            weightGrams = item.WeightGrams,
            unitPriceCents = item.UnitPriceCents,
            saleType = item.SaleType,
            note = item.Note,
        });
    }

    private static async Task CloseAsync(MesaCertaDbContext db, User user, string terminal, Tab tab)
    {
        if (tab.Payment is null)
            throw new AppException(ErrorCodes.InvalidState, "A ficha precisa ter um pagamento registrado.");
        tab.Status = TabStatus.Closed;
        tab.ClosedAt = Clock.Now();
        AuditTrail.Add(db, user, terminal, "TAB_CLOSED", tab.Id.ToString());
        if (tab.TableNumber is { } number)
            await db.DiningTables
                .Where(t => t.Number == number && t.ActiveTabId == tab.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.ActiveTabId, (Guid?)null));
    }
}
