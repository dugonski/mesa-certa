using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;

namespace MesaCerta.Desktop.ViewModels;

/// <summary>Contas abertas (caixa) ou últimas fichas encerradas (histórico).</summary>
public sealed class TabsListViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public TabsListViewModel(MainViewModel main)
    {
        _main = main;
        Rebuild();
    }

    public bool IsCashier => _main.Page == Pages.Cashier;

    public string Search
    {
        get => _main.Search;
        set
        {
            if (_main.Search == value) return;
            _main.Search = value;
            Rebuild();
        }
    }

    public IReadOnlyList<CardViewModel> Cards { get; private set; } = [];
    public bool IsEmpty => Cards.Count == 0;

    public string CountText => IsCashier
        ? $"{_main.Data.Tabs.Count} contas abertas"
        : $"Últimas {_main.Data.History.Count} fichas";

    public void Rebuild()
    {
        var term = Search.ToLowerInvariant();
        Cards = (IsCashier ? _main.Data.Tabs : _main.Data.History)
            .Where(tab => $"{tab.Number} {tab.Label}".ToLowerInvariant().Contains(term, StringComparison.Ordinal))
            .Select(tab => new CardViewModel(
                tab.Label,
                $"Ficha #{tab.Number} · {(IsCashier ? tab.Status == TabStatus.Paid ? "Paga · encerrar" : "Em aberto" : Formatting.Date(tab.ClosedAt))}",
                Formatting.Money(tab.TotalCents),
                new RelayCommand(() => _main.SelectTab(tab.Id))))
            .ToList();
        OnPropertyChanged(nameof(Cards));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class ProductsPageViewModel(MainViewModel main) : ViewModelBase
{
    public IReadOnlyList<ProductRowViewModel> Rows { get; } = main.Data.Products.Select(product => new ProductRowViewModel(
        product.Name,
        product.Category,
        (product.PriceCents > 0 ? Formatting.Money(product.PriceCents) : "Definir preço") + (product.SaleType == SaleType.Weight ? " / kg" : " / un."),
        product.Active ? "Disponível" : "Inativo",
        product.Active,
        new RelayCommand(() => main.OpenModal(new ProductDialogViewModel(main, product))))).ToList();

    public bool IsEmpty => Rows.Count == 0;
}

public sealed record ProductRowViewModel(string Name, string Category, string Price, string Status, bool IsActive, IRelayCommand EditCommand)
{
    public bool IsInactive => !IsActive;
}

public sealed class UsersPageViewModel(MainViewModel main) : ViewModelBase
{
    public IReadOnlyList<UserRowViewModel> Rows { get; } = main.Data.Users.Select(user => new UserRowViewModel(
        user.Name, user.Username, Permissions.RoleLabels[user.Role], user.Active ? "Ativo" : "Inativo")).ToList();
}

public sealed record UserRowViewModel(string Name, string Username, string Role, string Status);

public sealed class AuditPageViewModel(MainViewModel main) : ViewModelBase
{
    public IReadOnlyList<AuditRowViewModel> Rows { get; } = main.Data.AuditLogs.Select(log => new AuditRowViewModel(
        Formatting.ActionLabels.GetValueOrDefault(log.Action, log.Action),
        log.Summary,
        log.UserName,
        log.Terminal,
        Formatting.Date(log.CreatedAt))).ToList();
}

public sealed record AuditRowViewModel(string Action, string Detail, string UserName, string Terminal, string Date);
