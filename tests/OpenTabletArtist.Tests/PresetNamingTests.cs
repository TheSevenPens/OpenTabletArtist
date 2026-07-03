using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PresetNamingTests
{
    [Fact]
    public void Empty_ReturnsBaseName()
    {
        Assert.Equal("Profile", PresetNaming.NextSnapshotName([]));
    }

    [Fact]
    public void BaseTaken_ReturnsProfile2()
    {
        Assert.Equal("Profile 2", PresetNaming.NextSnapshotName(["Profile"]));
    }

    [Fact]
    public void SequentialNames_ReturnNextNumber()
    {
        Assert.Equal("Profile 3", PresetNaming.NextSnapshotName(["Profile", "Profile 2"]));
    }

    [Fact]
    public void LowestGap_IsReused()
    {
        // "Profile 2" is free even though "Profile 3" exists.
        Assert.Equal("Profile 2", PresetNaming.NextSnapshotName(["Profile", "Profile 3"]));
    }

    [Fact]
    public void Comparison_IsCaseInsensitive()
    {
        Assert.Equal("Profile 2", PresetNaming.NextSnapshotName(["profile"]));
    }

    [Fact]
    public void UnrelatedNames_AreIgnored()
    {
        Assert.Equal("Profile", PresetNaming.NextSnapshotName(["Foo", "Bar", "My Preset"]));
    }
}
