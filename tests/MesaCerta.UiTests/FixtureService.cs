using System.Text.Json;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Fiscal;
using MesaCerta.Server.Transport;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using MesaCerta.Shared.Json;

namespace MesaCerta.UiTests;

/// <summary>
/// Servidor de fixtures em memória para o teste de interface. Usa o transporte TCP e os
/// contratos reais, mas não comprova persistência MySQL.
/// </summary>
public sealed class FixtureService : IRequestService
{
    public const string Password = "senha-demo";

    private readonly object _lock = new();
    private readonly List<UserDto> _users =
    [
        new(Guid.NewGuid(), "Administrador", "admin", Role.Admin, true),
        new(Guid.NewGuid(), "Atendente", "atendente", Role.Attendant, true),
        new(Guid.NewGuid(), "Caixa", "caixa", Role.Cashier, true),
    ];
    private readonly Dictionary<string, UserDto> _sessions = [];
    private readonly List<ProductDto> _products =
    [
        Product("Água mineral", "Bebidas", 500),
        Product("Suco de laranja", "Bebidas", 950),
        Product("Café expresso", "Bebidas", 600),
        Product("Buffet regular", "Buffet", 0, SaleType.Weight, active: false),
        Product("Buffet especial carnes", "Buffet", 7990, SaleType.Weight),
    ];
    private readonly List<DiningTableDto> _tables = Enumerable.Range(1, 30).Select(n => new DiningTableDto(n, true, null)).ToList();
    private readonly List<TabDto> _tabs = [];
    private readonly Dictionary<Guid, string> _xml = [];
    private readonly List<AuditLogDto> _audit = [];
    private int _nextNumber = 1;

    private static ProductDto Product(string name, string category, int price, SaleType type = SaleType.Unit, bool active = true) =>
        new(Guid.NewGuid(), name, category, price, type, active, DateTime.UtcNow);

    public bool IsMutation(string action) => Router.Handlers.ContainsKey(action);

    public Task<JsonElement> ExecuteAsync(Envelope request, string peer)
    {
        lock (_lock) return Task.FromResult(MesaCertaJson.ToElement(Execute(request)));
    }

    private object Execute(Envelope request)
    {
        var data = Router.ParseData(request.Action, request.Data);
        if (data is LoginRequest login)
        {
            var found = _users.FirstOrDefault(u => u.Username == login.Username);
            if (found is null || login.Password != Password)
                throw new AppException(ErrorCodes.InvalidLogin, "Usuário ou senha incorretos.");
            var token = Guid.NewGuid().ToString("N");
            _sessions[token] = found;
            Audit(found, "LOGIN", found.Id.ToString(), new { });
            return new LoginResult(token, found);
        }
        if (request.Token is null || !_sessions.TryGetValue(request.Token, out var user))
            throw new AppException(ErrorCodes.Unauthorized, "Sua sessão expirou. Entre novamente.");
        if (Commands.MutationPermissions.TryGetValue(request.Action, out var permission))
            TabRules.RequirePermission(user.Role, permission);

        switch (data)
        {
            case OpenTabRequest open:
            {
                var index = _tables.FindIndex(t => t.Number == open.TableNumber);
                if (_tables[index].ActiveTabId is not null)
                    throw new AppException(ErrorCodes.Conflict, "Esta mesa já foi aberta. Selecione-a novamente.");
                var tab = new TabDto(Guid.NewGuid(), _nextNumber++, $"Mesa {open.TableNumber:00}", TabStatus.Open, 0, user.Id,
                    DateTime.UtcNow, null, open.TableNumber, [], null);
                _tabs.Insert(0, tab);
                _tables[index] = _tables[index] with { ActiveTabId = tab.Id };
                Audit(user, "TAB_OPENED", tab.Id.ToString(), new { label = tab.Label });
                return tab;
            }
            case AddItemRequest add:
            {
                var tab = Change(add.TabId, add.Version);
                var product = _products.Single(p => p.Id == add.ProductId);
                if (!product.Active || product.PriceCents <= 0)
                    throw new AppException(ErrorCodes.NotFound, "Produto indisponível. Atualize a lista.");
                var (quantity, grams) = TabRules.ItemMeasures(product.SaleType, add.Quantity, add.WeightGrams);
                var item = new TabItemDto(Guid.NewGuid(), tab.Id, product.Id, product.Name, product.PriceCents, quantity,
                    product.SaleType, grams, add.Note, user.Id, DateTime.UtcNow);
                Audit(user, "ITEM_ADDED", tab.Id.ToString(), new { product = product.Name });
                return Save(tab with { Items = [.. tab.Items, item] });
            }
            case UpdateItemRequest update:
            {
                var tab = Change(update.TabId, update.Version);
                var item = tab.Items.Single(i => i.Id == update.ItemId);
                var (quantity, grams) = TabRules.ItemMeasures(item.SaleType, update.Quantity, update.WeightGrams);
                Audit(user, "ITEM_UPDATED", tab.Id.ToString(), new { product = item.ProductName });
                return Save(tab with
                {
                    Items = tab.Items.Select(i => i.Id == item.Id ? i with { Quantity = quantity, WeightGrams = grams, Note = update.Note } : i).ToList(),
                });
            }
            case RemoveItemRequest remove:
            {
                var tab = Change(remove.TabId, remove.Version);
                Audit(user, "ITEM_REMOVED", tab.Id.ToString(), new { product = tab.Items.Single(i => i.Id == remove.ItemId).ProductName });
                return Save(tab with { Items = tab.Items.Where(i => i.Id != remove.ItemId).ToList() });
            }
            case PayTabRequest pay:
            {
                var tab = Change(pay.TabId, pay.Version);
                var payment = new PaymentDto(Guid.NewGuid(), pay.Method, TabRules.TotalCents(tab.Items), DateTime.UtcNow);
                _xml[tab.Id] = NfceXml.Build(
                    new NfceTab(tab.Number, tab.Label, tab.Items.Select(i => new NfceItem(i.ProductId.ToString(), i.ProductName, i.SaleType, i.Quantity, i.WeightGrams, i.UnitPriceCents, i.Note)).ToList()),
                    new NfcePayment(payment.CreatedAt, payment.Method, payment.AmountCents));
                Audit(user, "PAYMENT_SIMULATED", tab.Id.ToString(), new { });
                return Save(tab with { Status = TabStatus.Paid, Payment = payment });
            }
            case CloseTabRequest close:
            {
                var tab = Change(close.TabId, close.Version, TabStatus.Paid);
                var index = _tables.FindIndex(t => t.ActiveTabId == tab.Id);
                if (index >= 0) _tables[index] = _tables[index] with { ActiveTabId = null };
                Audit(user, "TAB_CLOSED", tab.Id.ToString(), new { });
                return Save(tab with { Status = TabStatus.Closed, ClosedAt = DateTime.UtcNow });
            }
            case ExportXmlRequest export:
            {
                TabRules.RequirePermission(user.Role, Permissions.TabsPay);
                var tab = _tabs.Single(t => t.Id == export.TabId);
                return new XmlExportDto(_xml[tab.Id], $"venda-demo-{tab.Number}.xml");
            }
            case SaveProductRequest save:
            {
                var product = new ProductDto(save.Id ?? Guid.NewGuid(), save.Name, save.Category, save.PriceCents, save.SaleType, save.Active, DateTime.UtcNow);
                var index = _products.FindIndex(p => p.Id == product.Id);
                if (index >= 0) _products[index] = product;
                else _products.Add(product);
                Audit(user, save.Id is null ? "PRODUCT_CREATED" : "PRODUCT_UPDATED", product.Id.ToString(), new { name = product.Name });
                return product;
            }
            case CreateUserRequest create:
            {
                var created = new UserDto(Guid.NewGuid(), create.Name, create.Username, create.Role, true);
                _users.Add(created);
                Audit(user, "USER_CREATED", created.Id.ToString(), new { username = created.Username });
                return created;
            }
            case ConfigureTablesRequest configure:
                _tables.RemoveAll(t => t.Number > configure.Count);
                for (var n = _tables.Count + 1; n <= configure.Count; n++) _tables.Add(new DiningTableDto(n, true, null));
                return new ConfigureTablesResult(configure.Count);
        }

        return request.Action switch
        {
            Commands.Snapshot => Snapshot(user),
            Commands.Logout => new LogoutResult(_sessions.Remove(request.Token)),
            _ => throw new AppException(ErrorCodes.UnknownAction, "Operação desconhecida."),
        };
    }

    private SnapshotDto Snapshot(UserDto user) => new(
        user,
        [.. _tables],
        _tabs.Where(t => t.Status != TabStatus.Closed).ToList(),
        _products.Where(p => Permissions.Can(user.Role, Permissions.ProductsWrite) || p.InCatalog).OrderBy(p => p.Name).ToList(),
        Permissions.Can(user.Role, Permissions.HistoryRead) ? _tabs.Where(t => t.Status == TabStatus.Closed).ToList() : [],
        Permissions.Can(user.Role, Permissions.UsersWrite) ? [.. _users] : [],
        Permissions.Can(user.Role, Permissions.AuditRead) ? Enumerable.Reverse(_audit).ToList() : []);

    private TabDto Change(Guid id, int version, TabStatus status = TabStatus.Open)
    {
        var tab = _tabs.SingleOrDefault(t => t.Id == id);
        TabRules.AssertTab(tab is null ? null : (tab.Version, tab.Status), version, status);
        return tab! with { Version = tab.Version + 1 };
    }

    private TabDto Save(TabDto tab)
    {
        _tabs[_tabs.FindIndex(t => t.Id == tab.Id)] = tab;
        return tab;
    }

    private void Audit(UserDto user, string action, string entity, object details) =>
        _audit.Add(new AuditLogDto(Guid.NewGuid(), user.Id, user.Name, "Terminal UI", action, entity,
            MesaCertaJson.ToElement(details), DateTime.UtcNow));
}
