using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ReportRecorderTests
{
    private static PenSample S(long ticks, double rawX, double pressure, int? hover = null) =>
        new(0, 0, rawX, 200, pressure, 1.5, -2.5, 0, IsDown: pressure > 0, HoverDistance: hover, Timestamp: ticks);

    [Fact]
    public void Csv_HasAHeader_AndOneRowPerReport_WithTimeSinceTheFirst()
    {
        var r = new ReportRecorder();
        var tick = System.Diagnostics.Stopwatch.Frequency / 1000; // 1 ms
        r.Add(S(5000, 100, 0.25));
        r.Add(S(5000 + tick, 101, 0.5, hover: 7));

        var lines = r.ToCsv().TrimEnd('\n').Split('\n');

        Assert.Equal("t_ms,raw_x,raw_y,pressure,tilt_x,tilt_y,hover", lines[0]);
        Assert.Equal("0,100,200,0.25,1.5,-2.5,", lines[1]);   // no hover on this report → blank
        Assert.Equal("1,101,200,0.5,1.5,-2.5,7", lines[2]);
    }

    [Fact]
    public void WriteAndReset_WritesTheFile_ThenStartsFresh()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ota-recorder-{System.Guid.NewGuid():N}.csv");
        try
        {
            var r = new ReportRecorder();
            r.Add(S(1, 10, 0.1));

            Assert.Equal(1, r.WriteAndReset(path));
            Assert.Contains("10,200,0.1", File.ReadAllText(path));
            Assert.Equal(0, r.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WriteAndReset_WithNothingRecorded_LeavesTheFileAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ota-recorder-{System.Guid.NewGuid():N}.csv");
        Assert.Equal(0, new ReportRecorder().WriteAndReset(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Recording_IsCapped()
    {
        var r = new ReportRecorder();
        for (var i = 0; i < ReportRecorder.MaxReports + 10; i++) r.Add(S(i + 1, i, 0.5));
        Assert.Equal(ReportRecorder.MaxReports, r.Count);
    }
}
