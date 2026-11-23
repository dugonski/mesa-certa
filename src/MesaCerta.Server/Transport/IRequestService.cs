using System.Text.Json;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Transport;

/// <summary>Contrato entre o transporte TCP e a aplicação (o serviço real ou um de testes).</summary>
public interface IRequestService
{
    Task<JsonElement> ExecuteAsync(Envelope request, string peer);

    bool IsMutation(string action);
}
