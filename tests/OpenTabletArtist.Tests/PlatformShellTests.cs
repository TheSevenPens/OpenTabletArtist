using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What PlatformShell would launch, without launching it (#885, #140).
/// </summary>
///
/// <remarks>
/// <para>
/// These used to call the real thing. <c>RevealInFileManager</c> was handed a path that does not exist, on
/// the theory that a missing path degrades to a no-op — but Explorer answers an unparseable argument by
/// opening its default folder, so every suite run left a window on the developer's desktop. The assertion
/// was <c>Assert.Null(ex)</c>, which cannot tell a no-op from a window, so it passed throughout.
/// </para>
/// <para>
/// Launching nothing is the smaller half of the change. These tests can now say which executable was
/// chosen, that the path survived as one argument, and — the actual best-effort contract — that a launcher
/// which <b>throws</b> is swallowed. None of the three was reachable while the launch was real.
/// </para>
/// </remarks>
public class PlatformShellTests
{
    [Theory]
    [InlineData(true, false, "explorer.exe")]   // Windows
    [InlineData(false, true, "open")]           // macOS → Finder
    [InlineData(false, false, "xdg-open")]      // Linux
    public void FileManagerExe_PicksTheLauncherForTheOs(bool isWindows, bool isMacOS, string expected)
        => Assert.Equal(expected, PlatformShell.FileManagerExe(isWindows, isMacOS));

    /// <summary>
    /// The path reaches the launcher as one argument, exactly as given.
    /// </summary>
    /// <remarks>
    /// The spaces and the embedded quote are the point: the class builds its command through
    /// <see cref="ProcessStartInfo.ArgumentList"/> precisely so neither has to be escaped by hand, and
    /// nothing checked that until the launch became visible. A regression to a concatenated argument
    /// string would split this path into three and be invisible to the old test.
    /// </remarks>
    [Fact]
    public void Reveal_HandsTheLauncherThePathUnmangled()
    {
        var launched = new List<ProcessStartInfo>();
        const string path = "/definitely/not real/xyz\"zy";

        PlatformShell.Reveal(path, launched.Add);

        var psi = Assert.Single(launched);
        Assert.Equal(PlatformShell.FileManagerExe(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()),
                     psi.FileName);
        Assert.Equal(path, Assert.Single(psi.ArgumentList));
        Assert.Equal("", psi.Arguments);   // built as a list, never as a concatenated string
        Assert.False(psi.UseShellExecute); // the path is data, not something for the shell to interpret
    }

    /// <summary>A launcher that throws is swallowed — the best-effort contract, finally exercised.</summary>
    /// <remarks>
    /// This is what "best-effort" was always about: a desktop with no registered handler, where
    /// <see cref="Process.Start(ProcessStartInfo)"/> raises <see cref="System.ComponentModel.Win32Exception"/>.
    /// The old test could not produce that case, because the launcher it used succeeded.
    /// </remarks>
    [Fact]
    public void Reveal_SwallowsALauncherThatThrows()
    {
        var ex = Record.Exception(
            () => PlatformShell.Reveal("/some/folder", _ => throw new System.ComponentModel.Win32Exception()));

        Assert.Null(ex);
    }

    /// <summary>Each OS asks for its own display-settings pane, and none of them is launched here.</summary>
    [Theory]
    [InlineData(true, false, false, "ms-settings:display")]
    [InlineData(false, true, false, "open")]
    public void OpenDisplaySettings_AsksTheOsForItsDisplayPane(
        bool isWindows, bool isMacOS, bool isLinux, string expected)
    {
        var launched = new List<ProcessStartInfo>();

        PlatformShell.OpenDisplaySettings(isWindows, isMacOS, isLinux, desktop: null, launched.Add);

        Assert.Equal(expected, Assert.Single(launched).FileName);
    }

    /// <summary>
    /// On an OS with no pane of its own, nothing is launched at all.
    /// </summary>
    /// <remarks>
    /// The nit left on the original test was to lock this down; it could not be, because the only
    /// observable was whether an exception escaped, and a no-op and a launch look identical through that.
    /// </remarks>
    [Fact]
    public void OpenDisplaySettings_LaunchesNothingOnAnOsWithoutOne()
    {
        var launched = new List<ProcessStartInfo>();

        PlatformShell.OpenDisplaySettings(
            isWindows: false, isMacOS: false, isLinux: false, desktop: null, start: launched.Add);

        Assert.Empty(launched);
    }

    /// <summary>The Linux pane follows the desktop environment, and falls back rather than giving up.</summary>
    [Theory]
    [InlineData("GNOME", "gnome-control-center")]
    [InlineData("ubuntu:GNOME", "gnome-control-center")]   // colon-separated, as GNOME sessions report it
    [InlineData("gnome", "gnome-control-center")]          // matched case-insensitively
    [InlineData("KDE", "systemsettings")]
    [InlineData("XFCE", "xdg-open")]                       // no pane of its own, so the generic handler
    [InlineData("", "xdg-open")]
    [InlineData(null, "xdg-open")]                         // a login that reports no desktop at all
    public void OpenDisplaySettings_OnLinux_PicksThePaneForTheDesktop(string? desktop, string expected)
    {
        var launched = new List<ProcessStartInfo>();

        PlatformShell.OpenDisplaySettings(
            isWindows: false, isMacOS: false, isLinux: true, desktop: desktop, start: launched.Add);

        Assert.Equal(expected, Assert.Single(launched).FileName);
    }
}
