using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;
using MesaCerta.Shared.Client;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Desktop.ViewModels;

public static class Pages
{
    public const string Tabs = "tabs";
    public const string Cashier = "cashier";
    public const string History = "history";
    public const string Products = "products";
    public const string Users = "users";
    public const string Audit = "audit";
}

/// <summary>
/// Estado da aplicação: sessão, snapshot, atualização entre terminais, seleção de ficha,
/// comandos e mensagens. A sequência das consultas impede uma resposta antiga de
/// substituir uma mais recente.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private static readonly SnapshotDto Blank = new(new UserDto(Guid.Empty, "", "", Role.Attendant, false), [], [], [], [], [], []);
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4.5);

    private readonly RestaurantApi _api;
    private readonly IFileSaver? _files;
    private int _refreshSequence;
    private bool _authorized;
    private bool _locked;
    private int _toastSequence;

    private TableMapViewModel? _tableMap;
    private OrderWorkspaceViewModel? _order;
    private TabsListViewModel? _tabsList;

    public MainViewModel(RestaurantApi api, SettingsStore settings, IFileSaver? files = null)
    {
        _api = api;
        _files = files;
        Login = new LoginViewModel(this, settings);
        _api.EventReceived += name => Dispatcher.UIThread.Post(() => OnServerEvent(name));
    }

    public LoginViewModel Login { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn), nameof(UserName))]
    private UserDto? _user;

    [ObservableProperty] private SnapshotDto _data = Blank;
    [ObservableProperty] private string _page = Pages.Tabs;
    [ObservableProperty] private Guid? _selectedId;
    [ObservableProperty] private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAppError))]
    private DialogViewModel? _modal;

    [ObservableProperty] private bool _busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAppError), nameof(HasError))]
    private string _error = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private string _toast = "";

    [ObservableProperty] private object? _content;
    [ObservableProperty] private string _headingTitle = "";
    [ObservableProperty] private string? _recordId;
    [ObservableProperty] private bool _showBack;
    [ObservableProperty] private bool _showManageTables;
    [ObservableProperty] private bool _showNewProduct;
    [ObservableProperty] private bool _showNewUser;

    public ObservableCollection<NavItemViewModel> Navigation { get; } = [];

    public bool IsLoggedIn => User is not null;
    public string UserName => User?.Name ?? "";
    public bool HasError => Error.Length > 0;
    public bool ShowAppError => HasError && Modal is null;
    public bool HasToast => Toast.Length > 0;

    public TabDto? Selected =>
        SelectedId is { } id ? Data.Tabs.Concat(Data.History).FirstOrDefault(tab => tab.Id == id) : null;

    public bool Allowed(string permission) => Permissions.Can(User?.Role, permission);

    partial void OnBusyChanged(bool value) => UpdateView();
    partial void OnDataChanged(SnapshotDto value) => UpdateView();
    partial void OnPageChanged(string value) => UpdateView();
    partial void OnSelectedIdChanged(Guid? value) => UpdateView();
    partial void OnUserChanged(UserDto? value) => UpdateView();
    partial void OnModalChanged(DialogViewModel? value) => UpdateView();

    private void OnServerEvent(string name)
    {
        if (name == "disconnected")
        {
            _authorized = false;
            _refreshSequence++;
            ResetSession();
            SelectedId = null;
            Error = "Conexão interrompida. Entre novamente para consultar os dados atualizados.";
        }
        else if (name == "changed" && _authorized)
        {
            _ = RefreshAsync().ContinueWith(task => Dispatcher.UIThread.Post(() => HandleError(task.Exception!.InnerException!)),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    public async Task RefreshAsync()
    {
        var sequence = ++_refreshSequence;
        var snapshot = await _api.SnapshotAsync();
        if (_authorized && sequence == _refreshSequence) Data = snapshot;
    }

    private void HandleError(Exception error)
    {
        Error = error.Message;
        if (error is ServerException { Code: ErrorCodes.Unauthorized })
        {
            _authorized = false;
            ResetSession();
        }
    }

    private void ResetSession()
    {
        User = null;
        Data = Blank;
        Modal = null;
        _tableMap = null;
        _order = null;
        _tabsList = null;
    }

    /// <summary>Executa uma operação por vez, com indicador de ocupado e tratamento de erro.</summary>
    public async Task<bool> OperateAsync(Func<Task> work)
    {
        if (_locked) return false;
        _locked = true;
        Busy = true;
        Error = "";
        try
        {
            await work();
            return true;
        }
        catch (Exception error) when (error is ServerException or IOException)
        {
            HandleError(error);
            if (error is ServerException { Code: ErrorCodes.Conflict })
            {
                try
                {
                    await RefreshAsync();
                }
                catch (ServerException refreshError)
                {
                    HandleError(refreshError);
                }
            }
            return false;
        }
        finally
        {
            _locked = false;
            Busy = false;
        }
    }

    public Task<bool> MutateAsync(string action, object payload, string success) =>
        OperateAsync(async () =>
        {
            var result = await _api.CommandAsync<System.Text.Json.JsonElement>(action, payload);
            Modal = null;
            if (action == Commands.TabsOpen) SelectedId = result.GetProperty("id").GetGuid();
            await RefreshAsync();
            ShowToast(success);
        });

    public void ShowToast(string message)
    {
        Toast = message;
        var sequence = ++_toastSequence;
        DispatcherTimer.RunOnce(() =>
        {
            if (sequence == _toastSequence) Toast = "";
        }, ToastDuration);
    }

    [RelayCommand]
    private void Navigate(string next)
    {
        Page = next;
        Search = "";
        SelectedId = null;
        Error = "";
    }

    public void SelectTab(Guid? id) => SelectedId = id;

    public void GoToPage(string page) => Page = page;

    public void OpenModal(DialogViewModel modal)
    {
        Modal = modal;
    }

    public void CloseModal()
    {
        Modal = null;
        Error = "";
    }

    [RelayCommand]
    private void DismissError() => Error = "";

    [RelayCommand]
    private void Back() => SelectedId = null;

    [RelayCommand]
    private Task Refresh() => OperateAsync(RefreshAsync);

    [RelayCommand]
    private void ManageTables() => OpenModal(new TablesDialogViewModel(this));

    [RelayCommand]
    private void NewProduct() => OpenModal(new ProductDialogViewModel(this, null));

    [RelayCommand]
    private void NewUser() => OpenModal(new UserDialogViewModel(this));

    public Task<bool> ExportXmlAsync(Guid tabId) =>
        OperateAsync(async () =>
        {
            var export = await _api.ExportXmlAsync(tabId);
            if (_files is null) throw new IOException("Salvar arquivos não está disponível neste ambiente.");
            if (await _files.SaveTextAsync("Salvar XML da venda", export.Filename, export.Xml)) ShowToast("XML salvo.");
        });

    public Task<bool> LoginAsync(LoginInput input) =>
        OperateAsync(async () =>
        {
            var current = await _api.LoginAsync(input);
            _authorized = true;
            await RefreshAsync();
            User = current;
            Page = Pages.Tabs;
            SelectedId = null;
        });

    [RelayCommand]
    private Task Logout() =>
        OperateAsync(async () =>
        {
            try
            {
                await _api.LogoutAsync();
            }
            finally
            {
                _authorized = false;
                ResetSession();
            }
        });

    /// <summary>Recalcula navegação, cabeçalho, conteúdo e janela aberta a partir do estado.</summary>
    private void UpdateView()
    {
        OnPropertyChanged(nameof(Selected));
        if (User is null)
        {
            Content = null;
            Navigation.Clear();
            Login.Update();
            return;
        }

        var items = new (string Id, string Title, string Icon, string? Permission)[]
        {
            (Pages.Tabs, "Mesas", Icons.Grid, null),
            (Pages.Cashier, "Caixa", Icons.Card, Permissions.TabsPay),
            (Pages.History, "Histórico", Icons.Receipt, Permissions.HistoryRead),
            (Pages.Products, "Produtos", Icons.Package, Permissions.ProductsWrite),
            (Pages.Users, "Usuários", Icons.Users, Permissions.UsersWrite),
            (Pages.Audit, "Atividades", Icons.Shield, Permissions.AuditRead),
        }.Where(item => item.Permission is null || Allowed(item.Permission)).ToList();
        Navigation.Clear();
        foreach (var item in items)
            Navigation.Add(new NavItemViewModel(item.Id, item.Title, item.Icon, Page == item.Id, NavigateCommand));

        var selected = Selected;
        HeadingTitle = selected is not null
            ? $"{(Page == Pages.Cashier ? "Pagamento · " : "")}{selected.Label}"
            : items.FirstOrDefault(item => item.Id == Page).Title ?? "";
        RecordId = selected is not null ? $"Ficha #{selected.Number}" : null;
        ShowBack = selected is not null;
        ShowManageTables = selected is null && Page == Pages.Tabs && Allowed(Permissions.TablesWrite);
        ShowNewProduct = selected is null && Page == Pages.Products;
        ShowNewUser = selected is null && Page == Pages.Users;

        Content = BuildContent(selected);
        Modal?.Update();
    }

    private object BuildContent(TabDto? selected)
    {
        if (selected is not null && Page is Pages.Cashier or Pages.History)
            return new PaymentWorkspaceViewModel(this, selected);
        if (selected is not null)
        {
            if (_order?.TabId != selected.Id) _order = new OrderWorkspaceViewModel(this, selected.Id);
            _order.Update(selected, Data.Products);
            return _order;
        }
        _order = null;
        switch (Page)
        {
            case Pages.Tabs:
                _tableMap ??= new TableMapViewModel(this);
                _tableMap.Update();
                return _tableMap;
            case Pages.History or Pages.Cashier:
                _tabsList ??= new TabsListViewModel(this);
                _tabsList.Rebuild();
                return _tabsList;
            case Pages.Products:
                return new ProductsPageViewModel(this);
            case Pages.Users:
                return new UsersPageViewModel(this);
            default:
                return new AuditPageViewModel(this);
        }
    }
}

public sealed record NavItemViewModel(string Id, string Title, string Icon, bool IsActive, IRelayCommand<string> Command);
