using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using MesaCerta.Shared.Contracts;
using MesaCerta.Shared.Json;

namespace MesaCerta.Shared.Client;

/// <summary>Erro devolvido pelo servidor ou pela conexão, com o código do protocolo.</summary>
public sealed class ServerException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Cliente TCP do protocolo MesaCerta. O token da sessão fica somente neste objeto;
/// a interface recebe apenas resultados e avisos ("changed" e "disconnected").
/// </summary>
public sealed class ServerConnection : IDisposable
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _socket;

    /// <summary>Avisos do servidor: "changed" ou "disconnected" (queda da conexão).</summary>
    public event Action<string>? EventReceived;

    public string? Token { get; set; }

    public bool IsConnected => _socket?.Connected == true;

    public async Task ConnectAsync(string host, int port)
    {
        Disconnect();
        var socket = new TcpClient { NoDelay = true };
        _socket = socket;
        using var timeout = new CancellationTokenSource(ConnectTimeout);
        try
        {
            await socket.ConnectAsync(host, port, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw new ServerException(ErrorCodes.Connection, "Tempo de conexão esgotado. Confira o endereço do servidor.");
        }
        catch (Exception error) when (error is SocketException or IOException or ArgumentException)
        {
            socket.Dispose();
            throw new ServerException(ErrorCodes.Connection, "Servidor indisponível. Confira o endereço e se ele está em execução.");
        }
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        try
        {
            socket.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 15);
        }
        catch (SocketException)
        {
        }
        _ = ReadAsync(socket);
    }

    private async Task ReadAsync(TcpClient socket)
    {
        var decoder = new LineDecoder();
        var buffer = new byte[64 * 1024];
        try
        {
            var stream = socket.GetStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                foreach (var message in decoder.Push(buffer.AsSpan(0, read)))
                    Dispatch(message);
            }
        }
        catch (Exception)
        {
            // Queda ou frame inválido: tratado como desconexão.
        }
        socket.Dispose();
        if (!ReferenceEquals(_socket, socket)) return;
        _socket = null;
        Token = null;
        FailPending("Conexão perdida. Entre novamente e confira a ficha antes de repetir uma operação.");
        EventReceived?.Invoke("disconnected");
    }

    private void Dispatch(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return;
        if (message.TryGetProperty("event", out var name) && name.ValueKind == JsonValueKind.String)
        {
            EventReceived?.Invoke(name.GetString()!);
            return;
        }
        var reply = message.Deserialize<ServerMessage>(MesaCertaJson.Options);
        if (reply?.Id is null || !_pending.TryRemove(reply.Id, out var pending)) return;
        if (reply.Ok == true) pending.TrySetResult(reply.Result ?? default);
        else
            pending.TrySetException(new ServerException(
                reply.Error?.Code ?? ErrorCodes.Internal,
                reply.Error?.Message ?? "Não foi possível concluir."));
    }

    public async Task<JsonElement> RequestAsync(string action, object? data = null, string? commandId = null)
    {
        var socket = _socket;
        if (socket is null || !socket.Connected)
            throw new ServerException(ErrorCodes.Connection, "Sem conexão com o servidor.");
        var id = Guid.NewGuid().ToString();
        var pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        var frame = LineDecoder.Encode(new Envelope
        {
            Id = id,
            Action = action,
            Token = Token,
            CommandId = commandId,
            Data = MesaCertaJson.ToElement(data ?? new { }),
        });
        await _writeLock.WaitAsync();
        try
        {
            await socket.GetStream().WriteAsync(frame);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _pending.TryRemove(id, out _);
            throw new ServerException(ErrorCodes.Connection, "Sem conexão com o servidor.");
        }
        finally
        {
            _writeLock.Release();
        }

        var finished = await Task.WhenAny(pending.Task, Task.Delay(RequestTimeout));
        if (finished != pending.Task)
        {
            _pending.TryRemove(id, out _);
            throw new ServerException(ErrorCodes.Timeout, "Servidor sem resposta. Consulte a ficha antes de repetir a operação.");
        }
        return await pending.Task;
    }

    public async Task<T> RequestAsync<T>(string action, object? data = null, string? commandId = null) =>
        MesaCertaJson.Deserialize<T>(await RequestAsync(action, data, commandId));

    /// <summary>Comando de escrita com identificador novo, para idempotência no servidor.</summary>
    public Task<T> CommandAsync<T>(string action, object data) =>
        RequestAsync<T>(action, data, Guid.NewGuid().ToString());

    public void Disconnect()
    {
        var socket = _socket;
        _socket = null;
        socket?.Dispose();
        Token = null;
        FailPending("Conexão encerrada.");
    }

    private void FailPending(string message)
    {
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var pending))
                pending.TrySetException(new ServerException(ErrorCodes.Connection, message));
    }

    public void Dispose()
    {
        Disconnect();
        _writeLock.Dispose();
    }
}
