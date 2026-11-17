namespace MesaCerta.Server.Data;

public static class Clock
{
    /// <summary>Hora UTC truncada em milissegundos, mesma precisão das colunas DATETIME(3).</summary>
    public static DateTime Now()
    {
        var ticks = DateTime.UtcNow.Ticks;
        return new DateTime(ticks - ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
