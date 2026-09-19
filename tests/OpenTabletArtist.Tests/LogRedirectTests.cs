using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// This suite does not write to the user's diagnostics log (#834).
///
/// It used to, and nothing said so. The log is the artefact a user is asked for when they report a
/// problem; it rolls at 1 MB keeping one previous generation, so a few suite runs between reproducing a
/// bug and collecting the log could push the real evidence out of both. Silent, and at exactly the moment
/// the evidence was wanted.
///
/// So the property is asserted rather than left to the module initializer being remembered. If the
/// redirect stops running — removed, renamed, or beaten to the punch — these fail rather than quietly
/// resuming the old behaviour.
/// </summary>
public class LogRedirectTests
{
    [Fact]
    public void TheLogIsNotTheUsers()
    {
        Assert.False(AppLog.WritesToTheUsersLog);
    }

    /// <summary>
    /// Asserted against the real path rather than only against a flag, because the flag is this
    /// assembly's own idea of the answer and the question is about a file on disk.
    /// </summary>
    [Fact]
    public void TheLogPathIsOutsideTheUsersApplicationData()
    {
        var mine = Path.GetFullPath(AppLog.LogFilePath);
        var theirs = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTabletArtist"));

        Assert.False(mine.StartsWith(theirs, StringComparison.OrdinalIgnoreCase),
            $"the suite would log into {mine}");
    }

    /// <summary>And it really does write — a redirect to somewhere unwritable would pass the two above
    /// while losing every line, which is its own way of being wrong.</summary>
    [Fact]
    public void AndTheRedirectedLogIsWritable()
    {
        var marker = $"redirect probe {Guid.NewGuid():N}";

        AppLog.Info(marker);

        Assert.True(File.Exists(AppLog.LogFilePath));
        Assert.Contains(marker, File.ReadAllText(AppLog.LogFilePath));
    }
}
