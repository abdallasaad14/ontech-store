using CommunityToolkit.Mvvm.ComponentModel;
using OnTech.Store.Client;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _successMessage;

    /// <summary>Runs a server call: shows the busy state, and turns failures into <see cref="ErrorMessage"/>.</summary>
    protected async Task<bool> RunAsync(Func<Task> work, bool clearMessages = true)
    {
        if (clearMessages)
        {
            ErrorMessage = null;
            SuccessMessage = null;
        }
        IsBusy = true;
        try
        {
            await work();
            return true;
        }
        catch (ApiError ex)
        {
            ErrorMessage = ex.Message;
            OnApiError(ex);
            return false;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Unexpected error: " + ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected virtual void OnApiError(ApiError error) { }
}

/// <summary>A screen shown in the main window's content area.</summary>
public abstract class PageViewModel(Session session) : ViewModelBase
{
    protected Session Session { get; } = session;
    protected StoreApiClient Api => Session.Api;

    /// <summary>Called each time the page is opened.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    protected override void OnApiError(ApiError error)
    {
        if (error.IsUnauthorized) Session.NotifyExpired();
    }
}
