using OpenTabletDriver.Plugin.Logging;

namespace OpenTabletArtist.Services;

/// <summary>
/// The slice of the daemon connection the Console page needs: the daemon's log stream and a
/// snapshot of the log buffer it already holds. Mirrors how <see cref="IDaemonDebugSession"/>
/// narrows the device-report stream — so <c>LogViewModel</c> depends on a small, fakeable
/// interface rather than the whole <see cref="DaemonClient"/>. Implemented by <see cref="DaemonClient"/>.
/// </summary>
public interface IDaemonLogSource
{
    /// <summary>Raised for each new daemon log message (the daemon's <c>Message</c> event). May fire
    /// off the UI thread — consumers marshal.</summary>
    event Action<LogMessage>? LogReceived;

    /// <summary>The log the daemon has buffered so far (used to seed the view on connect). Empty when
    /// not connected or on error.</summary>
    Task<List<LogMessage>> GetCurrentLogAsync();
}
