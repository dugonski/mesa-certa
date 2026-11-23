using MesaCerta.Shared;

namespace MesaCerta.Server.Data;

// Mesmas tabelas e colunas do esquema MySQL existente (antes gerenciado pelo Prisma).

public sealed class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class Session
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Terminal { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public sealed class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int PriceCents { get; set; }
    public SaleType SaleType { get; set; } = SaleType.Unit;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class Tab
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Label { get; set; } = "";
    public TabStatus Status { get; set; } = TabStatus.Open;
    public int Version { get; set; }
    public Guid OpenedBy { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? TableNumber { get; set; }
    public List<TabItem> Items { get; set; } = [];
    public Payment? Payment { get; set; }
}

public sealed class DiningTable
{
    public int Number { get; set; }
    public bool Active { get; set; } = true;
    public Guid? ActiveTabId { get; set; }
}

public sealed class TabItem : IPricedItem
{
    public Guid Id { get; set; }
    public Guid TabId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int UnitPriceCents { get; set; }
    public int Quantity { get; set; }
    public SaleType SaleType { get; set; } = SaleType.Unit;
    public int? WeightGrams { get; set; }
    public string Note { get; set; } = "";
    public Guid AddedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid TabId { get; set; }
    public PaymentMethod Method { get; set; }
    public int AmountCents { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Xml { get; set; } = "";
}

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Details { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class CommandReceipt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Result { get; set; } = "null";
    public DateTime CreatedAt { get; set; }
}
