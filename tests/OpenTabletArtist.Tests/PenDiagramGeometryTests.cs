using OpenTabletArtist.Controls;
using Xunit;
using static OpenTabletArtist.Controls.PenDiagramGeometry;

namespace OpenTabletArtist.Tests;

// #1015: the arithmetic behind the buttons tab. The lines from the pen to the cards are only worth drawing if
// each can be followed to its card, so the property that matters is that no two ever cross.
public class PenDiagramGeometryTests
{
    // ── Where things sit ───────────────────────────────────────────

    [Fact]
    public void TheButtons_RunAlongThePen_FromTheTip()
    {
        Assert.Equal(FirstButtonX, ButtonCenterX(1));
        Assert.Equal(ButtonCenterX(1) + ButtonPitch, ButtonCenterX(2));
        Assert.Equal(ButtonCenterX(2) + ButtonPitch, ButtonCenterX(3));
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]      // one button still gets two columns, so its card is half the width
    public void Slots_AreTheButtonsButNeverFewerThanTwo(int buttons, int slots) => Assert.Equal(slots, Slots(buttons));

    [Fact]
    public void ThreeCards_GoSideBySide_OnceTheyAllFitAtTheirMinimumWidth()
    {
        double needed = 3 * MinCardWidth + 2 * CardGap;

        Assert.Equal(3, Columns(needed, 3));
        Assert.Equal(1, Columns(needed - 1, 3));
        Assert.True(IsSideBySide(needed, 3));
        Assert.False(IsSideBySide(needed - 1, 3));
    }

    [Fact]
    public void APenWithOneButton_NeedsOnlyTwoColumnsOfRoom()
    {
        double needed = 2 * MinCardWidth + CardGap;

        Assert.True(IsSideBySide(needed, 1));
        Assert.False(IsSideBySide(needed - 1, 1));
        Assert.Equal(2, Columns(needed, 1));    // the unused column is simply left empty
    }

    [Fact]
    public void NoButtons_IsNeverSideBySide() => Assert.False(IsSideBySide(5000, 0));

    [Fact]
    public void TheCardCenters_AreInColumnOrder_AndEvenlySpaced()
    {
        double width = 1200;
        double a = CardCenterX(width, 3, 1), b = CardCenterX(width, 3, 2), c = CardCenterX(width, 3, 3);

        Assert.True(a < b && b < c);
        Assert.Equal(b - a, c - b, 6);
        double column = (width - 2 * CardGap) / 3;
        Assert.Equal(column / 2, a, 6);
        Assert.Equal(width - column / 2, c, 6);
    }

    // ── The lines ──────────────────────────────────────────────────

    [Fact]
    public void EachLine_StartsAtItsButton_AndEndsAtTheMiddleOfItsCard()
    {
        double width = 1300;
        foreach (var line in Connectors(width, 3))
        {
            Assert.Equal(new Point2(ButtonCenterX(line.Number), ButtonBottomY), line.Points[0]);
            Assert.Equal(new Point2(CardCenterX(width, 3, line.Number), ConnectorEndY), line.Points[^1]);
        }
    }

    [Fact]
    public void ALineWhoseCardIsDirectlyBelow_GoesStraightDown()
    {
        // Card 1's middle is column/2 across; make that land exactly under button 1.
        double column = 2 * ButtonCenterX(1);
        double width = 3 * column + 2 * CardGap;

        var line = Connectors(width, 3).Single(c => c.Number == 1);

        Assert.Equal(2, line.Points.Count);
        Assert.Equal(line.Points[0].X, line.Points[1].X);
    }

    [Fact]
    public void ALineWithSomewhereToGo_HasFourPoints_ElbowingBetweenTheButtonAndTheCard()
    {
        var line = Connectors(1300, 3).Single(c => c.Number == 3);

        Assert.Equal(4, line.Points.Count);
        Assert.Equal(line.Points[1].Y, line.Points[2].Y);   // the run across is level
        Assert.Equal(line.Points[0].X, line.Points[1].X);   // and the two legs are vertical
        Assert.Equal(line.Points[2].X, line.Points[3].X);
    }

    [Fact]
    public void TheElbows_StayBetweenTheButtonsAndTheEndOfTheLines()
    {
        foreach (var line in Connectors(1300, 3))
            foreach (var p in line.Points)
                Assert.InRange(p.Y, ButtonBottomY, ConnectorEndY);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoTwoLinesEverCross_AtAnyWidthTheyAreDrawnAt(int buttons)
    {
        for (double width = Slots(buttons) * MinCardWidth + (Slots(buttons) - 1) * CardGap; width <= 2600; width += 3)
        {
            var lines = Connectors(width, buttons);
            for (int i = 0; i < lines.Count; i++)
                for (int j = i + 1; j < lines.Count; j++)
                    Assert.False(Touch(lines[i], lines[j]),
                        $"lines {lines[i].Number} and {lines[j].Number} cross at width {width} with {buttons} buttons");
        }
    }

    [Fact]
    public void TheCrossingCheck_CanFail()
    {
        // The property above is only worth something if the checker can see a crossing. Two hand-drawn lines
        // that plainly cross, and two that plainly do not.
        var down = new Connector(1, new[] { new Point2(10, 0), new Point2(10, 100) });
        var across = new Connector(2, new[] { new Point2(0, 50), new Point2(20, 50) });
        var apart = new Connector(3, new[] { new Point2(40, 0), new Point2(40, 100) });

        Assert.True(Touch(down, across));
        Assert.False(Touch(down, apart));
    }

    // Whether any segment of one polyline meets any segment of the other (touching counts).
    private static bool Touch(Connector a, Connector b)
    {
        for (int i = 0; i + 1 < a.Points.Count; i++)
            for (int j = 0; j + 1 < b.Points.Count; j++)
                if (SegmentsMeet(a.Points[i], a.Points[i + 1], b.Points[j], b.Points[j + 1])) return true;
        return false;
    }

    // Only axis-aligned segments occur here, so overlap of the two boxes is exact.
    private static bool SegmentsMeet(Point2 p1, Point2 p2, Point2 q1, Point2 q2) =>
        Math.Min(p1.X, p2.X) <= Math.Max(q1.X, q2.X) && Math.Max(p1.X, p2.X) >= Math.Min(q1.X, q2.X) &&
        Math.Min(p1.Y, p2.Y) <= Math.Max(q1.Y, q2.Y) && Math.Max(p1.Y, p2.Y) >= Math.Min(q1.Y, q2.Y);
}
