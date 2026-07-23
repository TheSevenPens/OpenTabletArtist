using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Advanced → Developer: testing aids. Induce health warnings so the "Needs attention" cards can be
/// reviewed and screenshotted, reveal the normally-hidden Filters/JSON tabs on a tablet's page, and
/// deliberately introduce <em>real</em> misconfigurations for reproducing UX issues. The health/tab
/// state lives in the shared <see cref="DeveloperSettings"/> singleton (the view binds straight to
/// <see cref="Settings"/>); the "break config" commands mutate the live tablet settings through the
/// session, so they persist just like any other settings change.
/// </summary>
public sealed partial class DeveloperViewModel : ObservableObject
{
    // Non-null in the real app (wired from the session in MainViewModel); left null in design/test
    // construction, where the break-config commands simply report that no session is available.
    private readonly ISettingsCoordinator? _settings;
    private readonly IDeviceData? _device;

    public DeveloperViewModel(ISettingsCoordinator? settings = null, IDeviceData? device = null)
    {
        _settings = settings;
        _device = device;
    }

    public DeveloperSettings Settings => DeveloperSettings.Instance;

    /// <summary>Result of the last "introduce a config error" action (what changed, or why it couldn't).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfigErrorStatus))]
    private string _configErrorStatus = "";

    public bool HasConfigErrorStatus => !string.IsNullOrEmpty(ConfigErrorStatus);

    /// <summary>Deliberately push the active tablet's display mapping partly off-screen — a <em>real</em>
    /// settings change (persisted), not a synthetic warning — so the "mapped area is partly off-screen"
    /// UX can be reproduced with an actual bad mapping. The output area is straddled across the desktop's
    /// right edge so roughly half of it lands in dead space beyond every monitor, which reliably trips
    /// <see cref="Domain.DisplayMappingApplier.ClassifyMapping"/>'s off-screen check on any layout. Undo
    /// by remapping the tablet to a display on its page (or the tray's Switch display).</summary>
    [RelayCommand]
    private async Task PushMappingOffScreen()
    {
        var settings = _settings?.CurrentSettings;
        if (settings == null) { ConfigErrorStatus = "No settings loaded yet — connect the daemon first."; return; }

        // The mapping is per-tablet, so act on the active/detected tablet (as the monitor-cycle hotkey does).
        var tabletName = _device?.ActiveTabletName ?? _device?.DetectedTablets.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tabletName)) { ConfigErrorStatus = "No active tablet to remap."; return; }

        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        var abs = profile?.AbsoluteModeSettings;
        if (abs == null) { ConfigErrorStatus = $"{tabletName} isn't in an absolute mapping."; return; }

        var displays = DisplayEnumerator.Enumerate();
        if (displays.Count == 0) { ConfigErrorStatus = "No displays detected."; return; }

        // The stored Display area is in 0-based virtual-desktop coords with X/Y as its centre (see
        // DisplayMappingApplier.MappedCenter). Keep the current area size (fall back to the primary
        // monitor if degenerate) and centre it on the desktop's right edge: the right half then falls
        // outside every monitor, so ~50% is uncovered — comfortably past the classifier's 1% tolerance.
        float minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        float rightEdge = displays.Max(d => d.X - minX + d.Width);
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

        abs.Display.Width = abs.Display.Width > 0 ? abs.Display.Width : primary.Width;
        abs.Display.Height = abs.Display.Height > 0 ? abs.Display.Height : primary.Height;
        abs.Display.X = rightEdge;                                  // centre on the right edge → right half off-screen
        abs.Display.Y = primary.Y - minY + primary.Height / 2f;     // vertically centred on the primary (on-screen)

        await _settings!.ApplyAndSaveSettingsAsync(settings);
        ConfigErrorStatus = $"Pushed {tabletName}'s active area partly off-screen (across the desktop's right edge). " +
                            "Home's Needs attention list should now flag it. Remap the tablet to a display to restore it.";
    }

    /// <summary>Deliberately set the active tablet's active-area rotation to a non-cardinal angle (20°) — a
    /// <em>real</em>, persisted settings change — so the "unusual active-area rotation" health warning can
    /// be reproduced. The app only offers 0/90/180/270, so this angle can otherwise only arrive via external
    /// tooling. Undo by picking a standard rotation on the tablet's Display Mapping tab.</summary>
    [RelayCommand]
    private async Task PushMappingRotation()
    {
        var settings = _settings?.CurrentSettings;
        if (settings == null) { ConfigErrorStatus = "No settings loaded yet — connect the daemon first."; return; }

        var tabletName = _device?.ActiveTabletName ?? _device?.DetectedTablets.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tabletName)) { ConfigErrorStatus = "No active tablet to rotate."; return; }

        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        var abs = profile?.AbsoluteModeSettings;
        if (abs?.Tablet == null) { ConfigErrorStatus = $"{tabletName} isn't in an absolute mapping."; return; }

        abs.Tablet.Rotation = 20f; // non-cardinal → trips the rotation health check

        await _settings!.ApplyAndSaveSettingsAsync(settings);
        ConfigErrorStatus = $"Set {tabletName}'s active-area rotation to 20° (a non-standard angle). " +
                            "Home's Needs attention list should now flag it. Pick a standard rotation " +
                            "(0/90/180/270) on the Display Mapping tab to restore it.";
    }

    /// <summary>Deliberately turn on every artist-pen-behavior offender on the active tablet — Windows Ink
    /// off, pen tip / pressure / tilt disabled — a <em>real</em>, persisted change, so the "pen isn't set up
    /// for drawing" bundle card (and its one-click Fix) can be exercised on a real profile. The card's Fix
    /// restores all of them; this is the inverse (#artist-pen-health).</summary>
    [RelayCommand]
    private async Task BreakPenForDrawing()
    {
        var settings = _settings?.CurrentSettings;
        if (settings == null) { ConfigErrorStatus = "No settings loaded yet — connect the daemon first."; return; }

        var tabletName = _device?.ActiveTabletName ?? _device?.DetectedTablets.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tabletName)) { ConfigErrorStatus = "No active tablet."; return; }

        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) { ConfigErrorStatus = $"{tabletName} has no profile."; return; }

        if (OperatingSystem.IsWindows())
        {
            // Windows Ink off: a native (non-WinInk) output mode + the opt-out flag the health check reads.
            profile.OutputMode ??= new PluginSettingStore(typeof(object), true);
            profile.OutputMode.Path = "OpenTabletDriver.Desktop.Output.AbsoluteMode";
            WinInkAutoOptOut.OptOut(tabletName);
        }
        profile.BindingSettings.TipButton = null;       // disable the pen tip
        profile.BindingSettings.DisablePressure = true; // flat, pressure-less strokes
        profile.BindingSettings.DisableTilt = true;     // no tilt

        await _settings!.ApplyAndSaveSettingsAsync(settings);
        ConfigErrorStatus = $"Turned off Windows Ink + pen tip + pressure + tilt on {tabletName}. Home should now " +
                            "flag \"pen isn't set up for drawing\" — its Fix restores them all in one click.";
    }

    // Each simulated corner tap is jittered up to this many px off the target, so the solved calibration
    // is slightly — but visibly — wrong, and the report shows small random deltas.
    private const float MaxCalibrationJitterPx = 12f;

    /// <summary>Fabricate a slightly-wrong 4-point calibration on the active tablet's mapped display: a
    /// <em>real</em>, persisted homography calibration whose simulated corner "taps" each land a few px off
    /// target. Lets the calibration report + fit-quality UI and a genuinely-imperfect pointer be reproduced
    /// without a pen. Undo it with <b>Clear calibration</b> on the tablet's Calibration tab.</summary>
    [RelayCommand]
    private async Task CreateBadCalibration()
    {
        if (!TryResolveActiveCalibrationContext(out var ctx, out var error))
        { ConfigErrorStatus = error; return; }
        var (tabletName, digi, input, output, display) = ctx;
        var settings = _settings!.CurrentSettings!;

        // Simulate a 4-corner capture (TL, TR, BR, BL, inset 10%) where each tap lands a few px off target,
        // so the fitted affine is slightly wrong. Same corner insets the real overlay uses.
        var corners = new (double X, double Y)[] { (0.1, 0.1), (0.9, 0.1), (0.9, 0.9), (0.1, 0.9) };
        var rng = new Random();
        float Jitter() => (float)((rng.NextDouble() * 2 - 1) * MaxCalibrationJitterPx);

        var targetsDesktop = new List<Vector2>(4);
        var measuredRaw = new List<Vector2>(4);
        foreach (var (cx, cy) in corners)
        {
            var target = new Vector2((float)(display.X + cx * display.Width), (float)(display.Y + cy * display.Height));
            var jittered = new Vector2(target.X + Jitter(), target.Y + Jitter());
            if (AbsolutePositionMapper.MapFromDesktop(jittered, digi, input, output) is not { } raw)
            { ConfigErrorStatus = "Couldn't map the simulated taps back to tablet units."; return; }
            targetsDesktop.Add(target);
            measuredRaw.Add(raw);
        }

        // Least-squares affine — the same model the real 4-point overlay writes (#483).
        if (CalibrationSolver.Solve(targetsDesktop, measuredRaw, digi, input, output) is not { } m)
        { ConfigErrorStatus = "Couldn't solve a calibration from the simulated taps — try again."; return; }

        var fingerprint = CalibrationProfile.Fingerprint(input, output, display.Number);
        var data = new CalibrationProfile.CalibrationData(m, Enabled: true, Fingerprint: fingerprint)
            with { Report = BuildSimulatedReport(display, targetsDesktop, measuredRaw, digi, input, output, rng) };

        CalibrationProfile.Write(settings, tabletName, data);
        await _settings!.ApplyAndSaveSettingsAsync(settings);
        ConfigErrorStatus = $"Created a slightly-off 4-point calibration on {tabletName}'s mapped display " +
                            $"(Display {display.Number}). Open its Calibration tab to see the report, or Clear calibration to undo.";
    }

    // The recorded-points report for the simulated calibration — mirrors CalibrationViewModel.BuildReport
    // (display-relative target + pixel-equivalent, raw units, a plausible sample count per tap).
    private static CalibrationReport BuildSimulatedReport(
        DisplayInfo display, List<Vector2> targetsDesktop, List<Vector2> measuredRaw,
        TabletDigitizerSpec digi, MappingArea input, MappingArea output, Random rng)
    {
        float ox = (float)display.X, oy = (float)display.Y;
        var points = new List<CalibrationReportPoint>(measuredRaw.Count);
        for (int i = 0; i < measuredRaw.Count; i++)
        {
            var measuredPx = AbsolutePositionMapper.MapToDesktop(measuredRaw[i], digi, input, output, false, false);
            float mx = float.NaN, my = float.NaN;
            if (measuredPx is { } m) { mx = m.X - ox; my = m.Y - oy; }
            // A plausible natural hold (~65° altitude, leaning down-right) with a little jitter, so the
            // tilt readout has data to show (#481).
            float tiltX = 18f + (float)(rng.NextDouble() * 2 - 1) * 4f;
            float tiltY = -12f + (float)(rng.NextDouble() * 2 - 1) * 4f;
            points.Add(new CalibrationReportPoint(
                targetsDesktop[i].X - ox, targetsDesktop[i].Y - oy,
                measuredRaw[i].X, measuredRaw[i].Y, mx, my, rng.Next(300, 620), tiltX, tiltY));
        }
        var name = $"{display.DisplayTitle} ({display.Width}×{display.Height})";
        return new CalibrationReport(name, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), points);
    }

    // The active tablet's calibration mapping context (tablet name + digitizer + input/output areas +
    // the matched display). Shared by the config-error and capture export/import commands.
    private readonly record struct ActiveCalibrationContext(
        string TabletName, TabletDigitizerSpec Digitizer, MappingArea Input, MappingArea Output, DisplayInfo Display);

    /// <summary>Resolve the active tablet's calibration context (Absolute mapping + digitizer + matched
    /// display), or set <paramref name="error"/> explaining why it isn't available.</summary>
    private bool TryResolveActiveCalibrationContext(out ActiveCalibrationContext ctx, out string error)
    {
        ctx = default;
        var settings = _settings?.CurrentSettings;
        if (settings == null) { error = "No settings loaded yet — connect the daemon first."; return false; }

        var tabletName = _device?.ActiveTabletName ?? _device?.DetectedTablets.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tabletName)) { error = "No active tablet."; return false; }

        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        var abs = profile?.AbsoluteModeSettings;
        if (abs?.Tablet is not { } t || abs.Display is not { } disp
            || t.Width <= 0 || t.Height <= 0 || disp.Width <= 0 || disp.Height <= 0)
        { error = $"{tabletName} needs an Absolute mapping with a known area + display first."; return false; }

        if (_device?.GetDigitizerSpec(tabletName) is not { } digi)
        { error = $"Couldn't read {tabletName}'s digitizer spec."; return false; }

        var input = new MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation);
        var output = new MappingArea(disp.X, disp.Y, disp.Width, disp.Height);

        // The mapped display = the monitor whose top-left matches the output area's origin (as DialogService).
        var originX = disp.X - disp.Width / 2;
        var originY = disp.Y - disp.Height / 2;
        var displays = DisplayEnumerator.Enumerate();
        var display = displays.FirstOrDefault(d => Math.Abs(d.X - originX) <= 2 && Math.Abs(d.Y - originY) <= 2)
                      ?? displays.FirstOrDefault(d => disp.X >= d.X && disp.X <= d.X + d.Width && disp.Y >= d.Y && disp.Y <= d.Y + d.Height);
        if (display is null) { error = "Couldn't match the mapped area to a connected display."; return false; }

        ctx = new ActiveCalibrationContext(tabletName, digi, input, output, display);
        error = "";
        return true;
    }

}
