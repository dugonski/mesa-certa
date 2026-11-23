using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules.Tables;

public static class ConfigureTables
{
    /// <summary>Reduzir desativa os números maiores sem apagar histórico; mesas ocupadas impedem a redução.</summary>
    public static async Task<object> HandleAsync(CommandContext context)
    {
        var db = context.Db;
        var count = context.DataAs<ConfigureTablesRequest>().Count;
        var occupied = await db.DiningTables.AsNoTracking()
            .Where(t => t.Number > count && t.ActiveTabId != null)
            .OrderBy(t => t.Number)
            .FirstOrDefaultAsync();
        if (occupied is not null)
            throw new AppException(ErrorCodes.Conflict, $"Encerre a mesa {occupied.Number} antes de reduzir a quantidade.");

        var existing = await db.DiningTables.Where(t => t.Number <= count).Select(t => t.Number).ToListAsync();
        var known = existing.ToHashSet();
        for (var number = 1; number <= count; number++)
            if (!known.Contains(number))
                db.DiningTables.Add(new DiningTable { Number = number, Active = true });
        await db.SaveChangesAsync();

        await db.DiningTables.Where(t => t.Number <= count).ExecuteUpdateAsync(set => set.SetProperty(t => t.Active, true));
        await db.DiningTables.Where(t => t.Number > count).ExecuteUpdateAsync(set => set.SetProperty(t => t.Active, false));
        AuditTrail.Add(db, context.User, context.Terminal, "TABLES_CONFIGURED", "dining-room", new { count });
        await db.SaveChangesAsync();
        return new ConfigureTablesResult(count);
    }
}
