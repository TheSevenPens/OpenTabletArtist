using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin.Tablet;

namespace OtdInterop;

/// <summary>
/// The saved file as the daemon holds it after loading it.
/// </summary>
/// <remarks>
/// <para>
/// "Unsaved" compares the daemon's live settings with the saved file, and the daemon does not hold the
/// file as written. On load it stamps <see cref="Settings.Revision"/> with its own version and, for every
/// tablet it has detected, runs <see cref="BindingSettings.MatchSpecifications"/> over that tablet's
/// profile: button slots are added or trimmed to the specification, wheel step sizes are filled in, a
/// zero wheel threshold becomes one. None of it is written back. Compared raw, a file that predates any
/// of that read as unsaved the moment the daemon connected, with nothing edited — and every daemon
/// restart, including the one a plugin update triggers at launch, then asked about changes the artist
/// never made.
/// </para>
/// <para>
/// So the baseline is the file after the same load, and the daemon's own code does the matching, from
/// the OpenTabletDriver build this library links. That is what makes it exact rather than a list of
/// fields to keep in step with upstream. The one thing not reproduced is a detected tablet with no saved
/// profile: the daemon generates one from defaults, and those defaults ask its plugin manager for the
/// virtual screen, which does not exist in this process. That profile is genuinely absent from the file,
/// so reporting it unsaved is also the truth.
/// </para>
/// </remarks>
internal static class DaemonLoadNormalizer
{
    /// <summary>
    /// <paramref name="saved"/> as the daemon would hold it, given what it reports as
    /// <paramref name="live"/> and the <paramref name="tablets"/> it has detected. Null stays null.
    /// </summary>
    public static Settings? AsLoaded(Settings? saved, Settings live, JArray tablets)
    {
        if (saved is null) return null;
        var loaded = SettingsCodec.Clone(saved);
        loaded.Revision = live.Revision;
        if (loaded.Profiles is null) return loaded;
        foreach (var token in tablets)
        {
            TabletConfiguration? properties;
            try { properties = token.ToObject<TabletReference>()?.Properties; }
            catch (JsonException) { continue; }
            if (properties is null || string.IsNullOrEmpty(properties.Name)) continue;
            if (loaded.Profiles.GetProfile(properties.Name)?.BindingSettings is not { } bindings) continue;
            // A specification the daemon cannot match either — a wheel with no step count — throws in the
            // daemon too, which then falls back to its last valid settings. Left as the file has it.
            try { bindings.MatchSpecifications(properties.Specifications); }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException) { }
        }
        return loaded;
    }
}
