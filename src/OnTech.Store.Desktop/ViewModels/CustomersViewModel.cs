using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public sealed partial class CustomersViewModel(Session session) : PageViewModel(session)
{
    public ObservableCollection<CustomerDto> Customers { get; } = new();
    public bool CanEdit => Session.Can(Permission.ManageCustomers);

    [ObservableProperty] private string? _search;
    [ObservableProperty] private bool _showDisabled;
    [ObservableProperty] private CustomerDto? _selected;

    // Edit form
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private int? _editingId;
    [ObservableProperty] private string? _customerNumber;
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string? _phoneNumber;
    [ObservableProperty] private string? _taxNumber;
    [ObservableProperty] private string? _contactPerson;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _isActive = true;

    public string FormTitle => L.T(EditingId is null ? "Add customer" : "Edit customer");
    partial void OnEditingIdChanged(int? value) => OnPropertyChanged(nameof(FormTitle));

    public override Task LoadAsync() => RunAsync(ReloadAsync, clearMessages: false);

    private async Task ReloadAsync()
    {
        var list = await Api.GetCustomersAsync(Search, ShowDisabled);
        Customers.Clear();
        foreach (var c in list) Customers.Add(c);
    }

    [RelayCommand] private Task SearchAsync() => RunAsync(ReloadAsync);
    partial void OnShowDisabledChanged(bool value) => _ = RunAsync(ReloadAsync);

    [RelayCommand]
    public void StartNew()
    {
        if (!CanEdit) return;
        EditingId = null;
        CustomerNumber = null;
        CompanyName = "";
        PhoneNumber = TaxNumber = ContactPerson = Email = Address = Notes = null;
        IsActive = true;
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit(CustomerDto? c)
    {
        if (c is null || !CanEdit) return;
        EditingId = c.Id;
        CustomerNumber = c.CustomerNumber?.ToString();
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
        if (Clean(CustomerNumber) is { } numberText)
            number = int.TryParse(numberText, out var n) && n > 0 ? n : throw new ArgumentException(L.T("Customer number must be a whole number, 1 or more."));
        var req = new SaveCustomerRequest
        {
            CustomerNumber = number,
            CompanyName = CompanyName.Trim(),
            PhoneNumber = Clean(PhoneNumber),
            TaxNumber = Clean(TaxNumber),
            ContactPerson = Clean(ContactPerson),
            Email = Clean(Email),
            Address = Clean(Address),
            Notes = Clean(Notes),
            IsActive = IsActive,
        };
        var saved = EditingId is { } id ? await Api.UpdateCustomerAsync(id, req) : await Api.CreateCustomerAsync(req);
        IsEditing = false;
        await ReloadAsync();
        Selected = Customers.FirstOrDefault(c => c.Id == saved.Id);
        SuccessMessage = L.T(EditingId is null ? "Customer \"{0}\" added." : "Customer \"{0}\" saved.", saved.CompanyName);
    });

    /// <summary>Adds customers from an Excel or CSV sheet. Numbers already in the store are skipped, so importing twice is safe.</summary>
    [RelayCommand]
    private Task ImportAsync()
    {
        if (!CanEdit) return Task.CompletedTask;
        var dlg = new OpenFileDialog { Title = L.T("Import customers"), Filter = L.T("Excel or CSV") + "|*.xlsx;*.csv|" + L.T("All files") + "|*.*" };
        if (dlg.ShowDialog() != true) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            List<ImportCustomerRow> rows;
            try { rows = CustomerSheet.Read(dlg.FileName); }
            catch (System.IO.IOException) { throw new ArgumentException(L.T("Couldn't open the file. Close it in Excel and try again.")); }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or InvalidOperationException or System.Xml.XmlException)
            { throw new ArgumentException(L.T("Couldn't read this file. Save it as .xlsx or .csv and try again.")); }
            if (rows.Count == 0) throw new ArgumentException(L.T("No customers found in this file."));

            var sample = string.Join("\n", rows.Take(3).Select(r => $"  {r.CustomerNumber?.ToString() ?? L.T("(new number)")}  {r.CompanyName}"));
            if (!Dialogs.Confirm(L.T("Found {0} customers in {1}, for example:", rows.Count, System.IO.Path.GetFileName(dlg.FileName)) + $"\n\n{sample}\n\n" + L.T("Import them?"))) return;

            var result = await Api.ImportCustomersAsync(new ImportCustomersRequest { Rows = rows });
            IsEditing = false;
            await ReloadAsync();
            SuccessMessage = result.Skipped == 0
                ? L.T("{0} customers imported.", result.Added)
                : L.T("{0} customers imported, {1} skipped:", result.Added, result.Skipped) + " "
                  + string.Join("; ", result.Messages.Take(5).Select(L.Message)) + (result.Messages.Count > 5 ? "; ..." : "");
        });
    }

    [RelayCommand]
    private Task DisableAsync(CustomerDto? c)
    {
        if (c is null || !Dialogs.Confirm(L.T("Disable {0}? Their past orders stay in the log, but they can't be picked for new orders.", c.CompanyName))) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableCustomerAsync(c.Id);
            IsEditing = false;
            await ReloadAsync();
            SuccessMessage = L.T("{0} was disabled.", c.CompanyName);
        });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
