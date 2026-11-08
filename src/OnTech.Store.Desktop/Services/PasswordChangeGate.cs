namespace OnTech.Store.Desktop.Services;

/// <summary>
/// Opens the "choose your own password" window when the server answers 403 MustChangePassword, at most once at a time
/// (several calls can fail together when a page loads). After a successful change the page is loaded again; if the user
/// closes the window instead, they are signed out, the same as at sign-in.
/// </summary>
public sealed class PasswordChangeGate(Func<bool> askForNewPassword, Action onChanged, Action onRefused)
{
    private bool _open;

    public bool IsOpen => _open;

    public void Request()
    {
        if (_open) return;
        _open = true;
        bool changed;
        try { changed = askForNewPassword(); }
        finally { _open = false; }
        if (changed) onChanged();
        else onRefused();
    }
}
