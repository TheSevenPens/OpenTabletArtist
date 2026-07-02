using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A live circular gauge for a tablet wheel: a faint track, a marker at the current position with a
/// short fading motion trail streaming behind it, and a centre readout showing the current angle (and
/// the rotation direction while turning). Purely a status view — driven by <see cref="Position"/>
/// (0..1, null before the first report / when idle) and the turning flags. There's no fill "bar"; the
/// marker + trail carry the motion, which makes rotation obvious and fun to watch (#309). Bound to a
/// WheelEditor in the Wheel tab.
/// </summary>
public sealed class WheelGauge : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(0x24, 0x88, 0x88, 0x90));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(0xAA, 0x88, 0x88, 0x90));
    private static readonly IBrush DotRing = Brushes.White;
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    public static readonly StyledProperty<double?> PositionProperty =
        AvaloniaProperty.Register<WheelGauge, double?>(nameof(Position));
    public static readonly StyledProperty<bool> TurningClockwiseProperty =
        AvaloniaProperty.Register<WheelGauge, bool>(nameof(TurningClockwise));
    public static readonly StyledProperty<bool> TurningCounterClockwiseProperty =
        AvaloniaProperty.Register<WheelGauge, bool>(nameof(TurningCounterClockwise));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<WheelGauge, IBrush?>(nameof(AccentBrush));

    public double? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public bool TurningClockwise { get => GetValue(TurningClockwiseProperty); set => SetValue(TurningClockwiseProperty, value); }
    public bool TurningCounterClockwise { get => GetValue(TurningCounterClockwiseProperty); set => SetValue(TurningCounterClockwiseProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    // Fading motion trail: each dot is a recent position that decays to nothing over ~0.4s, so a moving
    // marker streams a comet-tail and a stopped one settles cleanly. Newest dot first.
    private sealed class TrailDot { public double Norm; public double Life; }
    private readonly List<TrailDot> _trail = new();
    private double? _lastNorm;
    private double _speed;   // 0..1 rotation-speed signal: rises with movement, decays when you slow
    private DispatcherTimer? _decay;

    private const int MaxTrail = 18;
    private const double DecayPerTick = 0.075;

    static WheelGauge()
    {
        AffectsRender<WheelGauge>(PositionProperty, TurningClockwiseProperty,
            TurningCounterClockwiseProperty, AccentBrushProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(132, 132);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PositionProperty)
            OnPositionChanged(change.GetNewValue<double?>());
    }

    private void OnPositionChanged(double? value)
    {
        if (value is not double norm) { _lastNorm = null; return; }  // idle / lifted — let the trail fade
        norm = Clamp01(norm);
        if (_lastNorm is double prev)
        {
            // Shortest wrapped distance, so a 0.99→0.01 crossing doesn't streak a trail the long way round.
            double d = Math.Abs(norm - prev);
            d = Math.Min(d, 1 - d);
            if (d > 0.0008)
            {
                _trail.Insert(0, new TrailDot { Norm = prev, Life = 1 });
                if (_trail.Count > MaxTrail) _trail.RemoveRange(MaxTrail, _trail.Count - MaxTrail);
                // Bigger jumps between reports = faster spin → ramp the speed signal (capped).
                _speed = Math.Min(1.0, _speed + d * 4.0);
                EnsureDecayRunning();
            }
        }
        _lastNorm = norm;
    }

    private void EnsureDecayRunning()
    {
        if (_decay != null) return;
        _decay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _decay.Tick += (_, _) =>
        {
            // Faster spins decay slower, so the comet-tail is visibly longer at speed.
            double decay = DecayPerTick * (1.0 - 0.55 * _speed);
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                _trail[i].Life -= decay;
                if (_trail[i].Life <= 0) _trail.RemoveAt(i);
            }
            _speed *= 0.85;
            if (_trail.Count == 0) { _speed = 0; _decay?.Stop(); _decay = null; }
            InvalidateVisual();
        };
        _decay.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _decay?.Stop();
        _decay = null;
        _trail.Clear();
        _lastNorm = null;
        _speed = 0;
    }

    public override void Render(DrawingContext ctx)
    {
        var accent = (AccentBrush as ISolidColorBrush)?.Color ?? FallbackAccent;

        var b = Bounds;
        var center = new Point(b.Width / 2, b.Height / 2);
        double radius = Math.Min(b.Width, b.Height) / 2 - 12;
        if (radius <= 0) return;

        // Faint track ring for context — no fill "bar" (#309).
        ctx.DrawEllipse(null, new Pen(Track, 4), center, radius, radius);

        // Fading motion trail behind the marker (newest brightest); it glows brighter the faster you spin.
        foreach (var dot in _trail)
        {
            var p = PointFor(center, radius, dot.Norm);
            byte a = (byte)(dot.Life * (0x60 + 0x70 * _speed));
            var brush = new SolidColorBrush(Color.FromArgb(a, accent.R, accent.G, accent.B));
            double r = (2 + 3.5 * dot.Life) * (1 + 0.4 * _speed);
            ctx.DrawEllipse(brush, null, p, r, r);
        }

        // Marker at the current position, with a soft glow halo that swells with speed.
        if (Position is double norm)
        {
            var marker = PointFor(center, radius, norm);
            if (_speed > 0.02)
            {
                byte g = (byte)(0x55 * _speed);
                double gr = 8 + 12 * _speed;
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(g, accent.R, accent.G, accent.B)), null, marker, gr, gr);
            }
            ctx.DrawEllipse(new SolidColorBrush(accent), new Pen(DotRing, 2), marker, 7, 7);
        }

        // Centre readout: the current angle, with a direction glyph above it while turning.
        var accentBrush = new SolidColorBrush(accent);
        bool turning = TurningClockwise || TurningCounterClockwise;
        if (Position is double n)
        {
            if (turning)
                DrawCentered(ctx, center, TurningClockwise ? "↻" : "↺", 22, accentBrush, -14);
            DrawCentered(ctx, center, $"{AngleDeg(n):0}°", 20, turning ? accentBrush : Muted, turning ? 6 : 0);
        }
        else if (turning)
            DrawCentered(ctx, center, TurningClockwise ? "↻" : "↺", 30, accentBrush, 0);
        else
            DrawCentered(ctx, center, "—", 20, Muted, 0);
    }

    // The reported position increases counter-clockwise on tested rings, so (1-norm) makes the marker
    // travel clockwise as the wheel turns clockwise; the displayed angle matches the marker's clock
    // position (0° at the top, increasing clockwise).
    private static double AngleDeg(double norm) => (360.0 * (1 - Clamp01(norm))) % 360.0;

    private static Point PointFor(Point center, double radius, double norm)
    {
        double rad = (AngleDeg(norm) - 90) * Math.PI / 180.0;   // -90° puts 0° at the top (12 o'clock)
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static void DrawCentered(DrawingContext ctx, Point center, string text, double size, IBrush brush, double dy)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
        ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2 + dy));
    }
}
