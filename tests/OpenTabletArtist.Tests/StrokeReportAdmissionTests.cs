using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class StrokeReportAdmissionTests
{
    private static readonly StrokeRecordingContext Context = new(
        TestReports.Tablet, "OpenTabletDriver 0.6.7", 1023, new TabletSpace(15200, 9500, 152.0, 95.0));

    private static PenSample Pen(double rawPressure = 200, double x = 100, int? hover = 10) =>
        new(0, 0, x, x * 2, rawPressure / 1023, 4, -3, 0, rawPressure > 0, hover, Timestamp: 1_000, RawPressure: rawPressure, HasTilt: true);

    private static (StrokeReportAdmission Admission, StrokeRecordingSession Session) NewAdmission()
    {
        var session = new StrokeRecordingSession(1023);
        return (new StrokeReportAdmission(Context, session), session);
    }

    [Fact]
    public void APenReportFromThisTabletIsRecorded()
    {
        var (admission, session) = NewAdmission();
        var s = Pen();

        admission.Offer(TestReports.Json(s), s);

        Assert.Equal(1, session.Count);
        Assert.Equal(0, admission.IgnoredOtherTablet + admission.IgnoredNotPen);
    }

    [Fact]
    public void TheNameIsMatchedWithoutRegardToCase()
    {
        var (admission, session) = NewAdmission();
        var s = Pen();

        admission.Offer(TestReports.Json(s, tablet: "WACOM ptk-470"), s);

        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void AnotherTabletsReportsAreNotRecordedUnderThisOnesName()
    {
        var (admission, session) = NewAdmission();
        var s = Pen(rawPressure: 4096);   // a pressure that could not exist on a 1023-level tablet

        admission.Offer(TestReports.Json(s, tablet: "Wacom PTK-670", maxPressure: 8191), s);

        Assert.Equal(0, session.Count);
        Assert.Equal(1, admission.IgnoredOtherTablet);
        Assert.False(admission.SpecificationsChanged);   // a different tablet is not this one changing
    }

    [Fact]
    public void AMouseReportThatCarriesAPositionIsNotAPenWithZeroPressure()
    {
        var (admission, session) = NewAdmission();
        var s = Pen(rawPressure: 0);

        // The readouts still read this as a hovering pen (TryParse is forgiving); a recording must not.
        admission.Offer(TestReports.Json(s, pressure: false, tilt: false), s);

        Assert.Equal(0, session.Count);
        Assert.Equal(1, admission.IgnoredNotPen);
    }

    [Theory]
    [InlineData("tiltX only")]
    [InlineData("tilt is a string")]
    [InlineData("hover is a string")]
    [InlineData("no y")]
    public void AReportMissingAPartOfWhatItClaimsIsNotRecorded(string flaw)
    {
        var (admission, session) = NewAdmission();
        var s = Pen();
        var json = TestReports.Json(s);

        switch (flaw)
        {
            case "tiltX only": ((JObject)json["Data"]!["Tilt"]!).Remove("Y"); break;
            case "tilt is a string": json["Data"]!["Tilt"] = "up"; break;
            case "hover is a string": json["Data"]!["HoverDistance"] = "near"; break;
            case "no y": ((JObject)json["Data"]!["Position"]!).Remove("Y"); break;
        }

        admission.Offer(json, s);

        Assert.Equal(0, session.Count);
        Assert.Equal(1, admission.IgnoredNotPen);
    }

    [Fact]
    public void AReportWithNoTiltAtAllIsAPenReportThatSimplyHasNoTiltColumn()
    {
        var (admission, session) = NewAdmission();
        var s = Pen() with { HasTilt = false };

        admission.Offer(TestReports.Json(s, tilt: false), s);

        Assert.Equal(1, session.Count);
        Assert.False(session.Channels.Tilt);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANumberThatIsNotANumberIsNotRecorded(double bad)
    {
        var (admission, session) = NewAdmission();
        var s = Pen() with { RawX = bad };

        admission.Offer(TestReports.Json(Pen()), s);

        Assert.Equal(0, session.Count);
        Assert.Equal(1, admission.IgnoredNotPen);
    }

    [Fact]
    public void WhenThisTabletsSpecificationsChangeTheRecordingStopsTakingReports()
    {
        var (admission, session) = NewAdmission();
        var s = Pen();

        admission.Offer(TestReports.Json(s), s);
        admission.Offer(TestReports.Json(s, maxPressure: 8191), s);   // same name, different pressure scale
        admission.Offer(TestReports.Json(s), s);                      // and it stays stopped

        Assert.True(admission.SpecificationsChanged);
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void ADifferentDigitizerSizeUnderTheSameNameIsAChange()
    {
        var (admission, session) = NewAdmission();
        var s = Pen();

        admission.Offer(TestReports.Json(s, maxX: 37400, maxY: 21000), s);

        Assert.True(admission.SpecificationsChanged);
        Assert.Equal(0, session.Count);
    }
}
