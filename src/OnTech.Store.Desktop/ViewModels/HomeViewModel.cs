using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public sealed partial class HomeViewModel(Session session, ShellViewModel shell) : PageViewModel(session)
{
    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var part = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
            return L.T(part + ", {0}", Session.User.FullName.Split(' ')[0]);
        }
    }

    public string Today => DateTime.Now.ToString("dddd, d MMMM yyyy");

    public bool CanCreateOrders => Session.Can(Permission.CreateOrders);
    public bool CanAddStock => Session.Can(Permission.AddStock);
    public bool CanManageCustomers => Session.Can(Permission.ManageCustomers);
    public bool CanViewStock => Session.Can(Permission.ViewStock);

    [ObservableProperty] private DashboardSummaryDto? _summary;
    [ObservableProperty] private string? _search;
    [ObservableProperty] private CategoryDto? _selectedCategory;

    public ObservableCollection<CategoryDto> Categories { get; } = new();
    public ObservableCollection<ItemDto> Items { get; } = new();

    private static readonly CategoryDto AllCategories = new() { Id = 0, Name = L.T("All categories") };
    private bool _loading;

    public override async Task LoadAsync()
    {
        if (!CanViewStock) return;
        await RunAsync(async () =>
        {
            var categoriesTask = Api.GetCategoriesAsync();
            Summary = await Api.GetDashboardAsync();
            Categories.Clear();
            Categories.Add(AllCategories);
            foreach (var c in await categoriesTask) Categories.Add(c);
            _loading = true;
            SelectedCategory = AllCategories;
            _loading = false;
            await LoadItemsAsync();
        });
    }

    partial void OnSelectedCategoryChanged(CategoryDto? value)
    {
        if (!_loading) _ = RefreshAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(async () =>
    {
        Summary = await Api.GetDashboardAsync();
        await LoadItemsAsync();
    });

    private async Task LoadItemsAsync()
    {
        var categoryId = SelectedCategory is { Id: > 0 } c ? c.Id : (int?)null;
        var items = await Api.GetItemsAsync(categoryId, Search);
        Items.Clear();
        foreach (var i in items) Items.Add(i);
    }

    [RelayCommand] private void NewOrder() => shell.Navigate(Page.NewOrder);
    [RelayCommand] private void StockIn() => shell.Navigate(Page.StockIn);
    [RelayCommand] private void AddCustomer() => shell.Open(Page.Customers, vm => ((CustomersViewModel)vm).StartNew());

    [RelayCommand]
    private void OrderItem(ItemDto? item)
    {
        if (item is null || !CanCreateOrders) return;
        shell.Open(Page.NewOrder, vm => ((NewOrderViewModel)vm).PreselectItemId = item.Id);
    }
}
