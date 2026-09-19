using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The app renders in the font it says it does (#718).
///
/// It did not, for the app's whole life. <c>App.axaml</c> declared the display family as the bare name
/// <c>Inter</c>, which goes to the <em>system</em> font manager — and the bundled Inter is registered by
/// <c>.WithInterFont()</c> as an embedded collection, which a bare name never reaches. On a machine
/// without Inter installed system-wide, every piece of text fell back to the OS default.
///
/// Nothing caught it because a fallback font looks fine. The failure has no error, no warning and no
/// visual tell unless you know what Inter looks like — so what is asserted here is not "a font resolved"
/// but "it resolved to <b>this</b> font", which is the only form of the check that can fail.
/// </summary>
public class AppFontTests
{
    /// <summary>
    /// The app's own resource — not a copy of the string — resolves to real Inter.
    ///
    /// Read from <see cref="Application.Current"/> so this guards the application's actual configuration.
    /// A test that hardcoded the expected value would pass while the app was misconfigured, which is
    /// precisely how this defect survived.
    /// </summary>
    [AvaloniaFact]
    public void TheAppsDisplayFont_ResolvesToInter()
    {
        Assert.True(Application.Current!.Resources.TryGetResource("DisplayFontFamily", null, out var found),
            "the app declares no DisplayFontFamily resource");
        var declared = Assert.IsType<FontFamily>(found);

        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(declared), out var glyphs));
        Assert.Equal("Inter", glyphs.FamilyName);
    }

    /// <summary>
    /// A bare family name does NOT resolve to Inter, which is the whole of the bug.
    ///
    /// Pinned because the fix is one character class away from being reverted by anyone who finds
    /// <c>fonts:Inter#Inter</c> odd and "tidies" it. Asserted against what a name that cannot exist
    /// resolves to: if the two agree, the bare name is resolving to the fallback and not to Inter.
    ///
    /// Note what is NOT asserted. <c>TryGetGlyphTypeface</c> returns <b>true</b> for every one of these,
    /// including the nonsense family — it succeeds by falling back. A test that checked the boolean
    /// would pass in all worlds and prove nothing.
    /// </summary>
    [AvaloniaFact]
    public void ABareFamilyName_SilentlyFallsBack()
    {
        FontManager.Current.TryGetGlyphTypeface(new Typeface("Inter"), out var bare);
        FontManager.Current.TryGetGlyphTypeface(new Typeface("ZZNoSuchFontExists"), out var nonsense);

        Assert.NotEqual("Inter", bare.FamilyName);
        Assert.Equal(nonsense.FamilyName, bare.FamilyName);
    }

    /// <summary>
    /// No XAML names a font family literally; they all point at the resource.
    ///
    /// Seven places hardcoded <c>Inter</c> — the issue found three — and one of them was the
    /// <c>:is(TopLevel)</c> setter every piece of text inherits from. Repeating a font URI is how six of
    /// them would have stayed wrong after the seventh was fixed, so there is one place to be wrong now
    /// and this is what keeps it that way.
    /// </summary>
    [Fact]
    public void NoXamlHardcodesAFontFamily()
    {
        // BOTH forms, and the second is the one that matters: four of the seven original offenders were
        // setters, including the :is(TopLevel) rule every piece of text inherits from. A check that only
        // knew the attribute form would have passed while most of the bug was still there -- which is
        // exactly what the first version of this test did.
        var literal = new Regex(
            """(FontFamily\s*=\s*"|Property\s*=\s*"FontFamily"\s+Value\s*=\s*")(?!\{)""");

        var root = Path.Combine(RepoRoot(), "OpenTabletArtist");
        var offenders = Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f)
                .Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line))
                .Where(x => literal.IsMatch(x.Text)))
            .Select(x => $"{x.File}:{x.Line}  {x.Text.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The repository root, from this file's own compile-time path.
    ///
    /// Not from the working directory: tests run from their output folder, which moves when the build
    /// output is redirected (which it is whenever the app is running and holding bin/). A walk up from
    /// there finds the repository or does not, depending on where someone built — and a source check
    /// that silently cannot find the source is a check that always passes.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        // <repo>/tests/OpenTabletArtist.UiTests/AppFontTests.cs
        var dir = Directory.GetParent(thisFile)!.Parent!.Parent!.FullName;
        Assert.True(Directory.Exists(Path.Combine(dir, "OpenTabletArtist")),
            $"expected the repository root, found {dir}");
        return dir;
    }
}
