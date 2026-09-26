using System.Text.Json;

namespace OtdHealth.Tests;

public class ReviewContractTests
{
    [Fact]
    public void AmbiguousTabletIdentitiesAreRejected()
    {
        var input = new HealthSnapshot
        {
            Tablets = [new("Same", true, true, TiltDisabled: true), new("Same", true, true, TiltDisabled: true)],
        };
        Assert.Throws<ArgumentException>(() => HealthEvaluator.Evaluate(input));
    }

    [Theory]
    [InlineData(HealthPlatform.Windows)]
    [InlineData(HealthPlatform.Linux)]
    [InlineData(HealthPlatform.Unspecified)]
    public void InputMonitoringAdviceRequiresMacOS(HealthPlatform platform)
    {
        Assert.Empty(HealthEvaluator.Evaluate(new HealthSnapshot
        {
            Platform = platform,
            DaemonConnected = true,
            DaemonCannotOpenTablet = true,
        }));
    }

    [Fact]
    public void DefaultJsonUsesNamedEnumValues()
    {
        Assert.Equal("\"Broken\"", JsonSerializer.Serialize(HealthSeverity.Broken));
        Assert.Equal("\"MacOS\"", JsonSerializer.Serialize(HealthPlatform.MacOS));
        Assert.Equal("\"OffScreen\"", JsonSerializer.Serialize(DisplayMappingStatus.OffScreen));
        Assert.Equal("\"PendingRestart\"", JsonSerializer.Serialize(HidAccessStatus.PendingRestart));
    }

    [Fact]
    public void ExplicitIdentitiesKeepSameNamedTabletsDistinctAcrossReorderingAndJson()
    {
        var input = new HealthSnapshot
        {
            Tablets =
            [
                new("Same", true, true, TiltDisabled: true, Id: "device:one"),
                new("Same", true, true, TiltDisabled: true, Id: "device:two"),
            ],
        };
        var findings = HealthEvaluator.Evaluate(input);
        Assert.Equal(["tablet.tiltDisabled:device:one", "tablet.tiltDisabled:device:two"],
            findings.Select(f => f.Id).ToArray());
        Assert.All(findings, f => Assert.Equal("Same", f.TabletName));
        Assert.Equal(["device:one", "device:two"], findings.Select(f => f.TabletId!).ToArray());
        Assert.Equal(findings, HealthEvaluator.Evaluate(input with { Tablets = input.Tablets.Reverse().ToArray() }));

        var copy = JsonSerializer.Deserialize<HealthSnapshot>(JsonSerializer.Serialize(input))!;
        Assert.Equal(findings, HealthEvaluator.Evaluate(copy));
        var output = JsonSerializer.Deserialize<HealthFinding[]>(JsonSerializer.Serialize(findings))!;
        Assert.Equal(findings, output);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(null, " ")]
    [InlineData("", "Tablet A")]
    [InlineData(" ", "Tablet A")]
    public void BlankEffectiveIdentitiesAreRejected(string? id, string name)
    {
        var input = new HealthSnapshot { Tablets = [new(name, false, true, Id: id)] };
        Assert.Throws<ArgumentException>(() => HealthEvaluator.Evaluate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("second")]
    public void ExplicitIdentitiesMustNotCollideWithExplicitOrFallbackIdentities(string? secondId)
    {
        var input = new HealthSnapshot
        {
            Tablets = [new("first", true, true, Id: "second"), new("second", false, true, Id: secondId)],
        };
        Assert.Throws<ArgumentException>(() => HealthEvaluator.Evaluate(input));
    }

    [Fact]
    public void UniqueNamesRemainTheDefaultIdentity()
    {
        var finding = Assert.Single(HealthEvaluator.Evaluate(new HealthSnapshot
        {
            Tablets = [new("Tablet A", true, true, TiltDisabled: true)],
        }));
        Assert.Equal("Tablet A", finding.TabletId);
        Assert.Equal("tablet.tiltDisabled:Tablet A", finding.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MacOSPermissionInferenceStillRequiresAConnection(bool connected)
    {
        var findings = HealthEvaluator.Evaluate(new HealthSnapshot
        {
            Platform = HealthPlatform.MacOS,
            DaemonConnected = connected,
            DaemonCannotOpenTablet = true,
        });
        if (connected)
            Assert.Equal(HealthCheckCodes.DaemonPermissionsMissing, Assert.Single(findings).Code);
        else
            Assert.Empty(findings);
    }

    [Fact]
    public void DefaultJsonEnumsRoundTripByName()
    {
        foreach (var value in Enum.GetValues<HealthSeverity>())
        {
            var json = JsonSerializer.Serialize(value);
            Assert.Equal($"\"{value}\"", json);
            Assert.Equal(value, JsonSerializer.Deserialize<HealthSeverity>(json));
        }
        foreach (var value in Enum.GetValues<HealthPlatform>())
            Assert.Equal(value, JsonSerializer.Deserialize<HealthPlatform>(JsonSerializer.Serialize(value)));
        foreach (var value in Enum.GetValues<DisplayMappingStatus>())
            Assert.Equal(value, JsonSerializer.Deserialize<DisplayMappingStatus>(JsonSerializer.Serialize(value)));
        foreach (var value in Enum.GetValues<HidAccessStatus>())
            Assert.Equal(value, JsonSerializer.Deserialize<HidAccessStatus>(JsonSerializer.Serialize(value)));
    }
}
