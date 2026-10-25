using Avalonia;
using OpenTabletArtist.Controls;
using Xunit;
using static OpenTabletArtist.Controls.PenDiagramGeometry;

namespace OpenTabletArtist.Tests;

// #1015: the pen is drawn from the designer's own outline (wacpen-full.svg), shared so every pen in the app looks the
// same. These check the outline's shape through what a trace of it says, and that the buttons and lines drawn on
// it sit where they should: the buttons on the grip, the lines clear of the body.
public class PenArtworkTests
{
    // A trace recorded as it is drawn: the points the outline passes through, curves sampled finely.
    private sealed class Recorder : PenArtwork.IPenSink
    {
        public List<Point> Points { get; } = new();
        public List<(Point From, Point To)> Lines { get; } = new();   // straight pieces, for the seams
        public int Moves, Closes;
        private Point _at;

        public void MoveTo(Point p) { Moves++; Points.Add(p); _at = p; }
        public void LineTo(Point p) { Lines.Add((_at, p)); Points.Add(p); _at = p; }
        public void CubicTo(Point c1, Point c2, Point end)
        {
            for (int i = 1; i <= 60; i++)
            {
                double t = i / 60.0, u = 1 - t;
                Points.Add(new Point(
                    u * u * u * _at.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * end.X,
                    u * u * u * _at.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * end.Y));
            }
            _at = end;
        }
        public void Close() => Closes++;

        // Distance from the centre line to the outline at this x, the larger of the two sides.
        public double HalfWidthAt(double x, double centerY)
        {
            double best = 0;
            for (int i = 0; i < Points.Count; i++)
            {
                var a = Points[i];
                var b = Points[(i + 1) % Points.Count];
                if ((a.X - x) * (b.X - x) > 0 || a.X == b.X) continue;
                double y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
                best = Math.Max(best, Math.Abs(y - centerY));
            }
            return best;
        }
    }

    private static Recorder Outline(double length = PenLength, double centerY = PenCenterY, double left = PenLeft)
    {
        var r = new Recorder();
        PenArtwork.Trace(r, length, centerY, left);
        return r;
    }

    // ── The shape ──────────────────────────────────────────────────

    [Fact]
    public void TheOutline_IsOneClosedFigure()
    {
        var r = Outline();

        Assert.Equal(1, r.Moves);
        Assert.Equal(1, r.Closes);
    }

    [Fact]
    public void TheTip_IsAtTheLeft_AndTheEraserEnd_ALengthFurtherOn()
    {
        var r = Outline(length: 806, left: 2);

        Assert.Equal(2, r.Points.Min(p => p.X), 6);
        Assert.Equal(808, r.Points.Max(p => p.X), 6);
        // Tip end: the leftmost point is on the centre line (the nib's end is rounded, a fraction of a pixel across).
        Assert.InRange(r.Points.OrderBy(p => p.X).First().Y, PenCenterY - 0.5, PenCenterY + 0.5);
    }

    [Fact]
    public void ThePen_IsSymmetricAboutItsCentreLine()
    {
        var r = Outline(centerY: 123);

        Assert.Equal(123, (r.Points.Min(p => p.Y) + r.Points.Max(p => p.Y)) / 2, 3);
        foreach (var x in new[] { 30.0, 100, 250, 400, 600, 770 })
        {
            var ys = r.Points.Where(p => Math.Abs(p.X - x) < 0.5).Select(p => p.Y - 123).ToList();
            if (ys.Count > 1) Assert.True(Math.Abs(ys.Max() + ys.Min()) < 1.5, $"not symmetric at x={x}");
        }
    }

    [Fact]
    public void TheLargestHalfThickness_MatchesTheRatioCallersLeaveRoomFor()
    {
        var r = Outline(length: 806, centerY: 0);

        Assert.Equal(PenArtwork.MaxHalfWidthPerLength * 806, r.Points.Max(p => p.Y), 2);
    }

    [Fact]
    public void ItScalesWithTheLength()
    {
        var small = Outline(length: 400, centerY: 0, left: 0);
        var big = Outline(length: 800, centerY: 0, left: 0);

        Assert.Equal(2 * small.Points.Max(p => p.Y), big.Points.Max(p => p.Y), 3);
        Assert.Equal(2 * small.Points.Max(p => p.X), big.Points.Max(p => p.X), 3);
    }

    [Fact]
    public void TheGripIsThickerThanTheEraserEnd_AndTheTipTapersToAPoint()
    {
        var r = Outline();

        Assert.True(r.HalfWidthAt(300, PenCenterY) > r.HalfWidthAt(790, PenCenterY) + 8);
        Assert.True(r.HalfWidthAt(4, PenCenterY) < 4);
        Assert.True(r.HalfWidthAt(4, PenCenterY) < r.HalfWidthAt(60, PenCenterY));
    }

    // ── The seams ──────────────────────────────────────────────────

    [Fact]
    public void TheSeams_AreTwoLinesAcrossThePen_OneNearEachEnd()
    {
        var r = new Recorder();
        PenArtwork.TraceSeams(r, PenLength, PenCenterY, PenLeft);

        Assert.Equal(2, r.Lines.Count);
        Assert.All(r.Lines, l =>
        {
            Assert.Equal(l.From.X, l.To.X, 6);                                  // straight across
            Assert.Equal(PenCenterY, (l.From.Y + l.To.Y) / 2, 3);               // centred on the pen
        });
        var xs = r.Lines.Select(l => l.From.X).OrderBy(x => x).ToList();
        Assert.True(xs[0] < 20, "the nib's seam is near the tip");
        Assert.True(xs[1] > PenLeft + PenLength - 50, "the eraser's seam is near the eraser end");
    }

    // ── Buttons and lines drawn on it ──────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachButton_SitsInsideThePensGrip_WithRoomAroundIt(int number)
    {
        var r = Outline();
        double cx = ButtonCenterX(number);

        foreach (double x in new[] { cx - ButtonWidth / 2, cx, cx + ButtonWidth / 2 })
            Assert.True(r.HalfWidthAt(x, PenCenterY) >= ButtonHeight / 2 + 6,
                $"button {number} runs off the pen at x={x}");
    }

    [Fact]
    public void ThePenIsCentredOnTheButtons()
    {
        Assert.Equal((ButtonTopY + ButtonBottomY) / 2, PenCenterY);
    }

    [Theory]
    [InlineData(1280, 3)]
    [InlineData(1052, 3)]
    [InlineData(900, 2)]
    [InlineData(2400, 3)]
    public void TheRunsAcross_AreBelowThePensBody_SoNoLineGoesThroughIt(double width, int buttons)
    {
        double bottom = PenCenterY + PenArtwork.MaxHalfWidthPerLength * PenLength;

        foreach (var line in Connectors(Math.Max(width, Slots(buttons) * MinCardWidth + (Slots(buttons) - 1) * CardGap), buttons))
            foreach (var p in line.Points.Skip(1).Take(line.Points.Count - 2))
                Assert.True(p.Y > bottom, $"line {line.Number} turns at y={p.Y}, inside the pen (bottom {bottom:0.0})");
    }

    [Fact]
    public void ThePictureIsTallEnoughForThePen_AndForTheLines()
    {
        double bottom = PenCenterY + PenArtwork.MaxHalfWidthPerLength * PenLength;

        Assert.True(HeightPenOnly >= bottom + 1.5);                 // plus the stroke
        Assert.True(HeightWithConnectors >= ConnectorEndY + 4);     // plus the dot at the end of each line
        Assert.True(PenCenterY - PenArtwork.MaxHalfWidthPerLength * PenLength >= 1.5);
    }
}
