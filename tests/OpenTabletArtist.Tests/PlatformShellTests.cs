using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What PlatformShell would launch, without launching it (#887, #885, #140).
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
/// What is covered here is the request that gets built and what happens when a launch fails. The two
/// public wrappers are not: they bind the real OS, environment and launcher, and covering them would mean
/// launching something. That boundary is stated on the class itself rather than left to be inferred from
/// which tests happen to exist.
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

    // --- revealing a folder ----------------------------------------------------------------------

    /// <summary>
    /// The path reaches the launcher as one argument, exactly as given.
    /// </summary>
    /// <remarks>
    /// The spaces are the point: the class builds its command through
    /// <see cref="ProcessStartInfo.ArgumentList"/> precisely so they need no hand-escaping, and nothing
    /// checked that until the launch became visible. A regression to a concatenated argument string would
    /// split this path in three.
    /// <para>
    /// This used to embed a quote as well. It cannot now: the folder has to exist before anything is
    /// launched, and Windows will not create a name containing one. Spaces are the case that actually
    /// occurs — <c>C:\Program Files\…</c> — and the quote was always synthetic.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reveal_HandsTheLauncherThePathUnmangled()
    {
        using var folder = new TempFolder("a folder with spaces");
        var launched = new List<ProcessStartInfo>();

        PlatformShell.Reveal(folder.Path, launched.Add);

        var psi = Assert.Single(launched);
        Assert.Equal(PlatformShell.FileManagerExe(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()),
                     psi.FileName);
        Assert.Equal(folder.Path, Assert.Single(psi.ArgumentList));
        Assert.Equal("", psi.Arguments);   // built as a list, never as a concatenated string
        Assert.False(psi.UseShellExecute); // the path is data, not something for the shell to interpret
    }

    /// <summary>
    /// A folder that is not there launches nothing at all.
    /// </summary>
    /// <remarks>
    /// This is #885 itself, at the layer that can finally see it. Explorer treats an argument it cannot
    /// resolve as "open my default folder", so on Windows the old behaviour was not a no-op but a window
    /// pointed somewhere nobody asked for. Asserting that nothing was launched is the only form of this
    /// assertion that can tell those two apart.
    /// </remarks>
    [Fact]
    public void Reveal_LaunchesNothingWhenTheFolderIsGone()
    {
        var launched = new List<ProcessStartInfo>();
        var gone = Path.Combine(Path.GetTempPath(), $"ota-gone-{Guid.NewGuid():N}");

        PlatformShell.Reveal(gone, launched.Add);

        Assert.Empty(launched);
    }

    /// <summary>Nor does a caller with no path at all launch anything.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reveal_LaunchesNothingWithoutAPath(string? path)
    {
        var launched = new List<ProcessStartInfo>();

        PlatformShell.Reveal(path, launched.Add);

        Assert.Empty(launched);
    }

    /// <summary>A launcher that throws is swallowed — the best-effort contract, finally exercised.</summary>
    /// <remarks>
    /// The folder is real on purpose. Against one that is not there the launcher is never called, and this
    /// test would pass without ever reaching the catch it exists for — so it asserts that it was called.
    /// <para>
    /// The case is a desktop with no registered handler, where <see cref="Process.Start(ProcessStartInfo)"/>
    /// raises <see cref="System.ComponentModel.Win32Exception"/>. The old test could not produce it,
    /// because the launcher it used succeeded.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reveal_SwallowsALauncherThatThrows()
    {
        using var folder = new TempFolder("real so the launch is attempted");
        var reached = false;

        var ex = Record.Exception(() => PlatformShell.Reveal(folder.Path, _ =>
        {
            reached = true;
            throw new System.ComponentModel.Win32Exception();
        }));

        Assert.Null(ex);
        Assert.True(reached, "the launcher was never called, so the catch was never exercised");
    }

    /// <summary>
    /// A failed launch leaves one line in the log, naming what was asked for.
    /// </summary>
    /// <remarks>
    /// The point of keeping the catch broad (#887) was that a silent swallow is the #718 pattern: the call
    /// "succeeds" and the evidence is gone. That only holds if something actually writes, so this asserts
    /// the line rather than trusting it — deleting the AppLog call breaks this test and nothing else.
    /// <para>
    /// Matched on the folder's own name because the suite runs in parallel and other lines land in the
    /// same log; the GUID in it makes the match unambiguous.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reveal_LeavesOneLineBehindWhenTheLaunchFails()
    {
        using var folder = new TempFolder("a folder worth naming");
        var lines = new List<string>();
        void Capture(string line) => lines.Add(line);

        AppLog.LineWritten += Capture;
        try
        {
            PlatformShell.Reveal(folder.Path, _ => throw new System.ComponentModel.Win32Exception());
        }
        finally
        {
            AppLog.LineWritten -= Capture;
        }

        var line = Assert.Single(lines, l => l.Contains(folder.Path, StringComparison.Ordinal));
        Assert.Contains("[WARNING]", line, StringComparison.Ordinal);
        Assert.Contains(                                  // which launcher was asked
            PlatformShell.FileManagerExe(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()),
            line,
            StringComparison.Ordinal);
        Assert.Contains("Win32Exception", line, StringComparison.Ordinal);   // and why it failed
    }

    // --- opening the display-settings pane -------------------------------------------------------

    /// <summary>
    /// Windows asks the shell for the <c>ms-settings:</c> URI.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessStartInfo.UseShellExecute"/> is asserted because it is load-bearing rather than
    /// incidental: a <c>ms-settings:</c> URI reaches the Settings app through its registered protocol
    /// handler, and with it false there is no executable of that name to run. Pinning only the file name
    /// would let that regress silently (#887).
    /// </remarks>
    [Fact]
    public void OpenDisplaySettings_OnWindows_AsksTheShellForTheSettingsUri()
    {
        var launched = new List<ProcessStartInfo>();

        PlatformShell.OpenDisplaySettings(
            isWindows: true, isMacOS: false, isLinux: false, desktop: null, start: launched.Add);

        var psi = Assert.Single(launched);
        Assert.Equal("ms-settings:display", psi.FileName);
        Assert.True(psi.UseShellExecute, "a ms-settings: URI only opens through its protocol handler");
    }

    /// <summary>macOS asks <c>open</c> for the Displays pane, as an argument rather than through the shell.</summary>
    [Fact]
    public void OpenDisplaySettings_OnMacOs_AsksOpenForTheDisplaysPane()
    {
        var launched = new List<ProcessStartInfo>();

        PlatformShell.OpenDisplaySettings(
            isWindows: false, isMacOS: true, isLinux: false, desktop: null, start: launched.Add);

        var psi = Assert.Single(launched);
        Assert.Equal("open", psi.FileName);
        Assert.Equal(
            "x-apple.systempreferences:com.apple.preference.displays", Assert.Single(psi.ArgumentList));
        Assert.False(psi.UseShellExecute);
    }

    /// <summary>
    /// The Windows branch swallows a throwing launcher too.
    /// </summary>
    /// <remarks>
    /// Worth its own test because that branch hands the launcher a <see cref="ProcessStartInfo"/> it built
    /// itself rather than going through the argument-list helper, so it reaches the catch by a different
    /// route (#887).
    /// </remarks>
    [Fact]
    public void OpenDisplaySettings_OnWindows_SwallowsALauncherThatThrows()
    {
        var reached = false;

        var ex = Record.Exception(() => PlatformShell.OpenDisplaySettings(
            isWindows: true, isMacOS: false, isLinux: false, desktop: null,
            start: _ => { reached = true; throw new System.ComponentModel.Win32Exception(); }));

        Assert.Null(ex);
        Assert.True(reached, "the launcher was never called, so the catch was never exercised");
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

    // --- harness ---------------------------------------------------------------------------------

    /// <summary>A real directory for the length of one test, since the helper now refuses absent ones.</summary>
    private sealed class TempFolder : IDisposable
    {
        private readonly string _root;

        public TempFolder(string name)
        {
            _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ota-shell-{Guid.NewGuid():N}");
            Path = System.IO.Path.Combine(_root, name);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { /* a temp folder that outlives the test is not worth failing it over */ }
        }
    }
}
