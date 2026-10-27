using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string? _phoneNumber;
    [ObservableProperty] private string? _taxNumber;
    [ObservableProperty] private string? _contactPerson;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _isActive = true;

    public string FormTitle => EditingId is null ? "Add customer" : "Edit customer";
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
        if (string.IsNullOrWhiteSpace(CompanyName)) throw new ArgumentException("Company name is required.");
        var req = new SaveCustomerRequest
        {
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
        SuccessMessage = EditingId is null ? $"Customer \"{saved.CompanyName}\" added." : $"Customer \"{saved.CompanyName}\" saved.";
    });

    [RelayCommand]
    private Task DisableAsync(CustomerDto? c)
    {
        if (c is null || !Dialogs.Confirm($"Disable {c.CompanyName}? Their past orders stay in the log, but they can't be picked for new orders.")) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableCustomerAsync(c.Id);
            IsEditing = false;
            await ReloadAsync();
            SuccessMessage = $"{c.CompanyName} was disabled.";
        });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
