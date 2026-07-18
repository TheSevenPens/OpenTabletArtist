using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>A tablet-page tab that a health-issue "Fix" can deep-link to (so the fix opens on the tab
/// that carries it, not the default About tab). See <see cref="TabletDetailViewModel.RequestTab"/>.</summary>
public enum TabletDetailTab
{
    PenBehavior,
    DisplayMapping,
}

/// <summary>One row of the Calibration tab's positional report (#460): the target it was captured for
/// (desktop px), the pixel-equivalent of where the uncorrected pen landed (<paramref name="Measured"/>),
/// that tap's on-screen error in px (<paramref name="Delta"/>, #461), the raw tablet coordinate the pen
/// reported, and the samples averaged for the tap. <paramref name="Measured"/>/<paramref name="Delta"/>
/// are "—" for legacy reports captured before the pixel-equivalent was recorded.</summary>
public sealed record CalibrationReportRow(string Index, string Target, string Measured, string Delta, string Raw, string Samples);

/// <summary>
/// View model for a single tablet's settings — the tabbed editor (Screen Mapping, Pen Switches,
/// ExpressKeys, Dynamics, Hover, Filters, JSON). Hosted either as an in-app page (the Tablets nav)
/// or in the focused Pen Dynamics dialog the tray opens. Delegate-based (apply / refresh / detection
/// probe / live pen input / calibrate) so the host wires it to the session.
/// </summary>
public partial class TabletDetailViewModel : ObservableObject, IDisposable
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string WinInkRelativeModePath = "VoiDPlugins.OutputMode.WinInkRelativeMode";
    // OTD's built-in output modes — used on macOS/Linux, where Windows Ink doesn't exist. Their type
    // names carry "Absolute"/"Relative", so the substring detection in RefreshFromProfile recognises
    // them exactly as it does the WinInk modes (#140).
    private const string NativeAbsoluteModePath = "OpenTabletDriver.Desktop.Output.AbsoluteMode";
    private const string NativeRelativeModePath = "OpenTabletDriver.Desktop.Output.RelativeMode";

    // The OTD mode path for a movement direction + Windows-Ink choice. Windows Ink (pressure/tilt via
    // VMulti) is the default on Windows; the "Don't use Windows Ink" toggle (#549) — and any non-Windows
    // host, where Windows Ink doesn't exist — selects OTD's native output instead. So all four
    // combinations are reachable: {Absolute, Relative} × {WinInk, native}.
    private static string ModePath(bool absolute, bool disableWinInk) =>
        !OperatingSystem.IsWindows() || disableWinInk
            ? (absolute ? NativeAbsoluteModePath : NativeRelativeModePath)
            : (absolute ? WinInkAbsoluteModePath : WinInkRelativeModePath);

    private Profile _profile;
    private Settings? _settings;
    private readonly Func<Settings, Task>? _applyAction;
    // Opens the modal binding editor for a card's current binding + label; returns the chosen binding
    // (or Unbound on Clear), or null on Cancel. Provided by the host that has the owner window.
    private readonly Func<AuxBinding, string, Task<AuxBinding?>>? _editBinding;
    // Detection source — when provided, the banner + tablet-dependent actions live-update on each
    // data load (tablet plug/unplug, #177). Unsubscribed in Dispose so a cached page VM doesn't leak.
    private readonly IDeviceData? _deviceData;
    // Removes this tablet's saved profile (wired by the page host). Null in the tray dialog.
    private readonly Func<Task>? _forgetAction;
    // Opens the calibration overlay for the chosen options; the host supplies the owner window.
    // Null when calibration isn't available (the focused Pen Dynamics dialog hides Screen Mapping).
    private readonly Func<CalibrationOptions, Task>? _onCalibrate;
    // Navigates to the CONFIGS page — the config-override card's Review button on the ABOUT tab (#467).
    private readonly Action? _openConfigsPage;
    // Returns the freshly-reloaded settings together with this tablet's profile from within them, so
    // the VM can keep _settings and _profile coherent (the profile is a reference inside the settings).
    private readonly Func<Task<(Settings? Settings, Profile? Profile)>>? _refreshAction;
    // Probes whether this tablet is currently detected/connected (re-checked on open and on Refresh).
    private readonly Func<bool>? _isDetectedProbe;
    private readonly (float Width, float Height)? _tabletDigitizer;

    // External-change reconciliation: the same app-owned daemon can be edited by another client
    // (notably the OTD UX changing a mapping). The daemon pushes no "settings changed" event on a
    // successful apply, so the shell re-pulls (on window activation, TabletsChanged, and the poll) and
    // calls ReconcileExternalChange. When a reload diverges from what this editor holds we adopt it
    // silently — unless the user has an unsaved edit, where we stash it and surface a banner instead
    // of discarding their in-progress change.
    private Settings? _pendingExternalSettings;
    private Profile? _pendingExternalProfile;

    /// <summary>Non-empty when the daemon's settings changed outside OTA while the user had an unsaved
    /// edit here; drives a header banner with a Reload action. Empty otherwise.</summary>
    [ObservableProperty] private string _externalChangeText = "";
    public bool HasExternalChange => !string.IsNullOrEmpty(ExternalChangeText);
    partial void OnExternalChangeTextChanged(string value) => OnPropertyChanged(nameof(HasExternalChange));

    [ObservableProperty] private IReadOnlyList<DisplayInfo> _displays = [];
    [ObservableProperty] private int? _selectedDisplayNumber;

    /// <summary>A display is selected, so "Apply" can map to it.</summary>
    public bool CanApplyDisplay => SelectedDisplayNumber != null && _applyAction != null;

    /// <summary>How the stored display mapping relates to the connected monitors, so the tab can flag an
    /// off-screen or custom mapping instead of silently rendering it. Recomputed via
    /// <see cref="RefreshTabletArea"/> on every mapping/display change.</summary>
    public DisplayMappingValidity MappingValidity => DisplayMappingApplier.ClassifyMapping(_profile, Displays);
    public bool ShowMappingOffScreen => MappingValidity == DisplayMappingValidity.OffScreen;
    public bool ShowMappingCustom => MappingValidity == DisplayMappingValidity.Custom;
    public string MappingValidityText => MappingValidity switch
    {
        DisplayMappingValidity.OffScreen =>
            "This tablet's mapped area extends beyond your displays — part of the tablet maps to off-screen " +
            "space, so the pen reaches dead zones there. Pick a display below and Apply mapping to fix it.",
        DisplayMappingValidity.Custom =>
            "This tablet isn't mapped to a single whole display (a custom or multi-display area). Pick a " +
            "display below and Apply mapping for a standard, undistorted 1:1 setup.",
        _ => "",
    };

    /// <summary>The selected display differs from the one currently applied, so the change still needs
    /// "Apply mapping" — drives the pending hint so it's obvious the selection isn't live yet (#179
    /// follow-up). Suppressed during the initial load so an as-opened profile doesn't read as pending
    /// before the user changes anything.</summary>
    [ObservableProperty] private bool _mappingChangePending;
    private bool _suppressMappingPending;

    private void RecomputeMappingPending() =>
        MappingChangePending = _applyAction != null
            && SelectedDisplayNumber != null
            && SelectedDisplayNumber != CurrentlyMappedNumber();

    partial void OnSelectedDisplayNumberChanged(int? value)
    {
        OnPropertyChanged(nameof(CanApplyDisplay));
        ApplyDisplayCommand.NotifyCanExecuteChanged();
        if (!_suppressMappingPending) RecomputeMappingPending();
    }

    partial void OnIsAbsoluteOutputModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAbsoluteMode)); // keep the Absolute/Relative toggle in sync
        OnPropertyChanged(nameof(CanCalibrate));   // calibration needs an Absolute mode (#127)
        OnPropertyChanged(nameof(CanRunCalibration));
        OnPropertyChanged(nameof(ShowConnectToCalibrateHint));
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(ModePath(true, DisableWindowsInk));
    }

    // --- Pointer calibration entry point (#127) ---

    /// <summary>Calibration corrects an Absolute mapping, so the calibration UI is only shown in an
    /// Absolute mode. (Whether it can actually run also needs a live tablet — see
    /// <see cref="CanRunCalibration"/>.)</summary>
    public bool CanCalibrate => IsAbsoluteOutputMode;

    /// <summary>Calibration captures live pen taps, so it additionally needs the tablet connected
    /// (#177) and a host that can open the overlay. Gates the Calibrate button so it can't be clicked
    /// into the "not detected" dead-end, and flips live as the tablet is (un)plugged.</summary>
    public bool CanRunCalibration => CanCalibrate && IsTabletDetected && _onCalibrate != null;

    /// <summary>In an Absolute mode but the tablet isn't connected, so calibration is shown but
    /// disabled — prompt the user to connect it (#177).</summary>
    public bool ShowConnectToCalibrateHint => CanCalibrate && !IsTabletDetected;

    /// <summary>Which display the calibration overlay opens on — the tablet's currently-mapped display —
    /// so the user knows which screen to watch. Recomputed whenever the mapping/displays change (via
    /// <see cref="RefreshTabletArea"/>).</summary>
    public string CalibrationDisplayText
    {
        get
        {
            var mapped = DisplayMappingApplier.CurrentlyMapped(_profile, Displays);
            return mapped != null
                ? $"Calibration opens on Display {mapped.Number} ({mapped.Name}) — where your tablet is mapped."
                : "Calibration opens on the display your tablet is mapped to.";
        }
    }

    /// <summary>Calibration capture presets: the corner method (→ homography, #195) or a finer grid
    /// (→ bilinear offsets, #196). Each backs a calibration card whose START button begins that mode.</summary>
    public IReadOnlyList<CalibrationModeChoice> CalibrationModeChoices { get; } = new List<CalibrationModeChoice>
    {
        new("4 point", CalibrationMode.Corners, 0, 0),
        new("9 point", CalibrationMode.Grid, 3, 3),
        new("25 point", CalibrationMode.Grid, 5, 5),
    };

    // Point count of the calibration currently applied to this profile (0 = none), so each card can show
    // whether it's the one in use.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent4Point))]
    [NotifyPropertyChangedFor(nameof(IsCurrent9Point))]
    [NotifyPropertyChangedFor(nameof(IsCurrent25Point))]
    private int _currentCalibrationPoints;

    public bool IsCurrent4Point => CurrentCalibrationPoints == 4;
    public bool IsCurrent9Point => CurrentCalibrationPoints == 9;
    public bool IsCurrent25Point => CurrentCalibrationPoints == 25;

    /// <summary>Start calibrating in the chosen mode — each calibration card's START button passes its
    /// <see cref="CalibrationModeChoice"/>. Reloads afterward so the status + stale hint stay coherent (#147).</summary>
    [RelayCommand]
    private async Task StartCalibration(CalibrationModeChoice? choice)
    {
        if (_onCalibrate == null || choice == null) return;
        await _onCalibrate(choice.ToOptions());
        await Refresh();
    }

    /// <summary>True when a calibration exists but was captured against a different area mapping than
    /// the current one — it may no longer be accurate, so suggest recalibrating (#147).</summary>
    [ObservableProperty] private bool _calibrationStale;

    /// <summary>A calibration is <em>stored</em> on this profile (enabled or not) — gates the enable
    /// toggle, the Clear button, and the report card.</summary>
    [ObservableProperty] private bool _hasCalibration;

    /// <summary>Whether the stored calibration is currently applying its correction. Two-way bound to the
    /// enable toggle so it can be turned off to compare with/without the calibration, without clearing or
    /// recapturing it. Setting it from the UI rewrites the stored calibration with the new Enabled state.</summary>
    [ObservableProperty] private bool _calibrationEnabled;

    /// <summary>Human-readable calibration state ("Not calibrated" / "Calibrated — …" / "Calibration off — …").</summary>
    [ObservableProperty] private string _calibrationStatusText = "Not calibrated";

    // Guards RefreshCalibrationStatus's assignment of CalibrationEnabled so reloading state doesn't
    // re-trigger a write.
    private bool _refreshingCalibration;

    public bool CanClearCalibration => HasCalibration && _applyAction != null;
    partial void OnHasCalibrationChanged(bool value) => OnPropertyChanged(nameof(CanClearCalibration));

    partial void OnCalibrationEnabledChanged(bool value)
    {
        if (_refreshingCalibration) return;
        _ = SetCalibrationEnabledAsync(value);
    }

    // Rewrite the stored calibration with the new enabled state, preserving its model/payload/report.
    private async Task SetCalibrationEnabledAsync(bool enabled)
    {
        var cal = CalibrationProfile.ReadProfile(_profile);
        if (cal == null) return;
        await ApplySettingsChange(p => CalibrationProfile.Write(_settings, p.Tablet ?? "", cal with { Enabled = enabled }));
    }

    // Positional report of the recorded points from the current calibration (#460). Shown as a card on
    // the Calibration tab whenever a calibration with stored points is active.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportCalibration))]
    private bool _hasCalibrationReport;
    [ObservableProperty] private string _calibrationReportSummary = "";
    // Fit quality derived from the recorded taps (#461): the pre-calibration pointing error, and a
    // warning when one tap looks like a misfire.
    [ObservableProperty] private bool _hasCalibrationFit;
    [ObservableProperty] private string _calibrationFitText = "";
    [ObservableProperty] private bool _hasCalibrationFitWarning;
    [ObservableProperty] private string _calibrationFitWarning = "";
    // Average pen tilt across the taps (#481) — surfaced so the natural drawing angle is visible.
    [ObservableProperty] private bool _hasCalibrationTilt;
    [ObservableProperty] private string _calibrationTiltText = "";
    public ObservableCollection<CalibrationReportRow> CalibrationReportRows { get; } = new();

    private void RefreshCalibrationStatus()
    {
        _refreshingCalibration = true;
        try
        {
            var cal = CalibrationProfile.ReadProfile(_profile);
            HasCalibration = cal != null;
            CalibrationStale = CalibrationProfile.IsStale(cal, CurrentMappingFingerprint());
            CalibrationEnabled = cal is { Enabled: true };

            static string ModeLabel(CalibrationProfile.CalibrationData c) => c.Model switch
            {
                CalibrationProfile.CalibrationModel.Homography => "4 point (perspective)", // older stores
                CalibrationProfile.CalibrationModel.Grid => $"{(c.Grid?.Cols ?? 0) * (c.Grid?.Rows ?? 0)} point",
                _ => "4 point", // least-squares affine — the current 4-point model (#483)
            };
            CalibrationStatusText = cal == null ? "Not calibrated"
                : CalibrationEnabled ? $"Calibrated — {ModeLabel(cal)}"
                : $"Calibration off — {ModeLabel(cal)}";

            // "In use" badges reflect the active correction, so nothing is "in use" while it's off.
            CurrentCalibrationPoints = !CalibrationEnabled || cal == null ? 0 : cal.Model switch
            {
                CalibrationProfile.CalibrationModel.Homography => 4,
                CalibrationProfile.CalibrationModel.Grid => (cal.Grid?.Cols ?? 0) * (cal.Grid?.Rows ?? 0),
                _ => 4,
            };
            // The report describes the stored capture — show it whether the correction is on or off.
            UpdateCalibrationReport(cal?.Report);
        }
        finally { _refreshingCalibration = false; }
    }

    // Rebuild the report rows from the calibration's stored points (#460), or clear them if none.
    private void UpdateCalibrationReport(CalibrationReport? report)
    {
        CalibrationReportRows.Clear();
        CalibrationFitText = "";
        CalibrationFitWarning = "";
        HasCalibrationFit = false;
        HasCalibrationFitWarning = false;
        CalibrationTiltText = "";
        HasCalibrationTilt = false;
        if (report is null || report.Points.Count == 0)
        {
            HasCalibrationReport = false;
            CalibrationReportSummary = "";
            return;
        }
        for (int i = 0; i < report.Points.Count; i++)
        {
            var p = report.Points[i];
            bool hasPx = !float.IsNaN(p.MeasuredX) && !float.IsNaN(p.MeasuredY);
            // Δ is the signed per-axis offset (measured − target) so its direction is visible, e.g. "-7, 3".
            string delta = hasPx
                ? $"{(int)MathF.Round(p.MeasuredX - p.TargetX)}, {(int)MathF.Round(p.MeasuredY - p.TargetY)}"
                : "—";
            CalibrationReportRows.Add(new CalibrationReportRow(
                (i + 1).ToString(),
                $"{p.TargetX:0}×{p.TargetY:0}",
                hasPx ? $"{p.MeasuredX:0}×{p.MeasuredY:0}" : "—",
                delta,
                $"{p.RawX:0}, {p.RawY:0}",
                p.Samples.ToString()));
        }
        CalibrationReportSummary = $"{report.DisplayName} · captured {report.CapturedAt}";
        HasCalibrationReport = true;

        // Fit quality: the pre-calibration pointing error the taps recorded (#461). A single outlier tap
        // is flagged as a likely misfire — the practical "this capture looks off" signal.
        if (report.ComputeFit() is { } fit)
        {
            CalibrationFitText = $"Pointing error corrected: {fit.RmsErrorPx:0} px RMS · up to {fit.MaxErrorPx:0} px";
            HasCalibrationFit = true;
            if (fit.HasOutlier && fit.OutlierIndex < report.Points.Count)
            {
                CalibrationFitWarning =
                    $"Point {fit.OutlierIndex + 1} stands out ({report.Points[fit.OutlierIndex].ErrorPx:0} px) — " +
                    "that tap may have missed the target; consider redoing the calibration.";
                HasCalibrationFitWarning = true;
            }
        }

        // Natural pen tilt across the taps (#481). Absent when the tablet doesn't report tilt.
        if (report.ComputeTilt() is { } tilt)
        {
            CalibrationTiltText =
                $"Pen tilt while calibrating: {tilt.AltitudeDeg:0}° from the surface, leaning {Compass(tilt.AzimuthDeg)}.";
            HasCalibrationTilt = true;
        }
    }

    // Azimuth (0° = +X / right, increasing toward +Y / down) → a rough compass word for the readout.
    private static string Compass(float azimuthDeg)
    {
        string[] dirs = { "right", "down-right", "down", "down-left", "left", "up-left", "up", "up-right" };
        int idx = (int)MathF.Round(azimuthDeg / 45f) & 7;
        return dirs[idx];
    }

    /// <summary>Copy the calibration report as tab-separated text for sharing / debugging (#460).</summary>
    [RelayCommand]
    private void CopyCalibrationReport()
    {
        if (!HasCalibrationReport) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Calibration report — {CalibrationReportSummary}");
        sb.AppendLine("#\tTarget (px)\tMeasured (px)\tΔ (px)\tMeasured raw (tablet)\tSamples");
        foreach (var r in CalibrationReportRows)
            sb.Append(r.Index).Append('\t').Append(r.Target).Append('\t').Append(r.Measured).Append('\t')
              .Append(r.Delta).Append('\t').Append(r.Raw).Append('\t').Append(r.Samples).Append('\n');
        if (HasCalibrationFit) sb.Append('\n').AppendLine(CalibrationFitText);
        if (HasCalibrationFitWarning) sb.AppendLine(CalibrationFitWarning);
        if (HasCalibrationTilt) sb.AppendLine(CalibrationTiltText);
        ClipboardText.TrySet(sb.ToString());
    }

    /// <summary>Remove the calibration filter, returning the pointer to its uncorrected default.</summary>
    [RelayCommand]
    private async Task ClearCalibration()
        => await ApplySettingsChange(p => CalibrationProfile.Clear(_settings, p.Tablet ?? ""));

    /// <summary>Fingerprint of the profile's current Absolute mapping (input + output area + mapped
    /// display), matching how <see cref="CalibrationProfile.Fingerprint"/> was written at calibration.</summary>
    private string? CurrentMappingFingerprint()
    {
        var abs = _profile.AbsoluteModeSettings;
        if (abs?.Tablet is not { } t || abs.Display is not { } d || CurrentlyMappedNumber() is not { } num)
            return null;
        var input = new MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation);
        var output = new MappingArea(d.X, d.Y, d.Width, d.Height);
        return CalibrationProfile.Fingerprint(input, output, num);
    }

    // ---- Calibration import / export (#545, moved from the Developer page). Save this tablet's
    // calibration (recorded taps + solved model) to a portable JSON, then restore it later without
    // re-tapping. Import is matching-only: it applies only when this tablet's current mapping matches the
    // captured one. The file picker itself lives in the view (it needs the window's StorageProvider). ----

    /// <summary>Status/summary line for the last calibration import/export.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaptureStatus))]
    private string _captureStatus = "";

    public bool HasCaptureStatus => !string.IsNullOrEmpty(CaptureStatus);

    /// <summary>Export is only offered when there's a calibration with recorded taps to save.</summary>
    public bool CanExportCalibration => HasCalibrationReport;

    /// <summary>A default file name for the export dialog: <c>calibration-{N}point-{tablet}-{stamp}.json</c>.</summary>
    public string SuggestedCaptureFileName
    {
        get
        {
            var cal = CalibrationProfile.ReadProfile(_profile);
            var count = cal?.Report is { Points.Count: > 0 } r ? $"{r.Points.Count}point-" : "";
            return $"calibration-{count}{Sanitize(TabletName)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        }
    }

    /// <summary>Build the export JSON for this tablet's saved calibration, or null (with a status set) when
    /// there's nothing to export. The view writes the returned text to the chosen file.</summary>
    public string? BuildCaptureJson()
    {
        if (!TryCalibrationContext(out var ctx, out var error)) { CaptureStatus = error; return null; }
        var cal = CalibrationProfile.ReadProfile(_profile);
        if (cal == null)
        { CaptureStatus = $"{TabletName} has no calibration to export — calibrate it first."; return null; }
        if (cal.Report == null || cal.Report.Points.Count == 0)
        { CaptureStatus = $"{TabletName}'s calibration has no recorded taps to export (it predates point capture)."; return null; }
        return CalibrationCaptureService.Build(TabletName, ctx.Digi, ctx.Input, ctx.Output, ctx.Display, cal).ToJson();
    }

    public void NoteCaptureExported(string path) => CaptureStatus = $"Exported calibration to {path}.";

    /// <summary>Import a previously exported calibration and apply it to this tablet. Matching-only — it
    /// applies only when the tablet's current mapping matches the capture; otherwise it explains why.</summary>
    public async Task ImportCalibrationAsync(string json, string fileName)
    {
        var capture = CalibrationCapture.FromJson(json);
        if (capture == null)
        { CaptureStatus = "Couldn't read that file as a calibration (invalid JSON)."; return; }
        if (capture.SchemaVersion > CalibrationCapture.CurrentSchemaVersion)
        { CaptureStatus = $"That file is newer (v{capture.SchemaVersion}) than this app supports (v{CalibrationCapture.CurrentSchemaVersion}). Update the app."; return; }
        if (capture.Points.Count == 0)
        { CaptureStatus = "That file has no recorded calibration taps."; return; }

        if (!TryCalibrationContext(out var ctx, out var error)) { CaptureStatus = error; return; }
        if (!CalibrationCaptureService.Matches(capture, ctx.Digi, ctx.Input, ctx.Output, ctx.Display.Number, out var reason))
        {
            CaptureStatus = $"This calibration doesn't match {TabletName}'s current setup ({reason}). " +
                            "Import is matching-only — map the tablet to the captured area, or recalibrate.";
            return;
        }

        var fingerprint = CalibrationProfile.Fingerprint(ctx.Input, ctx.Output, ctx.Display.Number);
        var data = CalibrationCaptureService.ToCalibrationData(capture, fingerprint);
        if (data == null)
        { CaptureStatus = "That file has no solved calibration model to apply."; return; }

        await ApplySettingsChange(p => CalibrationProfile.Write(_settings, p.Tablet ?? "", data));
        CaptureStatus = $"Imported {fileName} and applied it to {TabletName}.";
    }

    // This tablet's calibration mapping context (digitizer + input/output areas + the mapped display),
    // resolved from its current Absolute mapping — what Build/Matches need.
    private bool TryCalibrationContext(
        out (TabletDigitizerSpec Digi, MappingArea Input, MappingArea Output, DisplayInfo Display) ctx, out string error)
    {
        ctx = default;
        var abs = _profile.AbsoluteModeSettings;
        if (abs?.Tablet is not { } t || abs.Display is not { } disp
            || t.Width <= 0 || t.Height <= 0 || disp.Width <= 0 || disp.Height <= 0)
        { error = $"{TabletName} needs an Absolute mapping with a known area + display first."; return false; }

        if (_deviceData?.GetDigitizerSpec(_profile.Tablet ?? "") is not { } digi)
        { error = $"Couldn't read {TabletName}'s digitizer spec."; return false; }

        if (DisplayMappingApplier.CurrentlyMapped(_profile, Displays) is not { } display)
        { error = "Couldn't match the mapped area to a connected display."; return false; }

        var input = new MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation);
        var output = new MappingArea(disp.X, disp.Y, disp.Width, disp.Height);
        ctx = (digi, input, output, display);
        error = "";
        return true;
    }

    private static string Sanitize(string s)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Replace(' ', '-');
    }

    partial void OnIsRelativeOutputModeChanged(bool value)
    {
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(ModePath(false, DisableWindowsInk));
    }

    /// <summary>True when the profile is on an Absolute output mode (Windows Ink absolute on Windows, OTD's
    /// native absolute elsewhere) — drives the Movement cards' checked state (read-only / one-way). The mode
    /// is CHANGED via <see cref="SelectMovementCommand"/>, not by writing this: two RadioButtons in a group
    /// both TwoWay-bound here (one inverted) fought each other into an infinite apply↔reload loop, so
    /// selection is now command-driven and the checked state is display-only.</summary>
    public bool IsAbsoluteMode => IsAbsoluteOutputMode;

    /// <summary>User picked a Movement card (Absolute / Relative). Fires only on a real click — unlike a
    /// TwoWay IsChecked binding, which the RadioButton group re-triggers on every reload.</summary>
    [RelayCommand]
    private void SelectMovement(string mode)
    {
        bool wantAbsolute = mode == "absolute";
        // Already in the requested direction? No-op. This keeps a redundant click from re-applying (the
        // save-loop guard), AND stops a tablet that's already on a *native* Absolute/Relative mode from
        // being force-swapped to the Windows-Ink equivalent on a click — the one deliberate, documented
        // Windows behaviour change from the output-mode generalisation (#140 / the #510 review). Only a
        // genuine direction change applies the platform-preferred mode.
        if (wantAbsolute ? IsAbsoluteOutputMode : IsRelativeOutputMode) return;
        // Preserve the current Windows-Ink choice when switching direction (#549).
        _ = SetOutputMode(ModePath(wantAbsolute, DisableWindowsInk));
    }

    private async Task SetOutputMode(string path)
    {
        // Never re-apply the mode we're already on (also makes a redundant card click a no-op).
        if (OutputModePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
        await ApplySettingsChange(p =>
        {
            p.OutputMode ??= new PluginSettingStore(path, true);
            p.OutputMode.Path = path;
        });
        if (path.Contains("WinInk", StringComparison.OrdinalIgnoreCase))
            WinInkAutoOptOut.Clear(_profile.Tablet);
        else
            WinInkAutoOptOut.OptOut(_profile.Tablet);
    }

    // ── "Don't use Windows Ink" toggle — Windows-only, applies to both movement modes (#549) ─────────
    /// <summary>Whether the "Don't use Windows Ink" toggle applies: any mode on Windows. Off-Windows there
    /// is no Windows Ink (OTD's native output is already used), so the toggle is hidden.</summary>
    public bool CanDisableWindowsInk => OperatingSystem.IsWindows();

    /// <summary>On → swap the current mode's Windows-Ink variant for OTD's plain (native) output, so the pen
    /// behaves like a mouse at the cost of pressure and tilt. Orthogonal to Absolute/Relative — it applies
    /// to whichever direction is active (#549). Reads back from the profile in
    /// <see cref="RefreshFromProfile"/> (guarded); only a real user toggle applies a mode change.</summary>
    [ObservableProperty] private bool _disableWindowsInk;

    partial void OnDisableWindowsInkChanged(bool value)
    {
        if (_skipOutputModeChange || !OperatingSystem.IsWindows()) return;
        _ = SetOutputMode(ModePath(IsAbsoluteOutputMode, value));
    }

    public TabletDetailViewModel(Profile profile, Settings? settings,
        Func<Settings, Task>? applyAction = null,
        Func<Task<(Settings? Settings, Profile? Profile)>>? refreshAction = null,
        (float Width, float Height)? tabletDigitizer = null,
        IDaemonDebugSession? penInput = null,
        Func<bool>? isDetected = null,
        bool dynamicsOnly = false,
        IDeviceData? deviceData = null,
        Func<Task>? forgetAction = null,
        Func<CalibrationOptions, Task>? onCalibrate = null,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding = null,
        Action? openConfigsPage = null)
    {
        _profile = profile;
        _settings = settings;
        _applyAction = applyAction;
        _editBinding = editBinding;
        _refreshAction = refreshAction;
        _isDetectedProbe = isDetected;
        _tabletDigitizer = tabletDigitizer;
        _deviceData = deviceData;
        _forgetAction = forgetAction;
        _onCalibrate = onCalibrate;
        _openConfigsPage = openConfigsPage;
        DynamicsOnly = dynamicsOnly;

        if (penInput != null)
        {
            _penInput = new DaemonPenInputSource(penInput, AcceptDeviceReportForThisTablet);
            _penInput.Sample += OnPenSample;
            _penInput.AuxButtons += OnAuxButtons;
            _penInput.WheelButtons += OnWheelButtons;
            _penInput.WheelPositions += OnWheelPositions;
            _penInput.WheelDeltas += OnWheelDeltas;
        }

        // Live-refresh the detection banner + tablet-dependent actions as tablets connect/disconnect
        // while this view is open (#177, via the session's DataLoaded after a TabletsChanged push #170).
        if (_deviceData != null)
        {
            _deviceData.DataLoaded += RefreshDetectionStatus;
            _deviceData.DataLoaded += RefreshConfigOverride;   // config dir + files arrive/change on reload (#467)
        }

        // Show/hide the developer-only Filters/JSON tabs live as their Developer-tab toggles change.
        DeveloperSettings.Instance.PropertyChanged += OnDeveloperSettingsChanged;

        TabletName = profile.Tablet ?? "Unknown Tablet";
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        Displays = DisplayEnumerator.Enumerate();
        LoadAuxEnabledState();
        LoadWheelEnabledState();
        RefreshFromProfile();
        RefreshDetectionStatus();
        RefreshConfigOverride();
        // Highlight the display the tablet is currently mapped to (else the primary). Suppress the
        // pending flag for this initial, programmatic selection so it doesn't open "pending".
        _suppressMappingPending = true;
        SelectedDisplayNumber = DefaultSelectedDisplay();
        _suppressMappingPending = false;
    }

    /// <summary>Show the Filters tab — a developer-only view of the profile's raw filters, hidden unless
    /// enabled on Advanced → Developer (users never need it).</summary>
    public bool ShowFiltersTab => DeveloperSettings.Instance.ShowFiltersTab;
    /// <summary>Show the JSON tab — the raw settings JSON, hidden unless enabled on Advanced → Developer.</summary>
    public bool ShowJsonTab => DeveloperSettings.Instance.ShowJsonTab;
    /// <summary>Show the "Cut below input minimum" dead-zone checkbox in Pressure Dynamics — hidden unless
    /// re-enabled on the Developer tab (#569).</summary>
    public bool ShowCutBelowMinimum => DeveloperSettings.Instance.ShowCutBelowMinimum;

    private void OnDeveloperSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeveloperSettings.ShowFiltersTab)) OnPropertyChanged(nameof(ShowFiltersTab));
        else if (e.PropertyName == nameof(DeveloperSettings.ShowJsonTab)) OnPropertyChanged(nameof(ShowJsonTab));
        else if (e.PropertyName == nameof(DeveloperSettings.ShowCutBelowMinimum)) OnPropertyChanged(nameof(ShowCutBelowMinimum));
    }

    // Parameterless constructor for design-time
    public TabletDetailViewModel()
    {
        _profile = new Profile();
        TabletName = "Design Tablet";
        Displays = [];
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private static string GetBindingName(PluginSettingStore? store) =>
        PenSwitchBinding.GetBindingLabel(store, GetPluginFriendlyName);

    /// <summary>Set when an in-page Refresh finds this tablet's profile gone (unplugged/removed since
    /// it was opened); surfaced as a header warning. Cleared on a successful refresh.
    /// (#124 / Cursor review on #125)</summary>
    [ObservableProperty] private string? _refreshWarning;

    /// <summary>When true, this is the focused Pen Dynamics editor: the tab bar is hidden and only the
    /// Dynamics content shows (#133). The Dynamics tab is preselected by the view.</summary>
    [ObservableProperty] private bool _dynamicsOnly;

    // --- Tablet detected/connected banner (#132) ---

    /// <summary>True when this tablet is the currently-connected one (green check vs. amber warning).</summary>
    [ObservableProperty] private bool _isTabletDetected;
    /// <summary>Short detection status for the header chip ("Detected" / "Not detected").</summary>
    [ObservableProperty] private string _detectionText = "";
    /// <summary>Longer detail shown under the header when the tablet isn't detected (empty when it is).</summary>
    [ObservableProperty] private string _detectionDetail = "";

    /// <summary>Whether this view draws its own header (name + status chip + Refresh/Forget). The TABLET
    /// page host (#542) sets this false because it owns the switcher-dropdown header and mirrors the
    /// status/actions there; standalone uses (e.g. the focused Pen Dynamics dialog) keep it true.</summary>
    [ObservableProperty] private bool _showHeader = true;

    partial void OnIsTabletDetectedChanged(bool value)
    {
        // Tablet-dependent actions follow the live detection state (#177).
        OnPropertyChanged(nameof(CanRunCalibration));
        OnPropertyChanged(nameof(ShowConnectToCalibrateHint));
    }

    /// <summary>Re-evaluate the detection banner + tablet-dependent actions from the current session
    /// state. Called on open, on manual Refresh, and live whenever the daemon reports a tablet
    /// add/remove while the page stays open (#177, driven by the #170 TabletsChanged signal).</summary>
    public void RefreshDetectionStatus()
    {
        IsTabletDetected = _isDetectedProbe?.Invoke() ?? false;
        DetectionText = IsTabletDetected ? "Detected" : "Not detected";
        DetectionDetail = IsTabletDetected ? "" : "Not currently detected — showing this tablet's saved settings.";
        // Detection changing is exactly when the digitizer specs (dis)appear, so recompute the active
        // area too. This self-heals "Active-area details aren't available" on reconnect — no restart.
        RefreshTabletArea();
        RefreshAbout(); // the ABOUT facts come from the same specs, so recover them on reconnect too
    }

    /// <summary>Rebuild the ABOUT tab's fact lists from the tablet's live specs (empty when not detected).</summary>
    private void RefreshAbout()
    {
        var info = _deviceData != null ? TabletAboutInfo.From(_deviceData.Tablets, TabletName) : null;
        TabletFacts = info != null ? BuildFacts(info) : System.Array.Empty<TabletFact>();
        TabletFeatures = info != null ? BuildFeatures(info) : System.Array.Empty<TabletFact>();
    }

    /// <summary>The tablet's core spec read-out for the ABOUT tab's SPECIFICATIONS card (identity + active
    /// area size / diagonal / aspect ratio). Each row is included only when known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabletFacts))]
    private IReadOnlyList<TabletFact> _tabletFacts = System.Array.Empty<TabletFact>();

    public bool HasTabletFacts => TabletFacts.Count > 0;

    /// <summary>The tablet's capability read-out for the ABOUT tab's FEATURES card — everything after the
    /// active-area aspect ratio (resolution, pressure, buttons, wheels/strips, touch, USB id).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabletFeatures))]
    private IReadOnlyList<TabletFact> _tabletFeatures = System.Array.Empty<TabletFact>();

    public bool HasTabletFeatures => TabletFeatures.Count > 0;

    // Core dimensions — the SPECIFICATIONS card (up to and including the active-area aspect ratio).
    private static IReadOnlyList<TabletFact> BuildFacts(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (!string.IsNullOrEmpty(a.Name)) facts.Add(new("Name", a.Name));
        if (a.WidthMm > 0 && a.HeightMm > 0)
        {
            facts.Add(new("Active area",
                $"{a.WidthMm:0.#} × {a.HeightMm:0.#} mm  ({a.WidthMm / 25.4:0.0} × {a.HeightMm / 25.4:0.0} in)"));
            double diag = System.Math.Sqrt(a.WidthMm * a.WidthMm + a.HeightMm * a.HeightMm);
            facts.Add(new("Active area diagonal", $"{diag:0.#} mm  ({diag / 25.4:0.0} in)"));
            facts.Add(new("Active area aspect ratio", TabletAboutInfo.FormatAspectRatio(a.WidthMm, a.HeightMm)));
        }
        return facts;
    }

    // Capabilities — the FEATURES card (everything after the active-area aspect ratio).
    private static IReadOnlyList<TabletFact> BuildFeatures(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (a.LpMm is > 0 && a.Lpi is > 0)
            facts.Add(new("Digitizer resolution", $"{a.LpMm:N0} LPmm ({a.Lpi:N0} LPI)"));
        if (a.MaxPressure is > 0) facts.Add(new("Pressure levels", $"{a.MaxPressure:N0}"));
        if (a.PenButtons is { } pb) facts.Add(new("Pen buttons", pb.ToString()));
        if (a.ExpressKeys is > 0) facts.Add(new("Buttons", a.ExpressKeys!.Value.ToString()));
        if (a.MouseButtons is > 0) facts.Add(new("Mouse buttons", a.MouseButtons!.Value.ToString()));
        if (a.WheelCount > 0) facts.Add(new("Touch ring / wheel", a.WheelCount == 1 ? "Yes" : a.WheelCount.ToString()));
        if (a.StripCount > 0) facts.Add(new("Touch strips", a.StripCount.ToString()));
        if (a.HasTouch) facts.Add(new("Touch input", "Supported"));
        return facts;
    }

    // --- Config-override notice (#467): mirror the Home "Needs attention" card on the ABOUT tab, so the
    // use of a custom config that shadows OTD's built-in is visible right where the tablet is inspected. ---

    /// <summary>This tablet is driven by a user config file that overrides OTD's vetted built-in of the
    /// same name — shows an attention card on the ABOUT tab (#467).</summary>
    [ObservableProperty] private bool _isConfigOverride;

    private void RefreshConfigOverride()
    {
        IsConfigOverride = _deviceData != null
            && !string.IsNullOrEmpty(TabletName)
            && TabletConfigInspector.OverriddenBaseNames(_deviceData.ConfigurationDirectory).Contains(TabletName);
    }

    /// <summary>Open the CONFIGS page to review/remove the override (the card's Review button).</summary>
    [RelayCommand]
    private void ReviewConfig() => _openConfigsPage?.Invoke();

    [RelayCommand]
    private async Task Refresh()
    {
        if (_refreshAction == null) return;
        var (settings, profile) = await _refreshAction();
        if (profile == null)
        {
            // The tablet/profile is gone (unplugged or removed since it was opened). Keep showing the
            // last-known data rather than blanking it, but warn it may be stale.
            RefreshWarning = "This tablet is no longer detected — showing the last known settings.";
            RefreshDetectionStatus();
            return;
        }

        if (settings == null) { RefreshDetectionStatus(); return; }
        AdoptProfile(settings, profile);
    }

    /// <summary>Point the editor at a freshly-loaded settings/profile pair (from a manual Refresh or an
    /// external-change reload) and rebuild every bound view from it. Both are reassigned together so
    /// later edits push the same settings object the profile lives in — otherwise persists would mutate
    /// stale settings (#124).</summary>
    private void AdoptProfile(Settings settings, Profile profile)
    {
        _settings = settings;
        _profile = profile;
        // The profile must be a live reference inside the settings we now persist through; if a future
        // source returns a detached profile, edits would silently write elsewhere.
        Debug.Assert(settings.Profiles.Contains(profile),
            "Adopted profile must be a reference inside the adopted settings (#124).");
        RefreshWarning = null;
        ClearExternalChange();
        RefreshFromProfile();
        // Move the display picker to the now-current mapping (suppress the pending flag for this
        // programmatic selection so it doesn't read as an unapplied change).
        _suppressMappingPending = true;
        SelectedDisplayNumber = DefaultSelectedDisplay();
        _suppressMappingPending = false;
        MappingChangePending = false; // the selection now matches the adopted mapping (any prior pick is discarded)
        RefreshDetectionStatus();
    }

    /// <summary>Reconcile this editor with a fresh settings load from the session. Called by the shell
    /// on every data load (the window-activation pull, TabletsChanged, and the fallback poll). It's a
    /// no-op when the reload matches what we already show — including our own applies, since those
    /// mutate <c>_profile</c> before pushing, so its live fingerprint already equals the reloaded one.
    /// When it genuinely diverged (an external editor changed the daemon), adopt it silently, or — if
    /// the user has an unsaved edit — raise a non-destructive banner instead of discarding it.</summary>
    public void ReconcileExternalChange(Settings? freshSettings, Profile? freshProfile)
    {
        if (freshSettings == null || freshProfile == null) return; // tablet gone — detection banner owns that
        var freshFp = ProfileFingerprint.Compute(freshProfile);
        var ownFp = ProfileFingerprint.Compute(_profile);
        if (freshFp.Length == 0 || ownFp.Length == 0) return; // can't compare → don't risk a false positive
        if (freshFp == ownFp) { ClearExternalChange(); return; } // in sync (covers our own applies)

        if (HasUnsavedEdit)
        {
            _pendingExternalSettings = freshSettings;
            _pendingExternalProfile = freshProfile;
            ExternalChangeText =
                "These settings were changed outside OpenTabletArtist (for example in the OpenTabletDriver " +
                "UX). Reload to use the current values — your unsaved change here will be discarded.";
        }
        else
        {
            AdoptProfile(freshSettings, freshProfile);
        }
    }

    /// <summary>Adopt the externally-changed settings the banner is holding (the banner's Reload action).</summary>
    [RelayCommand]
    private void ReloadExternalChange()
    {
        if (_pendingExternalSettings != null && _pendingExternalProfile != null)
            AdoptProfile(_pendingExternalSettings, _pendingExternalProfile);
    }

    private void ClearExternalChange()
    {
        _pendingExternalSettings = null;
        _pendingExternalProfile = null;
        if (ExternalChangeText.Length != 0) ExternalChangeText = "";
    }

    /// <summary>The user has a change here that isn't yet persisted, so an external reload must not
    /// silently overwrite it. Currently just a picked-but-unapplied display mapping — every other edit
    /// auto-applies immediately; extend this if more deferred edits are added.</summary>
    private bool HasUnsavedEdit => MappingChangePending;

    private async Task ApplySettingsChange(Action<Profile> modify)
    {
        if (_applyAction == null || _settings == null) return;
        modify(_profile);
        await _applyAction(_settings);
        RefreshFromProfile();
    }

    /// <summary>Remove this tablet's saved profile. Forget now lives on the Home tablet cards (#575);
    /// this command + <c>_forgetAction</c> stay wired for a future non-dynamics tray dialog, but no view
    /// currently binds it.</summary>
    [RelayCommand]
    private async Task Forget()
    {
        if (_forgetAction != null) await _forgetAction();
    }

    // ── Pen input toggles (#469): per-profile BindingSettings flags applied on change ────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PressureDisabledNote))]
    private bool _disablePressure;
    [ObservableProperty] private bool _disableTilt;
    [ObservableProperty] private bool _dragBindingsEnabled;
    [ObservableProperty] private bool _disablePenTip;
    private bool _skipPenInputPersist;

    /// <summary>Backup slot (per tablet, persisted) for the tip binding stashed while the tip is
    /// disabled, so turning the tip back on restores exactly what was there — mirrors the aux master
    /// toggle's stash (#493).</summary>
    private string TipBackupKey => $"TipBackup:{_profile.Tablet}";

    /// <summary>Shown on the Pressure Dynamics tab when pressure is disabled — the curve then has no
    /// effect, because the output stage drops pressure entirely (#469).</summary>
    public bool PressureDisabledNote => DisablePressure;

    partial void OnDisablePressureChanged(bool value)
    {
        if (_skipPenInputPersist) return;
        _ = ApplySettingsChange(p => p.BindingSettings.DisablePressure = value);
    }

    partial void OnDisableTiltChanged(bool value)
    {
        if (_skipPenInputPersist) return;
        _ = ApplySettingsChange(p => p.BindingSettings.DisableTilt = value);
    }

    partial void OnDragBindingsEnabledChanged(bool value)
    {
        if (_skipPenInputPersist) return;
        _ = ApplySettingsChange(p => p.BindingSettings.EnableDragBindings = value);
    }

    partial void OnDisablePenTipChanged(bool value)
    {
        if (_skipPenInputPersist) return;
        _ = SetPenTipDisabledAsync(value);
    }

    /// <summary>Disable the pen tip by clearing its binding — OTD has no "disable tip" flag; the tip is
    /// a binding (<c>BindingSettings.TipButton</c>), so "off" means no binding. The prior binding is
    /// stashed so turning the tip back on restores exactly what was there, falling back to the Adaptive
    /// (Tip) default if there's no usable stash (#493).</summary>
    private async Task SetPenTipDisabledAsync(bool disabled)
    {
        if (_applyAction == null) return;
        if (disabled)
        {
            AppSettings.Set(TipBackupKey, JsonConvert.SerializeObject(_profile.BindingSettings.TipButton));
            await ApplySettingsChange(p => p.BindingSettings.TipButton = null);
        }
        else
        {
            var backup = AppSettings.Get(TipBackupKey);
            PluginSettingStore? restored = null;
            if (!string.IsNullOrEmpty(backup))
            {
                try { restored = JsonConvert.DeserializeObject<PluginSettingStore>(backup); }
                catch { /* corrupt stash — fall back to the default below */ }
            }
            restored ??= PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
            await ApplySettingsChange(p => p.BindingSettings.TipButton = restored);
            AppSettings.Set(TipBackupKey, "");
        }
    }

    private void RefreshFromProfile()
    {
        // Output mode
        OutputModePath = _profile.OutputMode?.Path ?? "Not set";
        OutputModeShort = OutputModePath.Split('.').LastOrDefault() ?? OutputModePath;

        var isWinInk = OutputModePath.Contains("WinInk", StringComparison.OrdinalIgnoreCase);

        _skipOutputModeChange = true;
        // Detect the movement direction from the mode-path name, so OTD's NATIVE modes
        // (OpenTabletDriver.Desktop.Output.AbsoluteMode/RelativeMode) are recognised as well as the
        // WinInk modes — both carry the word "Absolute"/"Relative" (#140). For a WinInk path this is the
        // same result the old exact-match gave, so Windows behaviour is unchanged.
        IsAbsoluteOutputMode = OutputModePath.Contains("Absolute", StringComparison.OrdinalIgnoreCase);
        IsRelativeOutputMode = OutputModePath.Contains("Relative", StringComparison.OrdinalIgnoreCase);
        // "Don't use Windows Ink" is on whenever a Windows tablet is on a non-WinInk mode — either
        // direction (#549).
        DisableWindowsInk = OperatingSystem.IsWindows() && !isWinInk;
        _skipOutputModeChange = false;

        // "Fix output mode → Windows Ink" is a Windows-only remediation (VMulti + WinInk). Off-Windows the
        // native mode is the correct output, so there's nothing to fix. On Windows it's fixable when the
        // mode isn't already a WinInk one — UNLESS the user deliberately opted out via the "Don't use
        // Windows Ink" sub-option, in which case nagging them to undo it would be wrong (#549).
        CanFixOutputMode = OperatingSystem.IsWindows() && !isWinInk && _applyAction != null
            && !WinInkAutoOptOut.IsOptedOut(_profile.Tablet);

        // Bindings — pen switches (tip, eraser, barrel buttons)
        var bindings = _profile.BindingSettings;
        RefreshPenSwitchRows();

        // Pen input toggles (#469) — load without re-persisting.
        _skipPenInputPersist = true;
        DisablePressure = bindings.DisablePressure;
        DisableTilt = bindings.DisableTilt;
        DragBindingsEnabled = bindings.EnableDragBindings;
        // The tip is disabled when it has no binding (#493). Derived from the profile so it stays in
        // sync when the tip binding is edited directly on the pen-switch card.
        DisablePenTip = bindings.TipButton?.Path == null;
        _skipPenInputPersist = false;
        OnPropertyChanged(nameof(PressureDisabledNote));

        // ExpressKeys — editable single-key bindings. While mapping is suspended we show the stashed
        // bindings (greyed) so the user still sees what will come back.
        var auxStores = EffectiveAuxStores();
        AuxButtonCount = auxStores.Count.ToString();
        var canEditAux = _applyAction != null && AuxButtonsEnabled;

        var newAuxButtons = new List<ButtonBinding>();
        for (int i = 0; i < auxStores.Count; i++)
        {
            var store = auxStores[i];
            var binding = AuxKeyBinding.ReadBinding(store); // null = a binding this editor can't model
            newAuxButtons.Add(new ButtonBinding(
                index: i + 1,
                binding: binding ?? AuxBinding.Unbound,
                isOtherBinding: binding == null,
                otherLabel: binding == null ? GetBindingName(store) : "",
                canEdit: canEditAux,
                applyBinding: ApplyAuxBindingAsync,
                editBinding: _editBinding));
        }
        AuxButtons = newAuxButtons;
        NoAuxButtons = newAuxButtons.Count == 0;
        ShowAuxControls = newAuxButtons.Count > 0 && _applyAction != null;

        // Wheel bindings — rotation (CW/CCW) + wheel buttons + thresholds per wheel.
        RefreshWheels();

        // Filters + raw JSON view (also refreshed after a dynamics toggle/edit persists, so the
        // Filters tab reflects the DynamicsFilter's enabled state without a manual Refresh).
        UpdateFiltersDisplay();

        // Active-area diagram (full + effective area + mapped display).
        RefreshTabletArea();

        // Pen dynamics — curve + smoothing (load without triggering a persist)
        var pc = PressureCurveProfile.Read(_settings, _profile.Tablet ?? "");
        var dynamics = pc?.Dynamics ?? PenDynamicsSettings.Default;
        _skipCurvePersist = true;
        Curve = dynamics.Curve;
        // Clamp both to their slider ceilings (#487, #496) so a profile saved with a heavier value (or one
        // set by another tool) reads back within the usable range.
        PressureSmoothing = Math.Min(dynamics.PressureSmoothing, PenSmoothing.MaxPressureSmoothingAmount);
        PositionSmoothing = Math.Min(dynamics.PositionSmoothing, PenSmoothing.MaxPositionSmoothingAmount);
        // Order is no longer user-configurable (#558): pressure smoothing always runs after the curve.
        SmoothAfterCurve = true;
        _skipCurvePersist = false;
        CanEditPressure = _applyAction != null;
        NotifyDynamicsStatus();

        // Hover limit (#188) — load without triggering a persist.
        var hover = HoverProfile.Read(_settings, _profile.Tablet ?? "");
        _skipHoverPersist = true;
        MaxHoverDistance = hover?.MaxHoverDistance ?? DefaultMaxHoverDistance;
        HoverLimitEnabled = hover?.Enabled ?? false;
        NearProximityOnly = hover?.NearProximityOnly ?? false;
        _skipHoverPersist = false;
        CanEditHover = _applyAction != null;

        RefreshCalibrationStatus();
    }

    /// <summary>Recomputes the Filters-tab list and the raw-JSON view from the current
    /// <see cref="_profile"/>. Called on a full refresh and again after a dynamics edit persists,
    /// so the Filters tab tracks the DynamicsFilter's enabled state without a manual Refresh.</summary>
    private void UpdateFiltersDisplay()
    {
        Filters.Clear();
        foreach (var f in _profile.Filters)
        {
            var path = f?.Path ?? "Unknown";
            var typeName = path.Split('.').LastOrDefault() ?? "Unknown";
            Filters.Add(new FilterCardViewModel(
                title: FriendlyFilterName(typeName),
                fullPath: path,
                enabled: f?.Enable ?? true,
                origin: ProfileFilterMaintenance.Classify(path)));
        }
        HasFilters = Filters.Count > 0;

        RawJson = JsonConvert.SerializeObject(_profile, Formatting.Indented);
    }

    /// <summary>Maps our plugin filter class names to the friendly labels they carry as
    /// <c>[PluginName]</c> in the plugin; anything else (third-party filters) keeps its type name.</summary>
    private static string FriendlyFilterName(string typeName) => typeName switch
    {
        "DynamicsFilter" => "Pen Dynamics",
        "HoverFilter" => "Hover Limit",
        "CalibrationFilter" => "Calibration",
        _ => typeName,
    };

    [RelayCommand]
    private async Task FixOutputMode()
    {
        await SetOutputMode(WinInkAbsoluteModePath);
    }

    [RelayCommand(CanExecute = nameof(CanApplyDisplay))]
    private async Task ApplyDisplay()
    {
        var display = Displays.FirstOrDefault(d => d.Number == SelectedDisplayNumber);
        if (_applyAction == null || _settings == null || display == null) return;

        // Same mapping the tray's "Switch display" uses — aspect-locked, full-monitor (#187).
        await ApplySettingsChange(p => DisplayMappingApplier.ApplyToProfile(p, _tabletDigitizer, display, Displays));
        MappingChangePending = false; // the selection is now the applied mapping
    }

    /// <summary>Re-read the connected monitors from Windows (manual Refresh or a live display change).</summary>
    [RelayCommand]
    private void RefreshDisplays()
    {
        var keep = SelectedDisplayNumber;
        Displays = DisplayEnumerator.Enumerate();
        SelectedDisplayNumber =
            (keep != null && Displays.Any(d => d.Number == keep)) ? keep
            : DefaultSelectedDisplay();
        RefreshTabletArea(); // displays changed → recompute the mapped-display side of the diagram
    }

    [RelayCommand]
    private void OpenDisplaySettings()
    {
        Services.PlatformShell.OpenDisplaySettings();
    }

    /// <summary>The display the profile is currently mapped to (full-monitor match), or null.</summary>
    private int? CurrentlyMappedNumber() => DisplayMappingApplier.CurrentlyMapped(_profile, Displays)?.Number;

    /// <summary>The display to pre-select in the picker. A clean mapping selects its monitor; a custom /
    /// off-screen mapping selects nothing (so the diagram doesn't fake a clean pick — the warning guides
    /// the user to choose); no mapping falls back to the primary as a sensible starting point.</summary>
    private int? DefaultSelectedDisplay() => DisplayMappingApplier.ClassifyMapping(_profile, Displays) switch
    {
        DisplayMappingValidity.Clean => CurrentlyMappedNumber(),
        DisplayMappingValidity.Custom or DisplayMappingValidity.OffScreen => null,
        _ => Displays.FirstOrDefault(d => d.IsPrimary)?.Number ?? Displays.FirstOrDefault()?.Number,
    };

    private void RefreshPenSwitchRows()
    {
        var bindings = _profile.BindingSettings;
        var canEdit = _applyAction != null;
        var rows = new List<PenSwitchRowViewModel>
        {
            new(PenSwitchKind.Tip, 1, bindings.TipButton, canEdit, ApplyPenSwitchBindingAsync),
            new(PenSwitchKind.Eraser, 1, bindings.EraserButton, canEdit, ApplyPenSwitchBindingAsync),
        };
        for (int i = 0; i < bindings.PenButtons.Count; i++)
            rows.Add(new(PenSwitchKind.PenButton, i + 1, bindings.PenButtons[i], canEdit, ApplyPenSwitchBindingAsync));
        if (rows.Count > 0) rows[0].IsFirst = true; // suppresses the leading divider in the merged card

        // Expose each switch by slot so the visual pen diagram can bind tip/eraser/buttons (#pen-switch-diagram).
        PenTipRow = rows[0];
        PenEraserRow = rows[1];
        var buttons = rows.Skip(2).ToList();
        PenButton1Row = buttons.ElementAtOrDefault(0);
        PenButton2Row = buttons.ElementAtOrDefault(1);
        PenButton3Row = buttons.ElementAtOrDefault(2);
    }

    private async Task ApplyPenSwitchBindingAsync(PenSwitchKind kind, int penButtonIndex, PluginSettingStore store)
    {
        await ApplySettingsChange(p =>
        {
            switch (kind)
            {
                case PenSwitchKind.Tip:
                    p.BindingSettings.TipButton = store;
                    break;
                case PenSwitchKind.Eraser:
                    p.BindingSettings.EraserButton = store;
                    break;
                case PenSwitchKind.PenButton:
                    if (penButtonIndex >= 1 && penButtonIndex <= p.BindingSettings.PenButtons.Count)
                        p.BindingSettings.PenButtons[penButtonIndex - 1] = store;
                    break;
            }
        });
    }

    // ── ExpressKeys (auxiliary buttons): key bindings, enable-all toggle, clear-all ──────────────
    private string AuxEnabledKey => $"AuxEnabled:{_profile.Tablet}";
    private string AuxBackupKey => $"AuxBackup:{_profile.Tablet}";

    /// <summary>Master switch (per tablet, persisted): when off, the buttons do nothing — their
    /// bindings are stashed and empty ones are written to the driver, restored when toggled on.</summary>
    [ObservableProperty] private bool _auxButtonsEnabled = true;
    /// <summary>Show the enable-all toggle + clear-all button (only when the tablet has aux buttons
    /// and this host can edit).</summary>
    [ObservableProperty] private bool _showAuxControls;
    private bool _suppressAuxEnabledApply;

    private void LoadAuxEnabledState()
    {
        _suppressAuxEnabledApply = true;
        AuxButtonsEnabled = AppSettings.Get(AuxEnabledKey) != "false"; // default enabled
        _suppressAuxEnabledApply = false;
    }

    /// <summary>The aux stores to display: the live profile when enabled, else the stash (so a
    /// suspended set is still visible) falling back to the profile.</summary>
    private PluginSettingStoreCollection EffectiveAuxStores()
    {
        if (!AuxButtonsEnabled)
        {
            var backup = AppSettings.Get(AuxBackupKey);
            if (!string.IsNullOrEmpty(backup))
            {
                try
                {
                    var restored = JsonConvert.DeserializeObject<PluginSettingStoreCollection>(backup);
                    if (restored != null) return restored;
                }
                catch { /* corrupt stash — fall back to the live (empty) profile */ }
            }
        }
        return _profile.BindingSettings.AuxButtons;
    }

    private async Task ApplyAuxBindingAsync(int buttonIndex, AuxBinding binding)
    {
        if (!AuxButtonsEnabled) return; // editing is locked while suspended
        var store = AuxKeyBinding.MakeBinding(binding); // null = unbound
        await ApplySettingsChange(p =>
        {
            var aux = p.BindingSettings.AuxButtons;
            if (buttonIndex >= 1 && buttonIndex <= aux.Count)
                aux[buttonIndex - 1] = store!;
        });
    }

    // ── Wheel tab: rotation bindings (CW/CCW), wheel buttons, thresholds, live flash ──
    // Reuses the ExpressKeys ButtonBinding editor — each rotation/button is a single OTD
    // PluginSettingStore, exactly what AuxKeyBinding.Read/MakeBinding handle.
    private string WheelEnabledKey => $"WheelEnabled:{_profile.Tablet}";
    private string WheelBackupKey => $"WheelBackup:{_profile.Tablet}";

    [ObservableProperty] private List<WheelEditor> _wheels = new();
    public bool HasWheels => Wheels.Count > 0;
    public bool NoWheels => Wheels.Count == 0;
    public bool ShowWheelControls => Wheels.Count > 0 && _applyAction != null;

    /// <summary>Master enable for all wheel bindings (stash/restore, like ExpressKeys).</summary>
    [ObservableProperty] private bool _wheelEnabled = true;
    private bool _suppressWheelEnabledApply;

    partial void OnWheelsChanged(List<WheelEditor> value)
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(NoWheels));
        OnPropertyChanged(nameof(ShowWheelControls));
    }

    private void LoadWheelEnabledState()
    {
        _suppressWheelEnabledApply = true;
        WheelEnabled = AppSettings.Get(WheelEnabledKey) != "false";
        _suppressWheelEnabledApply = false;
    }

    /// <summary>Live wheel bindings, or the stashed set while wheels are suspended (so the greyed
    /// editor still shows what will come back).</summary>
    private List<WheelBindingSettings> EffectiveWheelBindings()
    {
        if (!WheelEnabled)
        {
            var backup = AppSettings.Get(WheelBackupKey);
            if (!string.IsNullOrEmpty(backup))
            {
                try
                {
                    var restored = JsonConvert.DeserializeObject<List<WheelBindingSettings>>(backup);
                    if (restored != null) return restored;
                }
                catch { /* corrupt stash — fall through to live */ }
            }
        }
        return _profile.BindingSettings.WheelBindings;
    }

    private void RefreshWheels()
    {
        var wheels = EffectiveWheelBindings();
        var canEdit = _applyAction != null && WheelEnabled;
        var list = new List<WheelEditor>();
        bool multi = wheels.Count > 1;
        for (int w = 0; w < wheels.Count; w++)
        {
            var ws = wheels[w];
            int wi = w;
            // Physical rotation runs opposite to OTD's reported direction on tested rings (the Wacom
            // Intuos Pro ring's position increments counter-clockwise), so our "Clockwise" maps to
            // OTD's CounterClockwiseRotation and vice versa. WheelEditor's live flash is inverted to
            // match, and PersistThresholdsAsync swaps the threshold fields the same way.
            var cw = MakeWheelRow(wi, "Clockwise", ws.CounterClockwiseRotation, canEdit,
                (_, b) => ApplyWheelRotationAsync(wi, false, b));
            var ccw = MakeWheelRow(wi, "Counter-clockwise", ws.ClockwiseRotation, canEdit,
                (_, b) => ApplyWheelRotationAsync(wi, true, b));
            var buttons = new List<ButtonBinding>();
            for (int b = 0; b < ws.WheelButtons.Count; b++)
            {
                int bi = b;
                var label = ws.WheelButtons.Count > 1 ? $"Button {b + 1}" : "Wheel button";
                buttons.Add(MakeWheelRow(wi, label, ws.WheelButtons[b], canEdit,
                    (_, bind) => ApplyWheelButtonAsync(wi, bi, bind)));
            }
            list.Add(new WheelEditor(
                wheelIndex: wi,
                title: multi ? $"Wheel {w + 1}" : "Wheel",
                showTitle: multi,
                clockwise: cw, counterClockwise: ccw, buttons: buttons,
                clockwiseThreshold: ws.CounterClockwiseActivationThreshold,       // physical CW ↔ OTD CCW
                counterClockwiseThreshold: ws.ClockwiseActivationThreshold,
                stepSizeDegrees: ws.StepSize,
                applyThreshold: canEdit ? ApplyWheelThresholdAsync : null));
        }
        Wheels = list;
    }

    private ButtonBinding MakeWheelRow(int wheelIndex, string label, PluginSettingStore? store,
        bool canEdit, Func<int, AuxBinding, Task> apply)
    {
        var binding = AuxKeyBinding.ReadBinding(store); // null = a binding this editor can't model
        return new ButtonBinding(
            index: wheelIndex,
            binding: binding ?? AuxBinding.Unbound,
            isOtherBinding: binding == null,
            otherLabel: binding == null ? GetBindingName(store) : "",
            canEdit: canEdit,
            applyBinding: apply,
            label: label,
            editBinding: _editBinding);
    }

    private Task ApplyWheelRotationAsync(int wheelIndex, bool clockwise, AuxBinding binding)
    {
        if (!WheelEnabled) return Task.CompletedTask;
        var store = AuxKeyBinding.MakeBinding(binding); // null = unbound
        return ApplySettingsChange(p =>
        {
            var wheels = p.BindingSettings.WheelBindings;
            if (wheelIndex < 0 || wheelIndex >= wheels.Count) return;
            if (clockwise) wheels[wheelIndex].ClockwiseRotation = store;
            else wheels[wheelIndex].CounterClockwiseRotation = store;
        });
    }

    private Task ApplyWheelButtonAsync(int wheelIndex, int buttonIndex, AuxBinding binding)
    {
        if (!WheelEnabled) return Task.CompletedTask;
        var store = AuxKeyBinding.MakeBinding(binding); // null = unbound
        return ApplySettingsChange(p =>
        {
            var wheels = p.BindingSettings.WheelBindings;
            if (wheelIndex < 0 || wheelIndex >= wheels.Count) return;
            var buttons = wheels[wheelIndex].WheelButtons;
            if (buttonIndex >= 0 && buttonIndex < buttons.Count) buttons[buttonIndex] = store!;
        });
    }

    // Thresholds change rapidly while a slider is dragged — debounce into one apply (and one rebuild)
    // so the slider isn't yanked out from under the drag.
    private readonly Dictionary<(int Wheel, bool Clockwise), double> _pendingThresholds = new();
    private CancellationTokenSource? _wheelThresholdCts;

    private Task ApplyWheelThresholdAsync(int wheelIndex, bool clockwise, double degrees)
    {
        _pendingThresholds[(wheelIndex, clockwise)] = degrees;
        _wheelThresholdCts?.Cancel();
        var cts = _wheelThresholdCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);
        return Task.CompletedTask;

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(350, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistThresholdsAsync);
        }
    }

    private async Task PersistThresholdsAsync()
    {
        if (_pendingThresholds.Count == 0 || !WheelEnabled) return;
        var pending = new Dictionary<(int Wheel, bool Clockwise), double>(_pendingThresholds);
        _pendingThresholds.Clear();
        await ApplySettingsChange(p =>
        {
            var wheels = p.BindingSettings.WheelBindings;
            foreach (var (key, deg) in pending)
            {
                if (key.Wheel < 0 || key.Wheel >= wheels.Count) continue;
                // physical clockwise ↔ OTD CounterClockwise (see RefreshWheels)
                if (key.Clockwise) wheels[key.Wheel].CounterClockwiseActivationThreshold = (float)deg;
                else wheels[key.Wheel].ClockwiseActivationThreshold = (float)deg;
            }
        });
    }

    partial void OnWheelEnabledChanged(bool value)
    {
        if (_suppressWheelEnabledApply) return;
        _ = SetWheelEnabledAsync(value);
    }

    private async Task SetWheelEnabledAsync(bool enabled)
    {
        if (_applyAction == null) return;
        AppSettings.Set(WheelEnabledKey, enabled ? "true" : "false");
        if (!enabled)
        {
            // Suspend: stash the current bindings, then clear them so the wheel does nothing.
            AppSettings.Set(WheelBackupKey, JsonConvert.SerializeObject(_profile.BindingSettings.WheelBindings));
            await ApplySettingsChange(ClearWheels);
        }
        else
        {
            // Resume: restore the stashed rotation/button bindings (if any), then drop the stash.
            var backup = AppSettings.Get(WheelBackupKey);
            await ApplySettingsChange(p =>
            {
                if (string.IsNullOrEmpty(backup)) return;
                try
                {
                    var restored = JsonConvert.DeserializeObject<List<WheelBindingSettings>>(backup);
                    if (restored == null) return;
                    var wheels = p.BindingSettings.WheelBindings;
                    for (int i = 0; i < wheels.Count && i < restored.Count; i++)
                    {
                        wheels[i].ClockwiseRotation = restored[i].ClockwiseRotation;
                        wheels[i].CounterClockwiseRotation = restored[i].CounterClockwiseRotation;
                        var live = wheels[i].WheelButtons;
                        var saved = restored[i].WheelButtons;
                        for (int b = 0; b < live.Count && b < saved.Count; b++) live[b] = saved[b];
                    }
                }
                catch { /* corrupt stash — leave the (cleared) profile as-is */ }
            });
            AppSettings.Set(WheelBackupKey, "");
        }
    }

    /// <summary>Remove every wheel binding (rotations + buttons). Also drops any suspended stash.</summary>
    [RelayCommand]
    private async Task ClearWheelBindings()
    {
        if (_applyAction == null) return;
        AppSettings.Set(WheelBackupKey, "");
        await ApplySettingsChange(ClearWheels);
    }

    private static void ClearWheels(Profile p)
    {
        foreach (var w in p.BindingSettings.WheelBindings)
        {
            w.ClockwiseRotation = null;
            w.CounterClockwiseRotation = null;
            for (int b = 0; b < w.WheelButtons.Count; b++) w.WheelButtons[b] = null!;
        }
    }

    // ── Live wheel feedback: flash the matching rotation row / light a pressed wheel button ──
    private void OnWheelButtons(bool[][] states)
    {
        for (int w = 0; w < Wheels.Count; w++)
        {
            var buttons = Wheels[w].Buttons;
            var wheelStates = w < states.Length ? states[w] : Array.Empty<bool>();
            for (int b = 0; b < buttons.Count; b++)
                buttons[b].IsPressed = b < wheelStates.Length && wheelStates[b];
        }
    }

    private void OnWheelPositions(uint?[] positions)
    {
        for (int w = 0; w < Wheels.Count && w < positions.Length; w++)
            Wheels[w].OnAbsolutePosition(positions[w]);
    }

    private void OnWheelDeltas(int[] deltas)
    {
        for (int w = 0; w < Wheels.Count && w < deltas.Length; w++)
            Wheels[w].OnRelativeDelta(deltas[w]);
    }

    partial void OnAuxButtonsEnabledChanged(bool value)
    {
        if (_suppressAuxEnabledApply) return;
        _ = SetAuxButtonsEnabledAsync(value);
    }

    private async Task SetAuxButtonsEnabledAsync(bool enabled)
    {
        if (_applyAction == null) return;
        AppSettings.Set(AuxEnabledKey, enabled ? "true" : "false");
        if (!enabled)
        {
            // Suspend: stash the current bindings, then write empty ones so the buttons do nothing.
            AppSettings.Set(AuxBackupKey, JsonConvert.SerializeObject(_profile.BindingSettings.AuxButtons));
            await ApplySettingsChange(ClearAux);
        }
        else
        {
            // Resume: restore the stash (if any), then drop it.
            var backup = AppSettings.Get(AuxBackupKey);
            await ApplySettingsChange(p =>
            {
                if (string.IsNullOrEmpty(backup)) return;
                try
                {
                    var restored = JsonConvert.DeserializeObject<PluginSettingStoreCollection>(backup);
                    if (restored != null)
                    {
                        var aux = p.BindingSettings.AuxButtons;
                        for (int i = 0; i < aux.Count && i < restored.Count; i++) aux[i] = restored[i];
                    }
                }
                catch { /* corrupt stash — leave the (empty) profile as-is */ }
            });
            AppSettings.Set(AuxBackupKey, "");
        }
    }

    /// <summary>Remove every express-key binding. Also drops any suspended stash, so a later
    /// enable doesn't bring cleared bindings back.</summary>
    [RelayCommand]
    private async Task ClearAuxButtons()
    {
        if (_applyAction == null) return;
        AppSettings.Set(AuxBackupKey, "");
        await ApplySettingsChange(ClearAux);
    }

    private static void ClearAux(Profile p)
    {
        var aux = p.BindingSettings.AuxButtons;
        for (int i = 0; i < aux.Count; i++) aux[i] = null!;
    }

    public string TabletName { get; }

    /// <summary>Brand line for the header — the first word of the name (e.g. "Wacom"), shown small above
    /// the model. Empty when the name is a single word, so the header shows the model alone.</summary>
    public string TabletBrand
    {
        get { var i = TabletName.IndexOf(' '); return i > 0 ? TabletName[..i] : ""; }
    }

    /// <summary>Model line for the header — everything after the first word (e.g. "PTK-670"), or the whole
    /// name when it's a single word.</summary>
    public string TabletModel
    {
        get { var i = TabletName.IndexOf(' '); return i > 0 ? TabletName[(i + 1)..].TrimStart() : TabletName; }
    }

    public bool HasTabletBrand => TabletBrand.Length > 0;

    [ObservableProperty] private string _outputModeShort = "";
    [ObservableProperty] private string _outputModePath = "";
    [ObservableProperty] private bool _canFixOutputMode;
    [ObservableProperty] private bool _isAbsoluteOutputMode;
    [ObservableProperty] private bool _isRelativeOutputMode;
    private bool _skipOutputModeChange;
    public bool HasAreaMapping { get; }

    /// <summary>A tab the page should open on, set by a health-issue "Fix" deep-link (e.g. Display Mapping
    /// for an off-screen mapping) so the fix lands on the tab that carries it rather than the default
    /// About tab. The view reads and clears it on attach; <see cref="TabRequested"/> also fires so a page
    /// that's already visible switches immediately.</summary>
    public TabletDetailTab? PendingTab { get; private set; }

    /// <summary>Raised when a specific tab is requested — handled by the view to switch a page that's
    /// already shown (where re-navigation wouldn't re-attach it).</summary>
    public event Action<TabletDetailTab>? TabRequested;

    /// <summary>Ask the page to open on <paramref name="tab"/>. Call before navigating to it.</summary>
    public void RequestTab(TabletDetailTab tab)
    {
        PendingTab = tab;
        TabRequested?.Invoke(tab);
    }

    /// <summary>Read and clear the pending deep-link tab (one-shot, so navigating back later isn't
    /// forced onto it again).</summary>
    public TabletDetailTab? ConsumePendingTab()
    {
        var tab = PendingTab;
        PendingTab = null;
        return tab;
    }
    // Per-slot views for the visual pen diagram (button slots are null when the pen lacks that button).
    [ObservableProperty] private PenSwitchRowViewModel? _penTipRow;
    [ObservableProperty] private PenSwitchRowViewModel? _penEraserRow;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPenButtons))]
    private PenSwitchRowViewModel? _penButton1Row;
    [ObservableProperty] private PenSwitchRowViewModel? _penButton2Row;
    [ObservableProperty] private PenSwitchRowViewModel? _penButton3Row;

    /// <summary>True when the pen has at least one barrel button — gates the PEN BUTTONS tab (#494), so
    /// pens with none don't show an empty tab.</summary>
    public bool HasPenButtons => PenButton1Row != null;

    [ObservableProperty] private string _auxButtonCount = "0";
    [ObservableProperty] private bool _noAuxButtons;
    [ObservableProperty] private List<ButtonBinding> _auxButtons = [];
    /// <summary>One card per filter on the profile (Filters tab). Rebuilt by <see cref="UpdateFiltersDisplay"/>.</summary>
    public ObservableCollection<FilterCardViewModel> Filters { get; } = new();

    /// <summary>False when the profile has no filters — the Filters tab shows its empty state.</summary>
    [ObservableProperty] private bool _hasFilters;
    [ObservableProperty] private string _rawJson = "";

    // ── Pressure curve tab ──────────────────────────────────────

    [ObservableProperty] private PressureCurveSettings _curve = PressureCurveSettings.Default;
    [ObservableProperty] private bool _canEditPressure;

    // The Pen Dynamics filter is always enabled internally (#dynamics-always-on); there's no on/off toggle.
    // Users neutralize dynamics by leaving the curve linear and smoothing at 0 (a genuine no-op).
    private PenDynamicsSettings CurrentDynamics =>
        new(Curve, PressureSmoothing, PositionSmoothing, SmoothAfterCurve);

    private void NotifyDynamicsStatus() =>
        OnPropertyChanged(nameof(ShowLiveProcessed)); // depends on curve + pressure smoothing (#559)

    [ObservableProperty] private double _pressureSmoothing;
    [ObservableProperty] private double _positionSmoothing;
    [ObservableProperty] private bool _smoothAfterCurve = true;

    public string PressureSmoothingText => PressureSmoothing.ToString("0.00");
    public string PositionSmoothingText => PositionSmoothing.ToString("0.00");

    /// <summary>Upper bound for the position-smoothing slider (#487); heavier smoothing is too laggy to be
    /// useful, so the slider stops here (see <see cref="PenSmoothing.MaxPositionSmoothingAmount"/>).</summary>
    public double PositionSmoothingMax => PenSmoothing.MaxPositionSmoothingAmount;

    /// <summary>Upper bound for the pressure-smoothing slider (#496); past this the lag outweighs the jitter
    /// reduction, so the slider stops here (see <see cref="PenSmoothing.MaxPressureSmoothingAmount"/>).</summary>
    public double PressureSmoothingMax => PenSmoothing.MaxPressureSmoothingAmount;

    private bool _skipCurvePersist;
    private CancellationTokenSource? _persistCts;

    partial void OnPressureSmoothingChanged(double value)
    {
        OnPropertyChanged(nameof(PressureSmoothingText));
        NotifyDynamicsStatus();
        SchedulePersist();
    }

    partial void OnPositionSmoothingChanged(double value)
    {
        OnPropertyChanged(nameof(PositionSmoothingText));
        NotifyDynamicsStatus();
        SchedulePersist();
    }

    partial void OnSmoothAfterCurveChanged(bool value) => SchedulePersist();

    /// <summary>Softness slider value, projected onto the <see cref="Curve"/> struct.</summary>
    public double Softness
    {
        get => Curve.Softness;
        set { if (Curve.Softness != value) Curve = Curve with { Softness = value }; }
    }

    /// <summary>Cut (dead-zone) vs Clamp (floor) below the input minimum.</summary>
    public bool CutBelowMinimum
    {
        get => Curve.MinApproach == PressureMinApproach.Cut;
        set
        {
            var want = value ? PressureMinApproach.Cut : PressureMinApproach.Clamp;
            if (Curve.MinApproach != want) Curve = Curve with { MinApproach = want };
        }
    }

    public string SoftnessText => Curve.Softness.ToString("0.00");

    // Read-only display of the node values (#131). Editing is via dragging the chart nodes; these
    // just show where the pink (min) / cyan (max) nodes currently sit (input → output).
    public string InputMinimumText => Curve.InputMinimum.ToString("0.00");
    public string OutputMinimumText => Curve.Minimum.ToString("0.00");
    public string InputMaximumText => Curve.InputMaximum.ToString("0.00");
    public string OutputMaximumText => Curve.Maximum.ToString("0.00");

    // Live pressure read-out (#559): the current input pressure, for the LIVE PRESSURE bar's "raw" label.
    // "—" when the pen is up.
    public string LiveInputText => LivePressure is { } v ? v.ToString("0.00") : "—";
    public bool HasLivePressure => LivePressure is not null;

    // Live processed pressure for the top pressure-level bar (#559): the raw pressure run through the SAME
    // pipeline the daemon's Pen Dynamics filter uses — curve, then EMA smoothing — via a stateful
    // PenDynamicsProcessor fed each pen sample. So the processed dot reflects smoothing's lag too, not just
    // the curve. (Its exact magnitude tracks the UI sample rate, which may be coarser than the daemon's.)
    private readonly PenDynamicsProcessor _liveProcessor = new();
    [ObservableProperty] private double? _liveProcessed;
    public string LiveProcessedText => LiveProcessed is { } v ? v.ToString("0.00") : "—";
    partial void OnLiveProcessedChanged(double? value) => OnPropertyChanged(nameof(LiveProcessedText));

    /// <summary>Whether pen dynamics actually change the pressure, so the bar shows a processed dot.</summary>
    public bool ShowLiveProcessed => CurrentDynamics.CurveShapesPressure || CurrentDynamics.HasPressureSmoothing;

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    partial void OnCurveChanged(PressureCurveSettings value)
    {
        OnPropertyChanged(nameof(Softness));
        OnPropertyChanged(nameof(CutBelowMinimum));
        OnPropertyChanged(nameof(SoftnessText));
        OnPropertyChanged(nameof(InputMinimumText));
        OnPropertyChanged(nameof(InputMaximumText));
        OnPropertyChanged(nameof(OutputMinimumText));
        OnPropertyChanged(nameof(OutputMaximumText));
        NotifyDynamicsStatus();
        SchedulePersist();
    }

    /// <summary>Quick-start curve presets (#103).</summary>
    [RelayCommand]
    private void ApplyPreset(string kind) => Curve = kind switch
    {
        "soft" => PressureCurveSettings.Default with { Softness = 0.5 },   // lighter touch (concave)
        "firm" => PressureCurveSettings.Default with { Softness = -0.5 },  // firmer (convex)
        _ => PressureCurveSettings.Default,                               // linear
    };

    // ── Live device-report preview: pen-pressure dot (#102) + aux-button highlight + area map ──
    private readonly DaemonPenInputSource? _penInput;
    [ObservableProperty] private double? _livePressure;
    /// <summary>Live pen position (0..1 over the full tablet area) for the active-area diagram, or
    /// null when no pen is in range. (#250)</summary>
    [ObservableProperty] private double? _livePenX;
    [ObservableProperty] private double? _livePenY;

    /// <summary>With more than one tablet connected, the daemon debug stream interleaves all of them;
    /// only show this profile's tablet (same rule as Diagnostics #190).</summary>
    private bool AcceptDeviceReportForThisTablet(JObject data)
    {
        if (_deviceData is not { DetectedTablets.Count: > 1 }) return true;
        var reportTablet = data["Tablet"]?["Properties"]?["Name"]?.ToString();
        var ourTablet = _profile.Tablet;
        if (reportTablet == null || string.IsNullOrEmpty(ourTablet)) return true;
        return string.Equals(reportTablet, ourTablet, StringComparison.OrdinalIgnoreCase);
    }

    // DeviceReportSample normalizes to 0..1; feed both the pressure dot and the active-area map.
    private void OnPenSample(PenSample s)
    {
        LivePenX = s.X;
        LivePenY = s.Y;
        if (s.IsDown)
        {
            double raw = Clamp01(s.Pressure);
            LivePressure = raw;
            // Run the raw sample through the same curve+smoothing pipeline (#559) so the processed dot
            // reflects smoothing's lag. Settings are refreshed each sample to track live edits.
            _liveProcessor.Settings = CurrentDynamics;
            LiveProcessed = _liveProcessor.ProcessPressure(raw);
        }
        else
        {
            LivePressure = null;
            _liveProcessor.ResetPressure(); // next press starts crisp, matching the filter
            LiveProcessed = null;
        }
    }

    // Live input/output read-out (#468) tracks the live pressure.
    partial void OnLivePressureChanged(double? value)
    {
        OnPropertyChanged(nameof(LiveInputText));
        OnPropertyChanged(nameof(HasLivePressure));
    }

    // Light up each aux-button card while its physical button is held (express-key live highlight).
    private void OnAuxButtons(bool[] states)
    {
        for (int i = 0; i < AuxButtons.Count; i++)
            AuxButtons[i].IsPressed = i < states.Length && states[i];
    }

    /// <summary>Enables the daemon's device-report stream (live pressure dot + aux highlight). Driven
    /// by the view: on while the Dynamics or ExpressKeys tab is visible, off otherwise.</summary>
    public void StartLiveInput() => _ = _penInput?.StartAsync();

    /// <summary>Stops the stream and clears any live state so nothing stays lit after we look away.</summary>
    public void StopLiveInput()
    {
        LivePressure = null;
        LiveProcessed = null;
        _liveProcessor.ResetPressure();
        LivePenX = null;
        LivePenY = null;
        foreach (var b in AuxButtons) b.IsPressed = false;
        foreach (var w in Wheels) w.ClearLiveState();
        _ = _penInput?.StopAsync();
    }

    /// <summary>Active-area diagram geometry (full + effective area + mapped display), or null when
    /// the tablet has no absolute mapping / digitizer specs. (#250/#252)</summary>
    [ObservableProperty] private TabletAreaInfo? _tabletArea;

    // ── Active-area rotation (#199) ──────────────────────────────────────────────────────────────
    // Rotating the tablet's active area to match physically turning the tablet. OTD applies the TABLET
    // (input) area's Rotation about its centre, and OTA's mapper already honours it — so this is just
    // writing Tablet.Rotation through the normal apply path. Phase 1 supports 0° and 180° only; 90°/270°
    // need the fit-to-swapped-aspect resize and are disabled in the UI for now.

    /// <summary>Current active-area rotation in degrees, normalised to 0/90/180/270 (other angles set by
    /// OTD's own UX read back as the nearest and simply leave no option selected).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRotation0), nameof(IsRotation90), nameof(IsRotation180), nameof(IsRotation270))]
    private int _tabletRotation;

    public bool IsRotation0 => TabletRotation == 0;
    public bool IsRotation90 => TabletRotation == 90;
    public bool IsRotation180 => TabletRotation == 180;
    public bool IsRotation270 => TabletRotation == 270;

    /// <summary>Set the active-area rotation (0/90/180/270) and apply it, re-fitting the area to the
    /// mapped display so it stays undistorted — for 90/270 the area shrinks to fit the rotated tablet
    /// (#199). Ignores other values defensively.</summary>
    [RelayCommand]
    private async Task SetRotation(string? degrees)
    {
        if (!int.TryParse(degrees, out var deg) || deg is not (0 or 90 or 180 or 270) || deg == TabletRotation) return;
        var dig = _deviceData?.GetTabletDigitizer(_profile.Tablet ?? "") ?? _tabletDigitizer;
        await ApplySettingsChange(p => DisplayMappingApplier.ApplyRotation(p, dig, deg, Displays));
    }

    private void RefreshTabletArea()
    {
        var t = _profile.AbsoluteModeSettings?.Tablet;
        TabletRotation = t != null ? (((int)Math.Round(t.Rotation)) % 360 + 360) % 360 : 0;
        // Read the digitizer live: its specs can arrive after this (cached) view-model was created — e.g.
        // when the tablet (re)connects — so we don't rely only on the value captured at construction.
        // That snapshot is the fallback (e.g. the tray dialog has no live device data). Previously only
        // the snapshot was used, so a tablet whose specs weren't ready when its page was first opened
        // stayed stuck on "Active-area details aren't available" until an app restart.
        var dig = _deviceData?.GetTabletDigitizer(_profile.Tablet ?? "") ?? _tabletDigitizer;
        if (t != null && dig is { } d && d.Width > 0 && d.Height > 0)
        {
            var mapped = DisplayMappingApplier.CurrentlyMapped(_profile, Displays);
            TabletArea = new TabletAreaInfo(
                FullWidth: d.Width, FullHeight: d.Height,
                EffWidth: t.Width, EffHeight: t.Height, EffCenterX: t.X, EffCenterY: t.Y,
                HasDisplay: mapped != null,
                DisplayNumber: mapped?.Number ?? 0, DisplayName: mapped?.Name ?? "",
                DisplayWidth: mapped?.Width ?? 0, DisplayHeight: mapped?.Height ?? 0,
                Rotation: t.Rotation);
        }
        else
        {
            TabletArea = null;
        }
        // The mapped display drives where the calibration overlay pops up (Calibration tab hint).
        OnPropertyChanged(nameof(CalibrationDisplayText));
        // The stored mapping's validity drives the Display Mapping tab's warning/note.
        OnPropertyChanged(nameof(MappingValidity));
        OnPropertyChanged(nameof(ShowMappingOffScreen));
        OnPropertyChanged(nameof(ShowMappingCustom));
        OnPropertyChanged(nameof(MappingValidityText));
        UpdateActiveAreaSizePercent();
    }

    // ── Active-area editing (#199): interactive resize/move (from the diagram) + a Size slider + Maximize ──

    /// <summary>Persist an interactive active-area edit from the diagram (it has already clamped the
    /// values to the tablet + rotation). Keeps the aspect lock on.</summary>
    public Task CommitActiveArea(double width, double height, double centerX, double centerY) =>
        ApplySettingsChange(p =>
        {
            if (p.AbsoluteModeSettings?.Tablet is { } t)
            {
                t.Width = (float)width; t.Height = (float)height;
                t.X = (float)centerX; t.Y = (float)centerY;
            }
        });

    /// <summary>Reset the active area to the largest centred fit for the mapped display + current rotation.</summary>
    [RelayCommand]
    private async Task MaximizeActiveArea()
    {
        var dig = _deviceData?.GetTabletDigitizer(_profile.Tablet ?? "") ?? _tabletDigitizer;
        await ApplySettingsChange(p => DisplayMappingApplier.ApplyRotation(p, dig, TabletRotation, Displays));
    }

    /// <summary>Active-area size as a percent (10–100) of the maximum that fits — the Size slider. User
    /// changes are debounced and applied (resizing about the current centre); reads back from the stored
    /// area on every refresh.</summary>
    [ObservableProperty] private double _activeAreaSizePercent = 100;
    private bool _syncingSize;
    private bool _sizeEditPending;   // a user slider edit is in flight (see OnActiveAreaSizePercentChanged)
    private System.Threading.CancellationTokenSource? _sizeCts;

    partial void OnActiveAreaSizePercentChanged(double value)
    {
        if (_syncingSize) return;   // programmatic sync from the stored area, not a user edit
        // A user edit is now in flight until its debounced apply lands. Guard against the periodic
        // RefreshTabletArea poll (it fires every few hundred ms) clobbering the slider with the *old*
        // stored value mid-debounce — that was the "click a new spot, snaps back to the previous value"
        // bug (#size-snapback).
        _sizeEditPending = true;
        _sizeCts?.Cancel();
        _sizeCts = new System.Threading.CancellationTokenSource();
        var token = _sizeCts.Token;
        _ = DebouncedResize(token);
    }

    private async Task DebouncedResize(System.Threading.CancellationToken token)
    {
        try { await Task.Delay(250, token); } catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested) return;

        var dig = _deviceData?.GetTabletDigitizer(_profile.Tablet ?? "") ?? _tabletDigitizer;
        var display = DisplayMappingApplier.CurrentlyMapped(_profile, Displays);
        if (dig is not { } d || display == null) { _sizeEditPending = false; return; }

        var max = AreaMappingCalculator.FitForRotation(d.Width, d.Height, display.Width, display.Height, TabletRotation);
        float frac = (float)Math.Clamp(ActiveAreaSizePercent / 100.0, 0.1, 1.0);
        var cur = _profile.AbsoluteModeSettings?.Tablet;
        float cx = cur?.X ?? d.Width / 2f, cy = cur?.Y ?? d.Height / 2f;
        var clamped = AreaMappingCalculator.ClampArea(max.Width * frac, max.Height * frac, cx, cy,
            TabletRotation, d.Width, d.Height, max.Width * 0.1f);
        // Clear the guard just before committing: CommitActiveArea mutates _profile synchronously (before
        // its first await), so from here every write-back reads the new value and is safe to apply.
        _sizeEditPending = false;
        await CommitActiveArea(clamped.Width, clamped.Height, clamped.X, clamped.Y);
    }

    // Reflect the stored area's size into the slider without re-triggering an apply.
    private void UpdateActiveAreaSizePercent()
    {
        if (_sizeEditPending) return;   // don't overwrite an in-flight user edit with the pre-commit value
        var t = _profile.AbsoluteModeSettings?.Tablet;
        var dig = _deviceData?.GetTabletDigitizer(_profile.Tablet ?? "") ?? _tabletDigitizer;
        var display = DisplayMappingApplier.CurrentlyMapped(_profile, Displays);
        if (t == null || dig is not { } d || display == null) return;
        var max = AreaMappingCalculator.FitForRotation(d.Width, d.Height, display.Width, display.Height, TabletRotation);
        if (max.Width <= 0) return;
        _syncingSize = true;
        ActiveAreaSizePercent = Math.Round(Math.Clamp(t.Width / max.Width * 100.0, 10, 100));
        _syncingSize = false;
    }

    // ── Active Area tab: read-out of the full vs. effective (used) area ──────────
    // The diagram binds TabletArea directly; these drive the stat cells beside it. All recompute
    // when TabletArea changes (a new mapping applied, displays changed, or profile reloaded).
    partial void OnTabletAreaChanged(TabletAreaInfo? value)
    {
        OnPropertyChanged(nameof(ActiveAreaFullText));
        OnPropertyChanged(nameof(ActiveAreaUsedText));
        OnPropertyChanged(nameof(ActiveAreaUsagePercentText));
        OnPropertyChanged(nameof(FullDiagonalText));
        OnPropertyChanged(nameof(UsedDiagonalText));
        OnPropertyChanged(nameof(FullAspectText));
        OnPropertyChanged(nameof(UsedAspectText));
    }

    /// <summary>Show active-area lengths in inches instead of millimetres (the tab's unit toggle). A
    /// view-only display preference — OTD stores everything in mm, so nothing is converted on disk.</summary>
    [ObservableProperty] private bool _useImperialUnits;

    partial void OnUseImperialUnitsChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveAreaFullText));
        OnPropertyChanged(nameof(ActiveAreaUsedText));
        OnPropertyChanged(nameof(FullDiagonalText));
        OnPropertyChanged(nameof(UsedDiagonalText));
    }

    public string ActiveAreaFullText => TabletArea is { } a ? FormatSize(a.FullWidth, a.FullHeight) : "—";
    public string ActiveAreaUsedText => TabletArea is { } a ? FormatSize(a.EffWidth, a.EffHeight) : "—";

    /// <summary>Corner-to-corner diagonal of the full tablet area / the effective (active) area — the
    /// TABLET and ACTIVE AREA columns of the mapping-tab comparison table.</summary>
    public string FullDiagonalText => TabletArea is { } a ? FormatLength(Diagonal(a.FullWidth, a.FullHeight)) : "—";
    public string UsedDiagonalText => TabletArea is { } a ? FormatLength(Diagonal(a.EffWidth, a.EffHeight)) : "—";

    private static double Diagonal(double w, double h) => System.Math.Sqrt(w * w + h * h);

    /// <summary>Width-to-height aspect ratio of the full tablet area / the effective (active) area. A
    /// mismatch means the active area is shaped differently from the tablet — expected when it's mapped to
    /// a display whose aspect differs from the tablet's.</summary>
    public string FullAspectText => TabletArea is { FullHeight: > 0 } a ? (a.FullWidth / a.FullHeight).ToString("0.00") : "—";
    public string UsedAspectText => TabletArea is { EffHeight: > 0 } a ? (a.EffWidth / a.EffHeight).ToString("0.00") : "—";

    // Length display helpers: OTD's areas are millimetres; inches = mm / 25.4. Metric shows one decimal
    // (e.g. "269 mm"), imperial two (inches are ~25× larger, so a decimal buys real precision, "10.59 in").
    private const double MmPerInch = 25.4;
    private string UnitLabel => UseImperialUnits ? "in" : "mm";
    private string Num(double mm) =>
        (UseImperialUnits ? mm / MmPerInch : mm).ToString(UseImperialUnits ? "0.##" : "0.#");
    private string FormatLength(double mm) => $"{Num(mm)} {UnitLabel}";
    private string FormatSize(double wMm, double hMm) => $"{Num(wMm)} × {Num(hMm)} {UnitLabel}";

    /// <summary>Share of the full digitizer area covered by the effective area (width×height) — the
    /// ACTIVE AREA column of the "Area" row (the TABLET column is always 100%).</summary>
    public string ActiveAreaUsagePercentText => TabletArea is { FullWidth: > 0, FullHeight: > 0 } a
        ? $"{a.EffWidth * a.EffHeight / (a.FullWidth * a.FullHeight) * 100:0.0}%" : "—";

    /// <summary>Debounce rapid edits (node drags / slider) into a single daemon apply.</summary>
    private void SchedulePersist()
    {
        if (_skipCurvePersist || _applyAction == null || _settings == null) return;
        _persistCts?.Cancel();
        var cts = _persistCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistCurveAsync);
        }
    }

    private async Task PersistCurveAsync()
    {
        if (_applyAction == null || _settings == null) return;
        var dynamics = new PenDynamicsSettings(Curve, PressureSmoothing, PositionSmoothing, SmoothAfterCurve);
        // The filter is always enabled internally; users neutralize it with linear/zero settings, not a toggle.
        PressureCurveProfile.Write(_settings, _profile.Tablet ?? "", dynamics, enable: true);
        // The write mutated _profile.Filters (added/enabled/disabled the DynamicsFilter); reflect that
        // in the Filters tab and JSON view immediately rather than waiting for a manual Refresh.
        UpdateFiltersDisplay();
        await _applyAction(_settings);
    }

    // ── Hover limit tab (#188) ──────────────────────────────────

    public const int DefaultMaxHoverDistance = 127; // sensible mid-point when first enabled (range 0-255)
    public int HoverDistanceMax => HoverProfile.MaxDistance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverControlsOpacity))]
    private bool _hoverLimitEnabled;
    [ObservableProperty] private bool _canEditHover;
    [ObservableProperty] private double _maxHoverDistance = DefaultMaxHoverDistance;
    /// <summary>Only track while the pen is in the tablet's near-proximity band (Wacom's default). (#311)</summary>
    [ObservableProperty] private bool _nearProximityOnly;

    public string MaxHoverDistanceText => ((int)MaxHoverDistance).ToString();
    /// <summary>Dim the hover controls when the limit is off so they read as inactive (like Dynamics).</summary>
    public double HoverControlsOpacity => HoverLimitEnabled ? 1.0 : 0.4;

    private bool _skipHoverPersist;
    private CancellationTokenSource? _hoverPersistCts;

    partial void OnHoverLimitEnabledChanged(bool value) => SchedulePersistHover();
    partial void OnNearProximityOnlyChanged(bool value) => SchedulePersistHover();

    partial void OnMaxHoverDistanceChanged(double value)
    {
        OnPropertyChanged(nameof(MaxHoverDistanceText));
        SchedulePersistHover();
    }

    private void SchedulePersistHover()
    {
        if (_skipHoverPersist || _applyAction == null || _settings == null) return;
        _hoverPersistCts?.Cancel();
        var cts = _hoverPersistCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistHoverAsync);
        }
    }

    private async Task PersistHoverAsync()
    {
        if (_applyAction == null || _settings == null) return;
        HoverProfile.Write(_settings, _profile.Tablet ?? "", (int)MaxHoverDistance, HoverLimitEnabled, NearProximityOnly);
        UpdateFiltersDisplay();
        await _applyAction(_settings);
    }

    public void Dispose()
    {
        if (_deviceData != null)
        {
            _deviceData.DataLoaded -= RefreshDetectionStatus;
            _deviceData.DataLoaded -= RefreshConfigOverride;
        }
        DeveloperSettings.Instance.PropertyChanged -= OnDeveloperSettingsChanged;
        StopLiveInput();
    }
}

/// <summary>One label/value row in the tablet ABOUT tab's spec list.</summary>
public sealed record TabletFact(string Label, string Value);

/// <summary>A single filter on a tablet's profile, shown as a card in the Filters tab.</summary>
public sealed class FilterCardViewModel
{
    public FilterCardViewModel(string title, string fullPath, bool enabled,
        ProfileFilterMaintenance.FilterOrigin origin)
    {
        Title = title;
        FullPath = fullPath;
        Enabled = enabled;
        IsLegacy = origin == ProfileFilterMaintenance.FilterOrigin.Legacy;
    }

    /// <summary>Friendly label (e.g. "Pen Dynamics") or the raw type name for unknown filters.</summary>
    public string Title { get; }
    /// <summary>The filter's full type path (with namespace) — the subtitle. Showing the namespace is
    /// what makes a stale duplicate (old vs current namespace) visibly distinct rather than identical.</summary>
    public string FullPath { get; }
    public bool Enabled { get; }
    public string StatusText => Enabled ? "Enabled" : "Disabled";
    /// <summary>True for a filter left over from an older app/plugin name — inert (the driver has no
    /// plugin for it) and normally cleaned on load, but flagged so a stray one stands out.</summary>
    public bool IsLegacy { get; }
}

public partial class PenSwitchRowViewModel : ObservableObject
{
    private readonly PenSwitchKind _kind;
    private readonly int _penButtonIndex;
    private readonly Func<PenSwitchKind, int, PluginSettingStore, Task> _applyAsync;

    public PenSwitchRowViewModel(
        PenSwitchKind kind,
        int penButtonIndex,
        PluginSettingStore? store,
        bool canEdit,
        Func<PenSwitchKind, int, PluginSettingStore, Task> applyAsync)
    {
        _kind = kind;
        _penButtonIndex = penButtonIndex;
        _applyAsync = applyAsync;
        SectionLabel = kind switch
        {
            PenSwitchKind.Tip => "PEN TIP",
            PenSwitchKind.Eraser => "ERASER",
            PenSwitchKind.PenButton => $"BUTTON {penButtonIndex}",
            _ => "SWITCH"
        };
        RefreshFromStore(store, canEdit);
    }

    public string SectionLabel { get; }

    /// <summary>First row in the merged Pen Switches card — its separating divider is hidden.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private string _bindingLabel = "None";
    [ObservableProperty] private PenSwitchBindingMode _mode;
    [ObservableProperty] private bool _canEdit;

    public bool IsRecommended => Mode == PenSwitchBindingMode.Auto;
    public bool IsNotRecommended => Mode != PenSwitchBindingMode.Auto;
    /// <summary>Show the "Use Adaptive" fix only when the switch isn't already on Adaptive and the host
    /// can apply changes.</summary>
    public bool ShowUseAdaptive => CanEdit && Mode != PenSwitchBindingMode.Auto;

    partial void OnModeChanged(PenSwitchBindingMode value)
    {
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(IsNotRecommended));
        OnPropertyChanged(nameof(ShowUseAdaptive));
        SetAutoCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUseAdaptive));
        SetAutoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromStore(PluginSettingStore? store, bool canEdit)
    {
        CanEdit = canEdit;
        Mode = PenSwitchBinding.DetectMode(store, _kind, _penButtonIndex);
        BindingLabel = PenSwitchBinding.GetDisplayLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private bool CanSetAuto => CanEdit && Mode != PenSwitchBindingMode.Auto;

    [RelayCommand(CanExecute = nameof(CanSetAuto))]
    private Task SetAuto() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeAdaptiveBinding(_kind, _penButtonIndex));
}

public partial class ButtonBinding : ObservableObject
{
    private readonly Func<int, AuxBinding, Task>? _applyBinding;
    private readonly Func<AuxBinding, string, Task<AuxBinding?>>? _editBinding;
    private AuxBinding _applied;

    public ButtonBinding(int index, AuxBinding binding, bool isOtherBinding, string otherLabel,
        bool canEdit, Func<int, AuxBinding, Task>? applyBinding, string? label = null,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding = null)
    {
        Index = index;
        _label = label;
        IsOtherBinding = isOtherBinding;
        OtherLabel = otherLabel;
        CanEdit = canEdit;
        _applyBinding = applyBinding;
        _editBinding = editBinding;
        _applied = binding;
    }

    public int Index { get; }
    private readonly string? _label;
    /// <summary>Row title. Defaults to "Button N"; wheel rows pass a custom label (the direction).</summary>
    public string Label => _label ?? $"Button {Index}";

    /// <summary>Read-only summary shown on the card: the friendly name of a binding this editor can't
    /// model, else "Ctrl + Z" / "Left click" / "Scroll up" / "Unbound".</summary>
    public string Summary => IsOtherBinding && !_applied.IsBound ? OtherLabel : AuxKeyBinding.Describe(_applied);

    /// <summary>False disables the Edit button (read-only host, or button mapping suspended).</summary>
    public bool CanEdit { get; }

    /// <summary>True when this button already holds a binding this editor can't model (Windows Ink, an
    /// adaptive binding, or a multi-key macro) — the summary shows its friendly name until it's replaced.</summary>
    public bool IsOtherBinding { get; }
    public string OtherLabel { get; }

    /// <summary>True while the physical button is held down — highlights the card live.</summary>
    [ObservableProperty] private bool _isPressed;

    /// <summary>Open the modal editor and apply the result — a binding, or <see cref="AuxBinding.Unbound"/>
    /// from Clear. Cancel (null) leaves the binding untouched. Nothing is applied until the dialog
    /// returns, so there's no inline apply-on-change to loop.</summary>
    [RelayCommand]
    private async Task Edit()
    {
        if (_editBinding == null || !CanEdit) return;
        var result = await _editBinding(_applied, Label);
        if (result is not { } binding) return; // cancelled
        if (binding == _applied) return;        // no change
        _applied = binding;
        OnPropertyChanged(nameof(Summary));
        if (_applyBinding != null) await _applyBinding(Index, binding);
    }
}
