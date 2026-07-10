using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

// Phase 5 (#140): the OS→file-manager launcher behind PlatformShell.RevealInFileManager, which replaced
// the bare, unguarded Process.Start("explorer.exe", …) calls that threw on macOS. Paths go through
// ProcessStartInfo.ArgumentList (framework-escaped), so there's no manual quoting to test.
public class PlatformShellTests
{
    [Theory]
    [InlineData(true, false, "explorer.exe")]   // Windows
    [InlineData(false, true, "open")]           // macOS → Finder
    [InlineData(false, false, "xdg-open")]      // Linux
    public void FileManagerExe_PicksTheLauncherForTheOs(bool isWindows, bool isMacOS, string expected)
        => Assert.Equal(expected, PlatformShell.FileManagerExe(isWindows, isMacOS));

    [Fact]
    public void RevealInFileManager_NeverThrows_OnBogusPath()
    {
        // Best-effort contract: a non-existent path (or no handler) must degrade to a no-op, never throw.
        // Path deliberately contains spaces and a quote to exercise ArgumentList escaping.
        var ex = Record.Exception(() => PlatformShell.RevealInFileManager("/definitely/not real/xyz\"zy"));
        Assert.Null(ex);
    }

    [Fact]
    public void OpenDisplaySettings_NeverThrows()
    {
        // No-op on Linux CI, real launch elsewhere — either way it must not throw (nit: lock the no-op path).
        var ex = Record.Exception(PlatformShell.OpenDisplaySettings);
        Assert.Null(ex);
    }
}
