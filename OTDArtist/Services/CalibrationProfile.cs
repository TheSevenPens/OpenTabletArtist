using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdArtist.Domain;

namespace OtdArtist.Services;

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
    public const string FilterTypeName = "OtdArtist.Dynamics.CalibrationFilter";

    /// <summary>Which correction model a stored calibration uses. Legacy stores (no Model field) read
    /// as <see cref="Affine"/>. The 4-tap corner capture now writes <see cref="Homography"/> (#195);
    /// the finer-grid mode writes <see cref="Grid"/> (#196).</summary>
    public enum CalibrationModel { Affine, Homography, Grid }

    /// <summary>A stored calibration. <see cref="Transform"/> is the legacy/affine matrix; for the
    /// other models the relevant <see cref="Homography"/> / <see cref="Grid"/> payload is set. New
    /// fields are optional so existing call sites keep compiling.</summary>
    public sealed record CalibrationData(
        Matrix3x2 Transform, bool Enabled, string Fingerprint,
        CalibrationModel Model = CalibrationModel.Affine,
        Homography Homography = default,
        CalibrationGrid? Grid = null)
    {
        public static CalibrationData ForHomography(Homography h, bool enabled, string fingerprint) =>
            new(Matrix3x2.Identity, enabled, fingerprint, CalibrationModel.Homography, h);
        public static CalibrationData ForGrid(CalibrationGrid g, bool enabled, string fingerprint) =>
            new(Matrix3x2.Identity, enabled, fingerprint, CalibrationModel.Grid, default, g);
    }

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
        var model = GetStr("Model") switch
        {
            "Homography" => CalibrationModel.Homography,
            "Grid" => CalibrationModel.Grid,
            _ => CalibrationModel.Affine, // legacy stores have no Model field
        };
        var homography = Homography.TryParse(GetStr("Homography")) ?? default;
        var grid = CalibrationGrid.TryParse(GetStr("Grid"));
        return new CalibrationData(m, store.Enable, GetStr("MappingFingerprint"), model, homography, grid);
    }

    /// <summary>Writes (and enables/disables) the affine calibration filter (legacy convenience).</summary>
    public static void Write(Settings? settings, string tabletName, Matrix3x2 transform, bool enable, string fingerprint)
        => Write(settings, tabletName, new CalibrationData(transform, enable, fingerprint, CalibrationModel.Affine));

    /// <summary>Writes (and enables/disables) the calibration filter on the tablet's profile, ordered
    /// before the dynamics filter, using whichever model <paramref name="data"/> carries. Mutates
    /// <paramref name="settings"/>; no-op if the profile isn't found.</summary>
    public static void Write(Settings? settings, string tabletName, CalibrationData data)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.Filters == null) return;

        var store = profile.Filters.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store == null)
        {
            store = NewStore();
            profile.Filters.Add(store);
        }

        // Keep the affine M-matrix at identity for non-affine models so a legacy reader is a no-op.
        var m = data.Model == CalibrationModel.Affine ? data.Transform : Matrix3x2.Identity;

        store.Enable = data.Enabled;
        store.Settings = new ObservableCollection<PluginSetting>
        {
            new("M11", m.M11), new("M12", m.M12),
            new("M21", m.M21), new("M22", m.M22),
            new("M31", m.M31), new("M32", m.M32),
            new("Model", data.Model.ToString()),
            new("Homography", data.Model == CalibrationModel.Homography ? data.Homography.ToCsv() : ""),
            new("Grid", data.Model == CalibrationModel.Grid && data.Grid != null ? data.Grid.ToCsv() : ""),
            new("MappingFingerprint", data.Fingerprint),
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

    /// <summary>True when an enabled calibration's stored fingerprint differs from the current
    /// mapping's fingerprint — captured against a different mapping, so it may be inaccurate (#147).
    /// False when there's no calibration, it's disabled, has no stored fingerprint, or the current
    /// fingerprint is unknown.</summary>
    public static bool IsStale(CalibrationData? cal, string? currentFingerprint) =>
        cal is { Enabled: true }
        && !string.IsNullOrEmpty(cal.Fingerprint)
        && !string.IsNullOrEmpty(currentFingerprint)
        && cal.Fingerprint != currentFingerprint;

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
