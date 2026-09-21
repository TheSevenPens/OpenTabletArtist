using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Reconciliation hands every editor a snapshot and the stamp of that same snapshot (#910).
/// </summary>
///
/// <remarks>
/// <para>
/// An editor is given settings to show and a stamp to name them by, and it quotes that stamp back when
/// the artist accepts what it is showing. The session honours an acceptance whose stamp is the one it is
/// publishing. So if the settings and the stamp are read separately, a reload landing between the two
/// reads hands out an older snapshot under a newer stamp — and the acceptance is then honoured while the
/// artist is looking at something the session has already moved past. That is exactly the substitution
/// the stamp argument was added to prevent, arranged by the caller rather than by the session.
/// </para>
/// <para>
/// <b>Why a source scan and not a behaviour test.</b> The window is between two synchronous property
/// reads with nothing awaited in between, so no single-threaded test can land a reload inside it, and a
/// test that raced a background thread at it would fail sometimes and pass mostly — which is worse than
/// no test, because a guard that cries wolf gets ignored. Removing the window is the fix; this pins that
/// it stays removed.
/// </para>
/// <para>
/// <b>What this does not prove.</b> It reads the source of one method rather than running it, so it
/// cannot show the publication is used correctly once read — only that the method does not go back to
/// reading the two halves independently. The behaviour built on top of the stamp is covered where it
/// lives, in the acceptance tests.
/// </para>
/// </remarks>
public class ReconciliationReadsOnePublicationTests
{
    [Fact]
    public void ReconcileOpenTabletDetails_ReadsTheSnapshotAndItsStampAsOnePublication()
    {
        var body = MethodBody(
            File.ReadAllText(Path.Combine(SourceDirectory(), "ViewModels", "MainViewModel.cs")),
            "private void ReconcileOpenTabletDetails()");

        // The scan is worthless if it stopped finding the method, so say so rather than passing.
        Assert.False(string.IsNullOrWhiteSpace(body),
            "ReconcileOpenTabletDetails was not found; this guard is no longer checking anything");

        Assert.DoesNotContain("CurrentStamp", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentSettings", body, StringComparison.Ordinal);
        Assert.Contains("CurrentPublication", body, StringComparison.Ordinal);
    }

    /// <summary>The text between a method's opening brace and its matching close.</summary>
    private static string MethodBody(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return "";

        var open = source.IndexOf('{', at + signature.Length);
        if (open < 0) return "";

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[(open + 1)..i];
        }

        return "";
    }

    /// <summary>
    /// The application's source folder, found from this file rather than from the build output.
    /// </summary>
    /// <remarks>
    /// <c>AppContext.BaseDirectory</c> does not work here: this repository redirects
    /// <c>BaseOutputPath</c> while the app holds <c>bin/</c>, so walking up from the output lands
    /// somewhere else entirely. The same reasoning as <see cref="BoundCommandsExistTests"/>.
    /// </remarks>
    private static string SourceDirectory([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile)!;            // tests/OpenTabletArtist.Tests
        var repo = Path.GetFullPath(Path.Combine(dir, "..", ".."));
        return Path.Combine(repo, "OpenTabletArtist");
    }
}
