using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Reads/writes the pointer-calibration filter (<see cref="FilterTypeName"/>) on a tablet's profile,
/// mirroring <see cref="PressureCurveProfile"/>. The affine is stored as its six
/// <see cref="Matrix3x2"/> components in normalized tablet space, plus a mapping fingerprint so the
/// UI can flag a calibration as possibly stale after the area mapping changes (#127).
///
/// The calibration filter is ordered <em>before</em> the dynamics filter (both PreTransform) so the
/// raw position is corrected before any position smoothing.
/// </summary>
public static class CalibrationProfile
{
    public const string FilterTypeName = "OtdWindowsHelper.Dynamics.CalibrationFilter";

    public sealed record CalibrationData(Matrix3x2 Transform, bool Enabled, string Fingerprint);

    public static CalibrationData? Read(Settings? settings, string tabletName)
        => ReadProfile(settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName));

    public static CalibrationData? ReadProfile(Profile? profile)
    {
        var store = profile?.Filters?.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store == null) return null;

        float Get(string name, float fallback) =>
            store.Settings?.FirstOrDefault(s => s.Property == name) is { HasValue: true } s ? s.GetValue<float>() : fallback;
        string GetStr(string name) =>
            store.Settings?.FirstOrDefault(s => s.Property == name) is { HasValue: true } s ? (s.GetValue<string>() ?? "") : "";

        var m = new Matrix3x2(
            Get("M11", 1f), Get("M12", 0f),
            Get("M21", 0f), Get("M22", 1f),
            Get("M31", 0f), Get("M32", 0f));
        return new CalibrationData(m, store.Enable, GetStr("MappingFingerprint"));
    }

    /// <summary>Writes (and enables/disables) the calibration filter on the tablet's profile, ordered
    /// before the dynamics filter. Mutates <paramref name="settings"/>; no-op if the profile isn't found.</summary>
    public static void Write(Settings? settings, string tabletName, Matrix3x2 transform, bool enable, string fingerprint)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.Filters == null) return;

        var store = profile.Filters.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store == null)
        {
            store = NewStore();
            profile.Filters.Add(store);
        }

        store.Enable = enable;
        store.Settings = new ObservableCollection<PluginSetting>
        {
            new("M11", transform.M11), new("M12", transform.M12),
            new("M21", transform.M21), new("M22", transform.M22),
            new("M31", transform.M31), new("M32", transform.M32),
            new("MappingFingerprint", fingerprint),
        };

        EnsureBeforeDynamics(profile);
    }

    /// <summary>Removes the calibration filter from the profile (reset to no correction).</summary>
    public static void Clear(Settings? settings, string tabletName)
    {
        var filters = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName)?.Filters;
        var store = filters?.FirstOrDefault(f => f.Path == FilterTypeName);
        if (filters != null && store != null) filters.Remove(store);
    }

    /// <summary>A short, stable token identifying the area mapping a calibration was captured against
    /// (input area + output area + display number). When it changes, the calibration may be stale.</summary>
    public static string Fingerprint(MappingArea input, MappingArea output, int displayNumber)
    {
        static string A(MappingArea a) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##},{3:0.##},{4:0.##}",
                a.CenterX, a.CenterY, a.Width, a.Height, a.Rotation);
        return $"d{displayNumber}|in:{A(input)}|out:{A(output)}";
    }

    // Both filters sit at PreTransform; OTD runs them in profile order, so calibration must precede
    // dynamics. Move it ahead if a dynamics store exists after it.
    private static void EnsureBeforeDynamics(Profile profile)
    {
        var filters = profile.Filters!;
        int cal = filters.ToList().FindIndex(f => f.Path == FilterTypeName);
        int dyn = filters.ToList().FindIndex(f =>
            f.Path == PressureCurveProfile.FilterTypeName || f.Path == PressureCurveProfile.LegacyFilterTypeName);
        if (cal >= 0 && dyn >= 0 && cal > dyn)
        {
            var store = filters[cal];
            filters.RemoveAt(cal);
            filters.Insert(dyn, store);
        }
    }

    // Same trick as PressureCurveProfile: the app doesn't reference the plugin Type, so build an empty
    // store via the JSON ctor and fill Path + Settings. The daemon constructs the real filter.
    private static PluginSettingStore NewStore()
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        store.Path = FilterTypeName;
        store.Settings = new ObservableCollection<PluginSetting>();
        return store;
    }
}
