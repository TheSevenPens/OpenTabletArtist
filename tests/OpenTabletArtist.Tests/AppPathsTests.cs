using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Where this application writes its own data, and whether an override actually redirects it (#879).
/// </summary>
///
/// <remarks>
/// The release workflow's packaged-app smoke test set <c>LOCALAPPDATA</c> expecting to sandbox the run.
/// On Windows <c>Environment.GetFolderPath</c> reads the OS known-folder and ignores that variable, so
/// OTA's own settings and logs went to the real user profile while the daemon's went to the sandbox —
/// which made the isolation look like it worked. A packaged-app test then reflected the test machine's
/// configuration rather than the artifact's behaviour, and #880 spent a diagnosis on it.
/// </remarks>
public class AppPathsTests
{
    /// <summary>An override redirects the application's data directory.</summary>
    [Fact]
    public void AnOverride_RedirectsTheDataDirectory()
    {
        var resolved = AppPaths.Resolve(Path.Combine("X:", "sandbox"));

        Assert.StartsWith(Path.Combine("X:", "sandbox"), resolved, StringComparison.Ordinal);
        Assert.EndsWith("OpenTabletArtist", resolved, StringComparison.Ordinal);
    }

    /// <summary>Without one, it is the user's own directory.</summary>
    [Fact]
    public void WithoutAnOverride_ItIsTheUsersOwnDirectory()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenTabletArtist");

        Assert.Equal(expected, AppPaths.Resolve(null));
    }

    /// <summary>
    /// A relative root becomes absolute, once.
    /// </summary>
    /// <remarks>
    /// Left relative it would resolve against whatever the working directory happened to be — which for
    /// a packaged app is wherever it was launched from, and would move if anything changed it.
    /// </remarks>
    [Fact]
    public void ARelativeRoot_IsMadeAbsolute()
    {
        var resolved = AppPaths.Resolve(Path.Combine("sandbox", "data"));

        Assert.True(Path.IsPathFullyQualified(resolved), $"not absolute: {resolved}");
        Assert.EndsWith("OpenTabletArtist", resolved, StringComparison.Ordinal);
    }

    /// <summary>
    /// A variable that is set but blank is treated as absent.
    /// </summary>
    /// <remarks>
    /// An empty environment variable is almost always an accident — an unset shell variable expanding to
    /// nothing. Honouring it would put the data in the current directory, which for a packaged app is
    /// wherever it happened to be launched from.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankOverride_IsTreatedAsAbsent(string blank)
    {
        Assert.Equal(AppPaths.Resolve(null), AppPaths.Resolve(blank));
    }
}
