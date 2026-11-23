using System.Text.Json;
using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Fiscal;
using MesaCerta.Server.Modules.Auth;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using MesaCerta.Shared.Json;

namespace MesaCerta.Server.Modules;

/// <summary>
/// Recibos persistentes de comandos: cada escrita tem um UUID gravado na mesma transação,
/// então repetir o mesmo comando devolve o resultado original sem duplicar a operação.
/// </summary>
public sealed class CommandExecutor(Database database, FiscalConfig fiscal)
{
    public async Task<JsonElement> ExecuteAsync(
        string? commandIdText, User user, string terminal, string action, IRequestData data, CommandHandler handler)
    {
        if (!Rules.IsUuid(commandIdText)) throw new ContractException("commandId");
        var commandId = Guid.Parse(commandIdText!);
        var fingerprint = Identity.Hash(MesaCertaJson.Serialize(new { action, data = (object)data }));
        // O hash da senha é calculado fora da transação do banco.
        var passwordHash = action == Commands.UsersCreate
            ? await Task.Run(() => Identity.HashPassword(((CreateUserRequest)data).Password))
            : null;

        try
        {
            return await database.TransactionAsync(async tx =>
            {
                var previous = await tx.CommandReceipts.FindAsync(commandId);
                if (previous is not null)
                {
                    if (previous.UserId != user.Id || previous.Fingerprint != fingerprint)
                        throw new AppException(ErrorCodes.Conflict, "Identificador da operação já utilizado.");
                    return Parse(previous.Result);
                }
                var result = await handler(new CommandContext(tx, user, terminal, data, action, passwordHash, fiscal));
                var json = JsonSerializer.Serialize(result, result.GetType(), MesaCertaJson.Options);
                tx.CommandReceipts.Add(new CommandReceipt
                {
                    Id = commandId,
                    UserId = user.Id,
                    Fingerprint = fingerprint,
                    Result = json,
                    CreatedAt = Clock.Now(),
                });
                await tx.SaveChangesAsync();
                return Parse(json);
            });
        }
        catch (Exception error) when (MySqlErrors.IsDuplicate(error))
        {
            await using var db = database.CreateContext();
            var receipt = await db.CommandReceipts.FindAsync(commandId);
            if (receipt is not null && receipt.UserId == user.Id && receipt.Fingerprint == fingerprint)
                return Parse(receipt.Result);
            throw new AppException(ErrorCodes.Conflict, "Já existe um registro com esses dados. Atualize e tente novamente.");
        }
        catch (Exception error) when (MySqlErrors.IsTransient(error))
        {
            throw new AppException(ErrorCodes.Conflict, "Outra operação alterou os dados. Confira a ficha e tente novamente.");
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
