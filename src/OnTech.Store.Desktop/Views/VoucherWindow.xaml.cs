using System.Windows;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

/// <summary>Shows one order or stock-in from the item card.</summary>
public partial class VoucherWindow : Window
{
    public VoucherWindow(VoucherViewModel vm)
    {
        InitializeComponent();
        L.Apply(this);
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
