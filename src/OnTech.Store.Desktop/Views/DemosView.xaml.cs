using System.Windows;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class DemosView : System.Windows.Controls.UserControl
{
    public DemosView()
    {
        InitializeComponent();
        // The group header already names the customer (or the item), so its column is hidden. Columns can't bind, so set them here.
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DemosViewModel old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is DemosViewModel vm) vm.PropertyChanged += OnVmChanged;
            if (e.OldValue is DemosViewModel o) o.Rows.CollectionChanged -= OnRowsChanged;
            if (e.NewValue is DemosViewModel n) n.Rows.CollectionChanged += OnRowsChanged;
            SyncColumns();
        };
    }

    // WPF bug: when grouped rows arrive in a grid that was laid out empty, every column stays at its 20 px minimum
    // and the rows look empty. Setting the widths again once the rows are in makes the grid measure them anew.
    private bool _refreshQueued;
    private void OnRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _refreshQueued = false;
            foreach (var c in OpenGrid.Columns)
            {
                var width = c.Width;
                c.Width = 0;
                c.Width = width;
            }
        });
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DemosViewModel.GroupMode)) SyncColumns();
    }

    private void SyncColumns()
    {
        var byItem = DataContext is DemosViewModel { GroupMode: 1 };
        CustomerColumn.Visibility = byItem ? Visibility.Visible : Visibility.Collapsed;
        ItemColumn.Visibility = byItem ? Visibility.Collapsed : Visibility.Visible;
    }
}
