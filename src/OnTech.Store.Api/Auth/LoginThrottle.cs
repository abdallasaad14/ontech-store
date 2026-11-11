using System.Collections.Concurrent;

namespace OnTech.Store.Api.Auth;

/// <summary>
/// L-2: per-username lockout, kept in memory. After <see cref="MaxFailures"/> wrong passwords in a row the username is refused
/// for 30 s; each further lockout doubles the wait, up to 5 minutes. A right password resets everything.
/// While locked, the password is not even checked, so the answer never tells whether it was right.
/// </summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxFailures = 5;
    /// <summary>The "code" extension of the 429 answer. Same string as OnTech.Store.Contracts.ErrorCodes.TooManyAttempts.</summary>
    public const string Code = OnTech.Store.Contracts.ErrorCodes.TooManyAttempts;
    public const string TitleFormat = "Too many wrong passwords. Wait {0} seconds and try again.";
    public static readonly TimeSpan FirstLockout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(5);
    /// <summary>Wrong passwords this long ago no longer count, and the doubling starts again from 30 s.</summary>
    public static readonly TimeSpan Forget = TimeSpan.FromMinutes(15);
    private const int PruneAbove = 1000;

    private sealed class Entry
    {
        public int Failures;
        public int Lockouts;
        public DateTimeOffset LockedUntil;
        public DateTimeOffset LastFailure;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>How long the username is still locked, or null when it may try a password.</summary>
    public TimeSpan? LockedFor(string normalizedUsername)
    {
        if (!_entries.TryGetValue(normalizedUsername, out var e)) return null;
        var now = clock.GetUtcNow();
        lock (e)
            return e.LockedUntil > now ? e.LockedUntil - now : null;
    }

    /// <summary>Counts a wrong password. Returns how long the username is now locked, or null when it is not.</summary>
    public TimeSpan? Failed(string normalizedUsername)
    {
        var now = clock.GetUtcNow();
        if (_entries.Count > PruneAbove) Prune(now);
        var e = _entries.GetOrAdd(normalizedUsername, _ => new Entry());
        lock (e)
        {
            if (e.LockedUntil > now) return e.LockedUntil - now;
            if (now - e.LastFailure > Forget) { e.Failures = 0; e.Lockouts = 0; }
            e.LastFailure = now;
            if (++e.Failures < MaxFailures) return null;

            var seconds = Math.Min(FirstLockout.TotalSeconds * Math.Pow(2, Math.Min(e.Lockouts, 10)), MaxLockout.TotalSeconds);
            e.Lockouts++;
            e.Failures = 0;
            e.LockedUntil = now + TimeSpan.FromSeconds(seconds);
            return e.LockedUntil - now;
        }
    }

    /// <summary>A right password: forget the wrong ones.</summary>
    public void Succeeded(string normalizedUsername) => _entries.TryRemove(normalizedUsername, out _);

    /// <summary>The whole seconds to show in the message (never 0 while still locked).</summary>
    public static int Seconds(TimeSpan wait) => Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, e) in _entries)
        {
            bool stale;
            lock (e) stale = e.LockedUntil <= now && now - e.LastFailure > Forget;
            if (stale) _entries.TryRemove(key, out _);
        }
    }
}
