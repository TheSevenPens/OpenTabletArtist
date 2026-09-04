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

    // PluginDll (#plugin-version): the list used to read whichever DLL came first, which reported the
    // Windows Ink plugin as 13.0.0.0 — Newtonsoft.Json's version, since it sorts ahead of WindowsInk.dll.

    [Fact]
    public void PluginDll_PicksTheOneNamedAfterTheFolder_NotTheFirst()
    {
        // The real Windows Ink folder, in the order the filesystem returns it.
        var dlls = new[]
        {
            @"C:\p\Windows Ink\Newtonsoft.Json.dll",
            @"C:\p\Windows Ink\VMulti.dll",
            @"C:\p\Windows Ink\VoiD.dll",
            @"C:\p\Windows Ink\WindowsInk.dll",
        };

        Assert.Equal(@"C:\p\Windows Ink\WindowsInk.dll", PluginInventory.PluginDll("Windows Ink", dlls));
    }

    [Theory]
    [InlineData("Windows Ink", "WindowsInk")]                               // folder has a space an assembly name cannot
    [InlineData("windows ink", "WindowsInk")]                               // case differs
    [InlineData("OpenTabletArtist.Dynamics", "OpenTabletArtist.Dynamics")]  // punctuation on both sides
    public void PluginDll_IgnoresCaseAndPunctuationWhenMatching(string folder, string dllBaseName)
    {
        var dlls = new[] { @"C:\p\zzz-other.dll", $@"C:\p\{dllBaseName}.dll" };

        Assert.Equal($@"C:\p\{dllBaseName}.dll", PluginInventory.PluginDll(folder, dlls));
    }

    [Fact]
    public void PluginDll_FallsBackToTheFirst_WhenNothingMatchesTheFolder()
    {
        var dlls = new[] { @"C:\p\Alpha.dll", @"C:\p\Beta.dll" };

        Assert.Equal(@"C:\p\Alpha.dll", PluginInventory.PluginDll("Something Else", dlls));
    }

    [Fact]
    public void PluginDll_ReturnsNull_ForAnEmptyFolder()
    {
        Assert.Null(PluginInventory.PluginDll("Windows Ink", System.Array.Empty<string>()));
    }
}
