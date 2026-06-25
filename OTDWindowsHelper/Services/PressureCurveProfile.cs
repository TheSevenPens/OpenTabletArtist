using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Reads/writes our pressure-curve filter on a tablet's profile (the OTD <see cref="Settings"/>
/// model). The filter is stored as a <see cref="PluginSettingStore"/> in the profile's
/// <c>Filters</c>, keyed by the plugin's type name. The app doesn't reference the plugin assembly,
/// so the store is built by path (the daemon, which has the plugin, constructs the actual filter).
/// </summary>
public static class PressureCurveProfile
{
    /// <summary>Full type name of the filter in the plugin assembly (must match the daemon's view).</summary>
    public const string FilterTypeName = "OtdWindowsHelper.Dynamics.DynamicsFilter";

    /// <summary>Type name before the v0.2.0 → "Dynamics" rename. Read as a fallback and dropped on
    /// the next write so old profiles migrate cleanly (the orphaned legacy DLL is then unreferenced).</summary>
    public const string LegacyFilterTypeName = "OtdWindowsHelper.PressureCurve.PressureCurveFilter";

    /// <summary>The current dynamics (curve + smoothing) + enabled state for a tablet's profile,
    /// or null if not present.</summary>
    public static (PenDynamicsSettings Dynamics, bool Enabled)? Read(Settings? settings, string tabletName)
    {
        var store = FindStore(settings, tabletName);
        if (store == null) return null;

        float Get(string name, float fallback) =>
            store.Settings?.FirstOrDefault(s => s.Property == name) is { HasValue: true } s ? s.GetValue<float>() : fallback;
        bool GetBool(string name, bool fallback) =>
            store.Settings?.FirstOrDefault(s => s.Property == name) is { HasValue: true } s ? s.GetValue<bool>() : fallback;

        var curve = new PressureCurveSettings(
            Softness: Get(nameof(PressureCurveSettings.Softness), 0),
            InputMinimum: Get(nameof(PressureCurveSettings.InputMinimum), 0),
            InputMaximum: Get(nameof(PressureCurveSettings.InputMaximum), 1),
            Minimum: Get(nameof(PressureCurveSettings.Minimum), 0),
            Maximum: Get(nameof(PressureCurveSettings.Maximum), 1),
            MinApproach: GetBool("CutBelowMinimum", false) ? PressureMinApproach.Cut : PressureMinApproach.Clamp);
        var dynamics = new PenDynamicsSettings(
            curve,
            PressureSmoothing: Get("PressureSmoothing", 0),
            PositionSmoothing: Get("PositionSmoothing", 0),
            SmoothAfterCurve: GetBool("SmoothAfterCurve", true));
        return (dynamics, store.Enable);
    }

    /// <summary>Writes (and enables/disables) the filter on the tablet's profile. Mutates
    /// <paramref name="settings"/>. No-op if the profile isn't found.</summary>
    public static void Write(Settings? settings, string tabletName, PenDynamicsSettings dynamics, bool enable)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        // Drop any pre-rename store so the daemon doesn't run both the old curve-only filter and the
        // new one on this profile.
        var legacy = profile.Filters?.Where(f => f.Path == LegacyFilterTypeName).ToList();
        if (legacy != null)
            foreach (var old in legacy) profile.Filters!.Remove(old);

        var store = profile.Filters?.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store == null)
        {
            store = NewStore();
            profile.Filters?.Add(store);
        }

        var curve = dynamics.Curve;
        store.Enable = enable;
        store.Settings = new ObservableCollection<PluginSetting>
        {
            new(nameof(PressureCurveSettings.Softness), (float)curve.Softness),
            new(nameof(PressureCurveSettings.InputMinimum), (float)curve.InputMinimum),
            new(nameof(PressureCurveSettings.InputMaximum), (float)curve.InputMaximum),
            new(nameof(PressureCurveSettings.Minimum), (float)curve.Minimum),
            new(nameof(PressureCurveSettings.Maximum), (float)curve.Maximum),
            new("CutBelowMinimum", curve.MinApproach == PressureMinApproach.Cut),
            new("PressureSmoothing", (float)dynamics.PressureSmoothing),
            new("PositionSmoothing", (float)dynamics.PositionSmoothing),
            new("SmoothAfterCurve", dynamics.SmoothAfterCurve),
        };
    }

    private static PluginSettingStore? FindStore(Settings? settings, string tabletName)
    {
        var filters = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName)?.Filters;
        // Prefer the current store; fall back to the pre-rename one so old settings still show up
        // (the next write migrates them to the new type name).
        return filters?.FirstOrDefault(f => f.Path == FilterTypeName)
            ?? filters?.FirstOrDefault(f => f.Path == LegacyFilterTypeName);
    }

    // The app doesn't have the plugin Type (it's a separate net8 assembly), and PluginSettingStore's
    // only public ctors take a Type/instance — so build an empty store via its JSON constructor and
    // fill the fields. The daemon (which has the plugin) constructs the real filter from Path + Settings.
    private static PluginSettingStore NewStore()
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        store.Path = FilterTypeName;
        store.Settings = new ObservableCollection<PluginSetting>();
        return store;
    }
}
