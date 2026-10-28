using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>Admin: categories on the left, the selected category's items on the right, one edit form at a time.</summary>
public sealed partial class CatalogViewModel(Session session) : PageViewModel(session)
{
    public enum FormKind { None, Category, Item }

    public ObservableCollection<CategoryDto> Categories { get; } = new();
    public ObservableCollection<ItemDto> Items { get; } = new();

    [ObservableProperty] private bool _showDisabled;
    [ObservableProperty] private CategoryDto? _selectedCategory;
    [ObservableProperty] private ItemDto? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategoryForm), nameof(IsItemForm), nameof(IsFormOpen), nameof(FormTitle), nameof(IsNewItem))]
    private FormKind _form;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(IsNewItem))]
    private int? _editingId;

    public bool IsCategoryForm => Form == FormKind.Category;
    public bool IsItemForm => Form == FormKind.Item;
    public bool IsFormOpen => Form != FormKind.None;
    public bool IsNewItem => IsItemForm && EditingId is null;

    public string FormTitle => L.T((Form, EditingId) switch
    {
        (FormKind.Category, null) => "Add category",
        (FormKind.Category, _) => "Edit category",
        (FormKind.Item, null) => "Add item",
        (FormKind.Item, _) => "Edit item",
        _ => "",
    });

    // Category form
    [ObservableProperty] private string? _categoryCode;
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string? _categoryDescription;
    [ObservableProperty] private bool _categoryActive = true;

    // Item form
    [ObservableProperty] private CategoryDto? _itemCategory;
    [ObservableProperty] private string _itemName = "";
    [ObservableProperty] private string? _itemCode;
    [ObservableProperty] private string? _itemDescription;
    [ObservableProperty] private string _itemLowStock = "0";
    [ObservableProperty] private bool _itemActive = true;

    public override Task LoadAsync() => RunAsync(ReloadCategoriesAsync, clearMessages: false);

    private async Task ReloadCategoriesAsync()
    {
        var keep = SelectedCategory?.Id;
        var list = await Api.GetCategoriesAsync(ShowDisabled);
        Categories.Clear();
        foreach (var c in list) Categories.Add(c);
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == keep) ?? Categories.FirstOrDefault();
    }

    partial void OnShowDisabledChanged(bool value) => _ = RunAsync(ReloadCategoriesAsync);
    partial void OnSelectedCategoryChanged(CategoryDto? value) => _ = RunAsync(ReloadItemsAsync, clearMessages: false);

    private int _itemsVersion;

    private async Task ReloadItemsAsync()
    {
        // Several reloads can overlap (category click + save); only the latest one fills the list.
        var version = ++_itemsVersion;
        var list = SelectedCategory is null ? [] : await Api.GetItemsAsync(SelectedCategory.Id, includeInactive: ShowDisabled);
        if (version != _itemsVersion) return;
        Items.Clear();
        foreach (var i in list) Items.Add(i);
    }

    // ---- Categories ----

    [RelayCommand]
    private void NewCategory()
    {
        EditingId = null;
        CategoryCode = null;
        CategoryName = "";
        CategoryDescription = null;
        CategoryActive = true;
        Form = FormKind.Category;
    }

    [RelayCommand]
    private void EditCategory(CategoryDto? c)
    {
        if (c is null) return;
        EditingId = c.Id;
        CategoryCode = c.Code;
        CategoryName = c.Name;
        CategoryDescription = c.Description;
        CategoryActive = c.IsActive;
        Form = FormKind.Category;
    }

    [RelayCommand]
    private Task DeleteCategoryAsync(CategoryDto? c)
    {
        if (c is null || !Dialogs.Confirm(L.T("Remove the category \"{0}\"? If it still has items it is disabled instead of deleted.", c.Name))) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DeleteCategoryAsync(c.Id);
            Form = FormKind.None;
            await ReloadCategoriesAsync();
            SuccessMessage = L.T("Category \"{0}\" removed.", c.Name);
        });
    }

    // ---- Items ----

    [RelayCommand]
    private void NewItem()
    {
        EditingId = null;
        ItemCategory = SelectedCategory;
        ItemName = "";
        ItemCode = ItemDescription = null;
        ItemLowStock = "0";
        ItemActive = true;
        Form = FormKind.Item;
    }

    [RelayCommand]
    private void EditItem(ItemDto? i)
    {
        if (i is null) return;
        EditingId = i.Id;
        ItemCategory = Categories.FirstOrDefault(c => c.Id == i.CategoryId);
        ItemName = i.Name;
        ItemCode = i.Code;
        ItemDescription = i.Description;
        ItemLowStock = i.LowStockThreshold.ToString();
        ItemActive = i.IsActive;
        Form = FormKind.Item;
    }

    [RelayCommand]
    private void OpenCard(ItemDto? i)
    {
        if (i is null) return;
        new Views.ItemCardWindow(new ItemCardViewModel(Session, i)) { Owner = System.Windows.Application.Current.MainWindow }.Show();
    }

    [RelayCommand]
    private Task DisableItemAsync(ItemDto? i)
    {
        if (i is null || !Dialogs.Confirm(L.T("Disable \"{0}\"? It stays in old orders but can't be ordered or restocked.", i.Name))) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableItemAsync(i.Id);
            Form = FormKind.None;
            await ReloadCategoriesAsync();
            await ReloadItemsAsync();
            SuccessMessage = L.T("\"{0}\" was disabled.", i.Name);
        });
    }

    // ---- Shared form ----

    [RelayCommand] private void CancelForm() => Form = FormKind.None;

    [RelayCommand]
    private Task SaveFormAsync() => RunAsync(async () =>
    {
        if (Form == FormKind.Category) await SaveCategoryAsync();
        else if (Form == FormKind.Item) await SaveItemAsync();
    });

    private async Task SaveCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CategoryName)) throw new ArgumentException(L.T("Enter the category name."));
        var req = new SaveCategoryRequest
        {
            Code = string.IsNullOrWhiteSpace(CategoryCode) ? null : CategoryCode.Trim(),
            Name = CategoryName.Trim(),
            Description = string.IsNullOrWhiteSpace(CategoryDescription) ? null : CategoryDescription.Trim(),
            IsActive = CategoryActive,
        };
        var saved = EditingId is { } id ? await Api.UpdateCategoryAsync(id, req) : await Api.CreateCategoryAsync(req);
        Form = FormKind.None;
        await ReloadCategoriesAsync();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == saved.Id) ?? SelectedCategory;
        SuccessMessage = L.T("Category \"{0}\" saved.", saved.Name);
    }

    private async Task SaveItemAsync()
    {
        if (ItemCategory is null) throw new ArgumentException(L.T("Choose the category."));
        if (string.IsNullOrWhiteSpace(ItemName)) throw new ArgumentException(L.T("Enter the item name."));
        if (!int.TryParse(ItemLowStock?.Trim(), out var low) || low < 0) throw new ArgumentException(L.T("Low stock warning must be 0 or more."));
        ItemDto saved;
        if (EditingId is { } id)
        {
            saved = await Api.UpdateItemAsync(id, new UpdateItemRequest
            {
                CategoryId = ItemCategory.Id,
                Name = ItemName.Trim(),
                Code = string.IsNullOrWhiteSpace(ItemCode) ? null : ItemCode.Trim(),
                Description = string.IsNullOrWhiteSpace(ItemDescription) ? null : ItemDescription.Trim(),
                LowStockThreshold = low,
                IsActive = ItemActive,
            });
        }
        else
        {
            saved = await Api.CreateItemAsync(new CreateItemRequest
            {
                CategoryId = ItemCategory.Id,
                Name = ItemName.Trim(),
                Code = string.IsNullOrWhiteSpace(ItemCode) ? null : ItemCode.Trim(),
                Description = string.IsNullOrWhiteSpace(ItemDescription) ? null : ItemDescription.Trim(),
                LowStockThreshold = low,
            });
        }
        Form = FormKind.None;
        await ReloadCategoriesAsync();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == saved.CategoryId) ?? SelectedCategory;
        await ReloadItemsAsync();
        SelectedItem = Items.FirstOrDefault(i => i.Id == saved.Id);
        SuccessMessage = L.T("\"{0}\" saved.", saved.Name);
    }
}
