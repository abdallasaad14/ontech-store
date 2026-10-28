using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>Basic orders list with search, the receipt filter and paging. The full log screen comes in the next phase.</summary>
public sealed partial class OrdersViewModel(Session session) : PageViewModel(session)
{
    public ObservableCollection<OrderDto> Orders { get; } = new();
    public bool CanCancel => Session.Can(Permission.CancelOrders);

    // Shown in the current language; the position picks the filter (0 all, 1 with, 2 without).
    public IReadOnlyList<string> ReceiptFilters { get; } = [L.T("All orders"), L.T("With receipt"), L.T("Without receipt")];

    [ObservableProperty] private string? _search;
    [ObservableProperty] private int _receiptFilterIndex;
    [ObservableProperty] private OrderDto? _selected;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _totalCount;

    public string PageText => TotalCount == 0 ? L.T("No orders") : L.T("Page {0} of {1} · {2} orders", Page, TotalPages, TotalCount);

    public override Task LoadAsync() => RunAsync(ReloadAsync, clearMessages: false);

    private async Task ReloadAsync()
    {
        var result = await Api.SearchOrdersAsync(new OrderQuery
        {
            Search = Search,
            WithReceipt = ReceiptFilterIndex switch { 1 => true, 2 => false, _ => null },
            Page = Page,
            PageSize = 50,
        });
        var keep = Selected?.Id;
        Orders.Clear();
        foreach (var o in result.Items) Orders.Add(o);
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        OnPropertyChanged(nameof(PageText));
        Selected = Orders.FirstOrDefault(o => o.Id == keep);
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return RunAsync(ReloadAsync);
    }

    partial void OnReceiptFilterIndexChanged(int value) => _ = SearchAsync();

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
    private Task CancelOrderAsync(OrderDto? order)
    {
        if (order is null || order.Status == OrderStatus.Cancelled) return Task.CompletedTask;
        var reason = Dialogs.Prompt(L.T("Cancel {0}", order.OrderNumber),
            L.T("Why is this order cancelled? Its items go back into stock."), okText: L.T("Cancel order"));
        if (string.IsNullOrWhiteSpace(reason)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var cancelled = await Api.CancelOrderAsync(order.Id, reason.Trim());
            await ReloadAsync();
            Selected = Orders.FirstOrDefault(o => o.Id == cancelled.Id);
            SuccessMessage = L.T("{0} was cancelled and its stock returned.", cancelled.OrderNumber);
        });
    }
}
