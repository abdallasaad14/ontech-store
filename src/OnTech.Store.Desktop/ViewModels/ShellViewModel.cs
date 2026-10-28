using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public enum Page { Home, NewOrder, AddQuantity, Customers, Orders, Catalog, Users }

public sealed record NavItem(Page Page, string Title, string Glyph);

/// <summary>The main window: sidebar menu (filtered by the user's permissions) and the open page.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Session _session;

    public ShellViewModel(Session session, Page startPage = Page.Home)
    {
        _session = session;
        Add(MainMenu, Page.Home, "Home", "\uE80F", null);
        Add(MainMenu, Page.NewOrder, "New Order", "\uE7BF", Permission.CreateOrders);
        Add(MainMenu, Page.AddQuantity, "Add Quantity", "\uE710", Permission.AddStock);
        Add(MainMenu, Page.Customers, "Customers", "\uE716", null);
        Add(MainMenu, Page.Orders, "Orders Log", "\uE8A5", Permission.ViewOrders);
        Add(AdminMenu, Page.Catalog, "Items & Categories", "\uE8F1", Permission.ManageCatalog);
        Add(AdminMenu, Page.Users, "Users", "\uE77B", Permission.ManageUsers);
        Navigate(MainMenu.Concat(AdminMenu).Any(i => i.Page == startPage) ? startPage : Page.Home);
    }

    private void Add(ObservableCollection<NavItem> menu, Page page, string title, string glyph, Permission? needs)
    {
        if (needs is null || _session.Can(needs.Value)) menu.Add(new NavItem(page, L.T(title), glyph));
    }

    public ObservableCollection<NavItem> MainMenu { get; } = new();
    public ObservableCollection<NavItem> AdminMenu { get; } = new();
    public bool HasAdminMenu => AdminMenu.Count > 0;

    public string UserName => _session.User.FullName;
    public string UserRole => L.T(_session.User.IsAdmin ? "Admin" : "Store user");
    public string OtherLanguage => L.OtherLanguageName;
    public string UserInitials => string.Concat(_session.User.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    public string ServerAddress => _session.Api.ServerUrl?.Authority ?? "";

    [ObservableProperty] private PageViewModel? _currentPage;
    public Page CurrentPageKind { get; private set; }
    [ObservableProperty] private NavItem? _selectedMainItem;
    [ObservableProperty] private NavItem? _selectedAdminItem;

    private bool _syncingSelection;

    partial void OnSelectedMainItemChanged(NavItem? value)
    {
        if (value is not null && !_syncingSelection) Navigate(value.Page);
    }

    partial void OnSelectedAdminItemChanged(NavItem? value)
    {
        if (value is not null && !_syncingSelection) Navigate(value.Page);
    }

    public event EventHandler? SignOutRequested;
    public event EventHandler? ChangePasswordRequested;
    public event EventHandler? LanguageChangeRequested;

    [RelayCommand]
    public void Navigate(Page page) => Open(page, null);

    /// <summary>Opens a page; <paramref name="setup"/> can prepare it (e.g. open the "new customer" form).</summary>
    public void Open(Page page, Action<PageViewModel>? setup)
    {
        PageViewModel vm = page switch
        {
            Page.Home => new HomeViewModel(_session, this),
            Page.NewOrder => new NewOrderViewModel(_session, this),
            Page.AddQuantity => new AddQuantityViewModel(_session),
            Page.Customers => new CustomersViewModel(_session),
            Page.Orders => new OrdersViewModel(_session),
            Page.Catalog => new CatalogViewModel(_session),
            Page.Users => new UsersViewModel(_session),
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
        setup?.Invoke(vm);
        CurrentPage = vm;
        CurrentPageKind = page;

        _syncingSelection = true;
        SelectedMainItem = MainMenu.FirstOrDefault(i => i.Page == page);
        SelectedAdminItem = AdminMenu.FirstOrDefault(i => i.Page == page);
        _syncingSelection = false;

        _ = vm.LoadAsync();
    }

    public bool Can(Permission p) => _session.Can(p);

    [RelayCommand]
    private void SignOut() => SignOutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChangePassword() => ChangePasswordRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SwitchLanguage()
    {
        if (CurrentPage is NewOrderViewModel { Lines.Count: > 0 } && !Dialogs.Confirm(L.T("Changing the language reopens the window and clears this order. Continue?")))
            return;
        LanguageChangeRequested?.Invoke(this, EventArgs.Empty);
    }
}
