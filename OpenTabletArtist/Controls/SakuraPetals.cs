using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Controls;

/// <summary>
/// Decorative falling-sakura-petal overlay for the Anime/Sakura skin (#207). A lightweight custom
/// render layer: a handful of petals drift down with a gentle sway and spin, recycling to the top
/// when they leave the bottom edge.
///
/// It only runs while the Sakura theme is active AND the user hasn't switched petals off — in any
/// other theme the timer is stopped, so it costs nothing. Hit-test invisible, so it never blocks the
/// UI beneath it; placed behind the sidebar/content so petals read as part of the scene.
/// </summary>
public class SakuraPetals : Control
{
    private sealed class Petal
    {
        public double X, Y, Size, Angle, AngularVel, Fall, SwayPhase, SwaySpeed, SwayAmp, Opacity;
    }

    private const int PetalCount = 52;
    private static readonly IBrush PetalBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0xC8));
    private static readonly Geometry PetalGeometry = BuildPetal();

    private readonly List<Petal> _petals = new();
    private readonly Random _rng = new();
    private DispatcherTimer? _timer;

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
    private void OnSettingsChanged() => UpdateRunState();

    private bool ShouldRun =>
        ThemeService.AnimeVariant.Equals(ActualThemeVariant) && AnimationSettings.PetalsEnabled;

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

    private void Seed()
    {
        var (w, h) = Field();
        for (int i = 0; i < PetalCount; i++)
            _petals.Add(NewPetal(w, h, initial: true));
    }

    private Petal NewPetal(double w, double h, bool initial) => new()
    {
        X = _rng.NextDouble() * w,
        // Initial petals start scattered across the field; recycled ones drop in just above the top.
        Y = initial ? _rng.NextDouble() * h : -20 - _rng.NextDouble() * 80,
        Size = 28 + _rng.NextDouble() * 32,
        Angle = _rng.NextDouble() * Math.PI * 2,
        AngularVel = (_rng.NextDouble() - 0.5) * 0.06,
        Fall = 0.5 + _rng.NextDouble() * 1.1,
        SwayPhase = _rng.NextDouble() * Math.PI * 2,
        SwaySpeed = 0.01 + _rng.NextDouble() * 0.02,
        SwayAmp = 0.4 + _rng.NextDouble() * 1.2,
        Opacity = 0.30 + _rng.NextDouble() * 0.45,
    };

    private void Tick()
    {
        var (w, h) = Field();
        foreach (var p in _petals)
        {
            p.SwayPhase += p.SwaySpeed;
            p.X += Math.Sin(p.SwayPhase) * p.SwayAmp;
            p.Y += p.Fall;
            p.Angle += p.AngularVel;
            if (p.Y > h + 24)
                Recycle(p, NewPetal(w, h, initial: false));
        }
        InvalidateVisual();
    }

    private static void Recycle(Petal p, Petal n)
    {
        p.X = n.X; p.Y = n.Y; p.Size = n.Size; p.Angle = n.Angle; p.AngularVel = n.AngularVel;
        p.Fall = n.Fall; p.SwayPhase = n.SwayPhase; p.SwaySpeed = n.SwaySpeed; p.SwayAmp = n.SwayAmp;
        p.Opacity = n.Opacity;
    }

    public override void Render(DrawingContext context)
    {
        foreach (var p in _petals)
        {
            // Petal geometry is authored ~10px tall, centered at the origin; scale/rotate/translate it.
            var transform = Matrix.CreateScale(p.Size / 10.0, p.Size / 10.0)
                            * Matrix.CreateRotation(p.Angle)
                            * Matrix.CreateTranslation(p.X, p.Y);
            using (context.PushOpacity(p.Opacity))
            using (context.PushTransform(transform))
                context.DrawGeometry(PetalBrush, null, PetalGeometry);
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
