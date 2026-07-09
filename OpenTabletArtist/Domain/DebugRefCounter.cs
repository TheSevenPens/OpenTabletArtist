namespace OpenTabletArtist.Domain;

/// <summary>
/// The reference-count / transition decision behind the daemon's single global tablet-debug flag
/// (#119, #121). Several consumers share one stream (Diagnostics, the Test tab's Driver mode, the
/// Dynamics live-pressure dot), so the enable/disable RPC should fire only on a 0↔1 transition — the
/// first consumer turns the stream on, the last turns it off; intermediate acquires/releases just move
/// the count. This is the pure decision core; <c>DaemonClient</c> owns the lock and the RPC, so this is
/// not itself thread-safe (always call it under that lock). Unit-tested in isolation (#121).
/// </summary>
public sealed class DebugRefCounter
{
    private int _count;

    /// <summary>Current consumer count (for assertions / diagnostics).</summary>
    public int Count => _count;

    /// <summary>Acquire the stream for one consumer. Returns true when this took the count 0 → 1 — i.e.
    /// the caller should send the <c>enable</c> RPC.</summary>
    public bool Acquire() => ++_count == 1;

    /// <summary>Release one consumer. Returns true when this took the count 1 → 0 — i.e. the caller
    /// should send the <c>disable</c> RPC. A release at zero is ignored (returns false, count stays 0).</summary>
    public bool Release() => _count > 0 && --_count == 0;

    /// <summary>Undo a just-acquired count after its <c>enable</c> RPC failed, so a later acquire
    /// re-asserts the enable instead of being suppressed. No-op at zero.</summary>
    public void RollbackAcquire()
    {
        if (_count > 0) _count--;
    }

    /// <summary>Reset to zero — e.g. on disconnect, where the daemon forgets the debug flag, so the
    /// count must not stay stale (which would suppress a later enable).</summary>
    public void Reset() => _count = 0;
}
