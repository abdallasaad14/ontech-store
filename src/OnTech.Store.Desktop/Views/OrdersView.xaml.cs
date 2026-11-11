using System.Windows;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class OrdersView : System.Windows.Controls.UserControl
{
    public OrdersView()
    {
        InitializeComponent();
        // DataGrid columns aren't in the visual tree, so they can't bind to the page: set them when the log type is known.
        DataContextChanged += (_, _) =>
        {
            var demo = DataContext is OrdersViewModel { IsDemoLog: true };
            DemoStateColumn.Visibility = OpenColumn.Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
            ReceiptColumn.Visibility = demo ? Visibility.Collapsed : Visibility.Visible;
        };
    }
}
