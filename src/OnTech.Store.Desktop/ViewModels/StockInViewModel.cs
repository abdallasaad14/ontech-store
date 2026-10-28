using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>
/// Stock In: stock received from a supplier. Pick category, item, quantity and supplier, and the warehouse receipt
/// voucher number. The mirror of New Order; quantities are never typed in any other way.
/// </summary>
public sealed partial class StockInViewModel(Session session, ShellViewModel shell) : PageViewModel(session)
{
    public ObservableCollection<CategoryDto> Categories { get; } = new();
    public ObservableCollection<ItemDto> Items { get; } = new();
    public ObservableCollection<SupplierDto> Suppliers { get; } = new();
    public ObservableCollection<OrderLineViewModel> Lines { get; } = new();

    public bool CanAddSupplier => Session.Can(Permission.ManageSuppliers);

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
            if (SelectedItem is not { } item || !TryQuantity(out var qty)) return null;
            var after = item.Quantity + Lines.Where(l => l.Item.Id == item.Id).Sum(l => l.Quantity) + qty;
            return SelectedSupplier is { } s
                ? L.T("Receiving {0} × {1} from {2}. {3} in stock after this.", qty, item.Name, s.CompanyName, after)
                : L.T("Receiving {0} × {1}. {2} in stock after this.", qty, item.Name, after);
        }
    }

    private bool TryQuantity(out int qty) => int.TryParse(QuantityText?.Trim(), out qty) && qty > 0;

    public override Task LoadAsync() => RunAsync(async () =>
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
        var keep = SelectedSupplier;
        var matches = text.Length == 0
            ? _allSuppliers
            : _allSuppliers.Where(s => s.SupplierNumber?.ToString() == text || s.CompanyName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Suppliers.Clear();
        foreach (var s in matches) Suppliers.Add(s);
        if (matches.Count == 1) SelectedSupplier = matches[0];
        else if (keep is not null) SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == keep.Id);
    }

    [RelayCommand]
    private Task QuickAddSupplierAsync()
    {
        var name = Dialogs.Prompt(L.T("New supplier"), L.T("Company name"), okText: L.T("Add supplier"));
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
        if (Lines.Count == 0) throw new ArgumentException(L.T("Choose an item and a quantity."));
        if (SelectedSupplier is null) throw new ArgumentException(L.T("Choose the supplier."));
        if (string.IsNullOrWhiteSpace(VoucherNumber)) throw new ArgumentException(L.T("Enter the receipt voucher number."));

        var saved = await Api.CreateStockInAsync(new CreateStockInRequest
        {
            SupplierId = SelectedSupplier.Id,
            VoucherNumber = VoucherNumber.Trim(),
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(SupplierInvoiceNumber) ? null : SupplierInvoiceNumber.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Lines = Lines.Select(l => new OrderLineRequest { ItemId = l.Item.Id, Quantity = l.Quantity }).ToList(),
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

    [RelayCommand]
    private void Cancel()
    {
        if (Lines.Count > 0 && !Dialogs.Confirm(L.T("Discard this stock-in?"))) return;
        shell.Navigate(Page.Home);
    }
}
