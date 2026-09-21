using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The footer's one settings button, and the half of it that may go dark (#footer-split).
/// </summary>
///
/// <remarks>
/// <para>
/// Save, Reload and Revert were three buttons of equal weight. Save is the one an artist wants almost
/// every time and the only one with a shortcut; the other two are occasional, and the rarest of the
/// three read first. One SplitButton with Save as its default action says that, and the chevron keeps
/// the others a click away.
/// </para>
/// <para>
/// What that costs is the thing worth guarding. Save and Revert need an editable session; <b>Reload
/// does not</b>, and a paused session is exactly when it is needed, because it is the way out.
/// Disabling the SplitButton takes the chevron with it and strands the artist on a page they cannot
/// leave — and so does letting its command report that it cannot execute, which Avalonia 12 treats the
/// same way. I checked both rather than assuming. So the enabled state lives on the primary half alone.
/// </para>
/// <para>
/// <b>This reads the markup, not the running control</b>, and that is a weaker thing. The real check
/// would build the window and look at what is enabled, but <c>MainWindow</c> constructs a live
/// <c>MainViewModel</c> — daemon connection loop and all — and hangs a headless test; the view-load
/// suite covers <c>UserControl</c>s for that reason and this is a <c>Window</c>. Making the footer its
/// own view would fix that properly and is more than this change should carry. Until then: the trap is
/// one edit away and silent, so a guard that can at least see that edit is worth more than the comment
/// alone.
/// </para>
/// </remarks>
public class FooterActionsTests
{
    private static string Markup([CallerFilePath] string thisFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..",
            "OpenTabletArtist", "MainWindow.axaml"));

    /// <summary>The element itself follows only the busy gate, so the chevron survives a pause.</summary>
    [Fact]
    public void TheSettingsSplitButtonIsNotDisabledByAPause()
    {
        var split = Element(Markup(), "SplitButton");

        Assert.Contains("SettingsBusy", split);
        Assert.DoesNotContain("IsEnabled=\"{Binding SettingsSession.CanEditSettings}\"", split);
    }

    /// <summary>And the primary half does follow it, so Save still reads as unavailable.</summary>
    [Fact]
    public void ThePrimaryHalfFollowsWhetherSettingsCanBeEdited()
    {
        var markup = Markup();

        Assert.Matches(new Regex(
            @"Selector=""SplitButton\.settingsActions /template/ Button#PART_PrimaryButton""\s*>\s*"
            + @"<Setter Property=""IsEnabled"" Value=""\{Binding SettingsSession\.CanEditSettings\}"" */>",
            RegexOptions.Singleline), markup);
    }

    /// <summary>All three actions are in the menu, Save among them.</summary>
    /// <remarks>
    /// Promoting Save to the default must not drop it from the list. Someone opening the chevron is
    /// looking for the whole set, and a default action missing from its own menu reads as a mistake.
    /// </remarks>
    [Fact]
    public void TheMenuListsAllThreeActions()
    {
        var menu = Section(Markup(), "<MenuFlyout", "</MenuFlyout>");

        Assert.Contains(@"Header=""Save""", menu);
        Assert.Contains(@"Header=""Reload driver settings""", menu);
        Assert.Contains(@"Header=""Revert to saved""", menu);
    }

    /// <summary>The single element opening with <paramref name="tag"/>, up to its first '&gt;'.</summary>
    private static string Element(string markup, string tag)
    {
        var start = markup.IndexOf("<" + tag, System.StringComparison.Ordinal);
        Assert.True(start >= 0, $"No <{tag} in MainWindow.axaml — the footer was restructured.");
        var end = markup.IndexOf('>', start);
        return markup[start..end];
    }

    private static string Section(string markup, string open, string close)
    {
        var start = markup.IndexOf(open, System.StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {open} in MainWindow.axaml — the footer was restructured.");
        var end = markup.IndexOf(close, start, System.StringComparison.Ordinal);
        return markup[start..end];
    }
}
