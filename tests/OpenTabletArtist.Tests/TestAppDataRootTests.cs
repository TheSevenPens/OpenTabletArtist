using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// That the suite's own settings and logs are not the developer's (#947).
/// </summary>
///
/// <remarks>
/// <para>
/// This exists because a doc comment claimed the isolation and nothing checked it, and the claim was
/// wrong. <c>TestUserDataRoot</c> redirects OpenTabletDriver's roots through <c>LOCALAPPDATA</c>, which
/// on Windows does not move the OS known folder that <c>AppPaths</c> falls back to — so OTA's own
/// <c>settings.json</c> stayed real while everything around it was fake, and a test that wrote a
/// preference wrote it into a live installation.
/// </para>
/// <para>
/// It is deliberately an assertion about <see cref="AppPaths.LocalAppData"/> rather than about the
/// environment variable. The variable being set proves nothing: the path is resolved once in a static
/// initializer, so what matters is whether it was set <em>before</em> anything asked — which is the part
/// that was broken and the part a later reordering would break again.
/// </para>
/// </remarks>
public class TestAppDataRootTests
{
    /// <summary>OTA's data directory for this run is inside the throwaway root.</summary>
    [Fact]
    public void TheApplicationDirectoryIsInsideTheTemporaryRoot()
    {
        Assert.NotEqual("", TestAppDataRoot.Path);

        var resolved = Path.GetFullPath(AppPaths.LocalAppData);
        var root = Path.GetFullPath(TestAppDataRoot.Path);

        Assert.StartsWith(root, resolved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And it is emphatically not the real one.
    /// </summary>
    /// <remarks>
    /// The direct statement of the defect. The path above could sit inside the temp root and still be
    /// wrong if the fallback changed; this says the thing that actually went wrong, in the terms it
    /// went wrong in — a developer's own OpenTabletArtist directory being written to by a test run.
    /// </remarks>
    [Fact]
    public void ItIsNotTheRealUserDirectory()
    {
        var real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenTabletArtist");

        Assert.NotEqual(
            Path.GetFullPath(real).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(AppPaths.LocalAppData).TrimEnd(Path.DirectorySeparatorChar),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A setting written by a test lands in that directory and nowhere else.</summary>
    /// <remarks>
    /// The end-to-end version: the two above check where the path points, this checks where a write
    /// actually goes, which is the thing that damaged a real installation.
    /// </remarks>
    [Fact]
    public void ASettingWrittenHereLandsInTheTemporaryRoot()
    {
        AppSettings.Set("test.isolationProbe", "written");

        var settings = Path.Combine(AppPaths.LocalAppData, "settings.json");

        Assert.True(File.Exists(settings), $"expected a settings file at {settings}");
        Assert.StartsWith(
            Path.GetFullPath(TestAppDataRoot.Path),
            Path.GetFullPath(settings),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test.isolationProbe", File.ReadAllText(settings));
    }
}
