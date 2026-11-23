using MesaCerta.Server;
using MesaCerta.Server.Config;
using MesaCerta.Server.Data;
using MesaCerta.Server.Maintenance;
using MesaCerta.Server.Transport;

// Uso: MesaCerta.Server [serve|migrate|seed|reset-orders --confirm-reset-fichas|prepare-db-access]
var command = args.FirstOrDefault() ?? "serve";
var envFile = DotEnv.Find();
var root = envFile is not null ? Path.GetDirectoryName(envFile)! : Directory.GetCurrentDirectory();

try
{
    if (command == "prepare-db-access")
    {
        PrepareDbAccess.Run(root, Console.Out);
        return 0;
    }

    var env = DotEnv.Load(envFile);
    var config = ServerConfig.Load(env);
    var database = new Database(config.ConnectionString);

    switch (command)
    {
        case "migrate":
            await new Migrator(config.ConnectionString, Console.Out).ApplyAsync();
            return 0;
        case "seed":
            await Seed.RunAsync(database, env, Console.Out);
            return 0;
        case "reset-orders":
            await ResetOrders.RunAsync(config, args, root, Console.Out);
            return 0;
        case "serve":
            break;
        default:
            Console.Error.WriteLine($"Comando desconhecido: {command}");
            return 2;
    }

    await database.CheckConnectionAsync();
    await using var server = new TcpServer(new RestaurantService(database, config.Fiscal));
    server.Start(config.Host, config.Port);
    Console.WriteLine($"Servidor TCP pronto em {config.Host}:{server.Port}");

    var stopped = new TaskCompletionSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopped.TrySetResult();
    };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => stopped.TrySetResult();
    await stopped.Task;
    Console.WriteLine("Encerrando servidor...");
    return 0;
}
catch (Exception error) when (error is ConfigurationException or InvalidOperationException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch (Exception error) when (error is MySqlConnector.MySqlException or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine($"Servidor: {error.Message}");
    return 1;
}
