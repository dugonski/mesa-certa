using System.Reflection;
using MySqlConnector;

namespace MesaCerta.Server.Data;

/// <summary>
/// Aplica os scripts SQL versionados em <c>database/migrations</c>, na ordem do nome.
/// Bancos criados pela versão anterior são reconhecidos pela tabela <c>_prisma_migrations</c>.
/// </summary>
public sealed class Migrator(string connectionString, TextWriter? log = null)
{
    private const string HistoryTable = "_mesacerta_migrations";
    private readonly TextWriter _log = log ?? TextWriter.Null;

    public static IReadOnlyList<(string Name, string Sql)> Scripts()
    {
        var assembly = typeof(Migrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("migrations/", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (Path.GetFileNameWithoutExtension(name["migrations/".Length..]), Read(assembly, name)))
            .ToList();
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task<IReadOnlyList<string>> ApplyAsync()
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection,
            $"CREATE TABLE IF NOT EXISTS `{HistoryTable}` (`name` VARCHAR(255) NOT NULL, `appliedAt` DATETIME(3) NOT NULL, PRIMARY KEY (`name`)) DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = new MySqlCommand($"SELECT `name` FROM `{HistoryTable}`", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) applied.Add(reader.GetString(0));

        if (await TableExistsAsync(connection, "_prisma_migrations"))
        {
            await using var command = new MySqlCommand(
                "SELECT `migration_name` FROM `_prisma_migrations` WHERE `finished_at` IS NOT NULL AND `rolled_back_at` IS NULL", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) applied.Add(reader.GetString(0));
        }

        var executed = new List<string>();
        foreach (var (name, sql) in Scripts())
        {
            if (applied.Contains(name))
            {
                await RecordAsync(connection, name);
                continue;
            }
            _log.WriteLine($"Aplicando migração {name}...");
            // DDL no MySQL confirma implicitamente; cada script é aplicado uma única vez.
            await ExecuteAsync(connection, sql);
            await RecordAsync(connection, name);
            executed.Add(name);
        }
        _log.WriteLine(executed.Count == 0 ? "Banco já atualizado." : $"{executed.Count} migração(ões) aplicada(s).");
        return executed;
    }

    private static async Task RecordAsync(MySqlConnection connection, string name)
    {
        await using var command = new MySqlCommand(
            $"INSERT IGNORE INTO `{HistoryTable}` (`name`, `appliedAt`) VALUES (@name, UTC_TIMESTAMP(3))", connection);
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection connection, string table)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table", connection);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}
