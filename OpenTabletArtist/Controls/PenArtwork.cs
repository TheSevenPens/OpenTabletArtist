using Avalonia;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The pen's shape, from the designer's drawing (wacpen-full.svg in SevenPensWebUtils): the right half of the
/// outline, standing, mirrored for the left. Any place the app draws a pen should draw this one, so every pen
/// looks the same. It is a path only, with no buttons: the caller puts those on.
///
/// The numbers below are that drawing's own, in its 4725-unit square, eraser end at the top and tip at the bottom.
/// The drawing was since revised (a rounder dome and a longer nib): the revision is in a 19688-unit square, and these
/// are its numbers divided by 19687.5 / 4725.
/// <see cref="Trace"/> lays the pen on its side, tip on the left, at whatever length is wanted, and says what it
/// draws to an <see cref="IPenSink"/> so the shape can be measured in a test as well as painted.
/// </summary>
public static class PenArtwork
{
    /// <summary>Where a traced outline sends its moves, lines and curves.</summary>
    public interface IPenSink
    {
        void MoveTo(Point p);
        void LineTo(Point p);
        void CubicTo(Point control1, Point control2, Point end);
        void Close();
    }

    // The drawing's own coordinates. The centre line is x = CenterX; the eraser's dome starts at DomeTopY and the
    // tip ends at TipY.
    private const double CenterX = 2338.333, DomeTopY = 211.483, TipY = 4582.098, MirrorSum = 4676.666;

    private readonly record struct Seg(bool IsCurve, Point C1, Point C2, Point End);

    private static Seg Line(double x, double y) => new(false, default, default, new Point(x, y));
    private static Seg Curve(double x1, double y1, double x2, double y2, double x, double y) =>
        new(true, new Point(x1, y1), new Point(x2, y2), new Point(x, y));

    // Right half, from the top of the eraser's dome to the middle of the tip: the dome, the step out at the
    // eraser's seam, the grip, the swell and taper to the nib, and the nib's rounded end.
    private static readonly Point Start = new(CenterX, DomeTopY);
    private static readonly Seg[] RightHalf =
    {
        Curve(2369.487, 211.483, 2396.639, 236.755, 2410.262, 259.863),
        Curve(2421.521, 278.961, 2426.781, 344.920, 2428.795, 391.864),
        Line(2452.807, 391.864),
        Line(2489.204, 2019.789),
        Curve(2489.204, 2019.789, 2501.978, 3361.186, 2511.924, 3856.933),
        Curve(2514.076, 3964.223, 2536.040, 3985.115, 2536.306, 4037.548),
        Curve(2536.437, 4063.374, 2469.928, 4254.560, 2469.928, 4254.560),
        Line(2381.038, 4521.634),
        Line(2352.926, 4521.634),
        Line(2356.144, 4521.634),      // the nib is a little wider than the cone's end: a step of 3.2 units
        Line(2356.144, 4566.977),
        Curve(2356.144, 4570.987, 2354.457, 4574.833, 2351.453, 4577.669),
        Curve(2348.450, 4580.505, 2344.377, 4582.098, 2340.130, 4582.098),
        Line(CenterX, TipY),
    };

    // The two seams across the pen, the eraser's and the nib's: each is the right half's end of it and its mirror.
    private static readonly (double Y, double HalfWidth)[] Seams = { (391.864, 2428.795 - CenterX), (4521.634, 2352.926 - CenterX) };

    private static Point Mirror(Point p) => new(MirrorSum - p.X, p.Y);

    /// <summary>The pen's length in the drawing's units, for scaling.</summary>
    private const double DrawingLength = TipY - DomeTopY;

    /// <summary>The pen's largest half-thickness as a share of its length, so a caller can leave room for it.</summary>
    public const double MaxHalfWidthPerLength = (2536.306 - CenterX) / DrawingLength;

    /// <summary>
    /// Draws the closed outline lying down: the tip's end at <paramref name="left"/>, the eraser's end
    /// <paramref name="length"/> further on, the centre line at <paramref name="centerY"/>.
    /// </summary>
    public static void Trace(IPenSink sink, double length, double centerY, double left)
    {
        var map = Mapper(length, centerY, left);

        sink.MoveTo(map(Start));
        foreach (var seg in RightHalf) Emit(sink, map, seg);

        // Back up the left side: the right half in reverse, each piece mirrored and run the other way.
        for (int i = RightHalf.Length - 1; i >= 0; i--)
        {
            var seg = RightHalf[i];
            var from = i == 0 ? Start : RightHalf[i - 1].End;
            Emit(sink, map, seg.IsCurve
                ? new Seg(true, Mirror(seg.C2), Mirror(seg.C1), Mirror(from))
                : new Seg(false, default, default, Mirror(from)));
        }
        sink.Close();
    }

    /// <summary>The seams across the pen, drawn as open lines (they are not part of the outline).</summary>
    public static void TraceSeams(IPenSink sink, double length, double centerY, double left)
    {
        var map = Mapper(length, centerY, left);
        foreach (var (y, half) in Seams)
        {
            sink.MoveTo(map(new Point(CenterX - half, y)));
            sink.LineTo(map(new Point(CenterX + half, y)));
        }
    }

    private static void Emit(IPenSink sink, Func<Point, Point> map, Seg seg)
    {
        if (seg.IsCurve) sink.CubicTo(map(seg.C1), map(seg.C2), map(seg.End));
        else sink.LineTo(map(seg.End));
    }

    // Standing to lying down: along the pen (down the drawing, tip first) becomes left to right, and across the
    // pen becomes up and down. A quarter turn, so nothing is flipped.
    private static Func<Point, Point> Mapper(double length, double centerY, double left)
    {
        double scale = length / DrawingLength;
        return p => new Point(left + (TipY - p.Y) * scale, centerY + (p.X - CenterX) * scale);
    }

    /// <summary>The outline as a geometry, lying down, ready to fill and stroke.</summary>
    public static Geometry Outline(double length, double centerY, double left)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var sink = new GeometrySink(ctx, closed: true);
            Trace(sink, length, centerY, left);
        }
        return geometry;
    }

    /// <summary>The seams as a geometry, to stroke.</summary>
    public static Geometry SeamLines(double length, double centerY, double left)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var sink = new GeometrySink(ctx, closed: false);
            TraceSeams(sink, length, centerY, left);
            sink.End();
        }
        return geometry;
    }

    // Feeds a trace into a StreamGeometry. The outline is one figure; the seams are several open ones.
    private sealed class GeometrySink(StreamGeometryContext ctx, bool closed) : IPenSink
    {
        private bool _open;

        public void MoveTo(Point p)
        {
            EndFigure(isClosed: false);
            ctx.BeginFigure(p, isFilled: closed);
            _open = true;
        }

        public void LineTo(Point p) => ctx.LineTo(p);
        public void CubicTo(Point control1, Point control2, Point end) => ctx.CubicBezierTo(control1, control2, end);

        public void Close() => EndFigure(isClosed: true);

        /// <summary>Ends the last open figure, for a trace that never closes (the seams).</summary>
        public void End() => EndFigure(isClosed: false);

        private void EndFigure(bool isClosed)
        {
            if (!_open) return;
            ctx.EndFigure(isClosed);
            _open = false;
        }
    }
}
