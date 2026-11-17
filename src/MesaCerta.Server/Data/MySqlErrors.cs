using MySqlConnector;

namespace MesaCerta.Server.Data;

public static class MySqlErrors
{
    public const int DuplicateEntry = 1062;
    public const int LockWaitTimeout = 1205;
    public const int Deadlock = 1213;

    public static int? Number(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is MySqlException mysql) return mysql.Number;
        return null;
    }

    /// <summary>Conflito de serialização: a transação pode ser repetida.</summary>
    public static bool IsTransient(Exception error) => Number(error) is Deadlock or LockWaitTimeout;

    public static bool IsDuplicate(Exception error) => Number(error) is DuplicateEntry;
}
