using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The two pure decisions behind the "your desktop won't show a tray icon" health hint (#610): is this a
/// GNOME session, and did the StatusNotifierWatcher probe find a host. The probe itself shells out to
/// <c>gdbus</c> and is Linux-gated, so only its inputs and its reading are covered here.
/// <para>
/// Both lean the same way on purpose. The hint is advisory, so every ambiguous case has to resolve to
/// "don't hint" — a nag shown on a desktop that renders the tray perfectly well is worse than a missing
/// hint on one that doesn't.
/// </para>
/// </summary>
public class DesktopTrayEnvironmentTests
{
    [Theory]
    // The case the source comment calls out: XDG_CURRENT_DESKTOP is often colon-separated.
    [InlineData("ubuntu:GNOME", null, null)]
    [InlineData("GNOME", null, null)]
    [InlineData("gnome", null, null)]                 // matched case-insensitively
    [InlineData("GNOME-Classic:GNOME", null, null)]
    [InlineData(null, "gnome-xorg", null)]            // falls back when the primary var is unset
    [InlineData(null, null, "gnome")]
    public void IsGnomeDesktop_RecognisesGnome(string? current, string? session, string? desktop)
        => Assert.True(DesktopTrayEnvironment.IsGnomeDesktop(current, session, desktop));

    [Theory]
    [InlineData(null, null, null)]                    // nothing set — a login that reports no desktop
    [InlineData("", "", "")]
    [InlineData("KDE", null, null)]
    [InlineData("XFCE", "xfce", "xfce")]
    [InlineData("sway", null, null)]
    public void IsGnomeDesktop_LeavesOtherDesktopsAlone(string? current, string? session, string? desktop)
        => Assert.False(DesktopTrayEnvironment.IsGnomeDesktop(current, session, desktop));

    [Fact]
    public void HostPresentFromProbe_ReadsTheGdbusAnswer()
    {
        // gdbus prints these two forms for NameHasOwner.
        Assert.True(DesktopTrayEnvironment.HostPresentFromProbe(0, "(true,)\n"));
        Assert.False(DesktopTrayEnvironment.HostPresentFromProbe(0, "(false,)\n"));
    }

    [Theory]
    [InlineData(1, "")]                               // gdbus failed
    [InlineData(1, "(false,)\n")]                     // ...even when it printed an answer first
    [InlineData(127, "command not found")]            // gdbus isn't installed
    [InlineData(0, "something unexpected")]           // output we can't read
    public void HostPresentFromProbe_AssumesAHostWheneverItCannotTell(int exitCode, string output)
        => Assert.True(DesktopTrayEnvironment.HostPresentFromProbe(exitCode, output));

    /// <summary>"(false,)" must not be read as containing "true". Obvious, and precisely the kind of
    /// substring check that a later rewrite gets wrong.</summary>
    [Fact]
    public void HostPresentFromProbe_DoesNotSeeTrueInsideFalse()
        => Assert.False(DesktopTrayEnvironment.HostPresentFromProbe(0, "(false,)"));

    /// <summary>Off Linux the tray is rendered natively, so the hint never applies whatever the probe
    /// would have said. Guards the early return in Probe().</summary>
    [Fact]
    public void TrayHostUnavailable_IsFalseOffLinux()
    {
        if (OperatingSystem.IsLinux()) return; // the Linux answer depends on the live session
        Assert.False(DesktopTrayEnvironment.TrayHostUnavailable);
    }
}
