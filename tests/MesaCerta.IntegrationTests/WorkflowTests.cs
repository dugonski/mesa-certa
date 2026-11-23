using MesaCerta.Server;
using MesaCerta.Server.Config;
using MesaCerta.Server.Data;
using MesaCerta.Server.Transport;
using MesaCerta.Shared;
using MesaCerta.Shared.Client;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.IntegrationTests;

/// <summary>
/// Exige um MySQL migrado (DATABASE_URL no .env ou MESACERTA_TEST_DATABASE_URL). Usa registros
/// identificados exclusivamente para o teste e os remove ao terminar.
/// </summary>
public class WorkflowTests
{
    private static string? ConnectionString()
    {
        var env = DotEnv.Load();
        var url = env.GetValueOrDefault("MESACERTA_TEST_DATABASE_URL") ?? env.GetValueOrDefault("DATABASE_URL");
        return string.IsNullOrWhiteSpace(url) ? null : ServerConfig.ToConnectionString(url);
    }

    private static async Task<ServerException> Rejects(Task task)
    {
        var error = await Assert.ThrowsAsync<ServerException>(() => task);
        return error;
    }

    [SkippableFact]
    public async Task MySql_e_tres_terminais_TCP_permissoes_concorrencia_pagamento_XML_e_historico()
    {
        var connectionString = ConnectionString();
        Skip.If(connectionString is null, "Configure DATABASE_URL para o teste de integração.");
        var database = new Database(connectionString!);
        await new Migrator(connectionString!).ApplyAsync();

        var suffix = Guid.NewGuid().ToString()[..8];
        var userIds = new List<Guid>();
        var tabIds = new List<Guid>();
        var productIds = new List<Guid>();
        var clients = new List<ServerConnection>();
        var server = new TcpServer(new RestaurantService(database), TextWriter.Null);
        var notifications = 0;
        try
        {
            var password = Guid.NewGuid().ToString();
            await using (var db = database.CreateContext())
            {
                foreach (var role in new[] { Role.Admin, Role.Attendant, Role.Cashier })
                {
                    var user = new User
                    {
                        Id = Guid.NewGuid(),
                        Username = $"test-{EnumNames.ToWire(role)}-{suffix}",
                        Name = $"Teste {suffix}",
                        Role = role,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4),
                        CreatedAt = Clock.Now(),
                    };
                    db.Users.Add(user);
                    userIds.Add(user.Id);
                }
                await db.SaveChangesAsync();
            }

            server.Start("127.0.0.1", 0);
            foreach (var role in new[] { "ADMIN", "ATTENDANT", "CASHIER" })
            {
                var client = new ServerConnection();
                client.EventReceived += name =>
                {
                    if (name == "changed") Interlocked.Increment(ref notifications);
                };
                clients.Add(client);
                await client.ConnectAsync("127.0.0.1", server.Port);
                var login = await client.RequestAsync<LoginResult>(Commands.Login,
                    new { username = $"test-{role}-{suffix}", password, terminal = $"Teste-{role}" });
                client.Token = login.Token;
            }
            var (admin, attendant, cashier) = (clients[0], clients[1], clients[2]);
            Task<SnapshotDto> Snapshot(ServerConnection client) => client.RequestAsync<SnapshotDto>(Commands.Snapshot);

            var initialCount = (await Snapshot(admin)).Tables.Count;
            if (initialCount < 1000)
            {
                try
                {
                    await admin.CommandAsync<ConfigureTablesResult>(Commands.TablesConfigure, new { count = initialCount + 1 });
                    Assert.Equal(initialCount + 1, (await Snapshot(admin)).Tables.Count);
                }
                finally
                {
                    await admin.CommandAsync<ConfigureTablesResult>(Commands.TablesConfigure, new { count = initialCount });
                }
                Assert.Equal(initialCount, (await Snapshot(admin)).Tables.Count);
            }

            var product = await admin.CommandAsync<ProductDto>(Commands.ProductsSave,
                new { name = $"Produto teste {suffix}", category = "Teste", priceCents = 1234, active = true });
            productIds.Add(product.Id);

            DiningTable? free;
            await using (var db = database.CreateContext())
                free = await db.DiningTables.AsNoTracking().Where(t => t.Active && t.ActiveTabId == null)
                    .OrderByDescending(t => t.Number).FirstOrDefaultAsync();
            Assert.NotNull(free);

            Assert.Equal(ErrorCodes.Forbidden, (await Rejects(cashier.CommandAsync<ConfigureTablesResult>(Commands.TablesConfigure, new { count = 30 }))).Code);

            // Dois terminais abrindo a mesma mesa: só um vence.
            var opening = new[]
            {
                attendant.CommandAsync<TabDto>(Commands.TabsOpen, new { tableNumber = free.Number }),
                cashier.CommandAsync<TabDto>(Commands.TabsOpen, new { tableNumber = free.Number }),
            };
            try
            {
                await Task.WhenAll(opening);
            }
            catch (ServerException)
            {
            }
            foreach (var result in opening.Where(t => t.IsCompletedSuccessfully)) tabIds.Add(result.Result.Id);
            Assert.Equal(1, opening.Count(t => t.IsCompletedSuccessfully));
            Assert.Equal(ErrorCodes.Conflict, ((ServerException)opening.Single(t => t.IsFaulted).Exception!.InnerException!).Code);
            var tab = opening.Single(t => t.IsCompletedSuccessfully).Result;

            if (free.Number > 1)
                Assert.Equal(ErrorCodes.Conflict, (await Rejects(admin.CommandAsync<ConfigureTablesResult>(Commands.TablesConfigure, new { count = free.Number - 1 }))).Code);
            Assert.Equal(ErrorCodes.Forbidden, (await Rejects(attendant.CommandAsync<ConfigureTablesResult>(Commands.TablesConfigure, new { count = free.Number }))).Code);
            Assert.Equal(free.Number, tab.TableNumber);
            Assert.Equal(tab.Id, (await Snapshot(cashier)).Tables.Single(t => t.Number == free.Number).ActiveTabId);
            Assert.Contains((await Snapshot(cashier)).Tabs, t => t.Id == tab.Id);

            Assert.Equal(ErrorCodes.Forbidden, (await Rejects(attendant.CommandAsync<TabDto>(Commands.TabsPay, new { tabId = tab.Id, version = 0, method = "PIX" }))).Code);
            Assert.Equal(ErrorCodes.Forbidden, (await Rejects(cashier.CommandAsync<ProductDto>(Commands.ProductsSave,
                new { name = "Não permitido", category = "Bebidas", priceCents = 100, active = true }))).Code);

            // Idempotência: repetir o mesmo comando não duplica o item.
            var payload = new { tabId = tab.Id, version = 0, productId = product.Id, quantity = 2, note = "Sem açúcar & gelo" };
            var commandId = Guid.NewGuid().ToString();
            var first = await cashier.RequestAsync<TabDto>(Commands.TabsAdd, payload, commandId);
            var replay = await cashier.RequestAsync<TabDto>(Commands.TabsAdd, payload, commandId);
            Assert.Equal(first.Version, replay.Version);
            Assert.Single(replay.Items);
            Assert.Equal(ErrorCodes.Conflict, (await Rejects(cashier.RequestAsync<TabDto>(Commands.TabsAdd, payload with { quantity = 3 }, commandId))).Code);

            // Alterações concorrentes de sockets diferentes: exatamente uma confirma esta versão.
            var race = new[]
            {
                attendant.CommandAsync<TabDto>(Commands.TabsAdd, payload with { version = first.Version }),
                admin.CommandAsync<TabDto>(Commands.TabsAdd, payload with { version = first.Version }),
            };
            try
            {
                await Task.WhenAll(race);
            }
            catch (ServerException)
            {
            }
            Assert.Equal(1, race.Count(t => t.IsCompletedSuccessfully));
            Assert.Equal(ErrorCodes.Conflict, ((ServerException)race.Single(t => t.IsFaulted).Exception!.InnerException!).Code);
            var latest = (await Snapshot(cashier)).Tabs.Single(t => t.Id == tab.Id);
            Assert.Equal(2, latest.Items.Count);

            var draft = new Product
            {
                Id = Guid.NewGuid(),
                Name = $"Buffet teste {suffix}",
                Category = "Buffet",
                SaleType = SaleType.Weight,
                PriceCents = 0,
                Active = false,
                CreatedAt = Clock.Now(),
            };
            await using (var db = database.CreateContext())
            {
                db.Products.Add(draft);
                await db.SaveChangesAsync();
            }
            productIds.Add(draft.Id);
            foreach (var client in new[] { attendant, cashier })
            {
                Assert.Contains((await Snapshot(client)).Products, p => p.Id == draft.Id);
                await Rejects(client.CommandAsync<TabDto>(Commands.TabsAdd,
                    new { tabId = tab.Id, version = latest.Version, productId = draft.Id, quantity = 1, weightGrams = 350 }));
            }
            var weighted = await admin.CommandAsync<ProductDto>(Commands.ProductsSave,
                new { id = draft.Id, name = draft.Name, category = "Buffet", saleType = "WEIGHT", priceCents = 6590, active = true });
            Assert.Equal(ErrorCodes.Validation, (await Rejects(cashier.CommandAsync<TabDto>(Commands.TabsAdd,
                new { tabId = tab.Id, version = latest.Version, productId = weighted.Id, quantity = 1 }))).Code);

            latest = await cashier.CommandAsync<TabDto>(Commands.TabsAdd,
                new { tabId = tab.Id, version = latest.Version, productId = weighted.Id, quantity = 1, weightGrams = 250 });
            var weightedItem = latest.Items.Single(i => i.ProductId == weighted.Id);
            latest = await attendant.CommandAsync<TabDto>(Commands.TabsUpdate,
                new { tabId = tab.Id, version = latest.Version, itemId = weightedItem.Id, quantity = 1, weightGrams = 333, note = "Peso corrigido" });
            latest = await cashier.CommandAsync<TabDto>(Commands.TabsUpdate,
                new { tabId = tab.Id, version = latest.Version, itemId = latest.Items[0].Id, quantity = 3, note = "Sem açúcar & gelo" });

            var removedItem = latest.Items[1];
            var staleVersion = latest.Version;
            var removalId = Guid.NewGuid().ToString();
            var removal = new { tabId = tab.Id, version = latest.Version, itemId = removedItem.Id };
            latest = await attendant.RequestAsync<TabDto>(Commands.TabsRemove, removal, removalId);
            var repeated = await attendant.RequestAsync<TabDto>(Commands.TabsRemove, removal, removalId);
            Assert.Equal(latest.Version, repeated.Version);
            Assert.Equal(2, latest.Items.Count);
            Assert.Equal(ErrorCodes.Conflict, (await Rejects(cashier.CommandAsync<TabDto>(Commands.TabsRemove,
                new { tabId = tab.Id, version = staleVersion, itemId = weightedItem.Id }))).Code);

            // Mudar o preço do catálogo não altera itens já lançados.
            await admin.CommandAsync<ProductDto>(Commands.ProductsSave,
                new { id = product.Id, name = product.Name, category = "Teste", priceCents = 9999, active = true });

            var paid = await cashier.CommandAsync<TabDto>(Commands.TabsPay, new { tabId = tab.Id, version = latest.Version, method = "PIX" });
            Assert.Equal(5896, paid.Payment!.AmountCents);
            var xml = await cashier.RequestAsync<XmlExportDto>(Commands.TabsXml, new { tabId = tab.Id });
            Assert.Contains("<vNF>58.96</vNF>", xml.Xml);
            Assert.Contains("<qCom>0.3330</qCom>", xml.Xml);
            Assert.Contains("<vUnCom>65.9000000000</vUnCom>", xml.Xml);
            Assert.Contains("<mod>65</mod>", xml.Xml);
            Assert.Contains("<tpAmb>2</tpAmb>", xml.Xml);
            Assert.Contains("Sem açúcar &amp; gelo", xml.Xml);
            Assert.Equal($"venda-demo-{tab.Number}.xml", xml.Filename);

            Assert.Equal(ErrorCodes.InvalidState, (await Rejects(admin.CommandAsync<TabDto>(Commands.TabsAdd, payload with { version = paid.Version }))).Code);
            Assert.Equal(ErrorCodes.InvalidState, (await Rejects(attendant.CommandAsync<TabDto>(Commands.TabsRemove,
                new { tabId = tab.Id, version = paid.Version, itemId = weightedItem.Id }))).Code);
            Assert.Equal(ErrorCodes.InvalidState, (await Rejects(cashier.CommandAsync<TabDto>(Commands.TabsUpdate,
                new { tabId = tab.Id, version = paid.Version, itemId = weightedItem.Id, quantity = 1, weightGrams = 100 }))).Code);

            var closed = await cashier.CommandAsync<TabDto>(Commands.TabsClose, new { tabId = tab.Id, version = paid.Version });
            Assert.Equal(TabStatus.Closed, closed.Status);
            await using (var db = database.CreateContext())
                Assert.Null((await db.DiningTables.AsNoTracking().SingleAsync(t => t.Number == free.Number)).ActiveTabId);

            var next = await attendant.CommandAsync<TabDto>(Commands.TabsOpen, new { tableNumber = free.Number });
            tabIds.Add(next.Id);
            Assert.NotEqual(tab.Id, next.Id);
            Assert.Contains((await Snapshot(cashier)).History, t => t.Id == tab.Id);
            Assert.True(Volatile.Read(ref notifications) > 0);
            await using (var db = database.CreateContext())
                Assert.True(await db.AuditLogs.CountAsync(l => l.EntityId == tab.Id.ToString()) >= 5);

            Assert.Equal(ErrorCodes.Validation, (await Rejects(admin.RequestAsync<TabDto>(Commands.TabsOpen, new { tableNumber = 1 })))
                .Code);
            await admin.RequestAsync<LogoutResult>(Commands.Logout);
            Assert.Equal(ErrorCodes.Unauthorized, (await Rejects(admin.RequestAsync<SnapshotDto>(Commands.Snapshot))).Code);
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            await server.DisposeAsync();
            // Remove somente os registros criados por este teste, nunca os dados do usuário.
            await using var db = database.CreateContext();
            var tabKeys = tabIds.Select(id => id.ToString()).ToList();
            await db.DiningTables.Where(t => t.ActiveTabId != null && tabIds.Contains(t.ActiveTabId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ActiveTabId, (Guid?)null));
            await db.CommandReceipts.Where(r => userIds.Contains(r.UserId)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(l => userIds.Contains(l.UserId)).ExecuteDeleteAsync();
            await db.Payments.Where(p => tabIds.Contains(p.TabId)).ExecuteDeleteAsync();
            await db.TabItems.Where(i => tabIds.Contains(i.TabId)).ExecuteDeleteAsync();
            await db.Tabs.Where(t => tabIds.Contains(t.Id)).ExecuteDeleteAsync();
            await db.Products.Where(p => productIds.Contains(p.Id)).ExecuteDeleteAsync();
            await db.Sessions.Where(s => userIds.Contains(s.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }
}
