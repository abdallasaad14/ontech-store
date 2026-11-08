using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>Companies stock comes from. Same as the Customers page, without the Excel import.</summary>
public sealed partial class SuppliersViewModel(Session session) : PageViewModel(session)
{
    public ObservableCollection<SupplierDto> Suppliers { get; } = new();
    public bool CanEdit => Session.Can(Permission.ManageSuppliers);

    [ObservableProperty] private string? _search;
    [ObservableProperty] private bool _showDisabled;
    [ObservableProperty] private SupplierDto? _selected;

    // Edit form
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private int? _editingId;
    [ObservableProperty] private string? _supplierNumber;
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string? _phoneNumber;
    [ObservableProperty] private string? _taxNumber;
    [ObservableProperty] private string? _contactPerson;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _isActive = true;
    /// <summary>The Version of the record the form was loaded from; sent back so the server can refuse a stale save (M-1).</summary>
    private int? _editingVersion;

    public string FormTitle => L.T(EditingId is null ? "Add supplier" : "Edit supplier");
    partial void OnEditingIdChanged(int? value) => OnPropertyChanged(nameof(FormTitle));

    public override Task LoadAsync() => RunAsync(ReloadAsync, clearMessages: false);

    private async Task ReloadAsync()
    {
        var list = await Api.GetSuppliersAsync(Search, ShowDisabled);
        Suppliers.Clear();
        foreach (var c in list) Suppliers.Add(c);
    }

    [RelayCommand] private Task SearchAsync() => RunAsync(ReloadAsync);
    partial void OnShowDisabledChanged(bool value) => _ = RunAsync(ReloadAsync);

    [RelayCommand]
    public void StartNew()
    {
        if (!CanEdit) return;
        EditingId = null;
        _editingVersion = null;
        SupplierNumber = null;
        CompanyName = "";
        PhoneNumber = TaxNumber = ContactPerson = Email = Address = Notes = null;
        IsActive = true;
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit(SupplierDto? c)
    {
        if (c is null || !CanEdit) return;
        EditingId = c.Id;
        _editingVersion = c.Version;
        SupplierNumber = c.SupplierNumber?.ToString();
        CompanyName = c.CompanyName;
        PhoneNumber = c.PhoneNumber;
        TaxNumber = c.TaxNumber;
        ContactPerson = c.ContactPerson;
        Email = c.Email;
        Address = c.Address;
        Notes = c.Notes;
        IsActive = c.IsActive;
        IsEditing = true;
    }

    [RelayCommand] private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CompanyName)) throw new ArgumentException(L.T("Company name is required."));
        int? number = null;
        if (Clean(SupplierNumber) is { } numberText)
            number = Numbers.TryParseInt(numberText, out var n) && n > 0 ? n : throw new ArgumentException(L.T("Supplier number must be a whole number, 1 or more."));
        var req = new SaveSupplierRequest
        {
            SupplierNumber = number,
            CompanyName = CompanyName.Trim(),
            PhoneNumber = Clean(PhoneNumber),
            TaxNumber = Clean(TaxNumber),
            ContactPerson = Clean(ContactPerson),
            Email = Clean(Email),
            Address = Clean(Address),
            Notes = Clean(Notes),
            IsActive = IsActive,
            Version = EditingId is null ? null : _editingVersion,
        };
        SupplierDto saved;
        try
        {
            saved = EditingId is { } id ? await Api.UpdateSupplierAsync(id, req) : await Api.CreateSupplierAsync(req);
        }
        catch (ApiError ex) when (ex.Code == ErrorCodes.ConcurrencyConflict && EditingId is { } changedId)
        {
            // Changed on another PC: show what is saved now, keep the form open, and let the user check and save again.
            await ReloadEditedAsync(changedId);
            throw;
        }
        IsEditing = false;
        await ReloadAsync();
        Selected = Suppliers.FirstOrDefault(c => c.Id == saved.Id);
        SuccessMessage = L.T(EditingId is null ? "Supplier \"{0}\" added." : "Supplier \"{0}\" saved.", saved.CompanyName);
    });

    [RelayCommand]
    private Task DisableAsync(SupplierDto? c)
    {
        if (c is null || !Dialogs.Confirm(L.T("Disable {0}? Their past stock-ins stay in the log, but they can't be picked for new ones.", c.CompanyName))) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableSupplierAsync(c.Id);
            IsEditing = false;
            await ReloadAsync();
            SuccessMessage = L.T("{0} was disabled.", c.CompanyName);
        });
    }

    private async Task ReloadEditedAsync(int id)
    {
        try
        {
            var fresh = await Api.GetSupplierAsync(id);
            Edit(fresh);
            await ReloadAsync();
        }
        catch (ApiError) { /* the conflict message is shown; the user can reopen the record */ }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
