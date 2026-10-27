namespace OnTech.Store.Api.Infrastructure;

/// <summary>An expected failure that maps to an HTTP status and a readable message for the desktop app.</summary>
public class ApiException(int statusCode, string message, IDictionary<string, object?>? extensions = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public IDictionary<string, object?>? Extensions { get; } = extensions;

    public static ApiException NotFound(string what) => new(StatusCodes.Status404NotFound, $"{what} was not found.");
    public static ApiException Conflict(string message, IDictionary<string, object?>? ext = null) => new(StatusCodes.Status409Conflict, message, ext);
    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        int status;
        string title;
        IDictionary<string, object?>? ext = null;
        if (exception is ApiException api)
        {
            status = api.StatusCode;
            title = api.Message;
            ext = api.Extensions;
        }
        else
        {
            logger.LogError(exception, "Unhandled error on {Path}", context.Request.Path);
            status = StatusCodes.Status500InternalServerError;
            title = "Something went wrong on the server. Please try again.";
        }

        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = title };
        if (ext != null)
            foreach (var (k, v) in ext) problem.Extensions[k] = v;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}

/// <summary>Turns expected <see cref="ApiException"/>s into ProblemDetails inside MVC, so they aren't logged as server errors.</summary>
public sealed class ApiExceptionFilter : Microsoft.AspNetCore.Mvc.Filters.IExceptionFilter
{
    public void OnException(Microsoft.AspNetCore.Mvc.Filters.ExceptionContext context)
    {
        if (context.Exception is not ApiException api) return;
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = api.StatusCode, Title = api.Message };
        if (api.Extensions != null)
            foreach (var (k, v) in api.Extensions) problem.Extensions[k] = v;
        context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(problem) { StatusCode = api.StatusCode };
        context.ExceptionHandled = true;
    }
}
