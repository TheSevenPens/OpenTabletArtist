using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>How the overlay captures: 4 corners (→ perspective homography, #195) or an N×M grid
/// (→ per-node bilinear offsets, #196).</summary>
public enum CalibrationMode { Corners, Grid }

/// <summary>The capture mode + grid size chosen before calibrating.</summary>
public readonly record struct CalibrationOptions(CalibrationMode Mode, int Cols, int Rows);

/// <summary>A user-pickable calibration preset (label + the options it maps to), for the mode selector.</summary>
public sealed record CalibrationModeChoice(string Label, CalibrationMode Mode, int Cols, int Rows)
{
    public CalibrationOptions ToOptions() => new(Mode, Cols, Rows);
}

/// <summary>
/// Drives the pointer-calibration overlay (#127). Coordinates are exposed <em>normalized</em>
/// to the mapped display (0..1) so the view can place targets/live dot on a Canvas without caring
/// about DPI scaling. The measured raw positions come from the daemon debug stream (mapping- and
/// Windows-Ink-independent); on completion <see cref="CalibrationSolver"/> fits the affine and it is
/// written via <see cref="CalibrationProfile"/>.
///
/// During capture the existing calibration is disabled so taps are recorded uncorrected (so "Clear"
/// truly returns to identity and we don't calibrate on top of an old correction).
/// </summary>
public partial class CalibrationViewModel : ObservableObject
{
    public sealed record Context(
        string TabletName,
        TabletDigitizerSpec Digitizer,
        MappingArea Input,
        MappingArea Output,
        DisplayInfo Display,
        Settings Settings,
        Func<Settings, Task> Apply,
        IDaemonDebugSession Daemon,
        CalibrationMode Mode = CalibrationMode.Corners,
        int GridCols = 3,
        int GridRows = 3);

    public enum Phase { Capturing, Confirming, Failed }

    // Target positions normalized to the display (0..1). Corners = 4 inset corners (TL, TR, BR, BL);
    // Grid = a cols×rows lattice, row-major.
    private readonly List<(double X, double Y)> _targets;

    private static List<(double X, double Y)> BuildTargets(CalibrationMode mode, int cols, int rows)
    {
        if (mode == CalibrationMode.Corners)
            return new() { (0.1, 0.1), (0.9, 0.1), (0.9, 0.9), (0.1, 0.9) };

        var list = new List<(double X, double Y)>(cols * rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double x = cols <= 1 ? 0.5 : 0.08 + 0.84 * c / (cols - 1);
                double y = rows <= 1 ? 0.5 : 0.08 + 0.84 * r / (rows - 1);
                list.Add((x, y));
            }
        return list;
    }

    private const double HitRadiusN = 0.06;   // accept samples only this close to the active target
    /// <summary>Hold-to-average (#457): the pen must rest on the target long enough to gather this many
    /// on-target samples, which are then averaged into the recorded point — a steady hold cancels jitter
    /// far better than a quick tap. A filling ring shows the progress. Tunable; roughly 1–2 s of holding
    /// at typical tablet report rates. (Public so the tests can drive a full hold.)</summary>
    public const int HoldSamplesTarget = 600;

    private readonly Context _ctx;
    private readonly DaemonPenInputSource _input;
    private readonly CalibrationProfile.CalibrationData? _original;
    private readonly List<Vector2> _measuredRaw = new();   // one per completed target
    private readonly List<int> _tapSampleCounts = new();     // samples averaged for each completed tap (#460)
    private readonly List<Vector2> _measuredTilt = new();    // averaged pen tilt (TiltX, TiltY °) per tap (#481)
    private readonly List<Vector2> _tapAccum = new();        // raw samples for the in-progress hold
    private readonly List<Vector2> _tiltAccum = new();       // tilt samples for the in-progress hold (#481)

    public CalibrationViewModel(Context ctx)
    {
        _ctx = ctx;
        _targets = BuildTargets(ctx.Mode, ctx.GridCols, ctx.GridRows);
        _input = new DaemonPenInputSource(ctx.Daemon);
        _input.Sample += OnSample;
        _original = CalibrationProfile.ReadProfile(ctx.Settings.Profiles.FirstOrDefault(p => p.Tablet == ctx.TabletName));
        UpdateInstruction();
    }

    // --- Observable state for the overlay ---

    [ObservableProperty] private Phase _currentPhase = Phase.Capturing;
    [ObservableProperty] private int _currentTarget;          // 0..3 index being captured
    [ObservableProperty] private string _instruction = "";
    [ObservableProperty] private double _liveDotX;            // normalized 0..1 within the display
    [ObservableProperty] private double _liveDotY;
    [ObservableProperty] private bool _liveDotVisible;
    /// <summary>0..1 progress of the current hold on the active target (#457); the overlay fills a ring
    /// with it. Resets to 0 when the pen leaves the target or a point completes.</summary>
    [ObservableProperty] private double _holdProgress;

    public bool IsCapturing => CurrentPhase == Phase.Capturing;
    public bool IsConfirming => CurrentPhase == Phase.Confirming;
    public bool IsFailed => CurrentPhase == Phase.Failed;
    public bool ShowApply => IsConfirming;
    public bool ShowRedo => IsConfirming || IsFailed;
    /// <summary>Undo removes just the last recorded point (#458) — available once at least one is captured.</summary>
    public bool CanUndoPoint => _measuredRaw.Count > 0;

    /// <summary>Normalized target positions for the view to draw (in capture order).</summary>
    public IReadOnlyList<(double X, double Y)> Targets => _targets;
    /// <summary>How many targets are captured (for ticking them off in the view).</summary>
    public int CapturedCount => _measuredRaw.Count;

    /// <summary>Raised when the overlay should close.</summary>
    public event Action? CloseRequested;

    partial void OnCurrentPhaseChanged(Phase value)
    {
        OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(IsConfirming));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(ShowApply));
        OnPropertyChanged(nameof(ShowRedo));
    }

    // --- Lifecycle ---

    /// <summary>Disable any existing calibration (so capture is uncorrected) and start the pen stream.</summary>
    public async Task StartAsync()
    {
        await BypassCalibrationAsync();
        await _input.StartAsync();
    }

    public async Task StopAsync()
    {
        _input.Sample -= OnSample;
        await _input.StopAsync();
    }

    /// <summary>Disable whatever calibration is <em>currently</em> active so taps are captured
    /// uncorrected — covers both the calibration that existed at open and a preview applied before a
    /// Redo (otherwise recapture would be corrected by the matrix we're replacing). (Cursor review)</summary>
    private async Task BypassCalibrationAsync()
    {
        var current = CalibrationProfile.ReadProfile(_ctx.Settings.Profiles.FirstOrDefault(p => p.Tablet == _ctx.TabletName));
        if (current is { Enabled: true })
        {
            // Re-write the current calibration disabled, preserving its model + payload.
            CalibrationProfile.Write(_ctx.Settings, _ctx.TabletName, current with { Enabled = false });
            await _ctx.Apply(_ctx.Settings);
        }
    }

    // --- Capture (also the unit-testable core: feed PenSamples via OnSample) ---

    public void OnSample(PenSample s)
    {
        // Map the raw position to the display (uncorrected) for the live dot + hit-testing.
        var raw = new Vector2((float)s.RawX, (float)s.RawY);
        var desktop = AbsolutePositionMapper.MapToDesktop(raw, _ctx.Digitizer, _ctx.Input, _ctx.Output, false, false);
        if (desktop is { } d)
        {
            LiveDotX = (d.X - _ctx.Display.X) / Math.Max(1, _ctx.Display.Width);
            LiveDotY = (d.Y - _ctx.Display.Y) / Math.Max(1, _ctx.Display.Height);
            LiveDotVisible = true;
        }
        else
        {
            LiveDotVisible = false;
        }

        if (CurrentPhase != Phase.Capturing) return;

        // Hold-to-average (#457): while the pen rests on the active target, accumulate on-target samples
        // and fill the progress ring; commit once it's been held long enough. Leaving the target (lift or
        // move away) before it fills abandons the partial hold — the ring resets and the user re-holds.
        bool onTarget = s.IsDown && LiveDotVisible && Near(LiveDotX, LiveDotY, _targets[CurrentTarget]);
        if (onTarget)
        {
            _tapAccum.Add(raw);
            _tiltAccum.Add(new Vector2((float)s.TiltX, (float)s.TiltY));   // pen tilt while held (#481)
            HoldProgress = Math.Min(1.0, (double)_tapAccum.Count / HoldSamplesTarget);
            if (_tapAccum.Count >= HoldSamplesTarget)
                CommitTap();
        }
        else if (_tapAccum.Count > 0)
        {
            _tapAccum.Clear();
            _tiltAccum.Clear();
            HoldProgress = 0;
        }
    }

    private void CommitTap()
    {
        var avg = new Vector2(_tapAccum.Average(p => p.X), _tapAccum.Average(p => p.Y));
        _measuredRaw.Add(avg);
        _tapSampleCounts.Add(_tapAccum.Count);   // remember how many samples this hold averaged (#460)
        // Average the pen tilt over the hold — the natural angle the user drew at (#481).
        _measuredTilt.Add(_tiltAccum.Count > 0
            ? new Vector2(_tiltAccum.Average(t => t.X), _tiltAccum.Average(t => t.Y))
            : new Vector2(float.NaN, float.NaN));
        _tapAccum.Clear();
        _tiltAccum.Clear();
        HoldProgress = 0;
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CanUndoPoint));

        if (_measuredRaw.Count >= _targets.Count)
            _ = FinishAsync();
        else
        {
            CurrentTarget = _measuredRaw.Count;
            UpdateInstruction();
        }
    }

    private async Task FinishAsync()
    {
        var targetsDesktop = _targets.Select(t => new Vector2(
            (float)(_ctx.Display.X + t.X * _ctx.Display.Width),
            (float)(_ctx.Display.Y + t.Y * _ctx.Display.Height))).ToList();

        var fp = CalibrationProfile.Fingerprint(_ctx.Input, _ctx.Output, _ctx.Display.Number);

        // Corners → least-squares AFFINE; Grid → per-node bilinear offsets (#196).
        CalibrationProfile.CalibrationData? data = null;
        if (_ctx.Mode == CalibrationMode.Corners)
        {
            // Least-squares affine over the 4 corners (the original v1 model). It's over-determined
            // (6 DOF, 8 equations) so it AVERAGES tap noise rather than fitting it exactly — unlike a
            // 4-point homography, which fits all 4 taps perfectly and bakes their asymmetric noise into
            // spurious perspective that warps the corner neighbourhoods. Flat pen displays are near-affine,
            // so the affine is both more robust and more accurate there. (#483; reverts #195 for corners.)
            if (CalibrationSolver.Solve(targetsDesktop, _measuredRaw, _ctx.Digitizer, _ctx.Input, _ctx.Output) is { } m)
                data = new CalibrationProfile.CalibrationData(m, Enabled: true, Fingerprint: fp);
        }
        else
        {
            if (CalibrationSolver.SolveGrid(targetsDesktop, _measuredRaw, _ctx.Digitizer, _ctx.Input, _ctx.Output, _ctx.GridCols, _ctx.GridRows) is { } g)
                data = CalibrationProfile.CalibrationData.ForGrid(g, enabled: true, fp);
        }

        if (data is null)
        {
            CurrentPhase = Phase.Failed;
            Instruction = "Couldn't compute a calibration from those taps — they may be too close together or off-target. Redo.";
            return;
        }

        // Attach the recorded points so the Calibration tab can show a positional report (#460).
        data = data with { Report = BuildReport(targetsDesktop) };

        // Apply for the live preview ("move the pen around"). Apply == persist in this app; Cancel restores.
        CalibrationProfile.Write(_ctx.Settings, _ctx.TabletName, data);
        await _ctx.Apply(_ctx.Settings);

        CurrentPhase = Phase.Confirming;
        Instruction = "Move the pen around — does the cursor track the nib? Apply to keep, or Redo.";
    }

    // The recorded points paired with their on-screen targets, for the tab report (#460). Coordinates
    // are stored <em>relative to the calibrated display</em> (0..Width, 0..Height) rather than in
    // virtual-desktop px, so they read naturally against the one display we calibrated instead of
    // carrying that display's desktop offset (#461). Each point also carries the pixel-equivalent of its
    // raw tap — the raw position mapped back to the screen through the (uncorrected) capture-time
    // mapping — so the report can show, and score, how far the pen actually landed from the target.
    private CalibrationReport BuildReport(IReadOnlyList<Vector2> targetsDesktop)
    {
        float ox = (float)_ctx.Display.X, oy = (float)_ctx.Display.Y;   // display origin in the desktop
        var points = new List<CalibrationReportPoint>(_measuredRaw.Count);
        for (int i = 0; i < _measuredRaw.Count && i < targetsDesktop.Count; i++)
        {
            var raw = _measuredRaw[i];
            var measuredPx = AbsolutePositionMapper.MapToDesktop(raw, _ctx.Digitizer, _ctx.Input, _ctx.Output, false, false);
            float mx = float.NaN, my = float.NaN;
            if (measuredPx is { } m) { mx = m.X - ox; my = m.Y - oy; }
            var tilt = i < _measuredTilt.Count ? _measuredTilt[i] : new Vector2(float.NaN, float.NaN);
            points.Add(new CalibrationReportPoint(
                targetsDesktop[i].X - ox, targetsDesktop[i].Y - oy,
                raw.X, raw.Y,
                mx, my,
                i < _tapSampleCounts.Count ? _tapSampleCounts[i] : 0,
                tilt.X, tilt.Y));
        }
        var display = $"{_ctx.Display.DisplayTitle} ({_ctx.Display.Width}×{_ctx.Display.Height})";
        return new CalibrationReport(display, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), points);
    }

    // --- Commands ---

    [RelayCommand]
    private void Apply() => CloseRequested?.Invoke(); // already written+applied during preview

    [RelayCommand]
    private async Task Redo()
    {
        _measuredRaw.Clear();
        _tapSampleCounts.Clear();
        _measuredTilt.Clear();
        _tapAccum.Clear();
        _tiltAccum.Clear();
        HoldProgress = 0;
        CurrentTarget = 0;
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CanUndoPoint));
        await BypassCalibrationAsync(); // clear the preview correction before recapturing
        CurrentPhase = Phase.Capturing;
        UpdateInstruction();
    }

    /// <summary>Undo just the last recorded point (#458): pop it and re-arm that target. If we'd already
    /// finished (Confirming/Failed), drop the preview correction so the re-tap is captured uncorrected.</summary>
    [RelayCommand]
    private async Task UndoLastPoint()
    {
        if (_measuredRaw.Count == 0) return;

        bool wasPreviewing = CurrentPhase != Phase.Capturing;
        _measuredRaw.RemoveAt(_measuredRaw.Count - 1);
        if (_tapSampleCounts.Count > 0) _tapSampleCounts.RemoveAt(_tapSampleCounts.Count - 1);
        if (_measuredTilt.Count > 0) _measuredTilt.RemoveAt(_measuredTilt.Count - 1);
        _tapAccum.Clear();
        _tiltAccum.Clear();
        HoldProgress = 0;
        CurrentTarget = _measuredRaw.Count;
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CanUndoPoint));

        if (wasPreviewing)
            await BypassCalibrationAsync(); // we'd applied a preview; clear it so recapture is uncorrected
        CurrentPhase = Phase.Capturing;
        UpdateInstruction();
    }

    [RelayCommand]
    private async Task Clear()
    {
        CalibrationProfile.Clear(_ctx.Settings, _ctx.TabletName);
        await _ctx.Apply(_ctx.Settings);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task Cancel()
    {
        // Restore whatever calibration existed when the overlay opened (model + payload preserved).
        if (_original is null)
            CalibrationProfile.Clear(_ctx.Settings, _ctx.TabletName);
        else
            CalibrationProfile.Write(_ctx.Settings, _ctx.TabletName, _original);
        await _ctx.Apply(_ctx.Settings);
        CloseRequested?.Invoke();
    }

    private static bool Near(double x, double y, (double X, double Y) t)
        => Math.Sqrt((x - t.X) * (x - t.X) + (y - t.Y) * (y - t.Y)) <= HitRadiusN;

    private void UpdateInstruction()
    {
        int total = _targets.Count;
        if (_ctx.Mode == CalibrationMode.Corners)
        {
            string[] corner = { "top-left", "top-right", "bottom-right", "bottom-left" };
            var where = CurrentTarget < corner.Length ? corner[CurrentTarget] : $"#{CurrentTarget + 1}";
            Instruction = $"Rest the pen on the {where} target and hold still until the ring fills ({CurrentTarget + 1} of {total}).";
        }
        else
        {
            Instruction = $"Rest the pen on target {CurrentTarget + 1} of {total} and hold still until the ring fills.";
        }
    }
}
