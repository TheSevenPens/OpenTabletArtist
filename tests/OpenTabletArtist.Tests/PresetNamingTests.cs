using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PresetNamingTests
{
    [Fact]
    public void Empty_ReturnsBaseName()
    {
        Assert.Equal("Preset", PresetNaming.NextSnapshotName([]));
    }

    [Fact]
    public void BaseTaken_ReturnsPreset2()
    {
        Assert.Equal("Preset 2", PresetNaming.NextSnapshotName(["Preset"]));
    }

    [Fact]
    public void SequentialNames_ReturnNextNumber()
    {
        Assert.Equal("Preset 3", PresetNaming.NextSnapshotName(["Preset", "Preset 2"]));
    }

    [Fact]
    public void LowestGap_IsReused()
    {
        // "Preset 2" is free even though "Preset 3" exists.
        Assert.Equal("Preset 2", PresetNaming.NextSnapshotName(["Preset", "Preset 3"]));
    }

    [Fact]
    public void Comparison_IsCaseInsensitive()
    {
        Assert.Equal("Preset 2", PresetNaming.NextSnapshotName(["preset"]));
    }

    [Fact]
    public void UnrelatedNames_AreIgnored()
    {
        Assert.Equal("Preset", PresetNaming.NextSnapshotName(["Foo", "Bar", "My Preset"]));
    }
}
