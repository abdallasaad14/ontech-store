namespace OnTech.Store.Desktop.ViewModels;

/// <summary>
/// For lists that reload when a filter changes: starting a new load cancels the one still running, and a load that was
/// overtaken (its answer arrives late) is told so through <see cref="Ticket.IsCurrent"/> and must not fill the list (L-8).
/// </summary>
public sealed class LatestRequest
{
    private CancellationTokenSource? _cts;
    private int _version;

    public Ticket Begin()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return new Ticket(this, ++_version, _cts.Token);
    }

    public readonly record struct Ticket(LatestRequest Owner, int Version, CancellationToken Token)
    {
        /// <summary>False once a newer load started: drop this answer.</summary>
        public bool IsCurrent => Owner._version == Version && !Token.IsCancellationRequested;
    }
}
