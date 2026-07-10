using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

// Phase 5 (#140): the OS→file-manager command mapping behind PlatformShell.RevealInFileManager, which
// replaced the bare, unguarded Process.Start("explorer.exe", …) calls that threw on macOS.
public class PlatformShellTests
{
    [Fact]
    public void FileManagerCommand_Windows_UsesExplorer()
    {
        var (exe, args) = PlatformShell.FileManagerCommand(isWindows: true, isMacOS: false, @"C:\some path");
        Assert.Equal("explorer.exe", exe);
        Assert.Equal("\"C:\\some path\"", args);   // quoted so spaces are safe
    }

    [Fact]
    public void FileManagerCommand_MacOS_UsesOpen()
    {
        var (exe, args) = PlatformShell.FileManagerCommand(isWindows: false, isMacOS: true, "/Users/x/Library/App Support");
        Assert.Equal("open", exe);
        Assert.Equal("\"/Users/x/Library/App Support\"", args);
    }

    [Fact]
    public void FileManagerCommand_Linux_UsesXdgOpen()
    {
        var (exe, _) = PlatformShell.FileManagerCommand(isWindows: false, isMacOS: false, "/home/x/.config");
        Assert.Equal("xdg-open", exe);
    }

    [Fact]
    public void RevealInFileManager_NeverThrows_OnBogusPath()
    {
        // Best-effort contract: a non-existent path (or no handler) must degrade to a no-op, never throw.
        var ex = Record.Exception(() => PlatformShell.RevealInFileManager("/definitely/not/a/real/path/xyzzy"));
        Assert.Null(ex);
    }
}
