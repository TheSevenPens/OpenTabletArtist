using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What the library and its suite are allowed to <b>depend on</b>, read from what restore resolved.
/// </summary>
///
/// <remarks>
/// <para>
/// <see cref="BoundaryDependencyTests"/> reads the built assembly, which answers a narrower question than
/// it looks like: emitted managed references. A package can contribute analyzers, build targets, runtime
/// assets and transitive dependencies while the compiler emits no reference to any of its types — so a
/// forbidden package can be in the dependency graph, shipped and running, and leave that check green.
/// </para>
/// <para>
/// I had argued the opposite: that erosion happens when something is used rather than when it is
/// declared, and that an unused reference was a tidiness problem. That is wrong for the reasons above,
/// and convenient, which is a bad sign in an argument about one's own work. Both checks stay, because
/// they answer different questions: this one is about what the build pulls in, that one about what the
/// code came to lean on.
/// </para>
/// <para>
/// Read from <c>project.assets.json</c> — restore's own output — rather than by parsing csproj XML.
/// Hand-parsing a project file misses anything arriving through <c>Directory.Build.props</c>, a package
/// that brings another, or a reference added by an SDK.
/// </para>
/// </remarks>
public class BoundaryRestoreTests
{
    /// <summary>
    /// Names the library's dependency graph must not contain, matched on prefix.
    /// </summary>
    /// <remarks>
    /// OpenTabletDriver is expected and absent from this list: the library exists to talk to it.
    /// </remarks>
    private static readonly (string Prefix, string Why)[] Forbidden =
    [
        ("OpenTabletArtist", "the application: the library is what it is built on, not the reverse"),
        ("Avalonia", "a UI framework: the library must be usable by a host that has none"),
        ("CommunityToolkit.Mvvm", "presentation machinery, which a settings authority has no use for"),
        ("SkiaSharp", "a rendering library, which arrives with Avalonia and would mean it had too"),
    ];

    [Theory]
    [InlineData("OtdInterop")]
    [InlineData("tests/OtdInterop.Tests")]
    public void NothingForbiddenIsInTheRestoredGraph(string project)
    {
        var resolved = RestoredDependencies(project);

        // Established first: a graph this small would also produce "no offenders" if the file were
        // misread, and an empty result is exactly what a broken reader returns.
        Assert.Contains(resolved, name => name.StartsWith("OpenTabletDriver", StringComparison.Ordinal));

        var offenders = resolved
            .SelectMany(name => Forbidden
                .Where(f => name.StartsWith(f.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(f => $"{name} — {f.Why}"))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{project} must not depend on these, whether or not its code uses them:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Everything restore resolved for <paramref name="project"/>: its packages, its project references,
    /// and everything those brought with them.
    /// </summary>
    private static IReadOnlyCollection<string> RestoredDependencies(string project)
    {
        var assets = Path.Combine(RepoRoot(), project.Replace('/', Path.DirectorySeparatorChar),
                                  "obj", "project.assets.json");

        Assert.True(File.Exists(assets),
            $"No restore output for {project} at {assets}. This test reads what restore resolved, so it "
            + "needs the project to have been restored — which a build does.");

        using var doc = JsonDocument.Parse(File.ReadAllText(assets));

        var names = new List<string>();
        foreach (var framework in doc.RootElement.GetProperty("targets").EnumerateObject())
            foreach (var library in framework.Value.EnumerateObject())
                // "Name/version" — the version is not this test's business.
                names.Add(library.Name.Split('/')[0]);

        return names;
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
