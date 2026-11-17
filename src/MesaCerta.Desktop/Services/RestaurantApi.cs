using System.Text.RegularExpressions;
using MesaCerta.Shared;
using MesaCerta.Shared.Client;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Desktop.Services;

public sealed record LoginInput(string Host, int Port, string Username, string Password, string Terminal);

/// <summary>
/// Ponte restrita entre a interface e o servidor: só as operações do desktop são aceitas
/// e o token da sessão nunca chega aos view models.
/// </summary>
public partial class RestaurantApi : IDisposable
{
    private readonly ServerConnection _connection = new();

    public RestaurantApi()
    {
        _connection.EventReceived += name => EventReceived?.Invoke(name);
    }

    public event Action<string>? EventReceived;

    public virtual async Task<UserDto> LoginAsync(LoginInput input)
    {
        if (!HostPattern().IsMatch(input.Host) || input.Port is < 1 or > 65535)
            throw new ServerException(ErrorCodes.Connection, "Endereço ou porta inválidos.");
        await _connection.ConnectAsync(input.Host, input.Port);
        var result = await _connection.RequestAsync<LoginResult>(Commands.Login,
            new { username = input.Username, password = input.Password, terminal = input.Terminal });
        _connection.Token = result.Token;
        return result.User;
    }

    public virtual Task<SnapshotDto> SnapshotAsync() =>
        _connection.RequestAsync<SnapshotDto>(Commands.Snapshot);

    /// <summary>Comando de escrita com identificador novo (idempotência no servidor).</summary>
    public virtual Task<T> CommandAsync<T>(string action, object data)
    {
        if (!Commands.DesktopActions.Contains(action) || action == Commands.Snapshot)
            throw new ServerException(ErrorCodes.Connection, "Operação inválida.");
        return _connection.CommandAsync<T>(action, data);
    }

    public virtual Task<XmlExportDto> ExportXmlAsync(Guid tabId) =>
        _connection.RequestAsync<XmlExportDto>(Commands.TabsXml, new { tabId });

    public virtual async Task LogoutAsync()
    {
        try
        {
            if (_connection.Token is not null) await _connection.RequestAsync(Commands.Logout);
        }
        finally
        {
            _connection.Disconnect();
        }
    }

    public void Dispose() => _connection.Dispose();

    [GeneratedRegex("^[a-zA-Z0-9.:-]{1,253}$")]
    private static partial Regex HostPattern();
}
