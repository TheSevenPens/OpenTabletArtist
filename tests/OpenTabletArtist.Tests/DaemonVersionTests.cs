using System;
using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

// Phase 4 (#140): reading the daemon version off its binary, with the sibling-.dll fallback a native
// apphost (macOS/Linux, no Win32 version resource) needs.
public class DaemonVersionTests : IDisposable
{
    private readonly string _dir;
    private readonly string _siblingDll;
    private readonly string _knownVersion;

    public DaemonVersionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ota-daemonver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // Any real managed assembly carries a version resource — stand in for the daemon's sibling .dll.
        var src = typeof(Newtonsoft.Json.JsonConvert).Assembly.Location;
        _siblingDll = Path.Combine(_dir, DaemonVersion.SiblingAssemblyName);
        File.Copy(src, _siblingDll);
        _knownVersion = DaemonVersion.Read(_siblingDll);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    [Fact]
    public void Read_FromManagedAssembly_ReturnsNonEmptyVersion()
    {
        Assert.False(string.IsNullOrEmpty(_knownVersion));
        Assert.DoesNotContain('+', _knownVersion);   // SemVer build metadata stripped
    }

    [Fact]
    public void Read_NativeApphostWithNoVersionResource_FallsBackToSiblingDll()
    {
        // A native apphost has no Win32 version resource → the direct read is empty; model it with a
        // non-PE file next to the sibling .dll. Read must return the sibling's version.
        var apphost = Path.Combine(_dir, "OpenTabletDriver.Daemon");
        File.WriteAllText(apphost, "not a PE with a version resource");

        Assert.Equal(_knownVersion, DaemonVersion.Read(apphost));
    }

    [Fact]
    public void Read_NoVersionAndNoSibling_ReturnsEmpty()
    {
        // A directory with the apphost but no sibling .dll → nothing to read.
        var sub = Path.Combine(_dir, "lonely");
        Directory.CreateDirectory(sub);
        var apphost = Path.Combine(sub, "OpenTabletDriver.Daemon");
        File.WriteAllText(apphost, "no version, no sibling");

        Assert.Equal("", DaemonVersion.Read(apphost));
    }
}
