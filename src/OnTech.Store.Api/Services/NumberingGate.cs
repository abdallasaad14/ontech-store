namespace OnTech.Store.Api.Services;

/// <summary>
/// Hands out "next number" values (customer numbers, category and item codes) one request at a time.
/// Without it, two PCs saving at the same moment read the same highest number and get the same next one.
/// There is only ever one server process, so an in-process lock is enough and costs nothing.
/// </summary>
public static class NumberingGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try { return await work(); }
        finally { Gate.Release(); }
    }
}
