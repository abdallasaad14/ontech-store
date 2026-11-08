using System.Net;

namespace OnTech.Store.Client;

/// <summary>A failed API call, with the server's readable message (ProblemDetails title) ready to show to the user.</summary>
public sealed class ApiError(HttpStatusCode? status, string message, IReadOnlyList<StockShortage>? shortages = null, Exception? inner = null,
    string? code = null, IReadOnlyList<FieldError>? fieldErrors = null)
    : Exception(message, inner)
{
    /// <summary>Validation errors (400 with an "errors" dictionary), one per message, ready to show with the field's label.</summary>
    public IReadOnlyList<FieldError> FieldErrors { get; } = fieldErrors ?? Array.Empty<FieldError>();
    /// <summary>The machine-readable "code" extension of the ProblemDetails body (see <c>OnTech.Store.Contracts.ErrorCodes</c>), or null.</summary>
    public string? Code { get; } = code;
    /// <summary>Null when the server could not be reached at all.</summary>
    public HttpStatusCode? Status { get; } = status;
    public IReadOnlyList<StockShortage> Shortages { get; } = shortages ?? Array.Empty<StockShortage>();
    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;
    public bool IsConnectionError => Status is null;
}

public sealed class StockShortage
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public int Requested { get; init; }
    public int Available { get; init; }
}
