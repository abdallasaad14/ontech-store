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

    public IReadOnlyList<PermissionOption> PermissionOptions { get; } =
    [
        new(Permission.ViewStock, "View stock", "See categories, items and quantities"),
        new(Permission.CreateOrders, "New orders", "Take items out of the store for a customer"),
        new(Permission.AddStock, "Add quantity", "Add received stock to existing items"),
        new(Permission.ManageCustomers, "Customers", "Add and edit customers"),
        new(Permission.ViewOrders, "Orders log", "Search and view past orders"),
        new(Permission.CancelOrders, "Cancel orders", "Cancel an order and return its stock"),
        new(Permission.ManageCatalog, "Items & categories", "Add and edit categories and items, stock counts"),
        new(Permission.ManageUsers, "Users", "Add users, set permissions, reset passwords"),
    ];

    [ObservableProperty] private UserDto? _selected;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FormTitle), nameof(IsNew))] private int? _editingId;
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _mustChangePassword = true;

    public bool IsNew => EditingId is null;
    public string FormTitle => IsNew ? "Add user" : "Edit user";

    public static string Describe(UserDto u) => u.IsAdmin ? "Admin (everything)" : PermissionSummary(u.Permissions);

    private static string PermissionSummary(Permission p) => p switch
    {
        Permission.None => "No access",
        Permission.StandardUser => "Standard store user",
        _ => string.Join(", ", Enum.GetValues<Permission>()
            .Where(x => x != Permission.None && x != Permission.StandardUser && x != Permission.All && (p & x) == x)),
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
        FullName = Email = Password = "";
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
        Email = u.Email;
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
        if (string.IsNullOrWhiteSpace(FullName)) throw new ArgumentException("Enter the user's name.");
        if (string.IsNullOrWhiteSpace(Email)) throw new ArgumentException("Enter the user's email.");
        UserDto saved;
        if (EditingId is { } id)
        {
            if (id == Session.User.Id && (!IsActive || (Session.User.IsAdmin && !IsAdmin)))
                throw new ArgumentException("You can't remove your own admin rights or disable your own account.");
            saved = await Api.UpdateUserAsync(id, new UpdateUserRequest
            {
                FullName = FullName.Trim(), Email = Email.Trim(), IsAdmin = IsAdmin, IsActive = IsActive, Permissions = CheckedPermissions(),
            });
        }
        else
        {
            if (Password.Length < 8) throw new ArgumentException("The first password must be at least 8 characters.");
            saved = await Api.CreateUserAsync(new CreateUserRequest
            {
                FullName = FullName.Trim(), Email = Email.Trim(), Password = Password, IsAdmin = IsAdmin,
                Permissions = CheckedPermissions(), MustChangePassword = MustChangePassword,
            });
        }
        IsEditing = false;
        Password = "";
        await ReloadAsync();
        Selected = Users.FirstOrDefault(u => u.Id == saved.Id);
        SuccessMessage = $"{saved.FullName} saved.";
    });

    [RelayCommand]
    private Task ResetPasswordAsync(UserDto? u)
    {
        if (u is null) return Task.CompletedTask;
        var pwd = Dialogs.Prompt($"Reset password: {u.FullName}",
            "New password (at least 8 characters). They will be asked to choose their own at next sign-in.", isPassword: true, okText: "Reset password");
        if (pwd is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            if (pwd.Length < 8) throw new ArgumentException("The password must be at least 8 characters.");
            await Api.ResetPasswordAsync(u.Id, pwd, mustChangePassword: true);
            SuccessMessage = $"Password reset for {u.FullName}.";
        });
    }

    [RelayCommand]
    private Task DisableAsync(UserDto? u)
    {
        if (u is null) return Task.CompletedTask;
        if (u.Id == Session.User.Id)
        {
            ErrorMessage = "You can't disable your own account.";
            return Task.CompletedTask;
        }
        if (!Dialogs.Confirm($"Disable {u.FullName}? They will be signed out on every PC and can't sign in again until enabled.")) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableUserAsync(u.Id);
            IsEditing = false;
            await ReloadAsync();
            SuccessMessage = $"{u.FullName} was disabled.";
        });
    }
}
