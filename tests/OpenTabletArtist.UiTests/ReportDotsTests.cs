using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The scribble canvas's "report dots": a small red dot for every report that lands ink, on a layer above the
/// brush. The point is judging how reports fall along a stroke at high report rates, so the two things that
/// matter are that a dot survives the wide brush segments drawn after it, and that off means off.
/// </summary>
public class ReportDotsTests
{
    private static PenTestCanvas Shown()
    {
        var canvas = new PenTestCanvas { IgnorePointer = true };
        new Window { Width = 400, Height = 300, Content = canvas }.Show();
        Dispatcher.UIThread.RunJobs();
        return canvas;
    }

    private static PenSample At(double x, double y, double pressure = 1) =>
        new(x, y, 0, 0, pressure, 0, 0, 0, IsDown: pressure > 0);

    private static int RedPixels(PenTestCanvas canvas)
    {
        var (bgra, _, _) = canvas.Snapshot()!.Value;
        var red = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
            if (bgra[i + 2] > 200 && bgra[i + 1] < 60 && bgra[i] < 60) red++; // B, G, R, A
        return red;
    }

    [AvaloniaFact]
    public void WithDotsOn_EveryInkedReportLeavesARedDot()
    {
        var canvas = Shown();
        canvas.ShowReportDots = true;

        Assert.True(canvas.AddSample(At(0.2, 0.5)));
        Assert.True(canvas.AddSample(At(0.5, 0.5)));

        Assert.True(RedPixels(canvas) > 0);
    }

    [AvaloniaFact]
    public void ADot_IsNotPaintedOverByTheNextBrushSegment()
    {
        // At full pressure the brush is 48 px wide, so the segment to the next report covers the previous
        // point entirely. The dot is on its own layer, so it must still be there.
        var canvas = Shown();
        canvas.ShowReportDots = true;

        canvas.AddSample(At(0.2, 0.5));
        var afterFirst = RedPixels(canvas);
        canvas.AddSample(At(0.3, 0.5));
        canvas.AddSample(At(0.4, 0.5));

        Assert.True(RedPixels(canvas) > afterFirst, "the earlier dot should still show after later segments");
    }

    [AvaloniaFact]
    public void WithDotsOff_NothingIsRed()
    {
        var canvas = Shown();

        canvas.AddSample(At(0.2, 0.5));
        canvas.AddSample(At(0.5, 0.5));

        Assert.Equal(0, RedPixels(canvas));
    }

    [AvaloniaFact]
    public void Hovering_DropsNoDots()
    {
        var canvas = Shown();
        canvas.ShowReportDots = true;

        Assert.False(canvas.AddSample(At(0.5, 0.5, pressure: 0))); // pen up: no ink, so no dot

        Assert.Equal(0, RedPixels(canvas));
    }

    [AvaloniaFact]
    public void TurningDotsOff_HidesThem_AndClearRemovesThem()
    {
        var canvas = Shown();
        canvas.ShowReportDots = true;
        canvas.AddSample(At(0.5, 0.5));
        Assert.True(RedPixels(canvas) > 0);

        canvas.ShowReportDots = false;
        Assert.Equal(0, RedPixels(canvas)); // what you see (and copy) has no dots

        canvas.ShowReportDots = true;
        Assert.True(RedPixels(canvas) > 0); // they were kept, not discarded

        canvas.Clear();
        Assert.Equal(0, RedPixels(canvas));
    }
}
