namespace OpenTabletArtist.Services;

/// <summary>
/// A circuit-breaker that detects a runaway settings apply/save loop — e.g. a UI binding write-back that
/// feeds <c>ApplyAndSaveSettingsAsync</c> → reload → write-back — and stops it before it pegs the UI thread
/// and hangs the app (the class of bug behind #433). It counts apply attempts in a sliding window; once the
/// rate crosses the threshold it denies further calls (the caller skips the apply, which breaks the loop
/// because no reload follows). It re-arms automatically once the burst stops and the window clears.
///
/// <para>This is a safety net, not a fix: it converts an infinite hang into a brief, logged no-op so a
/// stray binding loop can never freeze the app again. Time is passed in (<see cref="Allow"/> takes the
/// tick), so the logic is fully deterministic and unit-tested without a real clock.</para>
///
/// <para>Thresholds are set well above any legitimate rate: real edits are debounced or one-per-action
/// (a few per second at most), whereas an apply loop runs continuously (~13+/sec observed).</para>
/// </summary>
public sealed class ApplyLoopBreaker
{
    private readonly int _threshold;
    private readonly long _windowMs;

    private long _windowStartTick;
    private int _countInWindow;
    private bool _tripped;

    /// <param name="threshold">Max applies allowed within <paramref name="windowMs"/> before tripping.</param>
    /// <param name="windowMs">Sliding-window length in milliseconds.</param>
    public ApplyLoopBreaker(int threshold = 20, long windowMs = 2000)
    {
        _threshold = threshold;
        _windowMs = windowMs;
    }

    /// <summary>True while the breaker is currently denying calls (a runaway loop was detected).</summary>
    public bool IsTripped => _tripped;

    /// <summary>
    /// Record an apply attempt at <paramref name="nowTick"/> (e.g. <c>Environment.TickCount64</c>).
    /// Returns <c>false</c> when the call rate indicates a runaway loop — the caller should skip the apply.
    /// </summary>
    public bool Allow(long nowTick)
    {
        // A gap longer than the window means the previous burst ended — start a fresh window and re-arm.
        if (nowTick - _windowStartTick > _windowMs)
        {
            _windowStartTick = nowTick;
            _countInWindow = 0;
            _tripped = false;
        }

        _countInWindow++;
        if (_countInWindow > _threshold)
            _tripped = true;

        return !_tripped;
    }
}
