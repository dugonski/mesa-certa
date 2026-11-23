using System.Text.Json;
using MesaCerta.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MesaCerta.Server.Data;

public sealed class MesaCertaDbContext(DbContextOptions<MesaCertaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Tab> Tabs => Set<Tab>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<TabItem> TabItems => Set<TabItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HavePrecision(3);
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>().HavePrecision(3);
        builder.Properties<Role>().HaveConversion<WireEnumConverter<Role>>();
        builder.Properties<TabStatus>().HaveConversion<WireEnumConverter<TabStatus>>();
        builder.Properties<PaymentMethod>().HaveConversion<WireEnumConverter<PaymentMethod>>();
        builder.Properties<SaleType>().HaveConversion<WireEnumConverter<SaleType>>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(user => user.Username).IsUnique();
        model.Entity<Session>().HasIndex(session => session.TokenHash).IsUnique();
        model.Entity<Session>().HasOne(session => session.User).WithMany().HasForeignKey(session => session.UserId);

        model.Entity<Tab>(tab =>
        {
            tab.Property(t => t.Number).ValueGeneratedOnAdd();
            tab.Property(t => t.Number).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            tab.HasIndex(t => t.Number).IsUnique();
            tab.HasMany(t => t.Items).WithOne().HasForeignKey(item => item.TabId);
            tab.HasOne(t => t.Payment).WithOne().HasForeignKey<Payment>(payment => payment.TabId);
        });

        model.Entity<DiningTable>(table =>
        {
            table.HasKey(t => t.Number);
            table.Property(t => t.Number).ValueGeneratedNever();
        });

        model.Entity<AuditLog>().Property(log => log.Details).HasColumnType("json");
        model.Entity<CommandReceipt>().Property(receipt => receipt.Result).HasColumnType("json");
        model.Entity<CommandReceipt>().Property(receipt => receipt.Id).ValueGeneratedNever();
        model.Entity<Payment>().Property(payment => payment.Xml).HasColumnType("longtext");

        // Tabelas com o nome do modelo e colunas em camelCase, como no esquema original.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.ClrType.Name);
            foreach (var property in entity.GetProperties())
                property.SetColumnName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        value => value.HasValue && value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value,
        value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

    private sealed class WireEnumConverter<T>() : ValueConverter<T, string>(
        value => EnumNames.ToWire(value),
        value => EnumNames.ParseWire<T>(value)) where T : struct, Enum;
}
