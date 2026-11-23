namespace MesaCerta.Shared;

/// <summary>Nomes do transporte e permissões exigidas, compartilhados entre desktop e servidor.</summary>
public static class Commands
{
    public const string Login = "login";
    public const string Logout = "logout";
    public const string Snapshot = "snapshot";
    public const string TabsOpen = "tabs.open";
    public const string TabsAdd = "tabs.add";
    public const string TabsUpdate = "tabs.update";
    public const string TabsRemove = "tabs.remove";
    public const string TabsPay = "tabs.pay";
    public const string TabsClose = "tabs.close";
    public const string TabsXml = "tabs.xml";
    public const string TablesConfigure = "tables.configure";
    public const string ProductsSave = "products.save";
    public const string UsersCreate = "users.create";

    public static readonly IReadOnlyDictionary<string, string> MutationPermissions = new Dictionary<string, string>
    {
        [TabsUpdate] = Permissions.TabsEdit,
        [TabsRemove] = Permissions.TabsEdit,
        [TablesConfigure] = Permissions.TablesWrite,
        [TabsOpen] = Permissions.TabsOpen,
        [TabsAdd] = Permissions.TabsAdd,
        [TabsPay] = Permissions.TabsPay,
        [TabsClose] = Permissions.TabsClose,
        [ProductsSave] = Permissions.ProductsWrite,
        [UsersCreate] = Permissions.UsersWrite,
    };

    /// <summary>Operações que a interface pode solicitar diretamente.</summary>
    public static readonly IReadOnlySet<string> DesktopActions =
        new HashSet<string>(MutationPermissions.Keys.Prepend(Snapshot));
}
