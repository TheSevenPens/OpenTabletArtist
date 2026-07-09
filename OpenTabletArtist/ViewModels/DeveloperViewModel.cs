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

    /// <summary>Result of the last "create Start-menu shortcut" action (path on success, error otherwise).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutStatus))]
    private string _shortcutStatus = "";

    public bool HasShortcutStatus => !string.IsNullOrEmpty(ShortcutStatus);

    /// <summary>Result of the last "introduce a config error" action (what changed, or why it couldn't).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfigErrorStatus))]
    private string _configErrorStatus = "";

    public bool HasConfigErrorStatus => !string.IsNullOrEmpty(ConfigErrorStatus);

    /// <summary>Create a per-user Start-menu shortcut to this exe. Registers the app under its display
    /// name so tooling keyed to the installed-app list (e.g. the UI-screenshot automation grant) can find
    /// a dev build run from its output folder. Makes it easy to set up on another machine.</summary>
    [RelayCommand]
    private void CreateStartMenuShortcut()
    {
        ShortcutStatus = StartMenuShortcut.TryCreate(out var path, out var error)
            ? $"Created: {path}"
            : $"Couldn't create the shortcut: {error}";
    }

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

    // ---- Calibration capture export / import (#484) — save a calibration's taps + solved model to a
    // portable JSON, reload it later without re-tapping, and re-solve the same taps with a different
    // algorithm to compare. Developer-only for now. Import is matching-only. ----

    /// <summary>Status/summary for the last capture export/import action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaptureStatus))]
    private string _captureStatus = "";

    public bool HasCaptureStatus => !string.IsNullOrEmpty(CaptureStatus);

    /// <summary>True once a capture has been imported and matched — reveals the apply / re-solve controls.</summary>
    [ObservableProperty]
    private bool _hasLoadedCapture;

    /// <summary>Models offered for re-solve of the loaded capture (Grid only when the capture is a grid).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _resolveModes = Array.Empty<string>();

    [ObservableProperty]
    private string _selectedResolveMode = "Affine";

    // The imported capture, held so Apply / Re-solve reuse it without re-reading the file.
    private CalibrationCapture? _loadedCapture;

    /// <summary>A default file name for the export save dialog: <c>calibration-{N}point-{tablet}-{stamp}</c>
    /// (the point count comes from the calibration being exported, e.g. 4/9/25).</summary>
    public string SuggestedCaptureFileName
    {
        get
        {
            var tablet = _device?.ActiveTabletName;
            string count = "";
            if (!string.IsNullOrEmpty(tablet)
                && CalibrationProfile.Read(_settings?.CurrentSettings, tablet)?.Report is { Points.Count: > 0 } report)
                count = $"{report.Points.Count}point-";
            return $"calibration-{count}{Sanitize(tablet ?? "tablet")}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        }
    }

    /// <summary>Build the export JSON for the active tablet's saved calibration, or null (with a status
    /// set) if there's nothing to export. The view writes the returned text to the chosen file.</summary>
    public string? BuildCaptureJson()
    {
        if (!TryResolveActiveCalibrationContext(out var ctx, out var error))
        { CaptureStatus = error; return null; }

        var cal = CalibrationProfile.Read(_settings!.CurrentSettings, ctx.TabletName);
        if (cal == null)
        { CaptureStatus = $"{ctx.TabletName} has no calibration to export — calibrate it first (or use Create a slightly-wrong calibration)."; return null; }
        if (cal.Report == null || cal.Report.Points.Count == 0)
        { CaptureStatus = $"{ctx.TabletName}'s calibration has no recorded taps to export (it predates point capture)."; return null; }

        var capture = CalibrationCaptureService.Build(ctx.TabletName, ctx.Digitizer, ctx.Input, ctx.Output, ctx.Display, cal);
        return capture.ToJson();
    }

    /// <summary>Report a completed export (the view writes the file, then calls this).</summary>
    public void NoteCaptureExported(string path) =>
        CaptureStatus = $"Exported calibration capture to {path}.";

    /// <summary>Parse and match-check an imported capture; on success hold it and reveal apply/re-solve.</summary>
    public void LoadCapture(string json, string fileName)
    {
        _loadedCapture = null;
        HasLoadedCapture = false;

        var capture = CalibrationCapture.FromJson(json);
        if (capture == null)
        { CaptureStatus = "Couldn't read that file as a calibration capture (invalid JSON)."; return; }
        if (capture.SchemaVersion > CalibrationCapture.CurrentSchemaVersion)
        { CaptureStatus = $"That capture is schema v{capture.SchemaVersion}, newer than this app supports (v{CalibrationCapture.CurrentSchemaVersion}). Update the app."; return; }
        if (capture.Points.Count == 0)
        { CaptureStatus = "That capture has no recorded taps."; return; }

        if (!TryResolveActiveCalibrationContext(out var ctx, out var error))
        { CaptureStatus = error; return; }

        if (!CalibrationCaptureService.Matches(capture, ctx.Digitizer, ctx.Input, ctx.Output, ctx.Display.Number, out var reason))
        {
            CaptureStatus = $"This capture doesn't match {ctx.TabletName}'s current setup ({reason}). " +
                            "Import is matching-only — re-map the tablet to the captured area, or re-capture.";
            return;
        }

        _loadedCapture = capture;
        HasLoadedCapture = true;
        ResolveModes = capture.IsGrid
            ? new[] { "Grid", "Affine", "Homography" }
            : new[] { "Affine", "Homography" };
        SelectedResolveMode = ResolveModes[0];
        CaptureStatus = $"Loaded {fileName} — {capture.Mode.ToLowerInvariant()} capture, {capture.Points.Count} taps, " +
                        $"captured {capture.CapturedAt}. Apply it as-is, or re-solve with a different model.";
    }

    /// <summary>Restore the capture's embedded solved model exactly (the known-good state).</summary>
    [RelayCommand]
    private async Task ApplyLoadedCapture()
    {
        if (_loadedCapture is not { } capture) { CaptureStatus = "Import a capture first."; return; }
        if (!TryResolveActiveCalibrationContext(out var ctx, out var error)) { CaptureStatus = error; return; }
        if (!CalibrationCaptureService.Matches(capture, ctx.Digitizer, ctx.Input, ctx.Output, ctx.Display.Number, out var reason))
        {
            CaptureStatus = $"Setup changed since import ({reason}) — re-import the capture.";
            _loadedCapture = null; HasLoadedCapture = false; return;
        }

        var settings = _settings!.CurrentSettings!;
        var fingerprint = CalibrationProfile.Fingerprint(ctx.Input, ctx.Output, ctx.Display.Number);
        var data = CalibrationCaptureService.ToCalibrationData(capture, fingerprint);
        if (data == null)
        { CaptureStatus = "This capture has no embedded solved model — use Re-solve & apply instead."; return; }

        CalibrationProfile.Write(settings, ctx.TabletName, data);
        await _settings.ApplyAndSaveSettingsAsync(settings);
        CaptureStatus = $"Applied the captured {data.Model} calibration to {ctx.TabletName}.";
    }

    /// <summary>Re-solve the loaded capture's taps with the selected model and apply it — the fast way to
    /// compare algorithms on the same taps without re-tapping.</summary>
    [RelayCommand]
    private async Task ReSolveLoadedCapture()
    {
        if (_loadedCapture is not { } capture) { CaptureStatus = "Import a capture first."; return; }
        if (!TryResolveActiveCalibrationContext(out var ctx, out var error)) { CaptureStatus = error; return; }
        if (!CalibrationCaptureService.Matches(capture, ctx.Digitizer, ctx.Input, ctx.Output, ctx.Display.Number, out var reason))
        {
            CaptureStatus = $"Setup changed since import ({reason}) — re-import the capture.";
            _loadedCapture = null; HasLoadedCapture = false; return;
        }

        var settings = _settings!.CurrentSettings!;
        var fingerprint = CalibrationProfile.Fingerprint(ctx.Input, ctx.Output, ctx.Display.Number);
        var data = CalibrationCaptureService.ReSolve(capture, SelectedResolveMode, fingerprint);
        if (data == null)
        { CaptureStatus = $"Couldn't solve a {SelectedResolveMode} model from the captured taps."; return; }

        CalibrationProfile.Write(settings, ctx.TabletName, data);
        await _settings.ApplyAndSaveSettingsAsync(settings);
        CaptureStatus = $"Re-solved {ctx.TabletName} as {SelectedResolveMode} and applied it — move the pen to test accuracy.";
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Replace(' ', '-');
    }
}
