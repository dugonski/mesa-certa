using MesaCerta.Server.Data;
using MesaCerta.Server.Modules.Auth;
using MesaCerta.Shared;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Maintenance;

/// <summary>Usuários e bebidas de demonstração. Nunca troca senhas nem duplica registros existentes.</summary>
public static class Seed
{
    private static readonly (string Username, string Name, Role Role, string Variable)[] Users =
    [
        ("admin", "Administrador", Role.Admin, "SEED_ADMIN_PASSWORD"),
        ("atendente", "Atendente", Role.Attendant, "SEED_ATTENDANT_PASSWORD"),
        ("caixa", "Caixa", Role.Cashier, "SEED_CASHIER_PASSWORD"),
    ];

    private static readonly (string Name, string Category, int PriceCents)[] Products =
    [
        ("Suco de laranja", "Bebidas", 950),
        ("Água mineral", "Bebidas", 500),
        ("Refrigerante lata", "Bebidas", 650),
        ("Café expresso", "Bebidas", 600),
    ];

    public static async Task RunAsync(Database database, IReadOnlyDictionary<string, string?> env, TextWriter output)
    {
        var missing = new List<(string Username, string Name, Role Role, string Password)>();
        await using (var db = database.CreateContext())
        {
            foreach (var user in Users)
            {
                if (await db.Users.AnyAsync(u => u.Username == user.Username)) continue;
                var password = env.GetValueOrDefault(user.Variable);
                if (password is null || password.Length < 8 || password.Length > 72)
                    throw new InvalidOperationException($"Defina a senha de {user.Username} no .env (8 a 72 caracteres).");
                missing.Add((user.Username, user.Name, user.Role, password));
            }
        }
        var hashed = missing.Select(user => (user.Username, user.Name, user.Role, Hash: Identity.HashPassword(user.Password))).ToList();

        await database.TransactionAsync(async tx =>
        {
            foreach (var user in hashed)
                if (!await tx.Users.AnyAsync(u => u.Username == user.Username))
                    tx.Users.Add(new User
                    {
                        Id = Guid.NewGuid(),
                        Username = user.Username,
                        Name = user.Name,
                        Role = user.Role,
                        PasswordHash = user.Hash,
                        Active = true,
                        CreatedAt = Clock.Now(),
                    });
            foreach (var product in Products)
                if (!await tx.Products.AnyAsync(p => p.Name == product.Name))
                    tx.Products.Add(new Product
                    {
                        Id = Guid.NewGuid(),
                        Name = product.Name,
                        Category = product.Category,
                        PriceCents = product.PriceCents,
                        SaleType = SaleType.Unit,
                        Active = true,
                        CreatedAt = Clock.Now(),
                    });
            await tx.SaveChangesAsync();
        });
        output.WriteLine("Usuários e produtos de demonstração preparados. Registros existentes foram preservados.");
    }
}
