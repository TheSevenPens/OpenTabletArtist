using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The freedesktop <c>.desktop</c> text OTA writes so it appears in the Linux application menu (#610).
/// Only the string build is covered — <see cref="DesktopEntry.TryCreate"/> writes files, shells out to
/// <c>update-desktop-database</c> and is Linux-gated, none of which a unit test should reach.
/// <para>
/// These pin the choices the format actually depends on. A malformed entry doesn't throw; it just quietly
/// fails to appear in the launcher, or appears twice, which is exactly the sort of thing that regresses
/// unnoticed.
/// </para>
/// </summary>
public class DesktopEntryTests
{
    private const string Exe = "/home/artist/apps/OpenTabletArtist/OpenTabletArtist";

    [Fact]
    public void BuildEntry_QuotesExec_SoAPathWithSpacesStaysOneArgument()
    {
        var spacey = "/home/an artist/My Apps/OpenTabletArtist";

        var entry = DesktopEntry.BuildEntry(spacey, icon: null);

        // The spec allows a whole argument to be double-quoted; without this a build-output path with a
        // space in it launches as two arguments and the entry silently does nothing.
        Assert.Contains($"Exec=\"{spacey}\"\n", entry);
    }

    [Fact]
    public void BuildEntry_OmitsIconEntirely_WhenThereIsNoIcon()
    {
        var entry = DesktopEntry.BuildEntry(Exe, icon: null);

        // Not "Icon=" with an empty value — a blank Icon key is a broken reference, whereas no key at all
        // just means no custom icon. Icon extraction is best-effort, so this path is reachable.
        Assert.DoesNotContain("Icon=", entry);
    }

    [Fact]
    public void BuildEntry_IncludesIcon_WhenOneWasExtracted()
    {
        var entry = DesktopEntry.BuildEntry(Exe, "/home/artist/.local/share/icons/opentabletartist.png");

        Assert.Contains("Icon=/home/artist/.local/share/icons/opentabletartist.png\n", entry);
    }

    /// <summary>Two categories can make the app appear twice in some menus, which is why the source picks
    /// exactly one. Easy to "helpfully" widen later; this says not to.</summary>
    [Fact]
    public void BuildEntry_ListsASingleCategory()
    {
        var entry = DesktopEntry.BuildEntry(Exe, icon: null);

        Assert.Contains("Categories=Graphics;\n", entry);
    }

    /// <summary>Without StartupWMClass the running window isn't associated with the entry, so the launcher
    /// shows a second, generic icon beside the pinned one.</summary>
    [Fact]
    public void BuildEntry_SetsStartupWmClass()
    {
        var entry = DesktopEntry.BuildEntry(Exe, icon: null);

        Assert.Contains("StartupWMClass=OpenTabletArtist\n", entry);
    }

    [Fact]
    public void BuildEntry_StartsWithTheRequiredHeaderAndType()
    {
        var entry = DesktopEntry.BuildEntry(Exe, icon: null);

        Assert.StartsWith("[Desktop Entry]\n", entry);
        Assert.Contains("Type=Application\n", entry);
        Assert.Contains("Name=OpenTabletArtist\n", entry);
        Assert.Contains("Terminal=false\n", entry);
    }

    /// <summary>The file is written verbatim by File.WriteAllText, so the separators here are what lands on
    /// disk. A CRLF would come from someone reaching for Environment.NewLine or a raw string literal on a
    /// Windows dev box — and this is a Linux file format.</summary>
    [Fact]
    public void BuildEntry_UsesUnixLineEndings()
    {
        var entry = DesktopEntry.BuildEntry(Exe, icon: null);

        Assert.DoesNotContain("\r", entry);
    }
}
