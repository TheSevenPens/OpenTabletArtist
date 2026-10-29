using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Helpers;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The pen lying flat (the shared <see cref="PenArtwork"/> outline), tip on the left, with its barrel buttons numbered from the tip. When the cards below sit
/// side by side, an elbow line joins each button to its card, so which card is which button is a line to follow
/// and not a number to match. All the layout arithmetic is in <see cref="PenDiagramGeometry"/>; this only paints it.
/// The pen is drawn at its natural size and shrinks evenly when the window is narrower than that.
/// </summary>
public sealed class PenButtonsDiagram : Control
{
    public static readonly StyledProperty<int> ButtonCountProperty =
        AvaloniaProperty.Register<PenButtonsDiagram, int>(nameof(ButtonCount), 3);
    public static readonly StyledProperty<bool> ShowConnectorsProperty =
        AvaloniaProperty.Register<PenButtonsDiagram, bool>(nameof(ShowConnectors));
    public static readonly StyledProperty<double> CardGapProperty =
        AvaloniaProperty.Register<PenButtonsDiagram, double>(nameof(CardGap), PenDiagramGeometry.CardGap);
    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<PenButtonsDiagram, IBrush?>(nameof(FillBrush));
    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<PenButtonsDiagram, IBrush?>(nameof(StrokeBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<PenButtonsDiagram, IBrush?>(nameof(AccentBrush));

    /// <summary>How many barrel buttons the pen has (1 to 3).</summary>
    public int ButtonCount { get => GetValue(ButtonCountProperty); set => SetValue(ButtonCountProperty, value); }
    /// <summary>Draw the lines to the cards. Off when the cards are stacked, where the lines would mean nothing.</summary>
    public bool ShowConnectors { get => GetValue(ShowConnectorsProperty); set => SetValue(ShowConnectorsProperty, value); }
    /// <summary>The gap between the cards, so the lines meet the middle of each.</summary>
    public double CardGap { get => GetValue(CardGapProperty); set => SetValue(CardGapProperty, value); }
    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush? StrokeBrush { get => GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    static PenButtonsDiagram()
    {
        AffectsRender<PenButtonsDiagram>(ButtonCountProperty, ShowConnectorsProperty, CardGapProperty,
            FillBrushProperty, StrokeBrushProperty, AccentBrushProperty);
        AffectsMeasure<PenButtonsDiagram>(ShowConnectorsProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? PenDiagramGeometry.NaturalWidth : availableSize.Width;
        return new Size(w, ShowConnectors ? PenDiagramGeometry.HeightWithConnectors : PenDiagramGeometry.HeightPenOnly);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        double width = Bounds.Width;
        if (width <= 0) return;

        var outline = StrokeBrush is null ? null : new Pen(StrokeBrush, 1.5, lineJoin: PenLineJoin.Round);
        double scale = Math.Clamp((width - 8) / PenDiagramGeometry.NaturalWidth, 0.3, 1);

        using (ctx.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            DrawPen(ctx, outline);
            DrawButtons(ctx);
        }

        if (ShowConnectors) DrawConnectors(ctx, width);
    }

    // The outline is the same at every size and scaled by the render transform, so it is built once.
    private static readonly Geometry PenBody =
        PenArtwork.Outline(PenDiagramGeometry.PenLength, PenDiagramGeometry.PenCenterY, PenDiagramGeometry.PenLeft);
    private static readonly Geometry PenSeams =
        PenArtwork.SeamLines(PenDiagramGeometry.PenLength, PenDiagramGeometry.PenCenterY, PenDiagramGeometry.PenLeft);

    private void DrawPen(DrawingContext ctx, IPen? outline)
    {
        ctx.DrawGeometry(FillBrush, outline, PenBody);
        if (outline is not null) ctx.DrawGeometry(null, outline, PenSeams);
    }

    private void DrawButtons(DrawingContext ctx)
    {
        int count = Math.Clamp(ButtonCount, 0, 3);
        for (int n = 1; n <= count; n++)
        {
            double cx = PenDiagramGeometry.ButtonCenterX(n);
            ctx.DrawRectangle(AccentBrush, null,
                new Rect(cx - PenDiagramGeometry.ButtonWidth / 2, PenDiagramGeometry.ButtonTopY,
                    PenDiagramGeometry.ButtonWidth, PenDiagramGeometry.ButtonHeight), 6, 6);
            DrawCentered(ctx, n.ToString(CultureInfo.InvariantCulture), cx, PenDiagramGeometry.ButtonTopY + PenDiagramGeometry.ButtonHeight / 2,
                12, Brushes.White, FontWeight.Bold);
        }
    }

    private void DrawConnectors(DrawingContext ctx, double width)
    {
        if (StrokeBrush is null) return;
        var line = new Pen(StrokeBrush, 1.5, lineJoin: PenLineJoin.Round);
        foreach (var connector in PenDiagramGeometry.Connectors(width, Math.Clamp(ButtonCount, 0, 3), CardGap))
        {
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                c.BeginFigure(ToPoint(connector.Points[0]), false);
                for (int i = 1; i < connector.Points.Count; i++) c.LineTo(ToPoint(connector.Points[i]));
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, line, geometry);
            ctx.DrawEllipse(StrokeBrush, null, ToPoint(connector.Points[^1]), 3, 3);
        }
    }

    private static Point ToPoint(PenDiagramGeometry.Point2 p) => new(p.X, p.Y);

    private static void DrawCentered(DrawingContext ctx, string text, double cx, double cy, double size,
        IBrush? brush, FontWeight weight)
    {
        if (brush is null) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            AppFonts.UiTypeface(weight: weight), size, brush);
        ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }
}
