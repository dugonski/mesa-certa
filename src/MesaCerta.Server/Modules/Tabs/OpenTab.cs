using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules.Tabs;

public static class OpenTab
{
    /// <summary>Abre a ficha e reserva a mesa na mesma transação.</summary>
    public static async Task<object> HandleAsync(CommandContext context)
    {
        var (db, user, terminal) = (context.Db, context.User, context.Terminal);
        var data = context.DataAs<OpenTabRequest>();
        var table = await db.DiningTables.AsNoTracking().FirstOrDefaultAsync(t => t.Number == data.TableNumber);
        if (table is not { Active: true })
            throw new AppException(ErrorCodes.NotFound, "Mesa não cadastrada ou desativada.");
        if (table.ActiveTabId is not null)
            throw new AppException(ErrorCodes.Conflict, "Esta mesa já foi aberta. Selecione-a novamente.");

        var tab = new Tab
        {
            Id = Guid.NewGuid(),
            Label = $"Mesa {table.Number:00}",
            TableNumber = table.Number,
            Status = TabStatus.Open,
            Version = 0,
            OpenedBy = user.Id,
            OpenedAt = Clock.Now(),
        };
        db.Tabs.Add(tab);
        await db.SaveChangesAsync();

        var reserved = await db.DiningTables
            .Where(t => t.Number == table.Number && t.Active && t.ActiveTabId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ActiveTabId, tab.Id));
        if (reserved != 1)
            throw new AppException(ErrorCodes.Conflict, "Esta mesa já foi aberta. Selecione-a novamente.");

        AuditTrail.Add(db, user, terminal, "TAB_OPENED", tab.Id.ToString(), new { label = tab.Label, number = tab.Number });
        await db.SaveChangesAsync();
        return tab.ToDto();
    }
}
