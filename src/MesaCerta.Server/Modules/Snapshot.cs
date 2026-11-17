using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules;

public static class Snapshot
{
    private const int RecentLimit = 100;

    /// <summary>Estado autorizado para o perfil: cada lista depende da permissão correspondente.</summary>
    public static Task<SnapshotDto> LoadAsync(Database database, User user)
    {
        TabRules.RequirePermission(user.Role, Permissions.TabsRead);
        return database.TransactionAsync(async tx =>
        {
            var active = new[] { TabStatus.Open, TabStatus.Paid };
            var tabs = await tx.Tabs.AsNoTracking().Include(t => t.Items).Include(t => t.Payment).AsSplitQuery()
                .Where(t => active.Contains(t.Status))
                .OrderByDescending(t => t.OpenedAt)
                .ToListAsync();

            var products = tx.Products.AsNoTracking();
            if (!Permissions.Can(user.Role, Permissions.ProductsWrite))
                products = products.Where(p => p.Active || (p.SaleType == SaleType.Weight && p.PriceCents == 0));

            var history = Permissions.Can(user.Role, Permissions.HistoryRead)
                ? await tx.Tabs.AsNoTracking().Include(t => t.Items).Include(t => t.Payment).AsSplitQuery()
                    .Where(t => t.Status == TabStatus.Closed)
                    .OrderByDescending(t => t.ClosedAt)
                    .Take(RecentLimit)
                    .ToListAsync()
                : [];

            var users = Permissions.Can(user.Role, Permissions.UsersWrite)
                ? await tx.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync()
                : [];

            var auditLogs = Permissions.Can(user.Role, Permissions.AuditRead)
                ? await tx.AuditLogs.AsNoTracking().OrderByDescending(l => l.CreatedAt).Take(RecentLimit).ToListAsync()
                : [];

            var tables = await tx.DiningTables.AsNoTracking().Where(t => t.Active).OrderBy(t => t.Number).ToListAsync();

            return new SnapshotDto(
                user.ToDto(),
                tables.Select(Mapping.ToDto).ToList(),
                tabs.Select(Mapping.ToDto).ToList(),
                (await products.OrderBy(p => p.Name).ToListAsync()).Select(Mapping.ToDto).ToList(),
                history.Select(Mapping.ToDto).ToList(),
                users.Select(Mapping.ToDto).ToList(),
                auditLogs.Select(Mapping.ToDto).ToList());
        });
    }
}
