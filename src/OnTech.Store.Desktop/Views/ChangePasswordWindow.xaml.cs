using System.Windows;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _vm;

    public ChangePasswordWindow(ChangePasswordViewModel vm)
    {
        InitializeComponent();
        L.Apply(this);
        DataContext = _vm = vm;
        vm.Done += (_, _) => DialogResult = true;
        Loaded += (_, _) => Current.Focus();
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        _vm.CurrentPassword = Current.Password;
        _vm.NewPassword = New.Password;
        _vm.ConfirmPassword = Confirm.Password;
    }
}
