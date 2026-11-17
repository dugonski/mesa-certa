using System.Text.Json;
using MesaCerta.Server.Modules;
using MesaCerta.Server.Modules.Products;
using MesaCerta.Server.Modules.Tables;
using MesaCerta.Server.Modules.Tabs;
using MesaCerta.Server.Modules.Users;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Transport;

/// <summary>Associa cada comando ao contrato de entrada e ao caso de uso.</summary>
public static class Router
{
    public static readonly IReadOnlyDictionary<string, Func<JsonElement?, IRequestData>> Schemas =
        new Dictionary<string, Func<JsonElement?, IRequestData>>
        {
            [Commands.Login] = Rules.Parse<LoginRequest>,
            [Commands.TabsOpen] = Rules.Parse<OpenTabRequest>,
            [Commands.TablesConfigure] = Rules.Parse<ConfigureTablesRequest>,
            [Commands.TabsAdd] = Rules.Parse<AddItemRequest>,
            [Commands.TabsPay] = Rules.Parse<PayTabRequest>,
            [Commands.TabsUpdate] = Rules.Parse<UpdateItemRequest>,
            [Commands.TabsRemove] = Rules.Parse<RemoveItemRequest>,
            [Commands.TabsClose] = Rules.Parse<CloseTabRequest>,
            [Commands.TabsXml] = Rules.Parse<ExportXmlRequest>,
            [Commands.ProductsSave] = Rules.Parse<SaveProductRequest>,
            [Commands.UsersCreate] = Rules.Parse<CreateUserRequest>,
        };

    public static readonly IReadOnlyDictionary<string, CommandHandler> Handlers = new Dictionary<string, CommandHandler>
    {
        [Commands.TabsOpen] = OpenTab.HandleAsync,
        [Commands.TabsAdd] = ChangeTab.HandleAsync,
        [Commands.TabsUpdate] = ChangeTab.HandleAsync,
        [Commands.TabsRemove] = ChangeTab.HandleAsync,
        [Commands.TabsPay] = ChangeTab.HandleAsync,
        [Commands.TabsClose] = ChangeTab.HandleAsync,
        [Commands.TablesConfigure] = ConfigureTables.HandleAsync,
        [Commands.ProductsSave] = SaveProduct.HandleAsync,
        [Commands.UsersCreate] = CreateUser.HandleAsync,
    };

    public static IRequestData ParseData(string action, JsonElement? data) =>
        Schemas.TryGetValue(action, out var parse) ? parse(data) : new EmptyRequest();
}
