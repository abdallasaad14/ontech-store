using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>
/// A receive voucher log with search and paging: purchases in the Stock In Log, demo returns in a tab of the Demo page.
/// The mirror of the orders log; both types share one number book.
/// </summary>
public sealed partial class StockInsViewModel(Session session, ShellViewModel shell, ReceiveType type = ReceiveType.Purchase) : PageViewModel(session)
{
    public ObservableCollection<StockInDto> StockIns { get; } = new();
    public ReceiveType Type { get; } = type;
    public bool IsDemoReturnLog => Type == ReceiveType.DemoReturn;
    public bool IsPurchaseLog => !IsDemoReturnLog;
    public bool CanCancel => Session.Can(Permission.CancelOrders);

    [ObservableProperty] private string? _search;

    /// <summary>Set when the search found nothing here but matches a voucher of the other type (same number book).</summary>
    [ObservableProperty] private string? _otherTypeHint;
    private string? _otherTypeSearch;
    [ObservableProperty] private StockInDto? _selected;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _totalCount;

    public string PageText => TotalCount == 0
        ? (IsDemoReturnLog ? L.T("No demo returns") : L.T("No stock-ins"))
        : IsDemoReturnLog ? L.T("Page {0} of {1} · {2} demo returns", Page, TotalPages, TotalCount)
        : L.T("Page {0} of {1} · {2} stock-ins", Page, TotalPages, TotalCount);

    /// <summary>Only this supplier's purchases, or this customer's demo returns.</summary>
    public PartyFilter Party => _party ??= new(() => _ = SearchAsync());
    private PartyFilter? _party;
    public string PartyLabel => IsDemoReturnLog ? L.T("Customer") : L.T("Supplier");
    public string PartyHint => IsDemoReturnLog ? L.T("Type the customer number or part of the name") : L.T("Type the supplier number or part of the name");
    public string PartyAllText => IsDemoReturnLog ? L.T("All customers") : L.T("All suppliers");
    public string PartyPlaceholder => IsDemoReturnLog ? L.T("Customer name or number") : L.T("Supplier name or number");
    public string SearchHint => IsDemoReturnLog ? L.T("Voucher, customer, item or notes") : L.T("Voucher, supplier invoice, supplier, item or notes");

    /// <summary>Over every page of the list; cancelled vouchers left out.</summary>
    [ObservableProperty] private string? _totalsText;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (!Party.IsLoaded)
            Party.SetAll(IsDemoReturnLog
                ? (await Api.GetCustomersAsync(includeInactive: true)).Select(c => new PartyChoice(c.Id, c.CustomerNumber, c.CompanyName))
                : (await Api.GetSuppliersAsync(includeInactive: true)).Select(s => new PartyChoice(s.Id, s.SupplierNumber, s.CompanyName)));
        await ReloadAsync();
    }, clearMessages: false);

    private readonly LatestRequest _requests = new();

    private async Task ReloadAsync()
    {
        var ticket = _requests.Begin();
        var result = await Api.SearchStockInsAsync(new StockInQuery
        {
            Search = Search,
            Type = Type,
            SupplierId = IsDemoReturnLog ? null : Party.Selected?.Id,
            CustomerId = IsDemoReturnLog ? Party.Selected?.Id : null,
            Page = Page,
            PageSize = 50,
        }, ticket.Token);
        if (!ticket.IsCurrent) return;
        var keep = Selected?.Id;
        StockIns.Clear();
        foreach (var s in result.Items) StockIns.Add(s);
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        OnPropertyChanged(nameof(PageText));
        TotalsText = result.Totals is { } t ? L.T("Total: {0} vouchers · {1} pieces", t.Vouchers, t.Quantity) : null;
        Selected = StockIns.FirstOrDefault(s => s.Id == keep);
        await CheckOtherTypeAsync(result.TotalCount, ticket);
    }

    /// <summary>Nothing found: look for the search text among the other type, so a demo return number typed in the purchase log isn't "missing".</summary>
    private async Task CheckOtherTypeAsync(int found, LatestRequest.Ticket ticket)
    {
        OtherTypeHint = null;
        _otherTypeSearch = null;
        var search = Search?.Trim();
        if (found > 0 || string.IsNullOrEmpty(search)) return;
        var other = IsDemoReturnLog ? ReceiveType.Purchase : ReceiveType.DemoReturn;
        var match = await Api.SearchStockInsAsync(new StockInQuery { Search = search, Type = other, PageSize = 1 }, ticket.Token);
        if (!ticket.IsCurrent || match.TotalCount == 0) return;
        _otherTypeSearch = search;
        var first = match.Items[0].VoucherNumber;
        OtherTypeHint = IsDemoReturnLog
            ? L.T("No demo returns match. {0} is a purchase voucher: it is in the Stock In Log.", first)
            : L.T("No purchases match. {0} is a demo return voucher: it is on the Demo page.", first);
    }

    /// <summary>Opens the other log with the same search.</summary>
    [RelayCommand]
    private void OpenOtherType()
    {
        var search = _otherTypeSearch;
        if (search is null) return;
        if (IsDemoReturnLog) shell.Open(ViewModels.Page.StockIns, vm => ((StockInsViewModel)vm).Search = search);
        else shell.Open(ViewModels.Page.Demos, vm => ((DemosViewModel)vm).ShowReturns(search));
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
