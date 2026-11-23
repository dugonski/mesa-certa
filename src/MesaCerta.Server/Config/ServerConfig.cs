using MesaCerta.Server.Fiscal;
using MySqlConnector;

namespace MesaCerta.Server.Config;

public sealed class ConfigurationException(string message) : Exception(message);

public sealed record ServerConfig(string DatabaseUrl, string ConnectionString, string Host, int Port, FiscalConfig Fiscal)
{
    public const int DefaultPort = 5050;

    /// <summary>Valida URL MySQL, host e porta sem expor credenciais nas mensagens de erro.</summary>
    public static ServerConfig Load(IReadOnlyDictionary<string, string?> env)
    {
        var invalid = new List<string>();
        var databaseUrl = env.GetValueOrDefault("DATABASE_URL")?.Trim() ?? "";
        string? connectionString = null;
        try
        {
            connectionString = ToConnectionString(databaseUrl);
        }
        catch (FormatException)
        {
            invalid.Add("DATABASE_URL");
        }

        var host = env.GetValueOrDefault("SERVER_HOST");
        host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

        var port = DefaultPort;
        var portText = env.GetValueOrDefault("SERVER_PORT");
        if (!string.IsNullOrWhiteSpace(portText)
            && (!int.TryParse(portText.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)
                || port is < 1 or > 65535))
            invalid.Add("SERVER_PORT");

        if (invalid.Count > 0)
            throw new ConfigurationException($"Configuração inválida: {string.Join(", ", invalid)}. Confira o .env.");
        return new ServerConfig(databaseUrl, connectionString!, host, port, FiscalConfig.FromEnv(env));
    }

    /// <summary>Converte <c>mysql://usuario:senha@host:porta/banco</c> em connection string.</summary>
    public static string ToConnectionString(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "mysql"
            || string.IsNullOrEmpty(uri.Host))
            throw new FormatException("URL inválida.");
        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (database.Length == 0 || database.Contains('/')) throw new FormatException("Banco não informado.");
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = (uint)(uri.IsDefaultPort || uri.Port < 0 ? 3306 : uri.Port),
            Database = database,
            UserID = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            CharacterSet = "utf8mb4",
            GuidFormat = MySqlGuidFormat.Char36,
            DefaultCommandTimeout = 30,
            ConnectionTimeout = 10,
        };
        return builder.ConnectionString;
    }

    /// <summary>Nome do banco, usado no backup de manutenção.</summary>
    public string DatabaseName => new MySqlConnectionStringBuilder(ConnectionString).Database;
}
