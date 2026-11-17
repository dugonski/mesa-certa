using System.Data;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Data;

/// <summary>Cria contextos e centraliza transações Serializable com repetição de conflitos transitórios.</summary>
public sealed class Database
{
    private const int Attempts = 3;
    private readonly DbContextOptions<MesaCertaDbContext> _options;

    public Database(string connectionString)
    {
        _options = new DbContextOptionsBuilder<MesaCertaDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)),
                mysql => mysql.CommandTimeout(10))
            .Options;
    }

    public MesaCertaDbContext CreateContext() => new(_options);

    public async Task<T> TransactionAsync<T>(Func<MesaCertaDbContext, Task<T>> work, IsolationLevel isolation = IsolationLevel.Serializable)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var db = CreateContext();
            try
            {
                await db.Database.OpenConnectionAsync();
                // Espera curta por bloqueios: o conflito é repetido ou devolvido ao terminal.
                await db.Database.ExecuteSqlRawAsync("SET SESSION innodb_lock_wait_timeout = 5");
                await using var transaction = await db.Database.BeginTransactionAsync(isolation);
                var result = await work(db);
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception error) when (MySqlErrors.IsTransient(error) && attempt < Attempts - 1)
            {
                // Outra transação venceu a disputa; repete do início com dados novos.
            }
        }
    }

    public Task TransactionAsync(Func<MesaCertaDbContext, Task> work) =>
        TransactionAsync(async db =>
        {
            await work(db);
            return true;
        });

    public async Task CheckConnectionAsync()
    {
        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync();
    }
}
