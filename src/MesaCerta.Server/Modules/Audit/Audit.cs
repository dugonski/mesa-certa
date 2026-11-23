using MesaCerta.Server.Data;
using MesaCerta.Shared.Json;

namespace MesaCerta.Server.Modules.Audit;

public static class AuditTrail
{
    /// <summary>Registra a operação dentro da mesma transação da alteração. Nunca grava senha ou token.</summary>
    public static void Add(MesaCertaDbContext db, User user, string terminal, string action, string entityId, object? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            UserName = user.Name,
            Terminal = terminal,
            Action = action,
            EntityId = entityId,
            Details = MesaCertaJson.Serialize(details ?? new { }),
            CreatedAt = Clock.Now(),
        });
}
