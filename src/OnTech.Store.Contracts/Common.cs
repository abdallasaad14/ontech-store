namespace OnTech.Store.Contracts;

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Limits on paged lists (CONTRACTS.md §8). Values outside them are refused with 400.</summary>
public static class Paging
{
    public const int MaxPage = 1_000_000;
    public const int MaxPageSize = 200;

    /// <summary>Rows to skip for a page, computed without overflow.</summary>
    public static int Skip(int page, int pageSize) =>
        (int)Math.Min(int.MaxValue, Math.Max(0L, ((long)page - 1) * pageSize));
}
