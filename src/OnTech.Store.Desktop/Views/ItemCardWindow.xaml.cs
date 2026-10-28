using System.Collections.Specialized;
using System.Windows;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

/// <summary>Shows one item's card. Opened from Items &amp; Categories.</summary>
public partial class ItemCardWindow : Window
{
    public ItemCardWindow(ItemCardViewModel vm)
    {
        InitializeComponent();
        L.Apply(this);
        DataContext = vm;
        // The newest movement is at the bottom, like a paper card; keep it in view.
        vm.Lines.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && vm.Lines.Count > 0) LinesGrid.ScrollIntoView(vm.Lines[^1]);
        };
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
