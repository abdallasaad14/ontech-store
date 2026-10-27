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

    public string FormTitle => (Form, EditingId) switch
    {
        (FormKind.Category, null) => "Add category",
        (FormKind.Category, _) => "Edit category",
        (FormKind.Item, null) => "Add item",
        (FormKind.Item, _) => "Edit item",
        _ => "",
    };

    // Category form
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string? _categoryDescription;
    [ObservableProperty] private bool _categoryActive = true;

    // Item form
    [ObservableProperty] private CategoryDto? _itemCategory;
    [ObservableProperty] private string _itemName = "";
    [ObservableProperty] private string? _itemCode;
    [ObservableProperty] private string? _itemDescription;
    [ObservableProperty] private string _itemInitialQuantity = "0";
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
        CategoryName = c.Name;
        CategoryDescription = c.Description;
        CategoryActive = c.IsActive;
        Form = FormKind.Category;
    }

    [RelayCommand]
    private Task DeleteCategoryAsync(CategoryDto? c)
    {
        if (c is null || !Dialogs.Confirm($"Remove the category \"{c.Name}\"? If it still has items it is disabled instead of deleted.")) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DeleteCategoryAsync(c.Id);
            Form = FormKind.None;
            await ReloadCategoriesAsync();
            SuccessMessage = $"Category \"{c.Name}\" removed.";
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
        ItemInitialQuantity = "0";
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
    private Task DisableItemAsync(ItemDto? i)
    {
        if (i is null || !Dialogs.Confirm($"Disable \"{i.Name}\"? It stays in old orders but can't be ordered or restocked.")) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Api.DisableItemAsync(i.Id);
            Form = FormKind.None;
            await ReloadCategoriesAsync();
            await ReloadItemsAsync();
            SuccessMessage = $"\"{i.Name}\" was disabled.";
        });
    }

    [RelayCommand]
    private Task AdjustItemAsync(ItemDto? i)
    {
        if (i is null) return Task.CompletedTask;
        var counted = Dialogs.Prompt($"Stock count: {i.Name}", $"The system shows {i.Quantity}. How many did you count?", i.Quantity.ToString(), okText: "Next");
        if (counted is null) return Task.CompletedTask;
        if (!int.TryParse(counted.Trim(), out var newQty) || newQty < 0)
        {
            ErrorMessage = "Enter a whole number of 0 or more.";
            return Task.CompletedTask;
        }
        var reason = Dialogs.Prompt($"Stock count: {i.Name}", "Reason for the correction (for example \"Monthly count\" or \"Damaged unit\")", okText: "Save count");
        if (string.IsNullOrWhiteSpace(reason)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var updated = await Api.AdjustQuantityAsync(i.Id, newQty, reason.Trim());
            await ReloadCategoriesAsync();
            await ReloadItemsAsync();
            SuccessMessage = $"\"{updated.Name}\" set to {updated.Quantity}.";
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
        if (string.IsNullOrWhiteSpace(CategoryName)) throw new ArgumentException("Enter the category name.");
        var req = new SaveCategoryRequest
        {
            Name = CategoryName.Trim(),
            Description = string.IsNullOrWhiteSpace(CategoryDescription) ? null : CategoryDescription.Trim(),
            IsActive = CategoryActive,
        };
        var saved = EditingId is { } id ? await Api.UpdateCategoryAsync(id, req) : await Api.CreateCategoryAsync(req);
        Form = FormKind.None;
        await ReloadCategoriesAsync();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == saved.Id) ?? SelectedCategory;
        SuccessMessage = $"Category \"{saved.Name}\" saved.";
    }

    private async Task SaveItemAsync()
    {
        if (ItemCategory is null) throw new ArgumentException("Choose the category.");
        if (string.IsNullOrWhiteSpace(ItemName)) throw new ArgumentException("Enter the item name.");
        if (!int.TryParse(ItemLowStock?.Trim(), out var low) || low < 0) throw new ArgumentException("Low stock warning must be 0 or more.");
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
            if (!int.TryParse(ItemInitialQuantity?.Trim(), out var initial) || initial < 0) throw new ArgumentException("Starting quantity must be 0 or more.");
            saved = await Api.CreateItemAsync(new CreateItemRequest
            {
                CategoryId = ItemCategory.Id,
                Name = ItemName.Trim(),
                Code = string.IsNullOrWhiteSpace(ItemCode) ? null : ItemCode.Trim(),
                Description = string.IsNullOrWhiteSpace(ItemDescription) ? null : ItemDescription.Trim(),
                InitialQuantity = initial,
                LowStockThreshold = low,
            });
        }
        Form = FormKind.None;
        await ReloadCategoriesAsync();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == saved.CategoryId) ?? SelectedCategory;
        await ReloadItemsAsync();
        SelectedItem = Items.FirstOrDefault(i => i.Id == saved.Id);
        SuccessMessage = $"\"{saved.Name}\" saved.";
    }
}
