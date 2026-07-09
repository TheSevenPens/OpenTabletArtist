using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTabletDriver.Configurations;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Services;

/// <summary>One tablet in OTD's built-in catalog (#155), reduced to the fields the Supported Tablets
/// dialog shows. <see cref="Name"/> matches the name OTD reports for a detected tablet, so the dialog
/// can highlight the connected one. The <c>*Value</c> fields are numeric sort keys behind the formatted
/// display strings; <see cref="Brand"/> (the name's first word) backs the brand filter. <see cref="Status"/>
/// / <see cref="Notes"/> come from OTD's TABLETS.md; <see cref="StatusRank"/> orders best→worst for sorting.</summary>
public sealed record SupportedTablet(
    string Name, string Brand,
    string ActiveArea, string Pressure, string Buttons,
    string Status, string Notes,
    double AreaValue, uint PressureValue, uint ButtonsValue, int StatusRank);

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
        var support = LoadSupportTable();
        try
        {
            return new DeviceConfigurationProvider().TabletConfigurations
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => Map(c, support))
                .ToList();
        }
        catch
        {
            return Array.Empty<SupportedTablet>();
        }
    }

    private static SupportedTablet Map(TabletConfiguration c, IReadOnlyDictionary<string, (string Status, string Notes)> support)
    {
        var digitizer = c.Specifications?.Digitizer;
        bool hasArea = digitizer is { Width: > 0, Height: > 0 };
        string area = hasArea ? $"{digitizer!.Width:0} × {digitizer.Height:0} mm" : "—";
        double areaValue = hasArea ? digitizer!.Width * digitizer.Height : 0;

        uint maxPressure = c.Specifications?.Pen?.MaxPressure ?? 0;
        string pressure = maxPressure > 0 ? maxPressure.ToString() : "—";

        uint buttons = c.Specifications?.AuxiliaryButtons?.ButtonCount ?? 0;

        support.TryGetValue(c.Name, out var sn);
        string status = sn.Status ?? "";
        string notes = sn.Notes ?? "";

        return new SupportedTablet(c.Name, Brand(c.Name), area, pressure, buttons.ToString(),
            status, notes, areaValue, maxPressure, buttons, StatusRank(status));
    }

    // Best → worst, so a Status sort surfaces fully-supported tablets first; unknown sorts last.
    private static int StatusRank(string status) => status switch
    {
        "Supported" => 0,
        "Has Quirks" => 1,
        "Missing Features" => 2,
        _ => 3,
    };

    // Parse OTD's TABLETS.md (a "| Tablet | Status | Notes |" markdown table, embedded at build) into a
    // name → (status, notes) map. Best-effort: any parse trouble just yields an empty map (no status shown).
    private static IReadOnlyDictionary<string, (string Status, string Notes)> LoadSupportTable()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var asm = typeof(SupportedTabletsCatalog).Assembly;
            using var stream = asm.GetManifestResourceStream("OpenTabletArtist.TABLETS.md");
            if (stream == null) return map;

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.TrimStart().StartsWith("|")) continue;

                var cells = line.Split('|');           // [0] is empty (before the first pipe)
                if (cells.Length < 3) continue;

                var name = cells[1].Trim();
                var status = cells[2].Trim();
                if (name.Length == 0) continue;
                if (name.Equals("Tablet", StringComparison.OrdinalIgnoreCase)) continue;   // header row
                if (IsSeparator(name) || IsSeparator(status)) continue;                     // |---|:--:|---|

                // Notes can contain nothing (empty cell / no trailing pipe); join any remaining cells back.
                var notes = cells.Length > 3 ? string.Join("|", cells.Skip(3)).Trim() : "";
                map[name] = (status, notes);
            }
        }
        catch { /* leave the map empty; the dialog simply shows no status/notes */ }
        return map;
    }

    private static bool IsSeparator(string cell) =>
        cell.Length > 0 && cell.All(ch => ch is '-' or ':' or ' ');

    // Brand = the name's first word (OTD config names lead with the manufacturer: "Wacom …", "XP-Pen …",
    // "Huion …"). There's no first-class Manufacturer field on the config, and this is reliable in practice.
    private static string Brand(string name)
    {
        var first = name.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        return string.IsNullOrEmpty(first) ? name : first;
    }
}
