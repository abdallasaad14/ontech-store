using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public sealed partial class OrderLineViewModel(ItemDto item, int quantity) : ObservableObject
{
    public ItemDto Item { get; } = item;
    [ObservableProperty] private int _quantity = quantity;
    public string Description => $"{Item.Name} ({Item.CategoryName})";
}

/// <summary>
/// New Order: pick category, item (with the available quantity), quantity and customer, the warehouse issue voucher
/// number (required) and optionally a receipt number.
/// One order can hold several items: "Add another item" moves the current pick into the list.
/// </summary>
public sealed partial class NewOrderViewModel(Session session, ShellViewModel shell) : PageViewModel(session)
{
    public ObservableCollection<CategoryDto> Categories { get; } = new();
    public ObservableCollection<ItemDto> Items { get; } = new();
    public ObservableCollection<CustomerDto> Customers { get; } = new();
    public ObservableCollection<OrderLineViewModel> Lines { get; } = new();

    public int? PreselectItemId { get; set; }
    public bool CanAddCustomer => Session.Can(Permission.ManageCustomers);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AvailableHint), nameof(Summary), nameof(HasSummary))]
    private CategoryDto? _selectedCategory;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AvailableHint), nameof(Summary), nameof(HasSummary))]
    private ItemDto? _selectedItem;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary), nameof(HasSummary))]
    private string _quantityText = "1";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary), nameof(HasSummary))]
    private CustomerDto? _selectedCustomer;

    /// <summary>Narrows the customer list by number or name; one match is picked straight away.</summary>
    [ObservableProperty] private string? _customerSearch;
    private List<CustomerDto> _allCustomers = new();

    [ObservableProperty] private string? _issueVoucherNumber;
    [ObservableProperty] private bool _withReceipt;
    [ObservableProperty] private string? _receiptNumber;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private OrderDto? _lastOrder;

    private bool _loading;

    public string? AvailableHint => SelectedItem is { } i
        ? L.T(i.IsLowStock ? "{0} available (low stock)" : "{0} available", Remaining(i, includeCurrent: false))
        : null;

    public bool HasSummary => Summary is not null;

    public string? Summary
    {
        get
        {
            if (SelectedItem is not { } item || !TryQuantity(out var qty)) return null;
            var left = Remaining(item, includeCurrent: false) - qty;
            if (left < 0) return L.T("Only {0} {1} available. Lower the quantity.", Remaining(item, false), item.Name);
            return SelectedCustomer is { } c
                ? L.T("Taking {0} × {1} for {2}. {3} left after this order.", qty, item.Name, c.CompanyName, left)
                : L.T("Taking {0} × {1}. {2} left after this order.", qty, item.Name, left);
        }
    }

    /// <summary>Available stock for an item minus what is already in the order's lines.</summary>
    private int Remaining(ItemDto item, bool includeCurrent)
    {
        var inLines = Lines.Where(l => l.Item.Id == item.Id).Sum(l => l.Quantity);
        var current = includeCurrent && TryQuantity(out var q) ? q : 0;
        return item.Quantity - inLines - current;
    }

    private bool TryQuantity(out int qty) => int.TryParse(QuantityText?.Trim(), out qty) && qty > 0;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            _loading = true;
            var categories = Api.GetCategoriesAsync();
            var customers = Api.GetCustomersAsync();
            Categories.Clear();
            foreach (var c in await categories) Categories.Add(c);
            _allCustomers = await customers;
            CustomerSearch = null;
            FilterCustomers();

            if (PreselectItemId is { } id)
            {
                var item = await Api.GetItemAsync(id);
                SelectedCategory = Categories.FirstOrDefault(c => c.Id == item.CategoryId);
                await LoadItemsAsync();
                SelectedItem = Items.FirstOrDefault(i => i.Id == id);
                PreselectItemId = null;
            }
            _loading = false;
        });
        _loading = false;
    }

    partial void OnSelectedCategoryChanged(CategoryDto? value)
    {
        if (!_loading) _ = RunAsync(LoadItemsAsync);
    }

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
        if (qty > Remaining(item, false))
        {
            ErrorMessage = L.T("Only {0} {1} available.", Remaining(item, false), item.Name);
            return false;
        }
        var existing = Lines.FirstOrDefault(l => l.Item.Id == item.Id);
        if (existing is not null) existing.Quantity += qty;
        else Lines.Add(new OrderLineViewModel(item, qty));
        OnPropertyChanged(nameof(AvailableHint));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
        return true;
    }

    [RelayCommand]
    private void RemoveLine(OrderLineViewModel? line)
    {
        if (line is null) return;
        Lines.Remove(line);
        OnPropertyChanged(nameof(AvailableHint));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
    }

    partial void OnCustomerSearchChanged(string? value) => FilterCustomers();

    private void FilterCustomers()
    {
        var text = CustomerSearch?.Trim() ?? "";
        var keep = SelectedCustomer;
        var matches = text.Length == 0
            ? _allCustomers
            : _allCustomers.Where(c => c.CustomerNumber?.ToString() == text || c.CompanyName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Customers.Clear();
        foreach (var c in matches) Customers.Add(c);
        if (matches.Count == 1) SelectedCustomer = matches[0];
        else if (keep is not null) SelectedCustomer = Customers.FirstOrDefault(c => c.Id == keep.Id);
    }

    [RelayCommand]
    private Task QuickAddCustomerAsync()
    {
        var name = Dialogs.Prompt(L.T("New customer"), L.T("Company name"), okText: L.T("Add customer"));
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var created = await Api.CreateCustomerAsync(new SaveCustomerRequest { CompanyName = name.Trim() });
            _allCustomers.Add(created);
            CustomerSearch = null;
            FilterCustomers();
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == created.Id);
            SuccessMessage = L.T("Customer \"{0}\" added. You can add the phone and tax number later under Customers.", created.CompanyName);
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
        if (Lines.Count == 0) throw new ArgumentException(L.T("Choose an item and a quantity."));
        if (SelectedCustomer is null) throw new ArgumentException(L.T("Choose the customer."));
        if (string.IsNullOrWhiteSpace(IssueVoucherNumber)) throw new ArgumentException(L.T("Enter the issue voucher number."));
        if (WithReceipt && string.IsNullOrWhiteSpace(ReceiptNumber)) throw new ArgumentException(L.T("Enter the receipt number, or untick \"Add a receipt\"."));

        var order = await Api.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = SelectedCustomer.Id,
            IssueVoucherNumber = IssueVoucherNumber.Trim(),
            WithReceipt = WithReceipt,
            ReceiptNumber = WithReceipt ? ReceiptNumber!.Trim() : null,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Lines = Lines.Select(l => new OrderLineRequest { ItemId = l.Item.Id, Quantity = l.Quantity }).ToList(),
        });

        LastOrder = order;
        ResetForm();
        await LoadItemsAsync(); // fresh quantities, including changes from other PCs
        SuccessMessage = order.WithReceipt
            ? L.T("Order {0} saved for {1}: {2} unit(s) taken from stock, issue voucher {3}, receipt {4}.", order.OrderNumber, order.CustomerName, order.TotalQuantity, order.IssueVoucherNumber, order.ReceiptNumber)
            : L.T("Order {0} saved for {1}: {2} unit(s) taken from stock, issue voucher {3}.", order.OrderNumber, order.CustomerName, order.TotalQuantity, order.IssueVoucherNumber);
    });

    private void ResetForm()
    {
        Lines.Clear();
        SelectedItem = null;
        QuantityText = "1";
        IssueVoucherNumber = null;
        WithReceipt = false;
        ReceiptNumber = null;
        Notes = null;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Lines.Count > 0 && !Dialogs.Confirm(L.T("Discard this order?"))) return;
        shell.Navigate(Page.Home);
    }

    protected override void OnApiError(Client.ApiError error)
    {
        base.OnApiError(error);
        // Stock may have changed on another PC: show the current numbers.
        if (error.Shortages.Count > 0) _ = RefreshItemsQuietlyAsync();
    }

    private async Task RefreshItemsQuietlyAsync()
    {
        try
        {
            var fresh = (await Api.GetItemsAsync()).ToDictionary(i => i.Id);
            for (var i = 0; i < Lines.Count; i++)
                if (fresh.TryGetValue(Lines[i].Item.Id, out var item))
                    Lines[i] = new OrderLineViewModel(item, Lines[i].Quantity);
            await LoadItemsAsync();
        }
        catch (Client.ApiError) { /* the error is already shown */ }
    }
}
