using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MesaCerta.Desktop.ViewModels;

namespace MesaCerta.Desktop;

/// <summary>Associa cada view model à view de mesmo nome (FooViewModel → FooView).</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        var name = data.GetType().FullName!.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"Tela não encontrada: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
