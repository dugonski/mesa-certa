using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules.Auth;

/// <summary>Login, sessão de 12 horas e proteção contra tentativas repetidas.</summary>
public sealed class AuthService(Database database, TimeProvider? time = null)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(1);
    private const int MaxAttempts = 10;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, (int Count, DateTime Until)> _attempts = new();

    public async Task<Session> AuthenticateAsync(string? token)
    {
        if (token is null) throw new AppException(ErrorCodes.Unauthorized, "Entre novamente para continuar.");
        var tokenHash = Identity.Hash(token);
        await using var db = database.CreateContext();
        var session = await db.Sessions.AsNoTracking().Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash);
        if (session is null || session.ExpiresAt < Clock.Now() || !session.User.Active)
            throw new AppException(ErrorCodes.Unauthorized, "Sua sessão expirou. Entre novamente.");
        return session;
    }

    public async Task<LoginResult> LoginAsync(LoginRequest data, string peer)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        lock (_attempts)
        {
            foreach (var key in _attempts.Where(pair => pair.Value.Until < now).Select(pair => pair.Key).ToList())
                _attempts.Remove(key);
            var entry = _attempts.TryGetValue(peer, out var known) ? known : (Count: 0, Until: now + AttemptWindow);
            if (entry.Count >= MaxAttempts)
                throw new AppException(ErrorCodes.RateLimit, "Muitas tentativas. Aguarde um minuto.");
            _attempts[peer] = (entry.Count + 1, entry.Until);
        }

        User? user;
        await using (var db = database.CreateContext())
            user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == data.Username);
        if (user is not { Active: true }
            || !await Task.Run(() => Identity.VerifyPassword(data.Password, user.PasswordHash)))
            throw new AppException(ErrorCodes.InvalidLogin, "Usuário ou senha incorretos.");

        lock (_attempts) _attempts.Remove(peer);
        var token = Identity.NewToken();
        await database.TransactionAsync(async tx =>
        {
            var current = Clock.Now();
            await tx.Sessions.Where(s => s.ExpiresAt < current).ExecuteDeleteAsync();
            tx.Sessions.Add(new Session
            {
                Id = Guid.NewGuid(),
                TokenHash = Identity.Hash(token),
                UserId = user.Id,
                Terminal = data.Terminal,
                ExpiresAt = current + SessionLifetime,
            });
            AuditTrail.Add(tx, user, data.Terminal, "LOGIN", user.Id.ToString());
            await tx.SaveChangesAsync();
        });
        return new LoginResult(token, user.ToDto());
    }

    public async Task<LogoutResult> LogoutAsync(Session session)
    {
        await database.TransactionAsync(async tx =>
        {
            await tx.Sessions.Where(s => s.Id == session.Id).ExecuteDeleteAsync();
            AuditTrail.Add(tx, session.User, session.Terminal, "LOGOUT", session.User.Id.ToString());
            await tx.SaveChangesAsync();
        });
        return new LogoutResult(true);
    }
}
