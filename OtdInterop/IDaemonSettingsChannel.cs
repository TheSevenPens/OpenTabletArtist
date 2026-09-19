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
/// One implementation and one consumer: the connection provides it, the settings session uses it. That
/// is enforced rather than assumed — see <see cref="TryClaimExclusiveUse"/>.
/// </para>
/// </remarks>
internal interface IDaemonSettingsChannel
{
    /// <summary>
    /// Claims this connection's settings channel for one session. False when something already holds it.
    /// </summary>
    /// <remarks>
    /// Two sessions over one connection is not two views of the same thing. Each has its own mutation
    /// gate, its own session generation, its own retry state and its own baseline, so neither can see
    /// what the other is doing: two applies go to the same daemon at once, each writes the other's
    /// settings out of its own file, and a reset clears only half the state. Everything the ordering in
    /// this library guarantees is guaranteed per session, and a second session ends all of it.
    ///
    /// Hiding the implementation does not establish one authority when the factory can manufacture
    /// several, so the claim lives here, on the thing being used exclusively.
    /// </remarks>
    /// <returns>True when the caller now holds the channel; false when it was already held.</returns>
    bool TryClaimExclusiveUse();

    /// <summary>The daemon's current in-memory settings. Null when not connected.</summary>
    Task<Settings?> GetSettingsAsync();

    /// <summary>
    /// Pushes settings to the daemon. False when there is no transport — the change was NOT sent, so a
    /// caller must not report it as live (#734). Throws when the daemon is reachable and the call fails.
    /// </summary>
    Task<bool> SetSettingsAsync(Settings settings);
}
