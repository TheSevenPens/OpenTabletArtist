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

    /// <summary>
    /// A Unix domain socket path is capped at 108 bytes, and <c>SingleInstance</c> binds one directly in
    /// XDG_RUNTIME_DIR under a name of roughly 78 characters. Nesting that directory under the
    /// descriptive test root pushed the full path past the limit, the bind failed silently, and the
    /// activation test sat out a 30-second timeout on the Linux lane. Keep the headroom.
    ///
    /// Linux-only: that is the one platform where this socket is bound.
    /// </summary>
    [Fact]
    public void TheRuntimeDirectoryIsShortEnoughForAUnixSocket()
    {
        Assert.NotEqual("", TestUserDataRoot.RuntimePath);
        Assert.True(Directory.Exists(TestUserDataRoot.RuntimePath));

        // Linux only, matching where the socket is actually bound: SingleInstance takes its Unix-socket
        // branch under OperatingSystem.IsLinux(), Windows uses a named event, and macOS takes neither.
        // Scoped to "Unix" instead, this failed on macOS — whose temp root is ~50 characters — for a
        // limit that platform never reaches.
        if (!OperatingSystem.IsLinux()) return;

        // What SingleInstance actually binds: "OpenTabletArtist.SingleInstance.Show" + the instance key
        // + ".sock", in this directory. Measured against the real limit rather than a round number, so
        // the failure message says how much room is left.
        const int SunPathLimit = 108;
        var socketName = "OpenTabletArtist.SingleInstance.Show" + "Test-" + new string('0', 32) + ".sock";
        var fullLength = TestUserDataRoot.RuntimePath.Length + 1 + socketName.Length;

        Assert.True(fullLength < SunPathLimit,
            $"The activation socket path would be {fullLength} bytes, over the {SunPathLimit}-byte limit. "
            + $"XDG_RUNTIME_DIR is {TestUserDataRoot.RuntimePath}");
    }

    [Fact]
    public void TheXdgRootsExist_NotJustNamed()
    {
        foreach (var variable in new[] { "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_RUNTIME_DIR" })
        {
            var dir = Environment.GetEnvironmentVariable(variable);
            Assert.False(string.IsNullOrEmpty(dir), $"{variable} is unset.");
            Assert.True(Directory.Exists(dir!), $"{variable} points at {dir}, which does not exist.");
        }
    }
}