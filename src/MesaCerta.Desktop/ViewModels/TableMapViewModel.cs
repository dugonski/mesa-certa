using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MesaCerta.Desktop.Services;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Desktop.ViewModels;

public sealed partial class TableMapViewModel(MainViewModel main) : ViewModelBase
{
    private static readonly (string Value, string Label)[] Filters =
        [("ALL", "Todas"), ("FREE", "Livres"), ("OPEN", "Ocupadas"), ("PAID", "Pagas")];

    [ObservableProperty] private string _filter = "ALL";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private IReadOnlyList<ChipViewModel> _chips = [];
    [ObservableProperty] private IReadOnlyList<TableTileViewModel> _tiles = [];
    [ObservableProperty] private IReadOnlyList<CardViewModel> _previous = [];
    [ObservableProperty] private bool _isEmpty;

    public bool HasPrevious => Previous.Count > 0;

    partial void OnFilterChanged(string value) => Update();
    partial void OnSearchChanged(string value) => Update();
    partial void OnPreviousChanged(IReadOnlyList<CardViewModel> value) => OnPropertyChanged(nameof(HasPrevious));

    public void Update()
    {
        var data = main.Data;
        var byId = data.Tabs.ToDictionary(tab => tab.Id);
        string State(DiningTableDto table) =>
            table.ActiveTabId is { } id
                ? byId.TryGetValue(id, out var tab) ? EnumNames.ToWire(tab.Status) : "OPEN"
                : "FREE";

        var canOpen = main.Allowed(Permissions.TabsOpen);
        var term = Search.Trim();
        Chips = Filters.Select(f => new ChipViewModel(
            f.Label,
            f.Value == "ALL" ? data.Tables.Count : data.Tables.Count(t => State(t) == f.Value),
            Filter == f.Value,
            new RelayCommand(() => Filter = f.Value))).ToList();

        Tiles = data.Tables
            .Where(t => (Filter == "ALL" || State(t) == Filter) && t.Number.ToString(Formatting.PtBr).Contains(term, StringComparison.Ordinal))
            .Select(table =>
            {
                var tab = table.ActiveTabId is { } id ? byId.GetValueOrDefault(id) : null;
                var state = State(table);
                return new TableTileViewModel(
                    Formatting.Number(table.Number),
                    state switch { "FREE" => "Livre", "PAID" => "Paga", _ => "Ocupada" },
                    state,
                    tab is null ? null : Formatting.Money(tab.TotalCents),
                    Enabled: !main.Busy && (table.ActiveTabId is not null || canOpen),
                    new AsyncRelayCommand(async () =>
                    {
                        if (tab is not null) main.SelectTab(tab.Id);
                        else await main.MutateAsync(Commands.TabsOpen, new { tableNumber = table.Number }, "Mesa aberta.");
                    }),
                    $"Mesa {table.Number} · {state}");
            }).ToList();
        IsEmpty = Tiles.Count == 0;

        Previous = data.Tabs.Where(tab => tab.TableNumber is null)
            .Select(tab => new CardViewModel(tab.Label, $"Ficha #{tab.Number}", null, new RelayCommand(() => main.SelectTab(tab.Id))))
            .ToList();
    }
}

public sealed record ChipViewModel(string Label, int Count, bool IsSelected, IRelayCommand Command);

public sealed record TableTileViewModel(
    string Number,
    string StatusLabel,
    string State,
    string? Amount,
    bool Enabled,
    IAsyncRelayCommand Command,
    string AccessibleName)
{
    public bool IsFree => State == "FREE";
    public bool IsOpen => State == "OPEN";
    public bool IsPaid => State == "PAID";
    public bool HasAmount => Amount is not null;
}

public sealed record CardViewModel(string Title, string Subtitle, string? Amount, IRelayCommand Command)
{
    public bool HasAmount => Amount is not null;
}
