using System.Text.Json;

namespace MesaCerta.Desktop.Services;

public sealed record ConnectionSettings(string Host = "127.0.0.1", int Port = 5050, string Terminal = "");

/// <summary>Endereço do servidor e nome do terminal, guardados no perfil do usuário do Windows.</summary>
public class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MesaCerta", "connection.json");
    }

    public virtual ConnectionSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(_path)) ?? new ConnectionSettings()
                : new ConnectionSettings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ConnectionSettings();
        }
    }

    public virtual void Save(ConnectionSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Preferência local: falhar ao salvar não impede o login.
        }
    }
}
