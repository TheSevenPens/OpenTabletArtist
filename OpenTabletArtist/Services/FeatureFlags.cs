namespace OpenTabletArtist.Services;

/// <summary>
/// Compile-time switches for features that are fully built but intentionally not exposed right now.
/// Flipping a flag back to <c>true</c> restores the feature wholesale — its UI and its behavior — with
/// no other change, since every entry point gates on the flag. The code and any saved data are left in
/// place while a flag is off, so nothing is lost.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Per-app profile auto-switching (#167): applies a saved profile when the foreground app changes.
    /// Hidden and inert while the switching model is being reconsidered. When <c>false</c>, the
    /// "Per-App Profiles" nav entry is hidden and the background switcher can never start (so no
    /// automatic switches, cue, or toast occur); the page, the saved app→profile mappings, and the
    /// switcher code are untouched, so setting this back to <c>true</c> brings the feature back exactly
    /// as it was.
    ///
    /// Still <c>false</c> after the #737 correctness work, deliberately. The ordering defects are fixed
    /// and covered — obsolete queued targets are cancelled, applies are serialized and generation-checked,
    /// and only a confirmed apply is committed. Baseline isolation is implemented too
    /// (<see cref="ISettingsCoordinator.HasEphemeralOverride"/> stops the background reload adopting a
    /// transient snapshot), but it is only verified at the applier contract; proving it end to end
    /// through the 30-second poll and a reconnect needs the injectable daemon transport from #740.
    /// Turning this on also wants a pass over the manual device matrix, since what it changes is what
    /// the tablet does while the user is in another application.
    /// </summary>
    public const bool PerAppProfiles = false;
}
