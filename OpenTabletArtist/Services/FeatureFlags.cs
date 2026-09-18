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
    /// Still <c>false</c>, but no longer for want of evidence. The #737 ordering defects are fixed and
    /// covered — obsolete queued targets are cancelled, applies are serialized and generation-checked,
    /// and only a confirmed apply is committed. Baseline isolation
    /// (<see cref="ISettingsCoordinator.HasEphemeralOverride"/> stopping the background reload adopting a
    /// transient snapshot) is now verified end to end through the real load path, across repeated polls
    /// and a reconnect, by <c>AppSessionSettingsTests</c> — which the injectable transport from #740 made
    /// possible.
    ///
    /// What remains before flipping it is a judgement, not a test: a pass over the manual device matrix
    /// (docs/dev/DEVICE-MATRIX.md), since what this changes is what the tablet does while the user is in
    /// another application, and no headless test can speak to that.
    /// </summary>
    public const bool PerAppProfiles = false;
}
