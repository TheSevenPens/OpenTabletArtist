using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OtdArtist.Domain;
using OtdArtist.Services;

namespace OtdArtist.ViewModels;

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

    private const double HitRadiusN = 0.06;   // accept a tap only this close to the active target
    private const int MinSamplesPerTap = 4;    // average at least this many down-samples

    private readonly Context _ctx;
    private readonly DaemonPenInputSource _input;
    private readonly CalibrationProfile.CalibrationData? _original;
    private readonly List<Vector2> _measuredRaw = new();   // one per completed target
    private readonly List<Vector2> _tapAccum = new();        // raw samples for the in-progress tap
    private bool _wasDown;

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

    public bool IsCapturing => CurrentPhase == Phase.Capturing;
    public bool IsConfirming => CurrentPhase == Phase.Confirming;
    public bool IsFailed => CurrentPhase == Phase.Failed;
    public bool ShowApply => IsConfirming;
    public bool ShowRedo => IsConfirming || IsFailed;

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

        if (CurrentPhase != Phase.Capturing) { _wasDown = s.IsDown; return; }

        bool nearTarget = LiveDotVisible && Near(LiveDotX, LiveDotY, _targets[CurrentTarget]);

        if (s.IsDown && nearTarget)
            _tapAccum.Add(raw);                 // collecting a tap on the active target

        // Pen lifted: commit the tap if we gathered enough samples on-target.
        if (_wasDown && !s.IsDown)
        {
            if (_tapAccum.Count >= MinSamplesPerTap)
                CommitTap();
            _tapAccum.Clear();
        }

        _wasDown = s.IsDown;
    }

    private void CommitTap()
    {
        var avg = new Vector2(_tapAccum.Average(p => p.X), _tapAccum.Average(p => p.Y));
        _measuredRaw.Add(avg);
        OnPropertyChanged(nameof(CapturedCount));

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

        // Corners → perspective homography (#195); Grid → per-node bilinear offsets (#196).
        CalibrationProfile.CalibrationData? data = null;
        if (_ctx.Mode == CalibrationMode.Corners)
        {
            if (CalibrationSolver.SolveHomography(targetsDesktop, _measuredRaw, _ctx.Digitizer, _ctx.Input, _ctx.Output) is { } h)
                data = CalibrationProfile.CalibrationData.ForHomography(h, enabled: true, fp);
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

        // Apply for the live preview ("move the pen around"). Apply == persist in this app; Cancel restores.
        CalibrationProfile.Write(_ctx.Settings, _ctx.TabletName, data);
        await _ctx.Apply(_ctx.Settings);

        CurrentPhase = Phase.Confirming;
        Instruction = "Move the pen around — does the cursor track the nib? Apply to keep, or Redo.";
    }

    // --- Commands ---

    [RelayCommand]
    private void Apply() => CloseRequested?.Invoke(); // already written+applied during preview

    [RelayCommand]
    private async Task Redo()
    {
        _measuredRaw.Clear();
        _tapAccum.Clear();
        CurrentTarget = 0;
        OnPropertyChanged(nameof(CapturedCount));
        await BypassCalibrationAsync(); // clear the preview correction before recapturing
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
            Instruction = $"Tap the {where} target with your pen ({CurrentTarget + 1} of {total}). Press on it and lift.";
        }
        else
        {
            Instruction = $"Tap target {CurrentTarget + 1} of {total} with your pen. Press on it and lift.";
        }
    }
}
