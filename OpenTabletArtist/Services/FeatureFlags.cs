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
    /// </summary>
    public const bool PerAppProfiles = false;
}
