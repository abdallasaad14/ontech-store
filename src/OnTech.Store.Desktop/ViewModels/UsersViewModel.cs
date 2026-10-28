using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public sealed partial class PermissionOption(Permission permission, string title, string description) : ObservableObject
{
    public Permission Permission { get; } = permission;
    public string Title { get; } = title;
    public string Description { get; } = description;
    [ObservableProperty] private bool _isChecked;
}

public sealed partial class UsersViewModel(Session session) : PageViewModel(session)
{
    public ObservableCollection<UserDto> Users { get; } = new();

    private static readonly (Permission Permission, string Title, string Description)[] PermissionTexts =
    [
        (Permission.ViewStock, "View stock", "See categories, items and quantities"),
        (Permission.CreateOrders, "New orders", "Take items out of the store for a customer"),
        (Permission.AddStock, "Add quantity", "Add received stock to existing items"),
        (Permission.ManageCustomers, "Customers", "Add and edit customers"),
        (Permission.ViewOrders, "Orders log", "Search and view past orders"),
        (Permission.CancelOrders, "Cancel orders", "Cancel an order and return its stock"),
        (Permission.ManageCatalog, "Items & categories", "Add and edit categories and items, stock counts"),
        (Permission.ManageUsers, "Users", "Add users, set permissions, reset passwords"),
    ];

    public IReadOnlyList<PermissionOption> PermissionOptions { get; } =
        PermissionTexts.Select(p => new PermissionOption(p.Permission, L.T(p.Title), L.T(p.Description))).ToList();

    [ObservableProperty] private UserDto? _selected;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FormTitle), nameof(IsNew))] private int? _editingId;
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _mustChangePassword = true;

    public bool IsNew => EditingId is null;
    public string FormTitle => L.T(IsNew ? "Add user" : "Edit user");

    public static string Describe(UserDto u) => u.IsAdmin ? L.T("Admin (everything)") : PermissionSummary(u.Permissions);

    private static string PermissionSummary(Permission p) => p switch
    {
        Permission.None => L.T("No access"),
        Permission.StandardUser => L.T("Standard store user"),
        _ => string.Join(L.IsArabic ? "، " : ", ", PermissionTexts.Where(x => (p & x.Permission) == x.Permission).Select(x => L.T(x.Title))),
    };

    public override Task LoadAsync() => RunAsync(ReloadAsync, clearMessages: false);

    private async Task ReloadAsync()
    {
        var list = await Api.GetUsersAsync();
        Users.Clear();
        foreach (var u in list) Users.Add(u);
    }

    [RelayCommand]
    private void NewUser()
    {
        EditingId = null;
        FullName = Username = Password = "";
        IsAdmin = false;
        IsActive = true;
        MustChangePassword = true;
        SetPermissions(Permission.StandardUser);
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit(UserDto? u)
    {
        if (u is null) return;
        EditingId = u.Id;
        FullName = u.FullName;
        Username = u.Username;
        Password = "";
        IsAdmin = u.IsAdmin;
        IsActive = u.IsActive;
        SetPermissions(u.Permissions);
        IsEditing = true;
    }

    [RelayCommand] private void UseStandardPermissions() => SetPermissions(Permission.StandardUser);

    private void SetPermissions(Permission p)
    {
        foreach (var o in PermissionOptions) o.IsChecked = (p & o.Permission) == o.Permission;
    }

    private Permission CheckedPermissions() =>
        PermissionOptions.Where(o => o.IsChecked).Aggregate(Permission.None, (acc, o) => acc | o.Permission);

    [RelayCommand] private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(FullName)) throw new ArgumentException(L.T("Enter the user's name."));
        if (string.IsNullOrWhiteSpace(Username)) throw new ArgumentException(L.T("Enter a username."));
        if (!System.Text.RegularExpressions.Regex.IsMatch(Username.Trim(), UserNames.Pattern)) throw new ArgumentException(L.T(UserNames.Rule));
        UserDto saved;
        if (EditingId is { } id)
        {
            if (id == Session.User.Id && (!IsActive || (Session.User.IsAdmin && !IsAdmin)))
                throw new ArgumentException(L.T("You can't remove your own admin rights or disable your own account."));
            saved = await Api.UpdateUserAsync(id, new UpdateUserRequest
            {
                FullName = FullName.Trim(), Username = Username.Trim(), IsAdmin = IsAdmin, IsActive = IsActive, Permissions = CheckedPermissions(),
            });
        }
        else
        {
            if (Password.Length < 8) throw new ArgumentException(L.T("The first password must be at least 8 characters."));
            saved = await Api.CreateUserAsync(new CreateUserRequest
            {
                FullName = FullName.Trim(), Username = Username.Trim(), Password = Password, IsAdmin = IsAdmin,
                Permissions = CheckedPermissions(), MustChangePassword = MustChangePassword,
            });
        }
        IsEditing = false;
        Password = "";
        await ReloadAsync();
        Selected = Users.FirstOrDefault(u => u.Id == saved.Id);
        SuccessMessage = L.T("{0} saved.", saved.FullName);
    });

    [RelayCommand]
    private Task ResetPasswordAsync(UserDto? u)
    {
        if (u is null) return Task.CompletedTask;
        var pwd = Dialogs.Prompt(L.T("Reset password: {0}", u.FullName),
            L.T("New password (at least 8 characters). They will be asked to choose their own at next sign-in."), isPassword: true, okText: L.T("Reset password"));
        if (pwd is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            if (pwd.Length < 8) throw new ArgumentException(L.T("The password must be at least 8 characters."));
            await Api.ResetPasswordAsync(u.Id, pwd, mustChangePassword: true);
            SuccessMessage = L.T("Password reset for {0}.", u.FullName);
        });
    }

    [RelayCommand]
    private Task DisableAsync(UserDto? u)
    {
        if (u is null) return Task.CompletedTask;
        if (u.Id == Session.User.Id)
        {
            ErrorMessage = L.T("You can't disable your own account.");
            return Task.CompletedTask;
        }
        if (!Dialogs.Confirm(L.T("Disable {0}? They will be signed out on every PC and can't sign in again until enabled.", u.FullName))) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableUserAsync(u.Id);
            IsEditing = false;
            await ReloadAsync();
            SuccessMessage = L.T("{0} was disabled.", u.FullName);
        });
    }
}
