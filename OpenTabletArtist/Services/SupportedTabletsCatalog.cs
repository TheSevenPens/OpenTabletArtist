using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Configurations;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Services;

/// <summary>One tablet in OTD's built-in catalog (#155), reduced to the fields the Supported Tablets
/// dialog shows. <see cref="Name"/> matches the name OTD reports for a detected tablet, so the dialog
/// can highlight the connected one.</summary>
public sealed record SupportedTablet(string Name, string ActiveArea, string Pressure, string Buttons);

/// <summary>
/// The list of tablets OpenTabletDriver supports, read from the ~339 tablet configs embedded in
/// <c>OpenTabletDriver.Configurations.dll</c> (via <see cref="DeviceConfigurationProvider"/>) — in
/// process, offline, and version-matched to the bundled daemon. Loaded lazily on first use and cached,
/// so opening the dialog repeatedly is instant.
/// </summary>
public static class SupportedTabletsCatalog
{
    private static IReadOnlyList<SupportedTablet>? _cache;

    /// <summary>All supported tablets, sorted by name. Empty if the catalog can't be read.</summary>
    public static IReadOnlyList<SupportedTablet> All => _cache ??= Load();

    private static IReadOnlyList<SupportedTablet> Load()
    {
        try
        {
            return new DeviceConfigurationProvider().TabletConfigurations
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Map)
                .ToList();
        }
        catch
        {
            return Array.Empty<SupportedTablet>();
        }
    }

    private static SupportedTablet Map(TabletConfiguration c)
    {
        var digitizer = c.Specifications?.Digitizer;
        string area = digitizer is { Width: > 0, Height: > 0 }
            ? $"{digitizer.Width:0} × {digitizer.Height:0} mm"
            : "—";

        uint maxPressure = c.Specifications?.Pen?.MaxPressure ?? 0;
        string pressure = maxPressure > 0 ? maxPressure.ToString() : "—";

        uint buttons = c.Specifications?.AuxiliaryButtons?.ButtonCount ?? 0;

        return new SupportedTablet(c.Name, area, pressure, buttons.ToString());
    }
}
