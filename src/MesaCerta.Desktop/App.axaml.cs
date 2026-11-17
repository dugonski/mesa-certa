using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MesaCerta.Desktop.Services;
using MesaCerta.Desktop.ViewModels;
using MesaCerta.Desktop.Views;

namespace MesaCerta.Desktop;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var api = new RestaurantApi();
            var window = new MainWindow();
            window.DataContext = new MainViewModel(api, new SettingsStore(), new StorageFileSaver(window));
            window.Closed += (_, _) => api.Dispose();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
