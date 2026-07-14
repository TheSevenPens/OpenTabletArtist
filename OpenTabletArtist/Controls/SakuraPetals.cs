using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Controls;

/// <summary>
/// Decorative falling-sakura-petal overlay for the Anime/Sakura skin (#207), reused by the Custom skin.
/// A lightweight custom render layer — no textures or WebGL — that leans on four cheap tricks to read
/// as a lively blossom fall rather than flat confetti:
///
///  • <b>Pseudo-3D tumble</b>: each petal's horizontal scale is driven by cos(flipPhase), so it turns
///    edge-on (thin) and over (mirrored "back") as it falls; opacity dips at edge-on to sell the flip.
///  • <b>Shared wind</b>: one global gusting wind (a sum of slow sines) blows every petal, so they surge
///    together in waves instead of each swaying independently.
///  • <b>Depth parallax</b>: a per-petal depth (0..1) scales size, fall speed, wind pull and opacity;
///    near petals are drawn last so they overlay far ones.
///  • <b>Palette variation</b>: petals pick from a pale→deep pink palette.
///
/// It only runs while the Sakura/Custom theme is active AND the user hasn't switched petals off — in any
/// other theme the timer is stopped, so it costs nothing. Hit-test invisible; placed behind the
/// sidebar/content so petals read as part of the scene.
/// </summary>
public class SakuraPetals : Control
{
    private sealed class Petal
    {
        public double X, Y, Size, Depth;
        public double Angle, AngularVel;        // in-plane spin
        public double FlipPhase, FlipSpeed;     // pseudo-3D tumble → horizontal scale
        public double WobblePhase, WobbleSpeed; // subtle height wobble
        public double Fall;                     // base descent per frame
        public double Drift;                    // constant sideways lean
        public double SwayPhase, SwaySpeed, SwayAmp; // local flutter on top of the wind
        public double Opacity;                  // base opacity (before edge-on fade)
        public int ColorIndex;
    }

    private const int PetalCount = 64;
    private const double Dt = 0.033; // matches the ~30fps tick

    // Pale → deep sakura pinks; each petal keeps one for the run.
    private static readonly IBrush[] PetalBrushes =
    {
        new SolidColorBrush(Color.FromRgb(0xFF, 0xE1, 0xEC)),
        new SolidColorBrush(Color.FromRgb(0xFF, 0xC7, 0xDD)),
        new SolidColorBrush(Color.FromRgb(0xFB, 0xA7, 0xC6)),
        new SolidColorBrush(Color.FromRgb(0xF5, 0x8A, 0xB0)),
        new SolidColorBrush(Color.FromRgb(0xEE, 0x6F, 0x9C)),
    };

    private static readonly Geometry PetalGeometry = BuildPetal();

    private readonly List<Petal> _petals = new();
    private readonly Random _rng = new();
    private DispatcherTimer? _timer;
    private double _time; // accumulates to drive the global wind
    private double _opacity = AnimationSettings.PetalsOpacity; // user-set overall petal opacity (#207)

    public SakuraPetals()
    {
        IsHitTestVisible = false;
        IsVisible = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
        AnimationSettings.Changed += OnSettingsChanged;
        _opacity = AnimationSettings.PetalsOpacity;
        UpdateRunState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
        AnimationSettings.Changed -= OnSettingsChanged;
        Stop();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateRunState();

    private void OnSettingsChanged()
    {
        _opacity = AnimationSettings.PetalsOpacity;
        UpdateRunState();
        // While running, the timer's Tick already invalidates each frame; when paused, force a repaint
        // so an opacity change is reflected even without a live animation.
        if (_timer == null) InvalidateVisual();
    }

    // Petals run in the Sakura + Dark Sakura skins and (reused) in the Custom skin, when enabled.
    private bool ShouldRun =>
        (ThemeService.AnimeVariant.Equals(ActualThemeVariant)
         || ThemeService.DarkSakuraVariant.Equals(ActualThemeVariant)
         || ThemeService.CustomVariant.Equals(ActualThemeVariant))
        && AnimationSettings.PetalsEnabled;

    private void UpdateRunState()
    {
        if (ShouldRun) Start();
        else Stop();
    }

    private void Start()
    {
        IsVisible = true;
        if (_timer != null) return;
        if (_petals.Count == 0) Seed();
        // ~30fps on the Background priority so it never competes with input/layout.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
        IsVisible = false;
    }

    private (double W, double H) Field()
    {
        var w = Bounds.Width > 0 ? Bounds.Width : 1100;
        var h = Bounds.Height > 0 ? Bounds.Height : 720;
        return (w, h);
    }

    // Global gusting wind (px/frame). A sum of slow, out-of-phase sines gives rolling gusts rather than
    // a steady breeze; petals scale their response by depth so near ones get pushed more.
    private double Wind() =>
        0.9 * Math.Sin(_time * 0.6)
        + 0.5 * Math.Sin(_time * 0.23 + 1.3)
        + 0.35 * Math.Sin(_time * 1.1 + 2.6);

    private void Seed()
    {
        var (w, h) = Field();
        for (int i = 0; i < PetalCount; i++)
            _petals.Add(NewPetal(w, h, initial: true));
    }

    private Petal NewPetal(double w, double h, bool initial)
    {
        var depth = _rng.NextDouble();
        return new Petal
        {
            Depth = depth,
            // Near petals (high depth) are bigger and fall faster — the core parallax cue.
            Size = 20 + depth * 34 + _rng.NextDouble() * 6,
            // Spawn a little off both sides too, so wind can carry petals in from the edges.
            X = -40 + _rng.NextDouble() * (w + 80),
            Y = initial ? _rng.NextDouble() * h : -30 - _rng.NextDouble() * 90,
            Angle = _rng.NextDouble() * Math.PI * 2,
            AngularVel = (_rng.NextDouble() - 0.5) * 0.05,
            FlipPhase = _rng.NextDouble() * Math.PI * 2,
            FlipSpeed = 0.03 + _rng.NextDouble() * 0.05,
            WobblePhase = _rng.NextDouble() * Math.PI * 2,
            WobbleSpeed = 0.02 + _rng.NextDouble() * 0.03,
            Fall = 0.4 + depth * 1.3 + _rng.NextDouble() * 0.3,
            Drift = (_rng.NextDouble() - 0.5) * 0.5,
            SwayPhase = _rng.NextDouble() * Math.PI * 2,
            SwaySpeed = 0.015 + _rng.NextDouble() * 0.025,
            SwayAmp = 0.3 + _rng.NextDouble() * 0.7,
            Opacity = 0.35 + depth * 0.5,
            ColorIndex = _rng.Next(PetalBrushes.Length),
        };
    }

    private void Tick()
    {
        var (w, h) = Field();
        _time += Dt;
        var wind = Wind();

        foreach (var p in _petals)
        {
            p.SwayPhase += p.SwaySpeed;
            p.FlipPhase += p.FlipSpeed;
            p.WobblePhase += p.WobbleSpeed;
            p.Angle += p.AngularVel;

            var depthPull = 0.45 + p.Depth; // 0.45..1.45 — near petals get more wind
            p.X += wind * depthPull + p.Drift + Math.Sin(p.SwayPhase) * p.SwayAmp;
            p.Y += p.Fall;

            if (p.Y > h + p.Size)
                Recycle(p, NewPetal(w, h, initial: false));
            else if (p.X < -80) p.X = w + 60;   // wrap sideways so a steady gust can't empty an edge
            else if (p.X > w + 80) p.X = -60;
        }

        InvalidateVisual();
    }

    private static void Recycle(Petal p, Petal n)
    {
        p.X = n.X; p.Y = n.Y; p.Size = n.Size; p.Depth = n.Depth;
        p.Angle = n.Angle; p.AngularVel = n.AngularVel;
        p.FlipPhase = n.FlipPhase; p.FlipSpeed = n.FlipSpeed;
        p.WobblePhase = n.WobblePhase; p.WobbleSpeed = n.WobbleSpeed;
        p.Fall = n.Fall; p.Drift = n.Drift;
        p.SwayPhase = n.SwayPhase; p.SwaySpeed = n.SwaySpeed; p.SwayAmp = n.SwayAmp;
        p.Opacity = n.Opacity; p.ColorIndex = n.ColorIndex;
    }

    public override void Render(DrawingContext context)
    {
        // Draw far → near so nearer (bigger, more opaque) petals overlay distant ones.
        _petals.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        foreach (var p in _petals)
        {
            // Pseudo-3D tumble: cos(FlipPhase) sweeps −1..1 → the petal turns over (mirror) and passes
            // edge-on (near 0, thin). WobblePhase gives a gentle height breathe. Fade toward edge-on.
            var flipX = Math.Cos(p.FlipPhase);
            var wobbleY = 0.82 + 0.18 * Math.Sin(p.WobblePhase);
            var edgeFade = 0.35 + 0.65 * Math.Abs(flipX);

            var s = p.Size / 10.0; // petal geometry is authored ~10px tall, centered at the origin
            var transform = Matrix.CreateScale(s * flipX, s * wobbleY)
                            * Matrix.CreateRotation(p.Angle)
                            * Matrix.CreateTranslation(p.X, p.Y);

            using (context.PushOpacity(p.Opacity * edgeFade * _opacity))
            using (context.PushTransform(transform))
                context.DrawGeometry(PetalBrushes[p.ColorIndex], null, PetalGeometry);
        }
    }

    private static Geometry BuildPetal()
    {
        // A small sakura petal: a teardrop with a soft notch at the wide (bottom) end.
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, -5), true);
            ctx.CubicBezierTo(new Point(4.2, -3), new Point(4.2, 3), new Point(1.3, 5));
            ctx.QuadraticBezierTo(new Point(0, 3.6), new Point(-1.3, 5)); // notch at the tip
            ctx.CubicBezierTo(new Point(-4.2, 3), new Point(-4.2, -3), new Point(0, -5));
            ctx.EndFigure(true);
        }
        return g;
    }
}
