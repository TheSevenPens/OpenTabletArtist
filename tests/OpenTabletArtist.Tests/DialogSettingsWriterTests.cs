using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// A dialog that writes settings must hold them open while it is up (#922).
/// </summary>
/// <remarks>
/// <para>
/// Calibration reads the whole document when it opens and submits a profile built from it when the
/// artist finishes tapping — minutes later in the slow case. Nothing about that is visible to the
/// editor-input predicate, because it is not an editor: it moves no slider and leaves no pending edit.
/// So a change arriving from the driver in between was adopted as the new baseline, and calibration's
/// submission then wrote its captured values back over the top, reverting settings nobody had touched.
/// </para>
/// <para>
/// <c>AppSession.ReserveEditing()</c> is what closes that: it stops automatic adoption while the hold
/// is open, and refuses the submission if something replaces the document anyway. The two regressions
/// for those behaviours live in <c>ExplicitSettingsTests</c>. This test is for the shape — so that the
/// next dialog that writes settings across a wait has to answer the same question, instead of
/// reproducing the same bug somewhere the existing tests do not look.
/// </para>
/// <para>
/// Every other settings writer in the app reads and submits in one uninterrupted stretch, with nothing
/// waiting on a person in between: <c>SetupActions</c>, <c>AppTray.SwitchDisplayAsync</c>,
/// <c>MonitorCycleService</c>, <c>ProfileSwitchService</c> and the six developer-page fixtures all do.
/// For those the coordinator's own pre-apply comparison is the whole protection, and a hold would add
/// nothing. <c>MainViewModel.ForgetTabletByNameAsync</c> does wait on a confirmation — and reads the
/// settings after it, which is the other way to be correct.
/// </para>
/// </remarks>
public class DialogSettingsWriterTests
{
    /// <summary>
    /// Methods that both open a dialog and write settings, and are allowed not to hold them.
    /// </summary>
    /// <remarks>
    /// One entry, and it is a factory rather than a writer: <c>CreateTabletDetail</c> builds the tablet
    /// editor and hands it callbacks. The dialog inside it is the binding editor, whose result goes back
    /// to that editor — which does report pending input, and does reconcile when the document is
    /// replaced. Adding to this list means arguing the same case for something else.
    /// </remarks>
    private static readonly string[] Exempt = ["CreateTabletDetail"];

    [Fact]
    public void EveryDialogThatWritesSettingsHoldsThemOpen()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            foreach (var (name, method) in Methods(File.ReadAllText(file)))
            {
                if (Exempt.Contains(name)) continue;
                var opensADialog = method.Contains("ShowDialog(") || method.Contains("Dialog.ShowAsync(");
                var writesSettings = method.Contains("ApplyProfileAsync(")
                    || method.Contains("ApplySettingsAsync(");
                if (opensADialog && writesSettings && !method.Contains("ReserveEditing()"))
                    offenders.Add($"{Path.GetFileName(file)}: {name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These open a dialog and write settings without holding them open, so an outside change "
            + "can be adopted under the captured document and then reverted by the submission (#922): "
            + string.Join(", ", offenders)
            + ". Wrap the read and the submission in AppSession.ReserveEditing(), or say here why the "
            + "document cannot move underneath.");
    }

    /// <summary>Splits a source file into method bodies, keyed by name. Brace-matched, not a parser.</summary>
    private static IEnumerable<(string Name, string Body)> Methods(string source)
    {
        foreach (Match m in Regex.Matches(source, @"(\w+)\s*\([^;{]*\)\s*\{"))
        {
            var open = source.IndexOf('{', m.Index + m.Length - 1);
            if (open < 0) continue;
            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                {
                    yield return (m.Groups[1].Value, source[open..(i + 1)]);
                    break;
                }
            }
        }
    }

    private static string AppDir([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "OpenTabletArtist"));
}
