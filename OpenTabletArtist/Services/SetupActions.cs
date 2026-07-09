using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Session-level setup helpers that apply a fix across the connected tablets, independent of any open
/// tablet page — so a one-click "fix" can be offered from the pages that surface the problem (e.g. the
/// Windows Ink plugin install flow, #361). Mutates the coordinator's current settings and applies +
/// persists through it, so the change is live and saved.
/// </summary>
public sealed class SetupActions
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";

    private readonly ISettingsCoordinator _settings;
    private readonly IDeviceData _device;

    public SetupActions(ISettingsCoordinator settings, IDeviceData device)
    {
        _settings = settings;
        _device = device;
    }

    private static bool IsWinInk(Profile p) =>
        (p.OutputMode?.Path ?? "").Contains("WinInk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Names of connected tablets whose output mode isn't a Windows Ink mode.</summary>
    public IReadOnlyList<string> DetectedTabletsNotOnWindowsInk() =>
        _device.Profiles.Where(p => p.IsDetected && !IsWinInk(p.Profile)).Select(p => p.Tablet).ToList();

    /// <summary>Sets connected non-Windows-Ink tablets to Windows Ink Absolute mode, then applies +
    /// persists. Returns how many tablets were changed (0 if none needed it or there are no settings).
    /// Optionally restricted to tablets matching <paramref name="include"/> — auto-setup passes a filter
    /// to honor per-tablet Windows-Ink opt-outs (#380/#406). The single source of truth for the mode-set
    /// (the manual "enable" button and the auto-setup both call this). Each switched tablet is cleared
    /// from the opt-out set, since it's now on Windows Ink.</summary>
    public async Task<int> SetDetectedTabletsToWindowsInkAsync(Func<string, bool>? include = null)
    {
        if (_settings.CurrentSettings is not { } settings) return 0;
        int changed = 0;
        foreach (var name in DetectedTabletsNotOnWindowsInk())
        {
            if (include != null && !include(name)) continue;
            var prof = settings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Tablet, name, StringComparison.OrdinalIgnoreCase));
            if (prof == null) continue;
            prof.OutputMode ??= new PluginSettingStore(WinInkAbsoluteModePath, true);
            prof.OutputMode.Path = WinInkAbsoluteModePath;
            WinInkAutoOptOut.Clear(name); // now on Windows Ink → no longer opted out (keeps the set fresh)
            changed++;
        }
        if (changed > 0) await _settings.ApplyAndSaveSettingsAsync(settings);
        return changed;
    }

    /// <summary>Connected tablets (with Absolute settings) that aren't mapped to a single monitor, when
    /// more than one display is present — so their pointer would otherwise span every monitor. Empty on
    /// a single-display setup (nothing to disambiguate).</summary>
    public IReadOnlyList<string> DetectedUnmappedTablets()
    {
        var displays = DisplayEnumerator.Enumerate();
        if (displays.Count < 2) return Array.Empty<string>();
        return _device.Profiles
            .Where(p => p.IsDetected
                && p.Profile.AbsoluteModeSettings != null
                && DisplayMappingApplier.CurrentlyMapped(p.Profile, displays) == null)
            .Select(p => p.Tablet)
            .ToList();
    }

    /// <summary>Maps the named tablet to the primary display (aspect-locked) and applies + persists.</summary>
    public async Task<bool> MapTabletToPrimaryAsync(string tablet)
    {
        if (_settings.CurrentSettings is not { } settings) return false;
        var prof = settings.Profiles.FirstOrDefault(p =>
            string.Equals(p.Tablet, tablet, StringComparison.OrdinalIgnoreCase));
        if (prof == null) return false;

        var displays = DisplayEnumerator.Enumerate();
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
        if (primary == null) return false;

        if (!DisplayMappingApplier.ApplyToProfile(prof, _device.GetTabletDigitizer(tablet), primary, displays))
            return false;
        await _settings.ApplyAndSaveSettingsAsync(settings);
        return true;
    }
}
