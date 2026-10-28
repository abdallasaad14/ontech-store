using System.Net;

namespace OnTech.Store.Client;

/// <summary>A failed API call, with the server's readable message (ProblemDetails title) ready to show to the user.</summary>
public sealed class ApiError(HttpStatusCode? status, string message, IReadOnlyList<StockShortage>? shortages = null, Exception? inner = null)
    : Exception(message, inner)
{
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
