using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Win32;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Full-display overlay for the pointer calibration (#127). Covers the mapped display and draws the
/// targets (driven by <see cref="CalibrationViewModel"/>), and starts/stops the daemon pen stream
/// with its own lifetime. The targets are positioned in code from the VM's normalized coordinates so
/// DPI scaling doesn't distort them. The active target is emphasized three ways: a vignette dimming
/// the rest of the screen, full-screen crosshair guides, and a looping pulse ring.
/// </summary>
public partial class CalibrationOverlayWindow : Window
{
    private const double CrossArm = 9; // crosshair half-length (px) marking each target's exact centre

    private readonly CalibrationViewModel? _vm;
    private readonly DisplayInfo? _display;
    private readonly List<Ellipse> _targets = new();
    private readonly List<Line> _crossH = new();
    private readonly List<Line> _crossV = new();
    private readonly List<TextBlock> _labels = new();   // per-target number, shown under each ring

    // Active-target emphasis.
    private Rectangle? _dim;            // radial vignette darkening everything but the active target
    private Line? _guideH, _guideV;     // full-screen crosshair guides through the active target
    private Ellipse? _pulse;            // looping pulse ring on the active target
    private ScaleTransform? _pulseScale;
    private DispatcherTimer? _pulseTimer;
    private double _pulsePhase;
    private Arc? _holdArc;              // fills clockwise as the pen rests on the active target (#457)
    // Same delegate instance for add + remove (a method group would create two, so remove wouldn't match).
    private static readonly Win32Properties.CustomWndProcHookCallback WndProcHookDelegate = WndProcHook;

    // Overlay light/dark palette. This is calibration-only and independent of the app theme — the
    // shapes are code-drawn, so all colours (window, panel, text, buttons, targets) are chosen here.
    private bool _light;
    private static readonly IBrush DarkWinBg = new SolidColorBrush(Color.Parse("#D9101018"));
    private static readonly IBrush LightWinBg = new SolidColorBrush(Color.Parse("#E6ECECF0"));
    // Panel is deliberately more opaque + lighter than the (near-black) window, with a visible border, so
    // it reads as a distinct floating control surface rather than blending into the dimmed calibration bg.
    private static readonly IBrush DarkPanelBg = new SolidColorBrush(Color.Parse("#F22B3048"));
    private static readonly IBrush LightPanelBg = new SolidColorBrush(Color.Parse("#FAFFFFFF"));
    private static readonly IBrush DarkPanelBorder = new SolidColorBrush(Color.Parse("#5AFFFFFF"));
    private static readonly IBrush LightPanelBorder = new SolidColorBrush(Color.Parse("#40000000"));
    private static readonly IBrush DarkText = Brushes.White;
    private static readonly IBrush LightText = new SolidColorBrush(Color.Parse("#11141A"));
    private static readonly IBrush DarkBtnFg = Brushes.White;
    private static readonly IBrush DarkBtnBg = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly IBrush DarkBtnBorder = new SolidColorBrush(Color.Parse("#73FFFFFF"));
    private static readonly IBrush LightBtnFg = new SolidColorBrush(Color.Parse("#11141A"));
    private static readonly IBrush LightBtnBg = new SolidColorBrush(Color.Parse("#18000000"));
    private static readonly IBrush LightBtnBorder = new SolidColorBrush(Color.Parse("#66000000"));
    private static readonly IBrush DarkPending = Brushes.White;
    private static readonly IBrush LightPending = new SolidColorBrush(Color.Parse("#22262E"));
    private static readonly IBrush DarkActive = Brushes.DeepSkyBlue;
    private static readonly IBrush LightActive = new SolidColorBrush(Color.Parse("#1565C0"));
    private static readonly IBrush DarkCaptured = Brushes.LimeGreen;
    private static readonly IBrush LightCaptured = new SolidColorBrush(Color.Parse("#2E7D32"));

    private IBrush PendingBrush => _light ? LightPending : DarkPending;
    private IBrush ActiveBrush => _light ? LightActive : DarkActive;
    private IBrush CapturedBrush => _light ? LightCaptured : DarkCaptured;
    private Color VignetteColor => _light ? Color.FromArgb(0xC8, 0xEE, 0xEE, 0xF2) : Color.FromArgb(0xB0, 0x08, 0x08, 0x10);

    // Parameterless ctor for the Avalonia designer/loader.
    public CalibrationOverlayWindow()
    {
        InitializeComponent();
    }

    public CalibrationOverlayWindow(CalibrationViewModel vm, DisplayInfo display)
    {
        InitializeComponent();
        _vm = vm;
        _display = display;
        DataContext = vm;

        vm.CloseRequested += OnCloseRequested;
        vm.PropertyChanged += (_, _) => UpdateVisuals();
        Surface.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) BuildAndLayout(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) vm.CancelCommand.Execute(null); };
    }

    private void OnCloseRequested() => Close();

    // Flip the overlay between light and dark (calibration-only; does not touch the app theme).
    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        _light = !_light;
        ApplyOverlayTheme();
    }

    private void ApplyOverlayTheme()
    {
        Background = _light ? LightWinBg : DarkWinBg;
        Panel.Background = _light ? LightPanelBg : DarkPanelBg;
        Panel.BorderBrush = _light ? LightPanelBorder : DarkPanelBorder;
        InstructionText.Foreground = _light ? LightText : DarkText;
        ProgressLabel.Foreground = _light ? LightText : DarkText;
        // The natural-tilt tip (#481) reads as secondary — a muted variant of the instruction colour.
        HoldTip.Foreground = new SolidColorBrush(_light ? Color.FromArgb(0xB0, 0x20, 0x20, 0x28)
                                                        : Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        ThemeToggle.Content = _light ? "Dark mode" : "Light mode";

        // The secondary (ghost) buttons are illegible with their default muted colours on this panel,
        // so colour them explicitly per mode.
        foreach (var b in new[] { UndoBtn, RedoBtn, ClearBtn, CancelBtn, ThemeToggle })
        {
            b.Foreground = _light ? LightBtnFg : DarkBtnFg;
            b.Background = _light ? LightBtnBg : DarkBtnBg;
            b.BorderBrush = _light ? LightBtnBorder : DarkBtnBorder;
        }

        UpdateVisuals(); // recolour the code-drawn targets/guides/pulse/vignette
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceOnDisplay();
        BuildAndLayout();
        ApplyOverlayTheme(); // colour the panel/buttons/shapes for the initial (dark) mode

        // Hide the OS cursor over the targets: seeing it invites aligning the *cursor* with the target,
        // when the point of calibration is to aim the *pen nib* there. The panel keeps a normal cursor so
        // its buttons stay easy to click (#127 follow-up).
        Cursor = new Cursor(StandardCursorType.None);
        Panel.Cursor = new Cursor(StandardCursorType.Arrow);

        // Opt the overlay out of Windows' press-and-hold gesture — see the doc block below.
        // Win32Properties + the WndProc hook are user32-backed, so guard off-Windows (#140).
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, WndProcHookDelegate);

        // …and turn off the shell's pen/touch visual feedback on this window (the same app-wide helper the
        // main window uses). On its own this doesn't fully stop the cursor re-showing at the dwell — the
        // SetCursor re-hide in the pulse timer handles that.
        ShellPenFeedback.DisableFor(this);

        // Drive the pulse ring (~30 fps). A timer keeps full control of the breathing animation and
        // re-targets for free as the active target advances (we just reposition the ring).
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _pulseTimer.Tick += (_, _) =>
        {
            UpdatePulse();
            // Keep the cursor hidden through the hold. At the ~1 s press-and-hold dwell Windows re-shows
            // the mouse cursor (mouse-emulation fallback), and because the pen is held *still* there's no
            // mouse-move afterwards to re-apply our None cursor — so it sticks. Re-hiding each frame only
            // while actually holding leaves the panel buttons usable when idle.
            if (_vm is { HoldProgress: > 0.001 } && OperatingSystem.IsWindows()) SetCursor(IntPtr.Zero);
        };
        _pulseTimer.Start();

        _ = _vm?.StartAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (OperatingSystem.IsWindows())
            Win32Properties.RemoveWndProcHookCallback(this, WndProcHookDelegate);
        _pulseTimer?.Stop();
        if (_vm != null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _ = _vm.StopAsync();
        }
    }

    // ── Disabling Windows' "press and hold → right-click" on the overlay ──────────────────────────────
    //
    // Problem: our hold-to-average calibration (#457) asks the user to rest the pen still on a target.
    // Windows treats a stationary pen/touch contact as the "press and hold" gesture (its equivalent of a
    // right-click): the shell draws an animated ring at the contact point and re-shows the cursor —
    // fighting our capture flow and our hidden cursor. It's a *system* gesture (governed by Pen & Windows
    // Ink / the legacy "Pen and Touch → Press and hold" setting), not anything the app does; it fires
    // because, in Windows Ink mode, the pen still generates real OS pointer input landing on this window.
    //
    // Fix (the canonical one, and poorly known): a window can opt out per-window by answering the
    // WM_TABLET_QUERYSYSTEMGESTURESTATUS message with TABLET_DISABLE_PRESSANDHOLD. Windows sends that
    // message to ask which system gestures the window wants; returning that flag suppresses press-and-hold
    // (ring + right-click) for this window only — no registry/OS-wide change, no effect on other apps.
    // We hook the Win32 WndProc through Avalonia's Win32Properties to answer it.
    private const uint WmTabletQuerySystemGestureStatus = 0x02CC;
    private const int TabletDisablePressAndHold = 0x00000001;

    private static IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmTabletQuerySystemGestureStatus)
        {
            handled = true;
            return new IntPtr(TabletDisablePressAndHold);
        }
        return IntPtr.Zero;
    }

    // Passing NULL hides the cursor (until the next WM_SETCURSOR, i.e. the next mouse-move). Used to
    // re-hide it during a still hold, where Windows shows it and no mouse-move follows to reset it.
    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    // Cover the mapped display. Move onto the target monitor (picked by containment of the display's
    // centre, robust to rounding / DPI — #179), then go FullScreen so the OS sizes the window to the
    // whole monitor. Doing the sizing ourselves via Bounds/Scaling mis-covered scaled displays.
    private void PlaceOnDisplay()
    {
        if (_display is not { } display) return;

        var centre = new PixelPoint(display.X + display.Width / 2, display.Y + display.Height / 2);
        var screen = Screens.ScreenFromPoint(centre);
        Position = screen?.Bounds.Position ?? new PixelPoint(display.X, display.Y);
        WindowState = WindowState.FullScreen;
    }

    private void BuildAndLayout()
    {
        if (_vm == null) return;
        if (_targets.Count == 0)
        {
            // Vignette behind everything: darkens the screen except around the active target.
            _dim = new Rectangle { IsHitTestVisible = false };
            Surface.Children.Add(_dim);

            // Full-screen guide lines through the active target.
            _guideH = new Line { StrokeThickness = 1, Opacity = 0.45, IsHitTestVisible = false };
            _guideV = new Line { StrokeThickness = 1, Opacity = 0.45, IsHitTestVisible = false };
            Surface.Children.Add(_guideH);
            Surface.Children.Add(_guideV);

            for (int i = 0; i < _vm.Targets.Count; i++)
            {
                var ring = new Ellipse
                {
                    Width = 40, Height = 40, StrokeThickness = 3,
                    Stroke = Brushes.White, Fill = Brushes.Transparent, IsHitTestVisible = false,
                };
                // Crosshair through the ring's centre so the user knows the exact point to aim at.
                var h = new Line { StrokeThickness = 1.5, IsHitTestVisible = false };
                var v = new Line { StrokeThickness = 1.5, IsHitTestVisible = false };
                // The target number, under the ring — so a specific point can be named when discussing
                // where the calibration is off. Fixed width + centred so it sits centred under the ring.
                var label = new TextBlock
                {
                    Text = (i + 1).ToString(), FontSize = 16, FontWeight = FontWeight.Bold,
                    Width = 34, TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
                };
                _targets.Add(ring); _crossH.Add(h); _crossV.Add(v); _labels.Add(label);
                Surface.Children.Add(ring);
                Surface.Children.Add(h);
                Surface.Children.Add(v);
                Surface.Children.Add(label);
            }

            // Pulse ring, on top, scaling about its own centre.
            _pulseScale = new ScaleTransform(1, 1);
            _pulse = new Ellipse
            {
                Width = 40, Height = 40, StrokeThickness = 3, Stroke = Brushes.DeepSkyBlue,
                Fill = Brushes.Transparent, IsHitTestVisible = false,
                RenderTransform = _pulseScale, RenderTransformOrigin = RelativePoint.Center,
            };
            Surface.Children.Add(_pulse);

            // Hold-progress ring (#457): sweeps clockwise from the top as HoldProgress fills, so the user
            // can see they need to keep holding — and for how much longer. Deliberately larger than the
            // 40px target ring (so it reads as a distinct outer ring, not merged with it) and thick.
            _holdArc = new Arc
            {
                Width = 76, Height = 76, StrokeThickness = 15, StartAngle = -90, SweepAngle = 0,
                IsHitTestVisible = false,
            };
            Surface.Children.Add(_holdArc);
        }
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_vm == null) return;
        double w = Surface.Bounds.Width, h = Surface.Bounds.Height;

        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _vm.Targets[i];
            var el = _targets[i];
            double cx = t.X * w, cy = t.Y * h;
            Canvas.SetLeft(el, cx - el.Width / 2);
            Canvas.SetTop(el, cy - el.Height / 2);

            bool active = i == _vm.CurrentTarget && _vm.IsCapturing;
            bool captured = i < _vm.CapturedCount;
            var brush = captured ? CapturedBrush
                      : active ? ActiveBrush : PendingBrush;
            el.Stroke = brush;
            el.StrokeThickness = active ? 5 : 3;

            // Dim targets that are neither active nor done, so the active one stands out among a full
            // grid of otherwise equally-bright rings.
            double opacity = active || captured ? 1.0 : 0.4;
            el.Opacity = opacity;

            // Crosshair, centred on the target and colour-matched to its ring.
            var hLine = _crossH[i];
            hLine.StartPoint = new Point(cx - CrossArm, cy);
            hLine.EndPoint = new Point(cx + CrossArm, cy);
            hLine.Stroke = brush;
            hLine.Opacity = opacity;
            var vLine = _crossV[i];
            vLine.StartPoint = new Point(cx, cy - CrossArm);
            vLine.EndPoint = new Point(cx, cy + CrossArm);
            vLine.Stroke = brush;
            vLine.Opacity = opacity;

            // Number under the ring, colour-matched and dimmed with its target.
            var label = _labels[i];
            label.Foreground = brush;
            label.Opacity = opacity;
            Canvas.SetLeft(label, cx - label.Width / 2);
            Canvas.SetTop(label, cy + el.Height / 2 + 3);
        }

        // --- Active-target emphasis (only while capturing) ---
        bool capturing = _vm.IsCapturing && _vm.CurrentTarget >= 0 && _vm.CurrentTarget < _vm.Targets.Count;
        double ax = 0, ay = 0;
        if (capturing)
        {
            var a = _vm.Targets[_vm.CurrentTarget];
            ax = a.X * w; ay = a.Y * h;
        }

        if (_dim != null)
        {
            _dim.Width = w; _dim.Height = h;
            Canvas.SetLeft(_dim, 0); Canvas.SetTop(_dim, 0);
            _dim.IsVisible = capturing;
            if (capturing && w > 0 && h > 0)
            {
                // Clear around the active target, fading to dark toward the edges.
                var centre = new RelativePoint(ax / w, ay / h, RelativeUnit.Relative);
                _dim.Fill = new RadialGradientBrush
                {
                    Center = centre,
                    GradientOrigin = centre,
                    RadiusX = new RelativeScalar(0.45, RelativeUnit.Relative),
                    RadiusY = new RelativeScalar(0.45, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x00, VignetteColor.R, VignetteColor.G, VignetteColor.B), 0),
                        new GradientStop(Color.FromArgb(0x00, VignetteColor.R, VignetteColor.G, VignetteColor.B), 0.35),
                        new GradientStop(VignetteColor, 1),
                    },
                };
            }
        }

        if (_guideH != null && _guideV != null)
        {
            _guideH.IsVisible = _guideV.IsVisible = capturing;
            if (capturing)
            {
                _guideH.StartPoint = new Point(0, ay);
                _guideH.EndPoint = new Point(w, ay);
                _guideV.StartPoint = new Point(ax, 0);
                _guideV.EndPoint = new Point(ax, h);
                _guideH.Stroke = _guideV.Stroke = ActiveBrush;
            }
        }

        bool holding = capturing && _vm.HoldProgress > 0.001;

        if (_pulse != null)
        {
            // The breathing "aim here" attractor hides once the user starts holding — the fill ring takes over.
            _pulse.IsVisible = capturing && !holding;
            _pulse.Stroke = ActiveBrush;
            if (capturing)
            {
                Canvas.SetLeft(_pulse, ax - _pulse.Width / 2);
                Canvas.SetTop(_pulse, ay - _pulse.Height / 2);
            }
        }

        if (_holdArc != null)
        {
            _holdArc.IsVisible = holding;
            if (holding)
            {
                _holdArc.SweepAngle = _vm.HoldProgress * 360;
                _holdArc.Stroke = CapturedBrush; // green fill signals "almost captured"
                Canvas.SetLeft(_holdArc, ax - _holdArc.Width / 2);
                Canvas.SetTop(_holdArc, ay - _holdArc.Height / 2);
            }
        }

        // Keep the control panel clear of the active target: pin it to the vertical half *opposite* the
        // target. Otherwise a low, centred target (common on 9/25-point grids) sits under the panel and is
        // impossible to reach. When not capturing, default to the bottom. (#127)
        bool targetLow = capturing && _vm.Targets[_vm.CurrentTarget].Y > 0.5;
        Panel.VerticalAlignment = targetLow ? Avalonia.Layout.VerticalAlignment.Top
                                            : Avalonia.Layout.VerticalAlignment.Bottom;
        Panel.Margin = targetLow ? new Thickness(0, 48, 0, 0) : new Thickness(0, 0, 0, 48);
    }

    // Breathing pulse on the active target: grows and fades on a loop (driven by the timer).
    private void UpdatePulse()
    {
        if (_pulse is null || _pulseScale is null || _vm is null) return;
        if (!_pulse.IsVisible) return;

        _pulsePhase += 0.045;
        double t = (Math.Sin(_pulsePhase) + 1) / 2; // 0..1
        double scale = 1.0 + 1.1 * t;               // 1.0 → 2.1
        _pulseScale.ScaleX = _pulseScale.ScaleY = scale;
        _pulse.Opacity = 0.85 * (1 - t);            // brightest small, fades as it grows
    }
}
