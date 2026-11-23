using System.Text.Json;
using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Fiscal;
using MesaCerta.Server.Modules;
using MesaCerta.Server.Modules.Auth;
using MesaCerta.Server.Modules.Payments;
using MesaCerta.Server.Transport;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using MesaCerta.Shared.Json;

namespace MesaCerta.Server;

/// <summary>
/// Fluxo de escrita: TCP → contrato → sessão → permissão → recibo/transação → caso de uso →
/// auditoria → recibo → commit → aviso de atualização.
/// </summary>
public sealed class RestaurantService : IRequestService
{
    private readonly Database _database;
    private readonly AuthService _auth;
    private readonly CommandExecutor _commands;

    public RestaurantService(Database database, FiscalConfig? fiscal = null)
    {
        _database = database;
        _auth = new AuthService(database);
        _commands = new CommandExecutor(database, fiscal ?? FiscalConfig.Default);
    }

    public bool IsMutation(string action) => Router.Handlers.ContainsKey(action);

    public async Task<JsonElement> ExecuteAsync(Envelope request, string peer)
    {
        var action = request.Action;
        var data = Router.ParseData(action, request.Data);
        if (action == Commands.Login) return MesaCertaJson.ToElement(await _auth.LoginAsync((LoginRequest)data, peer));

        var session = await _auth.AuthenticateAsync(request.Token);
        var user = session.User;
        switch (action)
        {
            case Commands.Logout:
                return MesaCertaJson.ToElement(await _auth.LogoutAsync(session));
            case Commands.Snapshot:
                return MesaCertaJson.ToElement(await Snapshot.LoadAsync(_database, user));
            case Commands.TabsXml:
                return MesaCertaJson.ToElement(await ExportXml.HandleAsync(_database, user, (ExportXmlRequest)data));
        }
        if (!Router.Handlers.TryGetValue(action, out var handler))
            throw new AppException(ErrorCodes.UnknownAction, "Operação desconhecida.");
        TabRules.RequirePermission(user.Role, Commands.MutationPermissions[action]);
        return await _commands.ExecuteAsync(request.CommandId, user, session.Terminal, action, data, handler);
    }
}
