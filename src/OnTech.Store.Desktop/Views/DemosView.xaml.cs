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
            SyncColumns();
        };
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
