using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class PluginInventoryTests
{
    [Theory]
    [InlineData("OtdArtist.Dynamics", "OtdArtist.Dynamics.DynamicsFilter", true)]
    [InlineData("VoiDPlugins", "VoiDPlugins.OutputMode.WinInkAbsoluteMode", true)]
    [InlineData("OtdArtist.Dynamics", "OtdArtist.Dynamics", true)]   // exact
    [InlineData("OtdArtist.Dynamics", "OtdArtist.DynamicsX.Foo", false)] // not a namespace boundary
    [InlineData("OtdArtist.Dynamics", "Other.Plugin.Type", false)]
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
