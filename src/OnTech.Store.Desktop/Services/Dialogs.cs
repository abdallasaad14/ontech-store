using System.Windows;
using OnTech.Store.Desktop.Views;

namespace OnTech.Store.Desktop.Services;

public static class Dialogs
{
    private static Window? Owner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public static bool Confirm(string message, string? title = null) =>
        MessageBox.Show(Owner!, message, title ?? L.T("OnTech Store"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, L.MessageBoxOptions)
        == MessageBoxResult.Yes;

    /// <summary>Asks for one line of text. Returns null if cancelled.</summary>
    public static string? Prompt(string title, string label, string? initial = null, bool isPassword = false, string? okText = null)
    {
        var dlg = new PromptWindow(title, label, initial, isPassword, okText ?? L.T("OK")) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
