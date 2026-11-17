using MesaCerta.Server.Data;
using MesaCerta.Server.Fiscal;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Modules;

/// <summary>Tudo que um caso de uso recebe. <see cref="Db"/> é o contexto da transação em andamento.</summary>
public sealed record CommandContext(
    MesaCertaDbContext Db,
    User User,
    string Terminal,
    IRequestData Data,
    string Action,
    string? PasswordHash,
    FiscalConfig Fiscal)
{
    public T DataAs<T>() where T : IRequestData => (T)Data;
}

public delegate Task<object> CommandHandler(CommandContext context);
