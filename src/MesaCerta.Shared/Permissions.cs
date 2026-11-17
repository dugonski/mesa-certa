namespace MesaCerta.Shared;

public static class Permissions
{
    public const string TabsRead = "tabs.read";
    public const string TabsOpen = "tabs.open";
    public const string TabsAdd = "tabs.add";
    public const string TabsEdit = "tabs.edit";
    public const string TablesWrite = "tables.write";
    public const string TabsPay = "tabs.pay";
    public const string TabsClose = "tabs.close";
    public const string HistoryRead = "history.read";
    public const string ProductsWrite = "products.write";
    public const string UsersWrite = "users.write";
    public const string AuditRead = "audit.read";

    public static readonly IReadOnlyDictionary<Role, IReadOnlySet<string>> ByRole =
        new Dictionary<Role, IReadOnlySet<string>>
        {
            [Role.Admin] = new HashSet<string>
            {
                TabsRead, TabsOpen, TabsAdd, TabsEdit, TablesWrite, TabsPay, TabsClose,
                HistoryRead, ProductsWrite, UsersWrite, AuditRead,
            },
            [Role.Attendant] = new HashSet<string> { TabsRead, TabsOpen, TabsAdd, TabsEdit },
            [Role.Cashier] = new HashSet<string>
            {
                TabsRead, TabsOpen, TabsAdd, TabsEdit, TabsPay, TabsClose, HistoryRead,
            },
        };

    public static bool Can(Role? role, string permission) =>
        role is { } value && ByRole.TryGetValue(value, out var granted) && granted.Contains(permission);

    public static readonly IReadOnlyDictionary<Role, string> RoleLabels = new Dictionary<Role, string>
    {
        [Role.Admin] = "Administrador",
        [Role.Attendant] = "Atendente",
        [Role.Cashier] = "Caixa",
    };
}
