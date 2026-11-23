using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Desktop.ViewModels;

/// <summary>Catálogo por categoria, composição do item e pedido da ficha selecionada.</summary>
public sealed partial class OrderWorkspaceViewModel(MainViewModel main, Guid tabId) : ViewModelBase
{
    private const string AllCategories = "Todos";
    private TabDto? _tab;
    private IReadOnlyList<ProductDto> _products = [];

    public Guid TabId { get; } = tabId;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _category = AllCategories;
    [ObservableProperty] private Guid? _selectedProductId;
    [ObservableProperty] private string _quantity = "1";
    [ObservableProperty] private string _weight = "";
    [ObservableProperty] private string _note = "";

    [ObservableProperty] private IReadOnlyList<ChipViewModel> _categories = [];
    [ObservableProperty] private IReadOnlyList<ProductCardViewModel> _cards = [];
    [ObservableProperty] private IReadOnlyList<OrderLineViewModel> _lines = [];
    [ObservableProperty] private ProductDto? _product;
    [ObservableProperty] private bool _canAdd;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private bool _canPay;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _isValid;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private TabStatus _status;

    public bool HasProduct => Product is not null;
    public bool IsWeighted => Product?.SaleType == SaleType.Weight;
    public bool IsUnit => Product is not null && !IsWeighted;
    public bool NeedsPrice => Product?.NeedsPrice == true;
    public bool CanConfigure => main.Allowed(Permissions.ProductsWrite);
    public bool CannotConfigure => !CanConfigure;
    public bool ProductsEmpty => Cards.Count == 0;
    public bool LinesEmpty => Lines.Count == 0;
    public bool HasLines => Lines.Count > 0;
    public bool CanSubmit => !Busy && IsValid;
    public bool InputsEnabled => !Busy;

    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnCategoryChanged(string value) => Rebuild();
    partial void OnSelectedProductIdChanged(Guid? value) => Rebuild();
    partial void OnQuantityChanged(string value) => Validate();
    partial void OnWeightChanged(string value) => Validate();

    partial void OnProductChanged(ProductDto? value)
    {
        OnPropertyChanged(nameof(HasProduct));
        OnPropertyChanged(nameof(IsWeighted));
        OnPropertyChanged(nameof(IsUnit));
        OnPropertyChanged(nameof(NeedsPrice));
    }

    partial void OnBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(InputsEnabled));
    }

    partial void OnIsValidChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    public void Update(TabDto tab, IReadOnlyList<ProductDto> products)
    {
        _tab = tab;
        _products = products;
        Busy = main.Busy;
        Status = tab.Status;
        CanAdd = tab.Status == TabStatus.Open && main.Allowed(Permissions.TabsAdd);
        CanEdit = tab.Status == TabStatus.Open && main.Allowed(Permissions.TabsEdit);
        CanPay = main.Allowed(Permissions.TabsPay);
        TotalText = Formatting.Money(tab.TotalCents);
        Lines = tab.Items.Select(item => new OrderLineViewModel(
            Formatting.Quantity(item),
            item.ProductName,
            item.SaleType == SaleType.Weight ? $"{Formatting.Money(item.UnitPriceCents)} / kg" : null,
            string.IsNullOrEmpty(item.Note) ? null : item.Note,
            Formatting.Money(Money.ItemTotalCents(item)),
            CanEdit,
            !main.Busy,
            new RelayCommand(() => main.OpenModal(new EditItemDialogViewModel(main, tab, item))),
            new RelayCommand(() => main.OpenModal(new RemoveItemDialogViewModel(main, tab, item))))).ToList();
        OnPropertyChanged(nameof(LinesEmpty));
        OnPropertyChanged(nameof(HasLines));
        Rebuild();
    }

    private void Rebuild()
    {
        var catalog = _products.Where(p => p.InCatalog).ToList();
        Categories = catalog.Select(p => p.Category).Distinct().Prepend(AllCategories)
            .Select(name => new ChipViewModel(name, 0, Category == name, new RelayCommand(() => Category = name)))
            .ToList();
        var term = Search.ToLower(Formatting.PtBr);
        Cards = catalog
            .Where(p => (Category == AllCategories || p.Category == Category) && p.Name.ToLower(Formatting.PtBr).Contains(term, StringComparison.Ordinal))
            .Select(p => new ProductCardViewModel(
                p.Name,
                p.NeedsPrice ? "Definir preço" : Formatting.Money(p.PriceCents),
                p.SaleType == SaleType.Weight ? " / kg" : " / un.",
                SelectedProductId == p.Id,
                !main.Busy,
                new RelayCommand(() => SelectProduct(p.Id))))
            .ToList();
        OnPropertyChanged(nameof(ProductsEmpty));
        Product = catalog.FirstOrDefault(p => p.Id == SelectedProductId);
        Validate();
    }

    private void SelectProduct(Guid id)
    {
        Quantity = "1";
        Weight = "";
        Note = "";
        SelectedProductId = id;
    }

    private (int Quantity, int? WeightGrams)? Measures()
    {
        if (Product is not { Active: true, PriceCents: > 0 }) return null;
        if (IsWeighted) return Catalog.KgToGrams(Weight) is { } grams ? (1, grams) : null;
        return int.TryParse(Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) && quantity is >= 1 and <= 99
            ? (quantity, null)
            : null;
    }

    private void Validate()
    {
        var measures = Measures();
        IsValid = measures is not null;
        PreviewText = measures is { } m && Product is { } product
            ? Formatting.Money(Money.ItemTotalCents(new PricedItem(product.SaleType, m.Quantity, m.WeightGrams, product.PriceCents)))
            : IsWeighted ? "Informe a quantidade em kg" : "Informe a quantidade";
    }

    [RelayCommand]
    private async Task Add()
    {
        if (_tab is null || Product is null || Measures() is not { } measures) return;
        var ok = await main.MutateAsync(Commands.TabsAdd, new
        {
            tabId = _tab.Id,
            version = _tab.Version,
            productId = Product.Id,
            quantity = measures.Quantity,
            weightGrams = measures.WeightGrams,
            note = Note,
        }, "Item adicionado.");
        if (ok)
        {
            Quantity = "1";
            Weight = "";
            Note = "";
        }
    }

    [RelayCommand]
    private void GoCashier() => main.GoToPage(Pages.Cashier);

    [RelayCommand]
    private void Configure()
    {
        if (Product is not null) main.OpenModal(new ProductDialogViewModel(main, Product));
    }
}

public sealed record ProductCardViewModel(string Name, string Price, string Unit, bool IsSelected, bool Enabled, IRelayCommand Command);

public sealed record OrderLineViewModel(
    string Quantity,
    string Name,
    string? PricePerKg,
    string? Note,
    string Total,
    bool CanEdit,
    bool Enabled,
    IRelayCommand EditCommand,
    IRelayCommand RemoveCommand)
{
    public bool HasPricePerKg => PricePerKg is not null;
    public bool HasNote => Note is not null;
}
