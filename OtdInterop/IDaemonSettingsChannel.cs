using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Reading and writing the daemon's settings directly, with none of the protections around them.
/// </summary>
///
/// <remarks>
/// <para>
/// Internal, and that is the whole reason it exists as a separate type (#807 Phase 4). These two verbs
/// used to sit on <see cref="IDaemonTransport"/> alongside the device list and the log stream, so every
/// class that legitimately wanted one of those also held a settings writer — bypassing ordering,
/// ownership, the session check, policy, the format guard, the no-op guard and the persistence
/// bookkeeping that <see cref="IOtdSettingsSession"/> applies. Nothing in the app used them, but nothing
/// stopped it either, and the next thing that wanted to change settings quickly would have found them.
/// </para>
/// <para>
/// One implementation and one consumer: the connection provides it, and the one settings session
/// <see cref="OtdSession.OpenSettings"/> hands out uses it. That there is only ever one of those is the
/// session's doing, not a rule anyone here has to remember.
/// </para>
/// </remarks>
internal interface IDaemonSettingsChannel
{
    /// <summary>The daemon's current in-memory settings. Null when not connected.</summary>
    Task<Settings?> GetSettingsAsync();

    /// <summary>
    /// Pushes settings to the daemon. False when there is no transport — the change was NOT sent, so a
    /// caller must not report it as live (#734). Throws when the daemon is reachable and the call fails.
    /// </summary>
    Task<bool> SetSettingsAsync(Settings settings);
}
