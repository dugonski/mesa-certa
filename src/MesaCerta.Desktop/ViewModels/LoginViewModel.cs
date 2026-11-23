using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;

namespace MesaCerta.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly SettingsStore _settings;

    public LoginViewModel(MainViewModel main, SettingsStore settings)
    {
        _main = main;
        _settings = settings;
        var saved = settings.Load();
        _host = saved.Host;
        _port = saved.Port.ToString(CultureInfo.InvariantCulture);
        _terminal = saved.Terminal;
    }

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _terminal;
    [ObservableProperty] private string _host;
    [ObservableProperty] private string _port;
    [ObservableProperty] private bool _showConnection;

    public string Error => _main.Error;
    public bool HasError => _main.HasError;
    public bool Busy => _main.Busy;
    public string SubmitLabel => _main.Busy ? "Conectando…" : "Entrar no sistema";

    public void Update()
    {
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Busy));
        OnPropertyChanged(nameof(SubmitLabel));
    }

    [RelayCommand]
    private void ToggleConnection() => ShowConnection = !ShowConnection;

    [RelayCommand]
    private async Task Submit()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password) || string.IsNullOrWhiteSpace(Terminal))
        {
            _main.Error = "Informe usuário, senha e identificação do terminal.";
            Update();
            return;
        }
        if (!int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            _main.Error = "Endereço ou porta inválidos.";
            ShowConnection = true;
            Update();
            return;
        }
        _settings.Save(new ConnectionSettings(Host.Trim(), port, Terminal.Trim()));
        var ok = await _main.LoginAsync(new LoginInput(Host.Trim(), port, Username.Trim(), Password, Terminal.Trim()));
        if (ok) Password = "";
        Update();
    }
}
