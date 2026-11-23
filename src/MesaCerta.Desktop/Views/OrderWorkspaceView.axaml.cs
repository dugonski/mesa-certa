using Avalonia.Controls;
using MesaCerta.Desktop.ViewModels;

namespace MesaCerta.Desktop.Views;

public partial class OrderWorkspaceView : UserControl
{
    public OrderWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyLayout();
    }

    /// <summary>Sem permissão de lançamento, o pedido ocupa uma coluna central (somente conferência).</summary>
    private void ApplyLayout()
    {
        if (DataContext is not OrderWorkspaceViewModel model) return;
        void Apply()
        {
            var reviewOnly = !model.CanAdd;
            Grid.SetColumn(OrderPanel, reviewOnly ? 0 : 2);
            Grid.SetColumnSpan(OrderPanel, reviewOnly ? 3 : 1);
            OrderPanel.MaxWidth = reviewOnly ? 950 : double.PositiveInfinity;
        }
        Apply();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrderWorkspaceViewModel.CanAdd)) Apply();
        };
    }
}
