using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class StockInsView : System.Windows.Controls.UserControl
{
    public StockInsView()
    {
        InitializeComponent();
        // DataGrid columns aren't in the visual tree, so they can't bind to the page: set them when the log type is known.
        DataContextChanged += (_, _) =>
            PartyColumn.Header = DataContext is StockInsViewModel { IsDemoReturnLog: true } ? L.T("CUSTOMER") : L.T("SUPPLIER");
    }
}
