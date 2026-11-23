using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MesaCerta.Server.Config;
using MesaCerta.Server.Data;
using MesaCerta.Shared.Json;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Maintenance;

/// <summary>
/// Manutenção offline: exige o servidor parado e a opção explícita de confirmação.
/// O backup é gravado, sincronizado e relido antes de qualquer exclusão.
/// </summary>
public static class ResetOrders
{
    public const string ConfirmFlag = "--confirm-reset-fichas";

    private static readonly HashSet<string> OrderActions =
        ["TAB_OPENED", "ITEM_ADDED", "ITEM_UPDATED", "ITEM_REMOVED", "PAYMENT_SIMULATED", "TAB_CLOSED"];

    private static readonly JsonSerializerOptions Pretty = new(MesaCertaJson.Options) { WriteIndented = true };

    public static async Task RunAsync(ServerConfig config, IReadOnlyList<string> args, string root, TextWriter output)
    {
        if (!args.Contains(ConfirmFlag))
            throw new InvalidOperationException($"Requer {ConfirmFlag}. Pare o servidor antes de executar.");

        // Mantém a porta da aplicação reservada durante toda a manutenção.
        var probe = new TcpListener(IPAddress.TryParse(config.Host, out var address) ? address : IPAddress.Loopback, config.Port);
        try
        {
            probe.Start();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException("Pare o servidor TCP antes da limpeza.");
        }

        try
        {
            var database = new Database(config.ConnectionString);
            var backupDir = Path.Combine(root, "backups");
            Directory.CreateDirectory(backupDir);
            var backupPath = Path.Combine(backupDir, $"fichas-{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ss-fffZ}.json");

            var report = await database.TransactionAsync(async tx =>
            {
                var tabs = await tx.Tabs.AsNoTracking().ToListAsync();
                var items = await tx.TabItems.AsNoTracking().ToListAsync();
                var payments = await tx.Payments.AsNoTracking().ToListAsync();
                var tables = await tx.DiningTables.AsNoTracking().OrderBy(t => t.Number).ToListAsync();
                var audit = await tx.AuditLogs.AsNoTracking().ToListAsync();
                var receipts = await tx.CommandReceipts.AsNoTracking().ToListAsync();
                var ids = tabs.Select(t => t.Id.ToString()).ToHashSet();
                var relatedAudit = audit.Where(log => ids.Contains(log.EntityId) || OrderActions.Contains(log.Action)).ToList();
                var relatedReceipts = receipts.Where(row => IsOrderReceipt(row.Result, ids)).ToList();

                // Hashes verificam a preservação sem copiar credenciais para o backup.
                var usersHash = await DigestAsync(tx.Users.AsNoTracking().OrderBy(u => u.Id));
                var productsHash = await DigestAsync(tx.Products.AsNoTracking().OrderBy(p => p.Id));

                var backup = new
                {
                    format = "mesacerta-orders-backup-v1",
                    createdAt = DateTime.UtcNow,
                    database = config.DatabaseName,
                    tabs,
                    items,
                    payments,
                    tables,
                    audit = relatedAudit,
                    receipts = relatedReceipts,
                };
                var text = JsonSerializer.Serialize(backup, Pretty);
                await WriteVerifiedAsync(backupPath, text);
                await WriteVerifiedAsync(backupPath + ".sha256", Digest(text) + "\n");

                await tx.DiningTables.ExecuteUpdateAsync(set => set.SetProperty(t => t.ActiveTabId, (Guid?)null));
                var auditIds = relatedAudit.Select(r => r.Id).ToList();
                var receiptIds = relatedReceipts.Select(r => r.Id).ToList();
                await tx.AuditLogs.Where(r => auditIds.Contains(r.Id)).ExecuteDeleteAsync();
                await tx.CommandReceipts.Where(r => receiptIds.Contains(r.Id)).ExecuteDeleteAsync();
                await tx.Payments.ExecuteDeleteAsync();
                await tx.TabItems.ExecuteDeleteAsync();
                await tx.Tabs.ExecuteDeleteAsync();

                Ensure(await tx.Tabs.CountAsync() == 0, "fichas");
                Ensure(await tx.TabItems.CountAsync() == 0, "itens");
                Ensure(await tx.Payments.CountAsync() == 0, "pagamentos");
                Ensure(await tx.DiningTables.CountAsync(t => t.ActiveTabId != null) == 0, "mesas");
                Ensure(await tx.AuditLogs.CountAsync() == audit.Count - relatedAudit.Count, "auditoria");
                Ensure(await tx.CommandReceipts.CountAsync() == receipts.Count - relatedReceipts.Count, "recibos");
                var after = await tx.DiningTables.AsNoTracking().OrderBy(t => t.Number).ToListAsync();
                Ensure(after.Select(t => (t.Number, t.Active)).SequenceEqual(tables.Select(t => (t.Number, t.Active))), "mesas");
                Ensure(await DigestAsync(tx.Users.AsNoTracking().OrderBy(u => u.Id)) == usersHash, "usuários");
                Ensure(await DigestAsync(tx.Products.AsNoTracking().OrderBy(p => p.Id)) == productsHash, "produtos");

                return new
                {
                    removed = new
                    {
                        tabs = tabs.Count,
                        items = items.Count,
                        payments = payments.Count,
                        audit = relatedAudit.Count,
                        receipts = relatedReceipts.Count,
                    },
                    freeTables = tables.Count(t => t.Active),
                    backup = backupPath,
                };
            });

            // ALTER TABLE confirma implicitamente no MySQL: só depois da transação de dados,
            // com a porta TCP ainda reservada e nenhum cliente gravando.
            await using var db = database.CreateContext();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Tab` AUTO_INCREMENT = 1");
            output.WriteLine(JsonSerializer.Serialize(new { report.removed, report.freeTables, report.backup, nextTabNumber = 1 }, Pretty));
        }
        finally
        {
            probe.Stop();
        }
    }

    private static bool IsOrderReceipt(string json, HashSet<string> tabIds)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var result = document.RootElement;
            if (result.ValueKind != JsonValueKind.Object) return false;
            string? Text(string name) =>
                result.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (Text("id") is { } id && tabIds.Contains(id)) return true;
            if (Text("tabId") is { } tabId && tabIds.Contains(tabId)) return true;
            return result.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.Number
                && Text("status") is "OPEN" or "PAID" or "CLOSED";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Digest(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static async Task<string> DigestAsync<T>(IQueryable<T> rows) =>
        Digest(MesaCertaJson.Serialize(await rows.ToListAsync()));

    private static async Task WriteVerifiedAsync(string path, string text)
    {
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            await file.WriteAsync(Encoding.UTF8.GetBytes(text));
            file.Flush(flushToDisk: true);
        }
        if (Digest(await File.ReadAllTextAsync(path, Encoding.UTF8)) != Digest(text))
            throw new IOException("Falha ao verificar o backup.");
    }

    private static void Ensure(bool condition, string what)
    {
        if (!condition) throw new DataException($"Verificação falhou ({what}); nenhuma alteração foi confirmada.");
    }
}
