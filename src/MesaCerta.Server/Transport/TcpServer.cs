using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using MesaCerta.Server.Domain;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Transport;

/// <summary>
/// Servidor TCP com JSON delimitado por nova linha. Cada conexão processa suas requisições
/// em ordem; mutações disparam apenas um aviso "changed" para clientes autenticados.
/// </summary>
public sealed class TcpServer(IRequestService service, TextWriter? log = null) : IAsyncDisposable
{
    public const int MaxConnections = 100;
    private const int MaxQueuedRequests = 50;
    private const int MaxQueuedResponses = 256;

    private readonly TextWriter _log = log ?? Console.Error;
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Connection> _clients = [];
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private int _disposed;

    public int Port => ((IPEndPoint)(_listener ?? throw new InvalidOperationException()).LocalEndpoint).Port;

    public void Start(string host, int port)
    {
        var address = IPAddress.TryParse(host, out var parsed)
            ? parsed
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
        _listener = new TcpListener(address, port);
        _listener.Start();
        _acceptLoop = AcceptAsync(_listener, _stop.Token);
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient socket;
            try
            {
                socket = await listener.AcceptTcpClientAsync(stop);
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            Connection connection;
            lock (_clients)
            {
                if (_clients.Count >= MaxConnections)
                {
                    socket.Dispose();
                    continue;
                }
                connection = new Connection(this, socket);
                _clients.Add(connection);
            }
            _ = connection.RunAsync(stop);
        }
    }

    private void Remove(Connection connection)
    {
        lock (_clients) _clients.Remove(connection);
    }

    private void Broadcast(ServerMessage message)
    {
        Connection[] clients;
        lock (_clients) clients = [.. _clients];
        foreach (var client in clients)
            if (client.SessionToken is not null) client.Send(message);
    }

    /// <summary>Desconecta todos os clientes e para de aceitar conexões.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _stop.Cancel();
        _listener?.Stop();
        Connection[] clients;
        lock (_clients) clients = [.. _clients];
        foreach (var client in clients) client.Close();
        if (_acceptLoop is not null) await _acceptLoop;
        _stop.Dispose();
    }

    private sealed class Connection(TcpServer server, TcpClient socket)
    {
        private readonly Channel<JsonElement> _requests = Channel.CreateBounded<JsonElement>(MaxQueuedRequests);
        private readonly Channel<byte[]> _responses = Channel.CreateBounded<byte[]>(MaxQueuedResponses);
        private readonly CancellationTokenSource _closed = new();
        private readonly string _peer = (socket.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "desconhecido";
        private int _isClosed;

        public volatile string? SessionToken;

        public async Task RunAsync(CancellationToken stop)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, _closed.Token);
            try
            {
                socket.NoDelay = true;
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                TrySetKeepAliveTime(socket.Client, 30);
                var stream = socket.GetStream();
                await Task.WhenAll(
                    ReadAsync(stream, linked.Token),
                    ProcessAsync(linked.Token),
                    WriteAsync(stream, linked.Token));
            }
            catch (Exception)
            {
                // Erros de rede encerram apenas esta conexão.
            }
            finally
            {
                Close();
                server.Remove(this);
            }
        }

        private async Task ReadAsync(NetworkStream stream, CancellationToken token)
        {
            var decoder = new LineDecoder();
            var buffer = new byte[64 * 1024];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, token);
                    if (read == 0) break;
                    foreach (var message in decoder.Push(buffer.AsSpan(0, read)))
                        if (!_requests.Writer.TryWrite(message))
                            throw new FrameException("Fila de requisições excedida.");
                }
            }
            finally
            {
                Close();
            }
        }

        private async Task ProcessAsync(CancellationToken token)
        {
            await foreach (var message in _requests.Reader.ReadAllAsync(token))
                await HandleAsync(message);
        }

        private async Task HandleAsync(JsonElement message)
        {
            Envelope? request = null;
            try
            {
                request = Envelope.Parse(message);
                var result = await server.ExecuteRequestAsync(request, _peer);
                if (request.Action == Commands.Login) SessionToken = result.GetProperty("token").GetString();
                if (request.Action == Commands.Logout) SessionToken = null;
                Send(ServerMessage.Success(request.Id, result));
                // Só um sinal de invalidação é transmitido; os dados exigem nova leitura autorizada.
                if (server.IsMutation(request.Action)) server.Broadcast(ServerMessage.Changed);
            }
            catch (Exception error)
            {
                var id = request?.Id ?? Envelope.TryReadId(message);
                Send(error switch
                {
                    AppException app => ServerMessage.Failure(id, app.Code, app.Message),
                    ContractException => ServerMessage.Failure(id, ErrorCodes.Validation, "Confira os campos informados."),
                    _ => Unexpected(id, error),
                });
            }
        }

        private ServerMessage Unexpected(string? id, Exception error)
        {
            server._log.WriteLine($"Falha na operação: {error.GetType().Name}");
            return ServerMessage.Failure(id, ErrorCodes.Internal, "Não foi possível concluir. Verifique o servidor e tente novamente.");
        }

        public void Send(ServerMessage message)
        {
            if (_isClosed == 1) return;
            try
            {
                if (!_responses.Writer.TryWrite(LineDecoder.Encode(message))) Close();
            }
            catch (FrameException)
            {
                Close();
            }
        }

        private async Task WriteAsync(NetworkStream stream, CancellationToken token)
        {
            await foreach (var frame in _responses.Reader.ReadAllAsync(token))
                await stream.WriteAsync(frame, token);
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) == 1) return;
            SessionToken = null;
            _requests.Writer.TryComplete();
            _responses.Writer.TryComplete();
            _closed.Cancel();
            socket.Dispose();
        }

        private static void TrySetKeepAliveTime(Socket socket, int seconds)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, seconds);
            }
            catch (SocketException)
            {
            }
        }
    }

    private Task<JsonElement> ExecuteRequestAsync(Envelope request, string peer) => service.ExecuteAsync(request, peer);

    private bool IsMutation(string action) => service.IsMutation(action);
}
