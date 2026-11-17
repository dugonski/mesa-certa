using Avalonia.Controls;
using Avalonia.Input;
using MesaCerta.Desktop.ViewModels;

namespace MesaCerta.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            // Esc fecha a janela de operação, como no diálogo original.
            if (e.Key == Key.Escape && DataContext is MainViewModel { Modal: not null, Busy: false } main)
            {
                main.CloseModal();
                e.Handled = true;
            }
        };
    }
}
