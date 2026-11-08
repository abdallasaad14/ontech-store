using Microsoft.EntityFrameworkCore;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Infrastructure;

/// <summary>An expected failure that maps to an HTTP status and a readable message for the desktop app.</summary>
public class ApiException(int statusCode, string message, IDictionary<string, object?>? extensions = null) : Exception(message)
{
    /// <summary>Title of the 409 when an edit was made from a form loaded before someone else saved the same record.</summary>
    public const string ConcurrencyConflictMessage = "This record was changed on another device. It has been reloaded; check it and save again.";

    public int StatusCode { get; } = statusCode;
    public IDictionary<string, object?>? Extensions { get; } = extensions;

    public static ApiException NotFound(string what) => new(StatusCodes.Status404NotFound, $"{what} was not found.");
    public static ApiException Conflict(string message, IDictionary<string, object?>? ext = null) => new(StatusCodes.Status409Conflict, message, ext);
    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);

    /// <summary>409 with code <see cref="ErrorCodes.Duplicate"/>: a number, code, voucher or name that must be unique is already used.</summary>
    public static ApiException Duplicate(string message) => Conflict(message, Code(ErrorCodes.Duplicate));

    /// <summary>409 with code <see cref="ErrorCodes.ConcurrencyConflict"/>.</summary>
    public static ApiException ConcurrencyConflict() => Conflict(ConcurrencyConflictMessage, Code(ErrorCodes.ConcurrencyConflict));

    /// <summary>Throws <see cref="ConcurrencyConflict"/> when the form sent a version and it is not the stored one. Null = no check.</summary>
    public static void ThrowIfStale(int? sentVersion, int storedVersion)
    {
        if (sentVersion is { } v && v != storedVersion) throw ConcurrencyConflict();
    }

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };
}

/// <summary>Recognises the database errors that are really "someone else got there first", on SQLite and SQL Server.</summary>
public static class DbErrors
{
    /// <summary>True when the save failed on a unique index (SQLite 2067/1555, SQL Server 2601/2627).</summary>
    public static bool IsUniqueViolation(this DbUpdateException ex)
    {
        for (Exception? e = ex.InnerException; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case Microsoft.Data.Sqlite.SqliteException s when s.SqliteErrorCode == 19 && s.SqliteExtendedErrorCode is 2067 or 1555:
                case Microsoft.Data.SqlClient.SqlException q when q.Number is 2601 or 2627:
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Saves, and turns a lost race into a readable 409: a concurrency-token mismatch becomes ConcurrencyConflict,
    /// a unique-index violation becomes Duplicate with <paramref name="duplicateMessage"/> (or a general message).
    /// </summary>
    public static Task<int> SaveOrConflictAsync(this DbContext db, CancellationToken ct, Func<string>? duplicateMessage = null) =>
        db.SaveOrConflictAsync(ct, duplicateMessage is null ? null : () => Task.FromResult(duplicateMessage()));

    /// <summary>As above; <paramref name="duplicateMessage"/> may query the database to tell which unique value was taken.</summary>
    public static async Task<int> SaveOrConflictAsync(this DbContext db, CancellationToken ct, Func<Task<string>>? duplicateMessage)
    {
        try { return await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ApiException.ConcurrencyConflict(); }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw ApiException.Duplicate(duplicateMessage is null ? ApiExceptionHandler.DuplicateMessage : await duplicateMessage());
        }
    }
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    /// <summary>Safety-net title for a unique-index violation that no controller turned into a specific message.</summary>
    public const string DuplicateMessage = "This number or code is already used. Reload and try again.";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // A lost race on the database is not a server bug: answer 409 like the controllers do.
        if (exception is DbUpdateConcurrencyException) exception = ApiException.ConcurrencyConflict();
        else if (exception is DbUpdateException db && db.IsUniqueViolation()) exception = ApiException.Duplicate(DuplicateMessage);

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
        var api = context.Exception switch
        {
            ApiException a => a,
            DbUpdateConcurrencyException => ApiException.ConcurrencyConflict(),
            DbUpdateException db when db.IsUniqueViolation() => ApiException.Duplicate(ApiExceptionHandler.DuplicateMessage),
            _ => null,
        };
        if (api is null) return;
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = api.StatusCode, Title = api.Message };
        if (api.Extensions != null)
            foreach (var (k, v) in api.Extensions) problem.Extensions[k] = v;
        context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(problem) { StatusCode = api.StatusCode };
        context.ExceptionHandled = true;
    }
}
