using Avalonia;
using Avalonia.Controls;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;

namespace MesaCerta.Desktop.Views;

/// <summary>Selo da ficha: Aberta (azul), Paga (amarelo) ou Encerrada (cinza).</summary>
public partial class StatusBadge : UserControl
{
    public static readonly StyledProperty<TabStatus> StatusProperty =
        AvaloniaProperty.Register<StatusBadge, TabStatus>(nameof(Status));

    public StatusBadge()
    {
        InitializeComponent();
        Render();
    }

    public TabStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty) Render();
    }

    private void Render()
    {
        Label.Text = Formatting.Status(Status);
        Badge.Classes.Set("open", Status == TabStatus.Open);
        Badge.Classes.Set("paid", Status == TabStatus.Paid);
    }
}
