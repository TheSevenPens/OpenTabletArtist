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
    /// <b>Not staged for release.</b> There is no plan to re-enable this in its current form — the
    /// question is whether automatic switching is the right idea at all, not whether this implementation
    /// works. It is kept because parts of it may be useful to whatever replaces it: the foreground
    /// watcher, the app→profile store, and the switch policy are each independently reusable.
    ///
    /// So don't read the flag as a countdown. Nothing is waiting on validation, and it does not need a
    /// device-matrix pass unless and until someone decides to ship switching in some form.
    ///
    /// It is nonetheless correct as far as it goes, which is worth knowing if that day comes: #737 fixed
    /// the ordering defects (obsolete queued targets are cancelled, applies are serialized and
    /// generation-checked, only a confirmed apply is committed), and baseline isolation — the reload not
    /// adopting a transient snapshot as the editor's default — is verified end to end across repeated
    /// polls and a reconnect by <c>AppSessionSettingsTests</c>.
    /// </summary>
    public const bool PerAppProfiles = false;
}
