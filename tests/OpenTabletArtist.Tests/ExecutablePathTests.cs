using System;
using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ExecutablePathTests
{
    // Build an absolute path rooted for the current OS, so Path.GetFullPath normalizes it the same way it
    // does in production. Windows-literal paths (C:\a\b) don't normalize on macOS/Linux — '\' isn't a path
    // separator there, so "..\" segments are never collapsed and the case tests read differently. Rooting
    // per-OS keeps the suite meaningful on the Linux CI lane (#140).
    private static string Abs(params string[] segments)
    {
        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        return root + string.Join(Path.DirectorySeparatorChar, segments);
    }

    [Fact]
    public void IdenticalPaths_AreSame()
        => Assert.True(ExecutablePath.SameFile(Abs("a", "b", "daemon"), Abs("a", "b", "daemon")));

    [Fact]
    public void CaseDifferentPaths_AreSame()
        => Assert.True(ExecutablePath.SameFile(Abs("A", "B", "Daemon"), Abs("a", "b", "daemon")));

    [Fact]
    public void NormalizedRelativeSegments_AreSame()
        => Assert.True(ExecutablePath.SameFile(Abs("a", "x", "..", "b", "daemon"), Abs("a", "b", "daemon")));

    [Fact]
    public void DifferentPaths_AreNotSame()
        => Assert.False(ExecutablePath.SameFile(Abs("a", "b", "daemon"), Abs("other", "daemon")));

    [Fact]
    public void NullOrEmpty_IsNotSame()
    {
        var real = Abs("a", "daemon");
        Assert.False(ExecutablePath.SameFile(null, real));
        Assert.False(ExecutablePath.SameFile(real, null));
        Assert.False(ExecutablePath.SameFile("", real));
        Assert.False(ExecutablePath.SameFile(real, ""));
        Assert.False(ExecutablePath.SameFile(null, null));
    }
}
