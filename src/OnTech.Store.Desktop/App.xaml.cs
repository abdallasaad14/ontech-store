using System.Windows;
using System.Windows.Threading;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using OnTech.Store.Desktop.Views;

namespace OnTech.Store.Desktop;

public partial class App : Application
{
    private readonly AppSettings _settings = AppSettings.Load();
    private Session? _session;
    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        L.Set(_settings.Language);
        DispatcherUnhandledException += OnUnhandledException;
        ShowLogin();
    }

    private void ShowLogin(LoginViewModel? previous = null)
    {
        var vm = new LoginViewModel(_settings, previous);
        var login = new LoginWindow(vm);
        if (login.ShowDialog() == true && vm.Result is { } session)
            StartSession(session);
        else if (vm.LanguageChanged)
            ShowLogin(vm); // reopen in the new language, keeping what was typed
        else
            Shutdown();
    }

    private void StartSession(Session session)
    {
        if (session.User.MustChangePassword)
        {
            var change = new ChangePasswordWindow(new ChangePasswordViewModel(session, isRequired: true))
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
            };
            if (change.ShowDialog() != true)
            {
                session.Api.Logout();
                ShowLogin();
                return;
            }
        }

        _session = session;
        session.Expired += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_session == session)
                BackToLogin(L.T("Your sign-in has ended (it expired, or your account was changed by an admin). Please sign in again."));
        });
        ShowMain(Page.Home);
    }

    /// <summary>Opens the main window, replacing the current one (after a language change) in the same place.</summary>
    private void ShowMain(Page page)
    {
        var session = _session!;
        var shell = new ShellViewModel(session, page);
        var main = new MainWindow(shell);

        shell.SignOutRequested += (_, _) => BackToLogin(null);
        shell.ChangePasswordRequested += (_, _) =>
            new ChangePasswordWindow(new ChangePasswordViewModel(session, isRequired: false)) { Owner = main }.ShowDialog();
        shell.LanguageChangeRequested += (_, _) =>
        {
            L.Switch(_settings);
            ShowMain(shell.CurrentPageKind);
        };
        main.Closed += (_, _) =>
        {
            if (_main == main) Shutdown();
        };

        var old = _main;
        if (old is not null)
        {
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            var bounds = old.RestoreBounds;
            (main.Left, main.Top, main.Width, main.Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            main.WindowState = old.WindowState == WindowState.Minimized ? WindowState.Normal : old.WindowState;
        }
        _main = main;
        MainWindow = main;
        main.Show();
        old?.Close();
    }

    private void BackToLogin(string? message)
    {
        if (_main is not { } main || _session is not { } session) return;
        _main = null;
        _session = null;
        session.Api.Logout();
        main.Close();
        if (message is not null)
            MessageBox.Show(message, L.T("OnTech Store"), MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, L.MessageBoxOptions);
        ShowLogin();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(L.T("Something went wrong:") + "\n\n" + e.Exception.Message, L.T("OnTech Store"),
            MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, L.MessageBoxOptions);
        e.Handled = true;
    }
}
