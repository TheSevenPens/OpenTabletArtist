using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Services;

/// <summary>
/// Reads/writes the hover-limit filter (<see cref="FilterTypeName"/>) on a tablet's profile,
/// mirroring <see cref="PressureCurveProfile"/>. Stores the max hover distance (0-255) and the
/// filter's enabled state (#188).
/// </summary>
public static class HoverProfile
{
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.HoverFilter";

    /// <summary>Hover distance is a 0-255 byte in the tablet report; 255 means "no limit".</summary>
    public const int MaxDistance = 255;

    public sealed record HoverData(int MaxHoverDistance, bool Enabled);

    public static HoverData? Read(Settings? settings, string tabletName)
        => ReadProfile(settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName));

    public static HoverData? ReadProfile(Profile? profile)
    {
        var store = profile?.Filters?.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store == null) return null;

        int max = store.Settings?.FirstOrDefault(s => s.Property == "Max Hover Distance") is { HasValue: true } s
            ? s.GetValue<int>() : MaxDistance;
        return new HoverData(max, store.Enable);
    }

    /// <summary>Writes (and enables/disables) the hover-limit filter on the tablet's profile. Mutates
    /// <paramref name="settings"/>; no-op if the profile isn't found.</summary>
    public static void Write(Settings? settings, string tabletName, int maxHoverDistance, bool enable)
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
            new("Max Hover Distance", maxHoverDistance),
        };
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
