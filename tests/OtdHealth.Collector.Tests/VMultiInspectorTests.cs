using System.Text.Json;

namespace OtdHealth.Collector.Tests;

public class VMultiInspectorTests
{
    private static VMultiNode Node(string id, string[] hardwareIds, string? service = null, string? description = null,
        uint problem = 0, bool enabled = true) =>
        new(id, hardwareIds, description, service, Present: true, enabled, problem, null, null, null);

    // The node as read from a working install: ROOT\HIDCLASS\0000, service vmulti, problem code 0.
    private static VMultiNode Working() => Node(@"ROOT\HIDCLASS\0000", [@"pentablet\hid"], "vmulti", "Pentablet HID");

    private static VMultiNode HidChild(int n) =>
        Node($@"HID\HID&COL0{n}\1&2D595CA7&1&000{n}", [$@"HID\hid&Col0{n}", "HID\\VID_00FF&UP:0001_U:0002"]);

    [Fact]
    public void WorkingInstall_IsInstalled_AndSaysWhichIdMatched()
    {
        var o = VMultiInspector.Build([Working(), HidChild(1), HidChild(2)], 0, null, null);
        Assert.True(o.Installed);
        Assert.Equal(@"hardware-id:pentablet\hid", o.Details.MatchedBy);
        Assert.Single(o.Details.Nodes);
        Assert.Empty(o.Details.NearMisses);              // the HID children are not "near misses"
        Assert.Contains("Installed", o.Details.SetupApiVerdict);
    }

    // The shape of the original bug: a real driver under an ID the probe does not know. It still reads as
    // "not installed", but the report now names the node that looks like VMulti.
    [Fact]
    public void UnknownIdUnderTheVMultiService_IsNotInstalled_ButIsReportedAsANearMiss()
    {
        var odd = Node(@"ROOT\HIDCLASS\0001", [@"vendor\newname"], "vmulti", "Some HID");
        var o = VMultiInspector.Build([odd, HidChild(1)], 0, null, null);
        Assert.False(o.Installed);
        Assert.Null(o.Details.MatchedBy);
        Assert.Empty(o.Details.Nodes);
        var near = Assert.Single(o.Details.NearMisses);
        Assert.Equal(@"ROOT\HIDCLASS\0001", near.InstanceId);
        Assert.Equal(1, o.Details.NearMissTotal);
    }

    [Theory]
    [InlineData("vmulti", null, null)]                      // by service
    [InlineData(null, "PenTablet virtual pen", null)]        // by description
    [InlineData(null, null, @"vendor\pentablet\other")]      // by a hardware ID that merely contains it
    public void NearMiss_IsFoundByServiceDescriptionOrHardwareId(string? service, string? description, string? hwId)
    {
        var n = Node(@"ROOT\X\0", [hwId ?? @"vendor\other"], service, description);
        Assert.Single(VMultiInspector.Build([n], 0, null, null).Details.NearMisses);
    }

    [Fact]
    public void UnrelatedDevices_AreNeitherNodesNorNearMisses()
    {
        var o = VMultiInspector.Build([Node(@"USB\VID_056A&PID_0357\1", [@"USB\VID_056A&PID_0357"], "usbccgp", "Wacom")], 0, null, null);
        Assert.False(o.Installed);
        Assert.Empty(o.Details.Nodes);
        Assert.Empty(o.Details.NearMisses);
    }

    [Fact]
    public void DriverlessLeftoversOnly_AreNotInstalled_ButAreListed()
    {
        // Code 28 (no driver) after an uninstall: present nodes under the upstream ID.
        var left = Node(@"ROOT\HIDCLASS\0002", [@"djpnewton\vmulti"], null, "VMulti", problem: 28);
        var o = VMultiInspector.Build([left], 0, null, null);
        Assert.False(o.Installed);
        Assert.Contains("leftover", o.Details.SetupApiVerdict);
        Assert.Single(o.Details.Nodes);
        Assert.Equal(@"hardware-id:djpnewton\vmulti", o.Details.MatchedBy);
    }

    [Fact]
    public void DisabledNode_IsInstalledButDisabled()
    {
        var o = VMultiInspector.Build([Node(@"ROOT\HIDCLASS\0000", [@"pentablet\hid"], "vmulti", problem: 0x16, enabled: false)], 0, null, null);
        Assert.True(o.Installed);
        Assert.Contains("disabled", o.Details.SetupApiVerdict);
    }

    [Fact]
    public void LongLists_AreCapped_AndTheRealCountIsKept()
    {
        var many = Enumerable.Range(0, 20).Select(i => Node($@"ROOT\X\{i}", [@"vendor\pentablet"], null)).ToList();
        var o = VMultiInspector.Build(many, 0, null, null);
        Assert.Equal(VMultiInspector.MaxListed, o.Details.NearMisses.Count);
        Assert.Equal(20, o.Details.NearMissTotal);
    }

    [Fact]
    public void StaleCount_PackageAndHid_ArePassedThrough()
    {
        var pkg = new VMultiDriverPackage("oem59.inf", "vmulti.inf", "Pentablet HID 1.1");
        var hid = new VMultiHidObservation(true, true, 5, null);
        var o = VMultiInspector.Build([Working()], 3, pkg, hid);
        Assert.Equal(3, o.Details.StaleNodeCount);
        Assert.Same(pkg, o.Details.DriverPackage);
        Assert.Same(hid, o.Details.Hid);
    }

    // ── report plumbing ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectorCarriesTheObservationOnTheProbe_AndTheVerdictIntoTheSnapshot()
    {
        var obs = VMultiInspector.Build([Working()], 0, null, null);
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.Windows,
            VMulti = _ => Task.FromResult(obs),
        }, options: new() { Coverage = [ProbeId.VMulti] }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Snapshot.VMultiInstalled);
        Assert.Same(obs.Details, report.Probes.Single(p => p.Id == ProbeId.VMulti).Details);
        Assert.True(report.IsComplete);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task ADetailsOnlyDifference_DoesNotChangeFindingsOrCompleteness()
    {
        var bare = VMultiDetectionTests.Observation(installed: false);
        var rich = bare with { Details = bare.Details with { Notes = ["x"], NearMissTotal = 5 } };
        async Task<HealthAnalysisReport> Run(VMultiObservation o) => await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.Windows, VMulti = _ => Task.FromResult(o),
        }, options: new() { Coverage = [ProbeId.VMulti] }, cancellationToken: TestContext.Current.CancellationToken);

        var a = await Run(bare);
        var b = await Run(rich);
        Assert.Equal(a.Findings.Select(f => f.Id), b.Findings.Select(f => f.Id));
        Assert.Equal(a.IsComplete, b.IsComplete);
    }

    [Fact]
    public void Details_RoundTripThroughJson_AsTheirOwnType()
    {
        var details = VMultiInspector.Build([Working()], 0, new("oem59.inf", "vmulti.inf", "P"), new(true, true, 5, null)).Details;
        var json = JsonSerializer.Serialize(new ProbeResult(ProbeId.VMulti, true, true, ProbeOutcome.Completed) { Details = details });
        Assert.Contains("\"kind\":\"vmulti\"", json);
        var back = JsonSerializer.Deserialize<ProbeResult>(json)!;
        var d = Assert.IsType<VMultiDetails>(back.Details);
        Assert.Equal(@"hardware-id:pentablet\hid", d.MatchedBy);
        Assert.Equal("ROOT\\HIDCLASS\\0000", d.Nodes.Single().InstanceId);
    }

    [Fact]
    public void ReportsSavedBeforeDetailsExisted_StillLoad()
    {
        const string old = """{"Id":"VMulti","Requested":true,"Applicable":true,"Outcome":"Completed","Failure":null}""";
        var back = JsonSerializer.Deserialize<ProbeResult>(old)!;
        Assert.Equal(ProbeId.VMulti, back.Id);
        Assert.Null(back.Details);
    }
}
