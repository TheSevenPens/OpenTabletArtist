using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PluginInventoryTests
{
    [Theory]
    [InlineData("OpenTabletArtist.Dynamics", "OpenTabletArtist.Dynamics.DynamicsFilter", true)]
    [InlineData("VoiDPlugins", "VoiDPlugins.OutputMode.WinInkAbsoluteMode", true)]
    [InlineData("OpenTabletArtist.Dynamics", "OpenTabletArtist.Dynamics", true)]   // exact
    [InlineData("OpenTabletArtist.Dynamics", "OpenTabletArtist.DynamicsX.Foo", false)] // not a namespace boundary
    [InlineData("OpenTabletArtist.Dynamics", "Other.Plugin.Type", false)]
    [InlineData("", "Anything", false)]
    public void PathBelongsToAssembly(string asm, string path, bool expected)
        => Assert.Equal(expected, PluginInventory.PathBelongsToAssembly(asm, path));

    [Fact]
    public void Status_ReflectsActive()
    {
        Assert.Equal("Active", new PluginInfo("X", "1.0", true).Status);
        Assert.Equal("Installed", new PluginInfo("X", "1.0", false).Status);
    }
}
