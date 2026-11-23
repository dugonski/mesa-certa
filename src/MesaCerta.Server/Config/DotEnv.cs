namespace MesaCerta.Server.Config;

/// <summary>
/// Leitor simples de arquivos .env. Variáveis já definidas no ambiente têm prioridade,
/// como no <c>node --env-file</c> usado anteriormente.
/// </summary>
public static class DotEnv
{
    public static Dictionary<string, string?> Load(string? path = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var file = path ?? Find();
        if (file is not null && File.Exists(file))
            foreach (var (key, value) in Parse(File.ReadAllLines(file)))
                values[key] = value;
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = (string?)entry.Value;
        return values;
    }

    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];
            else if (value.IndexOf(" #", StringComparison.Ordinal) is var comment and >= 0)
                value = value[..comment].TrimEnd();
            yield return (key, value);
        }
    }

    /// <summary>Procura o .env no diretório atual e nos diretórios acima dele.</summary>
    public static string? Find(string name = ".env")
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
