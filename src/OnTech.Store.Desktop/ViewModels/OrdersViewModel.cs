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

    public IReadOnlyList<string> ReceiptFilters { get; } = ["All orders", "With receipt", "Without receipt"];

    [ObservableProperty] private string? _search;
    [ObservableProperty] private string _receiptFilter = "All orders";
    [ObservableProperty] private OrderDto? _selected;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _totalCount;

    public string PageText => TotalCount == 0 ? "No orders" : $"Page {Page} of {TotalPages} · {TotalCount} orders";

    public override Task LoadAsync() => RunAsync(ReloadAsync, clearMessages: false);

    private async Task ReloadAsync()
    {
        var result = await Api.SearchOrdersAsync(new OrderQuery
        {
            Search = Search,
            WithReceipt = ReceiptFilter switch { "With receipt" => true, "Without receipt" => false, _ => null },
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

    partial void OnReceiptFilterChanged(string value) => _ = SearchAsync();

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
        var reason = Dialogs.Prompt($"Cancel {order.OrderNumber}",
            "Why is this order cancelled? Its items go back into stock.", okText: "Cancel order");
        if (string.IsNullOrWhiteSpace(reason)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var cancelled = await Api.CancelOrderAsync(order.Id, reason.Trim());
            await ReloadAsync();
            Selected = Orders.FirstOrDefault(o => o.Id == cancelled.Id);
            SuccessMessage = $"{cancelled.OrderNumber} was cancelled and its stock returned.";
        });
    }
}
