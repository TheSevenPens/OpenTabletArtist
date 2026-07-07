using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Cycles the active tablet's Absolute-mode area mapping to the next monitor (#89). A single shared
/// instance driven by the global "cycle mapped monitor" hotkey. The mapping is stored per-tablet, so a
/// press acts on the active/detected tablet's profile — moving it to the next display in the enumerated
/// list (wrapping around) — and persists the change (a deliberate remap, not a temporary override).
/// Reuses <see cref="DisplayMappingApplier"/> so the result matches picking a display on the tablet page.
/// UI-thread only (persist path verifies UI access).
/// </summary>
public sealed class MonitorCycleService
{
    private readonly ISettingsCoordinator _settings;
    private readonly IDeviceData _device;
    private readonly Func<IReadOnlyList<DisplayInfo>> _displays;

    /// <summary>Fired after each cycle attempt with a short user-facing message for the toast — either the
    /// new mapping ("Wacom … → Display 2") or why nothing changed ("Only one display connected").</summary>
    public event Action<string>? Cycled;

    public MonitorCycleService(ISettingsCoordinator settings, IDeviceData device,
        Func<IReadOnlyList<DisplayInfo>>? displays = null)
    {
        _settings = settings;
        _device = device;
        _displays = displays ?? DisplayEnumerator.Enumerate;
    }

    /// <summary>Remap the active tablet to the next monitor and persist. No-ops (with a toast message)
    /// when there's no active tablet, it isn't in an absolute mapping, or there's only one display.</summary>
    public async Task CycleAsync()
    {
        var settings = _settings.CurrentSettings;
        if (settings == null) { Cycled?.Invoke("No settings loaded yet"); return; }

        // The hotkey is app-wide but the mapping is per-tablet, so act on the active/detected tablet.
        var tabletName = _device.ActiveTabletName ?? _device.DetectedTablets.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tabletName)) { Cycled?.Invoke("No active tablet to remap"); return; }

        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.AbsoluteModeSettings == null)
        {
            Cycled?.Invoke($"{tabletName} isn't in an absolute mapping");
            return;
        }

        var displays = _displays();
        if (displays.Count < 2) { Cycled?.Invoke("Only one display connected"); return; }

        // Next display after the currently-mapped one (wrap). If it's not on a whole monitor, start at 0.
        var current = DisplayMappingApplier.CurrentlyMapped(profile, displays);
        int idx = current == null ? -1 : IndexOf(displays, current.Number);
        var next = displays[(idx + 1) % displays.Count];

        if (!DisplayMappingApplier.ApplyToProfile(profile, _device.GetTabletDigitizer(tabletName), next, displays))
        {
            Cycled?.Invoke($"Couldn't remap {tabletName}");
            return;
        }

        await _settings.ApplyAndSaveSettingsAsync(settings);
        Cycled?.Invoke($"{tabletName} → {next.DisplayTitle}");
    }

    private static int IndexOf(IReadOnlyList<DisplayInfo> displays, int number)
    {
        for (int i = 0; i < displays.Count; i++)
            if (displays[i].Number == number) return i;
        return -1;
    }
}
