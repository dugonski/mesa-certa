using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MesaCerta.Server.Maintenance;

/// <summary>
/// Prepara apenas arquivos locais: o SQL gerado deve ser executado no MySQL Workbench com
/// uma conexão administrativa autenticada. Nenhuma senha de root é necessária aqui.
/// </summary>
public static partial class PrepareDbAccess
{
    public static void Run(string root, TextWriter output)
    {
        var directory = Path.Combine(root, "artifacts");
        var sqlPath = Path.Combine(directory, "preparar-mysql.sql");
        var envPath = Path.Combine(root, ".env");
        var username = $"restaurante_{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var sql = string.Join("\n",
            "-- Execute este arquivo completo na conexão local do MySQL Workbench.",
            "-- Não altera o usuário root nem apaga dados existentes.",
            "-- Este arquivo contém uma credencial: não o compartilhe.",
            "CREATE DATABASE IF NOT EXISTS restaurante CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
            $"CREATE USER '{username}'@'localhost' IDENTIFIED BY '{password}';",
            $"CREATE USER '{username}'@'127.0.0.1' IDENTIFIED BY '{password}';",
            $"GRANT ALL PRIVILEGES ON restaurante.* TO '{username}'@'localhost';",
            $"GRANT ALL PRIVILEGES ON restaurante.* TO '{username}'@'127.0.0.1';",
            "SELECT 'Acesso da aplicação preparado.' AS resultado;",
            "");
        var env = File.ReadAllText(envPath);
        var connection = $"DATABASE_URL=\"mysql://{username}:{password}@127.0.0.1:3306/restaurante\"";
        var next = DatabaseUrlLine().IsMatch(env)
            ? DatabaseUrlLine().Replace(env, connection, 1)
            : $"{env}\n{connection}\n";
        Directory.CreateDirectory(directory);
        using (var file = new FileStream(sqlPath, FileMode.CreateNew, FileAccess.Write))
            file.Write(Encoding.UTF8.GetBytes(sql));
        File.WriteAllText(envPath, next, new UTF8Encoding(false));
        output.WriteLine("SQL preparado em artifacts/preparar-mysql.sql e .env atualizado. Nenhuma alteração no MySQL foi executada.");
    }

    [GeneratedRegex("^DATABASE_URL=.*$", RegexOptions.Multiline)]
    private static partial Regex DatabaseUrlLine();
}
