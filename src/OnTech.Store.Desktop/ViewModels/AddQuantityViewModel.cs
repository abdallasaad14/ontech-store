using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>Add received stock to an item that already exists.</summary>
public sealed partial class AddQuantityViewModel(Session session) : PageViewModel(session)
{
    public ObservableCollection<CategoryDto> Categories { get; } = new();
    public ObservableCollection<ItemDto> Items { get; } = new();

    [ObservableProperty] private CategoryDto? _selectedCategory;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary))] private ItemDto? _selectedItem;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary))] private string _quantityText = "";
    [ObservableProperty] private string? _note;

    public string? Summary => SelectedItem is { } i
        ? int.TryParse(QuantityText?.Trim(), out var q) && q > 0
            ? L.T("{0}: {1} now, {2} after adding {3}.", i.Name, i.Quantity, i.Quantity + q, q)
            : L.T("{0}: {1} in stock now.", i.Name, i.Quantity)
        : null;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Categories.Clear();
        foreach (var c in await Api.GetCategoriesAsync()) Categories.Add(c);
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
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (SelectedItem is null) throw new ArgumentException(L.T("Choose the item."));
        if (!int.TryParse(QuantityText?.Trim(), out var qty) || qty <= 0) throw new ArgumentException(L.T("Enter a quantity of 1 or more."));
        var updated = await Api.AddQuantityAsync(SelectedItem.Id, qty, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
        QuantityText = "";
        Note = null;
        await LoadItemsAsync();
        SuccessMessage = L.T("Added {0} × {1}. {2} now in stock.", qty, updated.Name, updated.Quantity);
    });
}
