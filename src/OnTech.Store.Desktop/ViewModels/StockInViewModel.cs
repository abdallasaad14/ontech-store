using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>One demo line a customer still holds, with the quantity coming back now (blank = none).</summary>
public sealed partial class DemoReturnLineViewModel(OpenDemoLineDto line) : ObservableObject
{
    public OpenDemoLineDto Line { get; } = line;
    public string Description => Line.ItemCode is { Length: > 0 } code ? $"{Line.ItemName} ({code})" : Line.ItemName;

    [ObservableProperty] private string? _returnText;

    /// <summary>The quantity typed in, or 0 when blank. -1 when it isn't a whole number from 0 to what the customer still has.</summary>
    public int ReturnQuantity => string.IsNullOrWhiteSpace(ReturnText) ? 0
        : Numbers.TryParseInt(ReturnText, out var n) && n >= 0 && n <= Line.OpenQuantity ? n : -1;

    public bool IsInvalid => ReturnQuantity < 0;

    partial void OnReturnTextChanged(string? value)
    {
        OnPropertyChanged(nameof(ReturnQuantity));
        OnPropertyChanged(nameof(IsInvalid));
        Changed?.Invoke();
    }

    public Action? Changed { get; set; }

    [RelayCommand] private void ReturnAll() => ReturnText = Line.OpenQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The open lines of one demo voucher.</summary>
public sealed partial class DemoReturnGroupViewModel(string voucherNumber, DateTimeOffset createdAt, List<DemoReturnLineViewModel> lines) : ObservableObject
{
    public string VoucherNumber { get; } = voucherNumber;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public List<DemoReturnLineViewModel> Lines { get; } = lines;

    [RelayCommand] private void ReturnAll() { foreach (var l in Lines) l.ReturnAllCommand.Execute(null); }
}

/// <summary>
/// Stock In: a purchase (stock received from a supplier) or a demo return (a demo coming back from a customer).
/// A purchase: pick category, item, quantity and supplier. A demo return: pick the customer, then type how much of each
/// open demo line comes back. Both need the warehouse receipt voucher number. The mirror of New Order; quantities are
/// never typed in any other way.
/// </summary>
public sealed partial class StockInViewModel(Session session, ShellViewModel shell, ReceiveType type = ReceiveType.Purchase) : PageViewModel(session)
{
    // ---- Purchase or demo return ----

    /// <summary>
    /// Fixed when the page opens: Stock In records purchases; the Demo page opens this page for demo returns.
    /// Both use the same receipt voucher number book.
    /// </summary>
    public bool IsDemoReturn { get; } = type == ReceiveType.DemoReturn;
    public bool IsPurchase => !IsDemoReturn;

    public string SaveText => IsDemoReturn ? L.T("Save demo return") : L.T("Save stock-in");

    public string Title => IsDemoReturn ? L.T("Demo Return") : L.T("Stock In");

    // Set by the Demo page's "Return" on a line: that customer, with that line filled in once the lists load.
    private int? _startCustomerId;
    private int? _startOrderLineId;

    /// <summary>Opens the return for one customer, with one open demo line ready to come back in full.</summary>
    public void StartReturn(int customerId, int? orderLineId)
    {
        _startCustomerId = customerId;
        _startOrderLineId = orderLineId;
    }

    public ObservableCollection<CustomerDto> Customers { get; } = new();
    public ObservableCollection<DemoReturnGroupViewModel> OpenDemos { get; } = new();
    private List<CustomerDto> _allCustomers = new();

    /// <summary>Narrows the customer list by number or name; one match is picked straight away.</summary>
    [ObservableProperty] private string? _customerSearch;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary), nameof(HasSummary), nameof(NoOpenDemos))]
    private CustomerDto? _selectedCustomer;

    /// <summary>True when a customer is picked and has nothing out on demo.</summary>
    public bool NoOpenDemos => SelectedCustomer is not null && OpenDemos.Count == 0 && !_loadingDemos;
    private bool _loadingDemos;

    private IEnumerable<DemoReturnLineViewModel> DemoLines => OpenDemos.SelectMany(g => g.Lines);

    private async Task LoadCustomersAsync()
    {
        _allCustomers = await Api.GetCustomersAsync();
        CustomerSearch = null;
        FilterCustomers();
        if (_startCustomerId is { } id) SelectedCustomer = Customers.FirstOrDefault(c => c.Id == id);
        _startCustomerId = null;
    }

    partial void OnCustomerSearchChanged(string? value) => FilterCustomers();

    private void FilterCustomers()
    {
        var text = CustomerSearch?.Trim() ?? "";
        var number = Numbers.NormalizeDigits(text);
        var keep = SelectedCustomer;
        var matches = text.Length == 0
            ? _allCustomers
            : _allCustomers.Where(c => c.CustomerNumber?.ToString() == number || c.CompanyName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Customers.Clear();
        foreach (var c in matches) Customers.Add(c);
        if (matches.Count == 1) SelectedCustomer = matches[0];
        else if (keep is not null) SelectedCustomer = Customers.FirstOrDefault(c => c.Id == keep.Id);
    }

    partial void OnSelectedCustomerChanged(CustomerDto? value) => _ = RunAsync(LoadOpenDemosAsync, clearMessages: false);

    private int _demosVersion;

    private async Task LoadOpenDemosAsync()
    {
        var version = ++_demosVersion;
        var customer = SelectedCustomer;
        _loadingDemos = true;
        try
        {
            var list = customer is null ? [] : await Api.GetOpenDemosAsync(new OpenDemoQuery { CustomerId = customer.Id });
            if (version != _demosVersion) return;
            OpenDemos.Clear();
            foreach (var g in list.GroupBy(l => l.OrderId).OrderBy(g => g.First().CreatedAt))
            {
                var lines = g.Select(l => new DemoReturnLineViewModel(l) { Changed = DemoLinesChanged }).ToList();
                OpenDemos.Add(new DemoReturnGroupViewModel(g.First().IssueVoucherNumber ?? "", g.First().CreatedAt, lines));
            }
            if (customer is not null && _startOrderLineId is { } lineId)
            {
                DemoLines.FirstOrDefault(l => l.Line.OrderLineId == lineId)?.ReturnAllCommand.Execute(null);
                _startOrderLineId = null;
            }
        }
        finally
        {
            if (version == _demosVersion) _loadingDemos = false;
            DemoLinesChanged();
            OnPropertyChanged(nameof(NoOpenDemos));
        }
    }

    private void DemoLinesChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
    }

    /// <summary>Fills every open line with all the customer still has.</summary>
    [RelayCommand]
    private void ReturnEverything() { foreach (var g in OpenDemos) g.ReturnAllCommand.Execute(null); }

    // ---- Purchase ----

    public ObservableCollection<CategoryDto> Categories { get; } = new();
    public ObservableCollection<ItemDto> Items { get; } = new();
    public ObservableCollection<SupplierDto> Suppliers { get; } = new();
    public ObservableCollection<OrderLineViewModel> Lines { get; } = new();

    public bool CanAddSupplier => Session.Can(Permission.ManageSuppliers);
    public bool CanOpenItemCard => ItemCards.CanOpen(Session);

    [ObservableProperty] private CategoryDto? _selectedCategory;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(InStockHint), nameof(Summary), nameof(HasSummary))]
    private ItemDto? _selectedItem;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary), nameof(HasSummary))]
    private string _quantityText = "1";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary), nameof(HasSummary))]
    private SupplierDto? _selectedSupplier;

    /// <summary>Narrows the supplier list by number or name; one match is picked straight away.</summary>
    [ObservableProperty] private string? _supplierSearch;
    private List<SupplierDto> _allSuppliers = new();

    [ObservableProperty] private string? _voucherNumber;
    [ObservableProperty] private string? _supplierInvoiceNumber;
    [ObservableProperty] private string? _notes;

    public string? InStockHint => SelectedItem is { } i ? L.T("{0} in stock", i.Quantity) : null;

    public bool HasSummary => Summary is not null;

    public string? Summary
    {
        get
        {
            if (IsDemoReturn)
            {
                if (SelectedCustomer is not { } customer) return null;
                var back = DemoLines.Where(l => l.ReturnQuantity > 0).Sum(l => l.ReturnQuantity);
                if (back == 0) return null;
                var still = DemoLines.Sum(l => l.Line.OpenQuantity) - back;
                return L.T("Receiving {0} unit(s) back from {1}. {2} still with the customer after this.", back, customer.CompanyName, still);
            }
            if (SelectedItem is not { } item || !TryQuantity(out var qty)) return null;
            var after = item.Quantity + Lines.Where(l => l.Item.Id == item.Id).Sum(l => l.Quantity) + qty;
            return SelectedSupplier is { } s
                ? L.T("Receiving {0} × {1} from {2}. {3} in stock after this.", qty, item.Name, s.CompanyName, after)
                : L.T("Receiving {0} × {1}. {2} in stock after this.", qty, item.Name, after);
        }
    }

    private bool TryQuantity(out int qty) => Numbers.TryParseInt(QuantityText, out qty) && qty > 0;

    public override Task LoadAsync() => IsDemoReturn ? RunAsync(LoadCustomersAsync) : RunAsync(async () =>
    {
        var categories = Api.GetCategoriesAsync();
        var suppliers = Api.GetSuppliersAsync();
        Categories.Clear();
        foreach (var c in await categories) Categories.Add(c);
        _allSuppliers = await suppliers;
        SupplierSearch = null;
        FilterSuppliers();
    });

    partial void OnSelectedCategoryChanged(CategoryDto? value) => _ = RunAsync(LoadItemsAsync, clearMessages: false);

    private int _itemsVersion;

    private async Task LoadItemsAsync()
    {
        // Only the latest of overlapping loads fills the list, so quick category changes can't mix items.
        var version = ++_itemsVersion;
        var keep = SelectedItem?.Id;
        var list = SelectedCategory is null ? [] : await Api.GetItemsAsync(SelectedCategory.Id);
        if (version != _itemsVersion) return;
        Items.Clear();
        foreach (var i in list) Items.Add(i);
        SelectedItem = keep is { } k ? Items.FirstOrDefault(i => i.Id == k) : null;
    }

    [RelayCommand]
    private void AddLine()
    {
        ErrorMessage = null;
        if (!TryTakeCurrentLine()) return;
        SelectedItem = null;
        QuantityText = "1";
    }

    /// <summary>Moves the current item + quantity into <see cref="Lines"/>. Returns false (with a message) if it isn't valid.</summary>
    private bool TryTakeCurrentLine()
    {
        if (SelectedItem is not { } item) { ErrorMessage = L.T("Choose an item."); return false; }
        if (!TryQuantity(out var qty)) { ErrorMessage = L.T("Enter a quantity of 1 or more."); return false; }
        var existing = Lines.FirstOrDefault(l => l.Item.Id == item.Id);
        if (existing is not null) existing.Quantity += qty;
        else Lines.Add(new OrderLineViewModel(item, qty));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
        return true;
    }

    [RelayCommand]
    private void RemoveLine(OrderLineViewModel? line)
    {
        if (line is null) return;
        Lines.Remove(line);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
    }

    partial void OnSupplierSearchChanged(string? value) => FilterSuppliers();

    private void FilterSuppliers()
    {
        var text = SupplierSearch?.Trim() ?? "";
        var number = Numbers.NormalizeDigits(text);
        var keep = SelectedSupplier;
        var matches = text.Length == 0
            ? _allSuppliers
            : _allSuppliers.Where(s => s.SupplierNumber?.ToString() == number || s.CompanyName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Suppliers.Clear();
        foreach (var s in matches) Suppliers.Add(s);
        if (matches.Count == 1) SelectedSupplier = matches[0];
        else if (keep is not null) SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == keep.Id);
    }

    [RelayCommand]
    private Task QuickAddSupplierAsync()
    {
        var name = Dialogs.Prompt(L.T("New supplier"), L.T("Company name"), okText: L.T("Add supplier"), maxLength: 200);
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var created = await Api.CreateSupplierAsync(new SaveSupplierRequest { CompanyName = name.Trim() });
            _allSuppliers.Add(created);
            SupplierSearch = null;
            FilterSuppliers();
            SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == created.Id);
            SuccessMessage = L.T("Supplier \"{0}\" added. You can add the phone and tax number later under Suppliers.", created.CompanyName);
        });
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        // The item picked on the form counts as a line unless the user already added it with "Add another item".
        if (SelectedItem is not null)
        {
            if (!TryTakeCurrentLine()) return;
            SelectedItem = null; // it is in Lines now, so a retry must not add it twice
            QuantityText = "1";
        }
        if (IsDemoReturn) { await SaveDemoReturnAsync(); return; }
        if (Lines.Count == 0) throw new ArgumentException(L.T("Choose an item and a quantity."));
        if (SelectedSupplier is null) throw new ArgumentException(L.T("Choose the supplier."));
        if (string.IsNullOrWhiteSpace(VoucherNumber)) throw new ArgumentException(L.T("Enter the receipt voucher number."));

        var saved = await Api.CreateStockInAsync(new CreateStockInRequest
        {
            Type = ReceiveType.Purchase,
            SupplierId = SelectedSupplier.Id,
            VoucherNumber = Numbers.NormalizeDigits(VoucherNumber).Trim(),
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(SupplierInvoiceNumber) ? null : Numbers.NormalizeDigits(SupplierInvoiceNumber).Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Lines = Lines.Select(l => new StockInLineRequest { ItemId = l.Item.Id, Quantity = l.Quantity }).ToList(),
        });

        Lines.Clear();
        SelectedItem = null;
        QuantityText = "1";
        VoucherNumber = null;
        SupplierInvoiceNumber = null;
        Notes = null;
        await LoadItemsAsync(); // fresh quantities, including changes from other PCs
        SuccessMessage = L.T("Stock-in {0} saved from {1}: {2} unit(s) added to stock, voucher {3}.",
            saved.StockInNumber, saved.SupplierName, saved.TotalQuantity, saved.VoucherNumber);
    });

    private async Task SaveDemoReturnAsync()
    {
        if (SelectedCustomer is not { } customer) throw new ArgumentException(L.T("Choose the customer."));
        if (DemoLines.FirstOrDefault(l => l.IsInvalid) is { } bad)
            throw new ArgumentException(L.T("{0}: enter a quantity from 0 to {1}.", bad.Line.ItemName, bad.Line.OpenQuantity));
        var lines = DemoLines.Where(l => l.ReturnQuantity > 0).ToList();
        if (lines.Count == 0) throw new ArgumentException(L.T("Type how many of each item came back."));
        if (string.IsNullOrWhiteSpace(VoucherNumber)) throw new ArgumentException(L.T("Enter the receipt voucher number."));

        var saved = await Api.CreateStockInAsync(new CreateStockInRequest
        {
            Type = ReceiveType.DemoReturn,
            CustomerId = customer.Id,
            VoucherNumber = Numbers.NormalizeDigits(VoucherNumber).Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Lines = lines.Select(l => new StockInLineRequest { ItemId = l.Line.ItemId, Quantity = l.ReturnQuantity, DemoOrderLineId = l.Line.OrderLineId }).ToList(),
        });

        VoucherNumber = null;
        Notes = null;
        await LoadOpenDemosAsync(); // what the customer still has, including returns saved on other PCs
        SuccessMessage = L.T("Demo return {0} saved from {1}: {2} unit(s) back in stock, voucher {3}.",
            saved.StockInNumber, saved.CustomerName, saved.TotalQuantity, saved.VoucherNumber);
    }

    protected override void OnApiError(Client.ApiError error)
    {
        base.OnApiError(error);
        // Another PC may have saved a return for the same demo: show what the customer has now.
        if (IsDemoReturn && error.Code == ErrorCodes.OverReturn) _ = RunAsync(LoadOpenDemosAsync, clearMessages: false);
    }

    [RelayCommand]
    private void Cancel()
    {
        var dirty = IsDemoReturn ? DemoLines.Any(l => l.ReturnQuantity != 0) : Lines.Count > 0;
        if (dirty && !Dialogs.Confirm(IsDemoReturn ? L.T("Discard this demo return?") : L.T("Discard this stock-in?"))) return;
        shell.Navigate(IsDemoReturn ? Page.Demos : Page.Home);
    }

    /// <summary>Opens the picked item's card, to check its history before saving.</summary>
    [RelayCommand] private void OpenCard() => ItemCards.Open(Session, SelectedItem);
}
