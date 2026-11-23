namespace MesaCerta.Desktop.Services;

/// <summary>Diálogo "Salvar como" da plataforma.</summary>
public interface IFileSaver
{
    /// <returns><c>false</c> quando o usuário cancela.</returns>
    Task<bool> SaveTextAsync(string title, string suggestedName, string content);
}
