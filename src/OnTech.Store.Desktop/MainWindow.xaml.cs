using System.Windows;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }
}
