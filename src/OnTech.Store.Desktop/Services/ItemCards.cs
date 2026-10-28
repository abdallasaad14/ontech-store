using System.Windows;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;
using OnTech.Store.Desktop.Views;

namespace OnTech.Store.Desktop.Services;

/// <summary>Opens an item's card from any screen. Each card is its own window, so several can be compared side by side.</summary>
public static class ItemCards
{
    public static void Open(Session session, ItemDto? item)
    {
        if (item is null) return;
        new ItemCardWindow(new ItemCardViewModel(session, item)) { Owner = Application.Current.MainWindow }.Show();
    }
}
