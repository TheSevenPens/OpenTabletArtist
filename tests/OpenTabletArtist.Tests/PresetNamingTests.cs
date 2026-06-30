using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PresetNamingTests
{
    [Fact]
    public void Empty_ReturnsBaseName()
    {
        Assert.Equal("Snapshot", PresetNaming.NextSnapshotName([]));
    }

    [Fact]
    public void BaseTaken_ReturnsSnapshot2()
    {
        Assert.Equal("Snapshot 2", PresetNaming.NextSnapshotName(["Snapshot"]));
    }

    [Fact]
    public void SequentialNames_ReturnNextNumber()
    {
        Assert.Equal("Snapshot 3", PresetNaming.NextSnapshotName(["Snapshot", "Snapshot 2"]));
    }

    [Fact]
    public void LowestGap_IsReused()
    {
        // "Snapshot 2" is free even though "Snapshot 3" exists.
        Assert.Equal("Snapshot 2", PresetNaming.NextSnapshotName(["Snapshot", "Snapshot 3"]));
    }

    [Fact]
    public void Comparison_IsCaseInsensitive()
    {
        Assert.Equal("Snapshot 2", PresetNaming.NextSnapshotName(["snapshot"]));
    }

    [Fact]
    public void UnrelatedNames_AreIgnored()
    {
        Assert.Equal("Snapshot", PresetNaming.NextSnapshotName(["Foo", "Bar", "My Preset"]));
    }
}
