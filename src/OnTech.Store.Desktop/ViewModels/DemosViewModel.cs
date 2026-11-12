using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>One demo line still out, as the "With customers now" tab shows it.</summary>
public sealed class OpenDemoRow(OpenDemoLineDto line, string group)
{
    public OpenDemoLineDto Line { get; } = line;
    /// <summary>The group header: the customer or the item, with how many units of it are out.</summary>
    public string Group { get; } = group;
    public string Item => Line.ItemCode is { Length: > 0 } code ? $"{Line.ItemName} ({code})" : Line.ItemName;
    public string Customer => Line.CustomerNumber is { } n ? $"{n} · {Line.CustomerName}" : Line.CustomerName;
    /// <summary>Whole days since it went out (0 = today).</summary>
    public int DaysOut => Math.Max(0, (DateTime.Today - Line.CreatedAt.LocalDateTime.Date).Days);
}

/// <summary>
/// The Demo page: everything about demos in one place, out of the sales and purchase logs.
/// Tab 0 "With customers now" lists what is still out (by customer or by item); tab 1 is the demo issue voucher log;
/// tab 2 the demo return log. The buttons on top open New Order and Stock In fixed to a demo and a demo return.
/// Numbers still come from the same issue and receipt voucher books as sales and purchases.
/// </summary>
public sealed partial class DemosViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;

    public DemosViewModel(Session session, ShellViewModel shell) : base(session)
    {
        _shell = shell;
        if (CanSeeLogs)
        {
            Issues = new OrdersViewModel(session, shell, IssueType.Demo);
            Returns = new StockInsViewModel(session, shell, ReceiveType.DemoReturn);
        }
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OpenDemoRow.Group)));
    }

    // ---- What the user may do ----

    /// <summary>What is still out: GET api/demos/open needs AddStock or ViewOrders.</summary>
    public bool CanSeeOpen => Session.Can(Permission.AddStock) || Session.Can(Permission.ViewOrders);
    public bool CanSeeLogs => Session.Can(Permission.ViewOrders);
    public bool CanCreateDemo => Session.Can(Permission.CreateOrders);
    public bool CanReturn => Session.Can(Permission.AddStock);
    public bool CanOpenCard => ItemCards.CanOpen(Session);
    public bool CannotSeeOpen => !CanSeeOpen;

    /// <summary>The demo issue voucher log (tab 1). Null without ViewOrders.</summary>
    public OrdersViewModel? Issues { get; }
    /// <summary>The demo return log (tab 2). Null without ViewOrders.</summary>
    public StockInsViewModel? Returns { get; }

    // ---- Tabs ----

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOpenTab), nameof(IsIssuesTab), nameof(IsReturnsTab))]
    private int _selectedTab;
    private bool _issuesLoaded, _returnsLoaded;

    partial void OnSelectedTabChanged(int value) => _ = LoadTabAsync(value);

    // For the tab buttons (radio buttons): each sets the tab when picked.
    public bool IsOpenTab { get => SelectedTab == 0; set { if (value) SelectedTab = 0; } }
    public bool IsIssuesTab { get => SelectedTab == 1; set { if (value) SelectedTab = 1; } }
    public bool IsReturnsTab { get => SelectedTab == 2; set { if (value) SelectedTab = 2; } }

    private async Task LoadTabAsync(int tab)
    {
        if (tab == 1 && Issues is not null && !_issuesLoaded) { _issuesLoaded = true; await Issues.LoadAsync(); }
        if (tab == 2 && Returns is not null && !_returnsLoaded) { _returnsLoaded = true; await Returns.LoadAsync(); }
    }

    /// <summary>Opens the demo issue voucher tab with a search (from the Orders Log's "it's a demo" hint).</summary>
    public void ShowIssues(string search)
    {
        if (Issues is null) return;
        Issues.Search = search;
        SelectedTab = 1;
    }

    /// <summary>Opens the demo return tab with a search (from the Stock In Log's "it's a demo return" hint).</summary>
    public void ShowReturns(string search)
    {
        if (Returns is null) return;
        Returns.Search = search;
        SelectedTab = 2;
    }

    public override Task LoadAsync() => SelectedTab == 0 ? RunAsync(ReloadOpenAsync, clearMessages: false) : LoadTabAsync(SelectedTab);

    // ---- With customers now ----

    private List<OpenDemoLineDto> _open = new();
    public ObservableCollection<OpenDemoRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    [ObservableProperty] private string? _search;
    [ObservableProperty] private OpenDemoRow? _selectedRow;

    // 0 by customer, 1 by item.
    public IReadOnlyList<string> GroupModes { get; } = [L.T("By customer"), L.T("By item")];
    [ObservableProperty] private int _groupMode;
    partial void OnGroupModeChanged(int value) => Fill();

    public int OpenUnits => _open.Sum(l => l.OpenQuantity);
    public int OpenCustomers => _open.Select(l => l.CustomerId).Distinct().Count();
    public string SummaryText => _open.Count == 0
        ? ""
        : L.T("{0} unit(s) out with {1} customer(s)", OpenUnits, OpenCustomers);

    private readonly LatestRequest _requests = new();

    private async Task ReloadOpenAsync()
    {
        if (!CanSeeOpen) return;
        var ticket = _requests.Begin();
        var list = await Api.GetOpenDemosAsync(new OpenDemoQuery { Search = Search }, ticket.Token);
        if (!ticket.IsCurrent) return;
        _open = list;
        Fill();
    }

    private void Fill()
    {
        var keep = SelectedRow?.Line.OrderLineId;
        Rows.Clear();
        var byItem = GroupMode == 1;
        var groups = byItem
            ? _open.GroupBy(l => l.ItemId).OrderBy(g => g.First().ItemName, StringComparer.CurrentCultureIgnoreCase)
            : _open.GroupBy(l => l.CustomerId).OrderBy(g => g.First().CustomerName, StringComparer.CurrentCultureIgnoreCase);
        foreach (var g in groups)
        {
            var first = g.First();
            var name = byItem
                ? (first.ItemCode is { Length: > 0 } code ? $"{first.ItemName} ({code})" : first.ItemName)
                : (first.CustomerNumber is { } n ? $"{n} · {first.CustomerName}" : first.CustomerName);
            var header = L.T("{0} · {1} unit(s) out", name, g.Sum(l => l.OpenQuantity));
            foreach (var l in g.OrderBy(l => l.CreatedAt)) Rows.Add(new OpenDemoRow(l, header));
        }
        SelectedRow = Rows.FirstOrDefault(r => r.Line.OrderLineId == keep);
        OnPropertyChanged(nameof(OpenUnits));
        OnPropertyChanged(nameof(OpenCustomers));
        OnPropertyChanged(nameof(SummaryText));
    }

    [RelayCommand]
    private Task SearchAsync() => RunAsync(ReloadOpenAsync);

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(ReloadOpenAsync);

    // ---- Actions ----

    [RelayCommand]
    private void NewDemo() => _shell.Navigate(Page.NewDemo);

    [RelayCommand]
    private void NewReturn() => _shell.Navigate(Page.DemoReturn);

    /// <summary>Opens a demo return for this line's customer, with the line filled in with all that is still out.</summary>
    [RelayCommand]
    private void ReturnLine(OpenDemoRow? row)
    {
        if (row is null || !CanReturn) return;
        _shell.Open(Page.DemoReturn, vm => ((StockInViewModel)vm).StartReturn(row.Line.CustomerId, row.Line.OrderLineId));
    }

    /// <summary>Shows the demo issue voucher the line went out on.</summary>
    [RelayCommand]
    private void OpenVoucher(OpenDemoRow? row)
    {
        if (row is null || !CanSeeLogs) return;
        new Views.VoucherWindow(new VoucherViewModel(Session, row.Line.OrderId, null)) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private Task OpenCardAsync(OpenDemoRow? row)
    {
        if (row is null || !CanOpenCard) return Task.CompletedTask;
        return RunAsync(async () => ItemCards.Open(Session, await Api.GetItemAsync(row.Line.ItemId)));
    }
}
