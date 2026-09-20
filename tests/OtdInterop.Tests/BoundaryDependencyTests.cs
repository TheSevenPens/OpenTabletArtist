using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What the library is allowed to depend on, checked against the built assembly.
/// </summary>
///
/// <remarks>
/// <para>
/// The separation in #807 is only worth having if it cannot quietly come undone. Every other guarantee
/// here is about behaviour and is protected by a test; this one is about shape, and until now it was
/// protected by whoever happened to read the project file. A single <c>ProjectReference</c> added for a
/// convenience would undo the whole extraction, build cleanly, and pass every other test.
/// </para>
/// <para>
/// Read from the assembly rather than from the csproj on purpose. A project file says what someone
/// intended; the assembly says what is actually there, including anything that arrived transitively
/// through a package.
/// </para>
/// <para>
/// Replaces the single direct check that used to sit among the API tests. That one compared this
/// assembly's own references against three names; this covers the same three, plus what arrives behind
/// them and the test suite itself, and says why each one would matter rather than only that it does.
/// </para>
/// <para>
/// <b>Which means a package reference nobody uses is not caught</b>, because the compiler elides the
/// reference and the assembly genuinely does not depend on it. Verified, rather than assumed: adding an
/// unused <c>CommunityToolkit.Mvvm</c> reference leaves these green, and using one type from it turns
/// two of them red. That is the right line — erosion happens when something is used, not when it is
/// declared — but it is worth knowing, because reading the csproj would answer differently.
/// </para>
/// </remarks>
public class BoundaryDependencyTests
{
    /// <summary>
    /// Assemblies the library must not reference, and why each one would matter.
    /// </summary>
    /// <remarks>
    /// Matched on the assembly-name prefix, so <c>Avalonia.Controls</c> is caught by <c>Avalonia</c>.
    /// </remarks>
    private static readonly (string Prefix, string Why)[] Forbidden =
    [
        ("OpenTabletArtist", "the application: the library is what the application is built on, not the "
                             + "other way round, and a reference here is the extraction undone"),
        ("Avalonia", "a UI framework: the library has to be usable by a host that has none, which is the "
                     + "whole claim being made about it"),
        ("CommunityToolkit.Mvvm", "presentation machinery: observable properties and commands belong to "
                                  + "whatever is presenting, not to a settings authority"),
        ("SkiaSharp", "a rendering library, which arrives with Avalonia and would mean it had too"),
    ];

    [Fact]
    public void TheLibraryReferencesNothingItShouldNot()
    {
        var offenders = Referenced(typeof(OtdSession).Assembly)
            .SelectMany(name => Forbidden
                .Where(f => name.StartsWith(f.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(f => $"{name} — {f.Why}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "OtdInterop must not reference these:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// And nothing it references drags one in behind it.
    /// </summary>
    /// <remarks>
    /// The direct check above is the one someone would defeat by accident; this is the one they would
    /// defeat by adding a package that happens to depend on a UI framework. Walks what is actually on
    /// disk beside the library, which is what would ship.
    /// </remarks>
    [Fact]
    public void NorDoesAnythingItReferencesDragOneIn()
    {
        var library = typeof(OtdSession).Assembly;
        var beside = Path.GetDirectoryName(library.Location)!;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offenders = new List<string>();
        var queue = new Queue<Assembly>([library]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var name in Referenced(current))
            {
                if (!seen.Add(name)) continue;

                foreach (var (prefix, why) in Forbidden)
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        offenders.Add($"{name}, reached through {current.GetName().Name} — {why}");

                // Only what sits beside the library: the framework itself is not this test's business,
                // and trying to load everything would make it about the runtime's probing rules.
                var path = Path.Combine(beside, name + ".dll");
                if (!File.Exists(path)) continue;

                try { queue.Enqueue(Assembly.LoadFrom(path)); }
                catch (BadImageFormatException) { /* native or otherwise unreadable; not a reference */ }
            }
        }

        Assert.True(offenders.Count == 0,
            "Nothing OtdInterop references may reference these either:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The test suite itself is checked too, because it is the other way this could rot.
    /// </summary>
    /// <remarks>
    /// A suite that referenced the application could go on testing the library through it, and the tests
    /// would keep passing while the thing they were meant to demonstrate — that the library stands on its
    /// own — stopped being true. This suite compiles a few files from the app's suite by source link,
    /// which is deliberate and is not a reference: source has no dependencies of its own.
    /// </remarks>
    [Fact]
    public void NorDoesThisSuite()
    {
        var offenders = Referenced(typeof(BoundaryDependencyTests).Assembly)
            .Where(name => Forbidden.Any(f =>
                name.StartsWith(f.Prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "OtdInterop.Tests must reference the library and not the application:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> Referenced(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").Where(n => n.Length > 0);
}
