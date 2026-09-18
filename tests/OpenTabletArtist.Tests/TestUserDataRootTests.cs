using System;
using System.IO;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Guards the redirect in <see cref="TestUserDataRoot"/> (#738). Without these, the redirect could stop
/// working — an upstream change to how OTD resolves its roots, a stray early <c>AppInfo</c> touch — and
/// nothing would notice, because the tests it protects pass either way on a machine that has OTD
/// installed. They only fail where the real directories are unreachable, which is the wrong place and
/// the wrong time to find out.
/// </summary>
public class TestUserDataRootTests
{
    [Fact]
    public void TheRedirectRan()
    {
        Assert.NotEqual("", TestUserDataRoot.Path);
        Assert.True(Directory.Exists(TestUserDataRoot.Path));
    }

    [Fact]
    public void OtdResolvesItsAppDataUnderTheTestRoot()
    {
        Assert.StartsWith(TestUserDataRoot.Path, AppInfo.Current.AppDataDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The directory whose creation was the actual damage: <c>DesktopPluginManager</c>'s
    /// constructor creates it, and constructing any <c>PluginSettingStore</c> reaches that manager.</summary>
    [Fact]
    public void OtdResolvesItsPluginDirectoryUnderTheTestRoot()
    {
        Assert.StartsWith(TestUserDataRoot.Path, AppInfo.Current.PluginDirectory,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(TestUserDataRoot.Path, AppInfo.PluginManager.PluginDirectory.FullName,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtdResolvesItsSettingsFileUnderTheTestRoot()
    {
        Assert.StartsWith(TestUserDataRoot.Path, AppInfo.Current.SettingsFile,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The specific call that used to reach the developer's install: building a binding store
    /// triggers the plugin manager's static initialization.</summary>
    [Fact]
    public void BuildingAPluginSettingStore_TouchesNothingOutsideTheTestRoot()
    {
        _ = new PluginSettingStore("SomePlugin.SomeType");

        Assert.StartsWith(TestUserDataRoot.Path, AppInfo.PluginManager.PluginDirectory.FullName,
            StringComparison.OrdinalIgnoreCase);
    }
}
