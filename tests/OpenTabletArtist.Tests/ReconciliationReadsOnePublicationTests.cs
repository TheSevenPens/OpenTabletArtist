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
/// reading the two halves independently. What the editors actually receive is covered behaviourally, by
/// the forwarding tests; what an acceptance then means is covered where the stamp is checked.
/// </para>
/// <para>
/// Supplementary now rather than load-bearing. The session no longer offers a stamp property beside its
/// settings, so there is nothing for a caller to pair up by hand — which is a better guarantee than this
/// scan and is why the scan is worth keeping only as a cheap second opinion.
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

        Assert.DoesNotContain("CurrentSettings", body, StringComparison.Ordinal);
        Assert.Contains("CurrentPublication", body, StringComparison.Ordinal);
    }

    /// <summary>And the other adoption route reads the same way.</summary>
    /// <remarks>
    /// The caller-level fix reached one of the two routes first. An editor's own Refresh adopts settings
    /// exactly as reconciliation does, so reading the two halves separately there has the same
    /// consequence and had to be found by something other than noticing it.
    /// </remarks>
    [Fact]
    public void TheEditorRefreshCallback_ReadsTheSnapshotAndItsStampAsOnePublication()
    {
        var source = File.ReadAllText(Path.Combine(SourceDirectory(), "Services", "DialogService.cs"));
        var body = MethodBody(source,
            "public TabletDetailViewModel? CreateTabletDetail(string tabletName, Func<Task> onForget, "
            + "Action? openConfigsPage = null)");

        Assert.False(string.IsNullOrWhiteSpace(body),
            "CreateTabletDetail was not found; this guard is no longer checking anything");
        Assert.Contains("refreshAction", body, StringComparison.Ordinal);

        var refresh = body[body.IndexOf("refreshAction", StringComparison.Ordinal)..];
        refresh = refresh[..refresh.IndexOf("tabletDigitizer", StringComparison.Ordinal)];

        Assert.DoesNotContain("CurrentSettings", refresh, StringComparison.Ordinal);
        Assert.Contains("CurrentPublication", refresh, StringComparison.Ordinal);
    }

    /// <summary>A method's body, whether it is written as a block or as an expression.</summary>
    /// <remarks>
    /// Both forms, because the method this guards became a one-liner while the guard was being written
    /// — and a scan that only understood blocks would have gone on passing by reading the next method's
    /// body instead of the one it was asked about.
    /// </remarks>
    private static string MethodBody(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return "";

        var rest = at + signature.Length;
        while (rest < source.Length && char.IsWhiteSpace(source[rest])) rest++;

        if (source[rest] == '=')                                  // => expression;
        {
            var end = source.IndexOf(';', rest);
            return end < 0 ? "" : source[rest..end];
        }

        if (source[rest] != '{') return "";

        var depth = 0;
        for (var i = rest; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[(rest + 1)..i];
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
