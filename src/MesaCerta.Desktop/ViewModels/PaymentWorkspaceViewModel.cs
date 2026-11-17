using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Desktop.ViewModels;

/// <summary>Conferência da conta e ações de caixa (pagar, salvar XML, encerrar).</summary>
public sealed partial class PaymentWorkspaceViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly TabDto _tab;

    public PaymentWorkspaceViewModel(MainViewModel main, TabDto tab)
    {
        _main = main;
        _tab = tab;
        Status = tab.Status;
        Lines = tab.Items.Select(item => new OrderLineViewModel(
            Formatting.Quantity(item), item.ProductName, null,
            string.IsNullOrEmpty(item.Note) ? null : item.Note,
            Formatting.Money(Money.ItemTotalCents(item)), false, false,
            new RelayCommand(() => { }), new RelayCommand(() => { }))).ToList();
        TotalText = Formatting.Money(tab.TotalCents);
        PaidInfo = tab.Payment is { } payment ? payment.Method == PaymentMethod.Pix ? "Pix" : "Cartão" : null;
        var busy = main.Busy;
        ShowPay = tab.Status == TabStatus.Open && main.Allowed(Permissions.TabsPay);
        PayEnabled = !busy && tab.Items.Count > 0;
        ShowExport = tab.Payment is not null && main.Allowed(Permissions.TabsPay);
        ShowClose = tab.Status == TabStatus.Paid && main.Allowed(Permissions.TabsClose);
        ShowBackToOrder = tab.Status == TabStatus.Open && main.Allowed(Permissions.TabsAdd);
        Enabled = !busy;
    }

    public TabStatus Status { get; }
    public IReadOnlyList<OrderLineViewModel> Lines { get; }
    public bool LinesEmpty => Lines.Count == 0;
    public string TotalText { get; }
    public string? PaidInfo { get; }
    public bool HasPaidInfo => PaidInfo is not null;
    public bool ShowPay { get; }
    public bool PayEnabled { get; }
    public bool ShowExport { get; }
    public bool ShowClose { get; }
    public bool ShowBackToOrder { get; }
    public bool Enabled { get; }

    [RelayCommand]
    private void Pay() => _main.OpenModal(new PayDialogViewModel(_main, _tab));

    [RelayCommand]
    private void Close() => _main.OpenModal(new CloseDialogViewModel(_main, _tab));

    [RelayCommand]
    private Task Export() => _main.ExportXmlAsync(_tab.Id);

    [RelayCommand]
    private void EditOrder() => _main.GoToPage(Pages.Tabs);
}
