using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>The stock-in log: purchases from suppliers and demo returns from customers, with search, a type filter and paging. The mirror of the orders log.</summary>
public sealed partial class StockInsViewModel(Session session) : PageViewModel(session)
{
    public ObservableCollection<StockInDto> StockIns { get; } = new();
    public bool CanCancel => Session.Can(Permission.CancelOrders);

    [ObservableProperty] private string? _search;

    // 0 all, 1 purchases, 2 demo returns.
    public IReadOnlyList<string> TypeFilters { get; } = [L.T("Purchases and demo returns"), L.T("Purchases only"), L.T("Demo returns only")];
    [ObservableProperty] private int _typeFilterIndex;
    partial void OnTypeFilterIndexChanged(int value) => _ = SearchAsync();
    [ObservableProperty] private StockInDto? _selected;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _totalCount;

    public string PageText => TotalCount == 0 ? L.T("No stock-ins") : L.T("Page {0} of {1} · {2} stock-ins", Page, TotalPages, TotalCount);

    public override Task LoadAsync() => RunAsync(ReloadAsync, clearMessages: false);

    private async Task ReloadAsync()
    {
        var result = await Api.SearchStockInsAsync(new StockInQuery
        {
            Search = Search,
            Type = TypeFilterIndex switch { 1 => ReceiveType.Purchase, 2 => ReceiveType.DemoReturn, _ => null },
            Page = Page,
            PageSize = 50,
        });
        var keep = Selected?.Id;
        StockIns.Clear();
        foreach (var s in result.Items) StockIns.Add(s);
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        OnPropertyChanged(nameof(PageText));
        Selected = StockIns.FirstOrDefault(s => s.Id == keep);
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return RunAsync(ReloadAsync);
    }

    [RelayCommand]
    private Task NextPageAsync()
    {
        if (Page >= TotalPages) return Task.CompletedTask;
        Page++;
        return RunAsync(ReloadAsync);
    }

    [RelayCommand]
    private Task PreviousPageAsync()
    {
        if (Page <= 1) return Task.CompletedTask;
        Page--;
        return RunAsync(ReloadAsync);
    }

    [RelayCommand]
    private Task CancelStockInAsync(StockInDto? stockIn)
    {
        if (stockIn is null || stockIn.Status == OrderStatus.Cancelled) return Task.CompletedTask;
        var reason = Dialogs.Prompt(L.T("Cancel {0}", stockIn.StockInNumber),
            stockIn.Type == ReceiveType.DemoReturn
                ? L.T("Why is this demo return cancelled? Its items are taken back out of stock and show as with the customer again.")
                : L.T("Why is this stock-in cancelled? Its items are taken back out of stock."), okText: L.T("Cancel stock-in"));
        if (string.IsNullOrWhiteSpace(reason)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var cancelled = await Api.CancelStockInAsync(stockIn.Id, reason.Trim());
            await ReloadAsync();
            Selected = StockIns.FirstOrDefault(s => s.Id == cancelled.Id);
            SuccessMessage = L.T("{0} was cancelled and its stock taken back out.", cancelled.StockInNumber);
        });
    }
}
