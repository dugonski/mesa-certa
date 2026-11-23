using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Desktop.ViewModels;

/// <summary>
/// Janela de operação. Quando a ficha muda em outro terminal enquanto a janela está aberta,
/// o envio fica bloqueado até o usuário conferir os dados atualizados.
/// </summary>
public abstract partial class DialogViewModel(MainViewModel main, TabDto? tab = null) : ViewModelBase
{
    protected MainViewModel Main { get; } = main;
    protected TabDto? Tab { get; } = tab;

    public abstract string Title { get; }
    public abstract string SubmitText { get; }

    public string SubmitLabel => Main.Busy ? "Salvando…" : SubmitText;
    public bool Busy => Main.Busy;
    public bool NotBusy => !Main.Busy;
    public string Error => Main.Error;
    public bool HasError => Main.HasError;
    public bool IsStale => Tab is not null && Main.Selected?.Version != Tab.Version;
    public bool CanSubmit => !Main.Busy && !IsStale;

    public void Update()
    {
        foreach (var name in new[] { nameof(SubmitLabel), nameof(Busy), nameof(NotBusy), nameof(Error), nameof(HasError), nameof(IsStale), nameof(CanSubmit) })
            OnPropertyChanged(name);
    }

    protected void Fail(string message)
    {
        Main.Error = message;
        Update();
    }

    protected string Summary => Tab is null ? "" : $"Ficha #{Tab.Number} · {Tab.Label}";
    protected string Total => Tab is null ? "" : Formatting.Money(Tab.TotalCents);

    [RelayCommand]
    private void Cancel()
    {
        if (!Main.Busy) Main.CloseModal();
    }

    [RelayCommand]
    private async Task Submit()
    {
        if (!CanSubmit) return;
        await SubmitAsync();
        Update();
    }

    protected abstract Task SubmitAsync();
}

public sealed partial class TablesDialogViewModel(MainViewModel main) : DialogViewModel(main)
{
    public override string Title => "Quantidade de mesas";
    public override string SubmitText => "Salvar";

    [ObservableProperty] private string _count = main.Data.Tables.Count.ToString(CultureInfo.InvariantCulture);

    protected override Task SubmitAsync()
    {
        if (int.TryParse(Count, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is >= 1 and <= 1000)
            return Main.MutateAsync(Commands.TablesConfigure, new { count }, "Mesas atualizadas.");
        Fail("Informe uma quantidade de 1 a 1.000 mesas.");
        return Task.CompletedTask;
    }
}

public sealed partial class PayDialogViewModel(MainViewModel main, TabDto tab) : DialogViewModel(main, tab)
{
    public override string Title => "Conferir pagamento";
    public override string SubmitText => "Registrar";
    public string SummaryText => Summary;
    public string TotalText => Total;
    public string ItemsText => $"{Tab!.Items.Sum(item => item.Quantity)} itens conferidos";

    [ObservableProperty] private bool _isPix;
    [ObservableProperty] private bool _isCard;

    protected override Task SubmitAsync()
    {
        if (!IsPix && !IsCard)
        {
            Fail("Escolha a forma de pagamento.");
            return Task.CompletedTask;
        }
        return Main.MutateAsync(Commands.TabsPay,
            new { tabId = Tab!.Id, version = Tab!.Version, method = IsPix ? PaymentMethod.Pix : PaymentMethod.Card },
            "Pagamento registrado.");
    }
}

public sealed class CloseDialogViewModel(MainViewModel main, TabDto tab) : DialogViewModel(main, tab)
{
    public override string Title => "Encerrar ficha";
    public override string SubmitText => "Encerrar ficha";
    public string SummaryText => Summary;
    public string TotalText => Total;

    protected override Task SubmitAsync() =>
        Main.MutateAsync(Commands.TabsClose, new { tabId = Tab!.Id, version = Tab!.Version }, "Ficha encerrada.");
}

public sealed partial class ProductDialogViewModel : DialogViewModel
{
    private readonly ProductDto? _product;

    public ProductDialogViewModel(MainViewModel main, ProductDto? product) : base(main)
    {
        _product = product;
        _name = product?.Name ?? "";
        _saleTypeIndex = product?.SaleType == SaleType.Weight ? 1 : 0;
        _price = product is { PriceCents: > 0 } ? (product.PriceCents / 100m).ToString("0.00", Formatting.PtBr) : "";
        // Um buffet aguardando preço já vem marcado como disponível.
        _active = product?.PriceCents == 0 || (product?.Active ?? true);
    }

    public override string Title => _product is null ? "Novo produto" : "Editar produto";
    public override string SubmitText => "Salvar produto";

    [ObservableProperty] private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceLabel))]
    private int _saleTypeIndex;

    [ObservableProperty] private string _price;
    [ObservableProperty] private bool _active;

    public IReadOnlyList<string> SaleTypes { get; } = ["Bebida · por unidade", "Buffet · por peso"];
    public string PriceLabel => SaleTypeIndex == 1 ? "Preço por kg (R$)" : "Preço por unidade (R$)";

    protected override Task SubmitAsync()
    {
        var name = Name.Trim();
        if (name.Length is < 1 or > 100)
        {
            Fail("Informe o nome do produto (até 100 caracteres).");
            return Task.CompletedTask;
        }
        if (Formatting.ParsePriceCents(Price) is not { } priceCents)
        {
            Fail("Informe um preço entre R$ 0,01 e R$ 10.000,00.");
            return Task.CompletedTask;
        }
        var weighted = SaleTypeIndex == 1;
        return Main.MutateAsync(Commands.ProductsSave, new SaveProductRequest
        {
            Id = _product?.Id,
            Name = name,
            Category = weighted ? "Buffet" : "Bebidas",
            SaleType = weighted ? SaleType.Weight : SaleType.Unit,
            PriceCents = priceCents,
            Active = Active,
        }, "Produto salvo.");
    }
}

public sealed partial class UserDialogViewModel(MainViewModel main) : DialogViewModel(main)
{
    public override string Title => "Novo usuário";
    public override string SubmitText => "Criar usuário";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private int _roleIndex = -1;

    public IReadOnlyList<string> Roles { get; } = [.. Permissions.RoleLabels.Values];

    protected override Task SubmitAsync()
    {
        var name = Name.Trim();
        var username = Username.Trim();
        if (name.Length is < 2 or > 100) Fail("Informe o nome (2 a 100 caracteres).");
        else if (!Rules.UsernamePattern().IsMatch(username)) Fail("Usuário: 3 a 60 letras, números, ponto, hífen ou sublinhado.");
        else if (Password.Length is < 8 or > 72) Fail("A senha inicial deve ter de 8 a 72 caracteres.");
        else if (RoleIndex < 0) Fail("Selecione um perfil.");
        else
            return Main.MutateAsync(Commands.UsersCreate, new
            {
                name,
                username,
                password = Password,
                role = Permissions.RoleLabels.Keys.ElementAt(RoleIndex),
            }, "Usuário cadastrado.");
        return Task.CompletedTask;
    }
}

public sealed partial class EditItemDialogViewModel(MainViewModel main, TabDto tab, TabItemDto item) : DialogViewModel(main, tab)
{
    public override string Title => "Editar item";
    public override string SubmitText => "Salvar";
    public string ProductName => item.ProductName;
    public bool IsWeighted => item.SaleType == SaleType.Weight;
    public bool IsUnit => !IsWeighted;

    [ObservableProperty] private string _quantity = item.Quantity.ToString(CultureInfo.InvariantCulture);
    [ObservableProperty] private string _weight = item.WeightGrams is { } grams ? Catalog.FormatKg(grams) : "";
    [ObservableProperty] private string _note = item.Note;

    protected override Task SubmitAsync()
    {
        int quantity = 1;
        int? weightGrams = null;
        if (IsWeighted)
        {
            weightGrams = Catalog.KgToGrams(Weight);
            if (weightGrams is null)
            {
                Fail("Informe o peso em kg, com até três casas decimais (ex.: 0,350).");
                return Task.CompletedTask;
            }
        }
        else if (!int.TryParse(Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity is < 1 or > 99)
        {
            Fail("Informe uma quantidade de 1 a 99.");
            return Task.CompletedTask;
        }
        if (Note.Length > 240)
        {
            Fail("A observação aceita até 240 caracteres.");
            return Task.CompletedTask;
        }
        return Main.MutateAsync(Commands.TabsUpdate,
            new { tabId = Tab!.Id, version = Tab!.Version, itemId = item.Id, quantity, weightGrams, note = Note },
            "Item atualizado.");
    }
}

public sealed class RemoveItemDialogViewModel(MainViewModel main, TabDto tab, TabItemDto item) : DialogViewModel(main, tab)
{
    public override string Title => "Remover item";
    public override string SubmitText => "Remover";
    public string ProductName => item.ProductName;

    protected override Task SubmitAsync() =>
        Main.MutateAsync(Commands.TabsRemove, new { tabId = Tab!.Id, version = Tab!.Version, itemId = item.Id }, "Item removido.");
}
