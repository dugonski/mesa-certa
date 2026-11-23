using MesaCerta.Server.Data;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Modules.Users;

public static class CreateUser
{
    public static async Task<object> HandleAsync(CommandContext context)
    {
        var data = context.DataAs<CreateUserRequest>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = data.Name,
            Username = data.Username,
            Role = data.Role,
            PasswordHash = context.PasswordHash!,
            Active = true,
            CreatedAt = Clock.Now(),
        };
        context.Db.Users.Add(user);
        AuditTrail.Add(context.Db, context.User, context.Terminal, "USER_CREATED", user.Id.ToString(),
            new { username = user.Username, role = user.Role });
        await context.Db.SaveChangesAsync();
        return user.ToDto();
    }
}
