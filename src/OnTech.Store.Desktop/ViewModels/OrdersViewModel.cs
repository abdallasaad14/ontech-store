using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>
/// An issue voucher log with search, a filter and paging. The Orders Log shows sales only (with the receipt filter);
/// the Demo page shows demos only (with the demo state filter) in one of its tabs. Both types share one number book,
/// so a number search that only matches the other type says where that voucher is.
/// </summary>
public sealed partial class OrdersViewModel(Session session, ShellViewModel shell, IssueType type = IssueType.Sale) : PageViewModel(session)
{
    public ObservableCollection<OrderDto> Orders { get; } = new();
    public IssueType Type { get; } = type;
    public bool IsDemoLog => Type == IssueType.Demo;
    public bool IsSaleLog => !IsDemoLog;
    public bool CanCancel => Session.Can(Permission.CancelOrders);
    /// <summary>Only an admin may fix an order saved with the wrong type.</summary>
    public bool CanChangeType => Session.User.IsAdmin && Type == IssueType.Sale;

    // Shown in the current language; the position picks the filter (0 all, 1 with, 2 without).
    public IReadOnlyList<string> ReceiptFilters { get; } = [L.T("All orders"), L.T("With receipt"), L.T("Without receipt")];

    [ObservableProperty] private string? _search;
    [ObservableProperty] private int _receiptFilterIndex;

    // Demo log only. 0 all, then DemoState + 1 (still out, nothing back, part back, all back).
    public IReadOnlyList<string> DemoStateFilters { get; } =
        [L.T("All demo vouchers"), L.T("Still out"), L.T("Nothing back yet"), L.T("Part back"), L.T("All back")];
    [ObservableProperty] private int _demoStateFilterIndex;
    partial void OnDemoStateFilterIndexChanged(int value) => _ = SearchAsync();

    /// <summary>Set when the search found nothing here but matches a voucher of the other type (same number book).</summary>
    [ObservableProperty] private string? _otherTypeHint;
    private string? _otherTypeSearch;
    [ObservableProperty] private OrderDto? _selected;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _totalCount;

    public string PageText => TotalCount == 0
        ? (IsDemoLog ? L.T("No demo vouchers") : L.T("No orders"))
        : IsDemoLog ? L.T("Page {0} of {1} · {2} demo vouchers", Page, TotalPages, TotalCount)
        : L.T("Page {0} of {1} · {2} orders", Page, TotalPages, TotalCount);

    /// <summary>Only this customer's vouchers.</summary>
    public PartyFilter Party => _party ??= new(() => _ = SearchAsync());
    private PartyFilter? _party;
    public string PartyLabel => L.T("Customer");
    public string PartyHint => L.T("Type the customer number or part of the name");
    public string PartyAllText => L.T("All customers");

    /// <summary>Over every page of the list; cancelled vouchers left out.</summary>
    [ObservableProperty] private string? _totalsText;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (!Party.IsLoaded)
            Party.SetAll((await Api.GetCustomersAsync(includeInactive: true)).Select(c => new PartyChoice(c.Id, c.CustomerNumber, c.CompanyName)));
        await ReloadAsync();
    }, clearMessages: false);

    private readonly LatestRequest _requests = new();

    private async Task ReloadAsync()
    {
        // Changing the filter or the page quickly cancels the search still running and drops its late answer.
        var ticket = _requests.Begin();
        var result = await Api.SearchOrdersAsync(new OrderQuery
        {
            Search = Search,
            WithReceipt = IsDemoLog ? null : ReceiptFilterIndex switch { 1 => true, 2 => false, _ => null },
            Type = Type,
            CustomerId = Party.Selected?.Id,
            DemoState = IsDemoLog && DemoStateFilterIndex > 0 ? (DemoState)(DemoStateFilterIndex - 1) : null,
            Page = Page,
            PageSize = 50,
        }, ticket.Token);
        if (!ticket.IsCurrent) return;
        var keep = Selected?.Id;
        Orders.Clear();
        foreach (var o in result.Items) Orders.Add(o);
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        OnPropertyChanged(nameof(PageText));
        TotalsText = result.Totals is { } t ? L.T("Total: {0} vouchers · {1} pieces (cancelled not counted)", t.Vouchers, t.Quantity) : null;
        Selected = Orders.FirstOrDefault(o => o.Id == keep);
        await CheckOtherTypeAsync(result.TotalCount, ticket);
    }

    /// <summary>Nothing found: look for the search text among the other type, so a demo number typed in the sales log isn't "missing".</summary>
    private async Task CheckOtherTypeAsync(int found, LatestRequest.Ticket ticket)
    {
        OtherTypeHint = null;
        _otherTypeSearch = null;
        var search = Search?.Trim();
        if (found > 0 || string.IsNullOrEmpty(search)) return;
        var other = IsDemoLog ? IssueType.Sale : IssueType.Demo;
        var match = await Api.SearchOrdersAsync(new OrderQuery { Search = search, Type = other, PageSize = 1 }, ticket.Token);
        if (!ticket.IsCurrent || match.TotalCount == 0) return;
        _otherTypeSearch = search;
        var first = match.Items[0].IssueVoucherNumber;
        OtherTypeHint = IsDemoLog
            ? L.T("No demo vouchers match. {0} is a sale voucher: it is in the Orders Log.", first)
            : L.T("No sales match. {0} is a demo voucher: it is on the Demo page.", first);
    }

    /// <summary>Opens the other log with the same search.</summary>
    [RelayCommand]
    private void OpenOtherType()
    {
        var search = _otherTypeSearch;
        if (search is null) return;
        if (IsDemoLog) shell.Open(ViewModels.Page.Orders, vm => ((OrdersViewModel)vm).Search = search);
        else shell.Open(ViewModels.Page.Demos, vm => ((DemosViewModel)vm).ShowIssues(search));
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
        if (order.Type == IssueType.Demo && order.ReturnedQuantity > 0)
        {
            ErrorMessage = L.T("Some of this demo has already come back, so it can't be cancelled. Cancel its demo return vouchers first.");
            return Task.CompletedTask;
        }
        var reason = Dialogs.Prompt(L.T("Cancel issue voucher {0}", order.IssueVoucherNumber),
            L.T("Why is this order cancelled? Its items go back into stock."), okText: L.T("Cancel order"));
        if (string.IsNullOrWhiteSpace(reason)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var cancelled = await Api.CancelOrderAsync(order.Id, reason.Trim());
            await ReloadAsync();
            Selected = Orders.FirstOrDefault(o => o.Id == cancelled.Id);
            SuccessMessage = L.T("Issue voucher {0} was cancelled and its stock returned.", cancelled.IssueVoucherNumber);
        });
    }

    /// <summary>Admin only: turns a sale into a demo, for orders saved with the wrong type. Never a demo into a sale. Stock does not change.</summary>
    [RelayCommand]
    private Task ChangeTypeAsync(OrderDto? order)
    {
        if (order is null || order.Status == OrderStatus.Cancelled || order.Type == IssueType.Demo) return Task.CompletedTask;
        var question = L.T("Mark issue voucher {0} as a demo? Its items will show as still with {1} until a demo return brings them back. Stock does not change.", order.IssueVoucherNumber, order.CustomerName);
        if (!Dialogs.Confirm(question)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var changed = await Api.ChangeOrderTypeAsync(order.Id, IssueType.Demo);
            await ReloadAsync();
            Selected = Orders.FirstOrDefault(o => o.Id == changed.Id);
            SuccessMessage = L.T("Issue voucher {0} is now a demo. It moved to the Demo page.", changed.IssueVoucherNumber);
        });
    }
}
