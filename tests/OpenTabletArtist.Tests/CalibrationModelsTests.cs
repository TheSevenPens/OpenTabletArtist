using System.Linq;
using System.Numerics;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class CalibrationModelsTests
{
    private static readonly Vector2[] Corners =
        { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1), new(0, 0) };

    // --- Homography (#195) ---

    [Fact]
    public void SolveHomography_RecoversAKnownPerspectiveMap()
    {
        var h = new Homography(1.1f, 0.05f, 0.02f, -0.03f, 0.95f, -0.01f, 0.08f, -0.04f, 1f);
        var to = Corners.Select(h.Project).ToArray();

        var solved = CalibrationMath.SolveHomography(Corners, to);

        Assert.NotNull(solved);
        foreach (var p in Corners)
        {
            var e = h.Project(p);
            var a = solved!.Value.Project(p);
            Assert.Equal(e.X, a.X, 3);
            Assert.Equal(e.Y, a.Y, 3);
        }
    }

    [Fact]
    public void SolveHomography_IsIdentity_WhenFromEqualsTo()
    {
        var h = CalibrationMath.SolveHomography(Corners, Corners);
        Assert.NotNull(h);
        foreach (var p in Corners)
        {
            var a = h!.Value.Project(p);
            Assert.Equal(p.X, a.X, 4);
            Assert.Equal(p.Y, a.Y, 4);
        }
    }

    [Fact]
    public void SolveHomography_NullWithFewerThanFourPoints()
    {
        var three = Corners.Take(3).ToArray();
        Assert.Null(CalibrationMath.SolveHomography(three, three));
    }

    [Fact]
    public void Homography_CsvRoundTrips()
    {
        var h = new Homography(1.1f, 0.05f, 0.02f, -0.03f, 0.95f, -0.01f, 0.08f, -0.04f, 1f);
        var parsed = Homography.TryParse(h.ToCsv());
        Assert.NotNull(parsed);
        Assert.Equal(h, parsed!.Value);
    }

    [Fact]
    public void Homography_TryParse_RejectsGarbage()
    {
        Assert.Null(Homography.TryParse(""));
        Assert.Null(Homography.TryParse("1,2,3"));
        Assert.Null(Homography.TryParse("a,b,c,d,e,f,g,h,i"));
    }

    // --- Grid (#196) ---

    [Fact]
    public void Grid_ZeroOffsets_LeavePointUnchanged()
    {
        var g = new CalibrationGrid(2, 2, -1, -1, 1, 1,
            new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero });
        var p = new Vector2(0.3f, -0.2f);
        Assert.Equal(p, g.Apply(p));
    }

    [Fact]
    public void Grid_BilinearlyInterpolatesOffsets()
    {
        // 2×2 over [-1,1]²; X-offset 0 on the left edge, 0.2 on the right edge.
        var g = new CalibrationGrid(2, 2, -1, -1, 1, 1, new[]
        {
            new Vector2(0, 0), new Vector2(0.2f, 0),
            new Vector2(0, 0), new Vector2(0.2f, 0),
        });

        var r = g.Apply(new Vector2(0, 0)); // centre → halfway → +0.1
        Assert.Equal(0.10f, r.X, 4);
        Assert.Equal(0f, r.Y, 4);
    }

    [Fact]
    public void Grid_ClampsOutsideBounds()
    {
        var g = new CalibrationGrid(2, 2, -1, -1, 1, 1, new[]
        {
            new Vector2(0.5f, 0), new Vector2(0.5f, 0),
            new Vector2(0.5f, 0), new Vector2(0.5f, 0),
        });
        var r = g.Apply(new Vector2(5, 5)); // clamped to the (uniform) corner offset
        Assert.Equal(5.5f, r.X, 4);
    }

    [Fact]
    public void Grid_CsvRoundTrips()
    {
        var g = new CalibrationGrid(3, 2, -1, -0.8f, 0.9f, 1, new[]
        {
            new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f), new Vector2(0.5f, 0.6f),
            new Vector2(0.7f, 0.8f), new Vector2(0.9f, 1.0f), new Vector2(1.1f, 1.2f),
        });

        var p = CalibrationGrid.TryParse(g.ToCsv());

        Assert.NotNull(p);
        Assert.Equal(3, p!.Cols);
        Assert.Equal(2, p.Rows);
        Assert.Equal(g.MinX, p.MinX, 4);
        Assert.Equal(g.MaxY, p.MaxY, 4);
        for (int i = 0; i < g.Offsets.Length; i++)
        {
            Assert.Equal(g.Offsets[i].X, p.Offsets[i].X, 4);
            Assert.Equal(g.Offsets[i].Y, p.Offsets[i].Y, 4);
        }
    }

    [Fact]
    public void Grid_TryParse_RejectsWrongLength()
    {
        Assert.Null(CalibrationGrid.TryParse(""));
        Assert.Null(CalibrationGrid.TryParse("2,2,-1,-1,1,1,0,0")); // says 2×2 but only one offset
    }

    // --- Calibration report (#460) ---

    [Fact]
    public void CalibrationReport_RoundTripsThroughSerialize()
    {
        var report = new CalibrationReport("DELL U4323QE (3840×2160)", "2026-07-08 14:22", new[]
        {
            new CalibrationReportPoint(384f, 216f, 12840.5f, 7220f, 6),
            new CalibrationReportPoint(1920f, 1080f, 30390f, 17810.25f, 8),
        });

        var parsed = CalibrationReport.TryParse(report.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal("2026-07-08 14:22", parsed!.CapturedAt);
        Assert.Equal(2, parsed.Points.Count);
        Assert.Equal(report.Points[0], parsed.Points[0]);
        Assert.Equal(report.Points[1], parsed.Points[1]);
    }

    [Fact]
    public void CalibrationReport_TryParse_RejectsGarbageAndEmpty()
    {
        Assert.Null(CalibrationReport.TryParse(""));
        Assert.Null(CalibrationReport.TryParse("only|two"));               // missing the points field
        Assert.Null(CalibrationReport.TryParse("d|t|1,2,3,4"));            // a point needs 5 fields
    }
}
