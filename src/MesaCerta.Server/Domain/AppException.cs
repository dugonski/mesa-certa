namespace MesaCerta.Server.Domain;

/// <summary>Erro esperado de negócio: código e mensagem chegam ao cliente.</summary>
public sealed class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
