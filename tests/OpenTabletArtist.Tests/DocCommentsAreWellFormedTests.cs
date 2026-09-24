using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Every <c>///</c> comment in the tree is well-formed XML (#951).
/// </summary>
///
/// <remarks>
/// <para>
/// Nothing else checks. Documentation generation is off, so the compiler never parses these, and a
/// comment can carry an unclosed tag, a stray closing one, or a bare <c>&amp;</c> for as long as nobody
/// reads it closely. <c>OtdRelease.InstallDirectory</c> had two <c>&lt;/summary&gt;</c> tags with prose
/// between them, which meant half its explanation was not documentation at all — and it survived
/// several reviews of the file it lives in.
/// </para>
/// <para>
/// This is cheap in the way the tree's other scanning tests are cheap, and it earns its place the same
/// way: it cost a minute to write and immediately found something a human had read past.
/// </para>
/// <para>
/// It deliberately does <b>not</b> check what the tags mean — whether a <c>cref</c> resolves, whether a
/// <c>param</c> matches. Those are real questions and this answers none of them; it answers the one a
/// parser can, which is the one nothing was asking.
/// </para>
/// </remarks>
public class DocCommentsAreWellFormedTests
{
    /// <summary>Directories whose contents are not ours to hold to this.</summary>
    private static readonly string[] Skipped = ["external", "obj", "bin", ".git", ".vs"];

    [Fact]
    public void EveryDocCommentParses()
    {
        var broken = new List<string>();
        var blocks = 0;

        foreach (var file in SourceFiles())
        {
            foreach (var (line, body) in DocComments(File.ReadAllText(file)))
            {
                blocks++;
                try
                {
                    // Wrapped, because a doc comment is a fragment: several sibling elements and loose
                    // text with no single root of its own.
                    XDocument.Parse($"<d>{body}</d>");
                }
                catch (System.Xml.XmlException e)
                {
                    broken.Add($"{Relative(file)}:{line} — {e.Message}");
                }
            }
        }

        // Without this the scan could stop finding files and pass forever while proving nothing, which
        // is the failure mode of every test that searches rather than asserts.
        Assert.True(blocks > 500, $"only {blocks} doc comments found, so the scan is no longer scanning");

        Assert.True(broken.Count == 0,
            $"{broken.Count} malformed doc comment(s):\n  " + string.Join("\n  ", broken));
    }

    /// <summary>Consecutive <c>///</c> lines, with the line each run starts on.</summary>
    private static IEnumerable<(int Line, string Body)> DocComments(string source)
    {
        var run = new List<string>();
        var start = 0;
        var lines = source.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("///"))
            {
                if (run.Count == 0) start = i + 1;
                run.Add(trimmed[3..]);
            }
            else if (run.Count > 0)
            {
                yield return (start, string.Join("\n", run));
                run.Clear();
            }
        }

        if (run.Count > 0) yield return (start, string.Join("\n", run));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !Skipped.Any(skip =>
                f.Contains($"{Path.DirectorySeparatorChar}{skip}{Path.DirectorySeparatorChar}")));

    private static string Relative(string file) =>
        Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');

    /// <summary>The checkout, found from this file rather than from the working directory.</summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
