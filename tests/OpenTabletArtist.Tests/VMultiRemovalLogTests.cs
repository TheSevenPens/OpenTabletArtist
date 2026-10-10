using System.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The removal script logs every step the way the install script does, and the log is read back to say
/// when something really went wrong. Exit codes cannot say that: devcon and DIFxCmd are non-zero when there
/// was nothing left to remove, which is a normal outcome of an uninstall.
/// </summary>
public class VMultiRemovalLogTests
{
    private static string[] Script() => VMultiInstaller.BuildRemovalScript();
    private static string Joined() => string.Join("\n", Script());

    // ── the script ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Script_RunsThePackagesOwnRemovalSteps_ThenTheLeftoverCleanup_InOrder()
    {
        var cmds = Script().Where(l => l.Contains("DIFxCmd.exe") || l.Contains("devcon.exe")).ToArray();
        Assert.Equal(3, cmds.Length);
        Assert.Contains(@"DIFxCmd.exe"" /u ""%~dp0vmulti.inf""", cmds[0]);
        Assert.Contains(@"devcon.exe"" remove ""pentablet\hid""", cmds[1]);
        Assert.Contains(@"devcon.exe"" remove ""djpnewton\vmulti""", cmds[2]);
    }

    [Fact]
    public void EveryCommand_AppendsItsOutputAndErrorsToTheLog_AndRecordsItsExitCode()
    {
        var lines = Script();
        foreach (var i in Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains(".exe\"")))
        {
            Assert.EndsWith(@">> ""%LOG%"" 2>&1", lines[i]);                    // stdout AND stderr
            Assert.Equal(@"echo exit=%errorlevel% >> ""%LOG%""", lines[i + 1]); // its exit code, straight after
            Assert.StartsWith("echo === ", lines[i - 1]);                       // under its own header
        }
    }

    [Fact]
    public void LogIsTruncatedFirst_AndLivesNextToTheScript()
    {
        var lines = Script();
        Assert.Contains($@"set ""LOG=%~dp0{VMultiInstaller.RemovalLogName}""", lines);
        Assert.True(System.Array.IndexOf(lines, @"type nul > ""%LOG%""") < lines.ToList().FindIndex(l => l.StartsWith("echo === ")));
    }

    // devcon / DIFxCmd report non-zero when there was nothing to remove; that must not look like a failure.
    [Fact]
    public void Script_StillAlwaysExitsZero()
        => Assert.Equal("exit /b 0", Script().Last(l => l.Length > 0));

    [Fact]
    public void Script_UsesAbsoluteScriptRelativePaths_ForTheToolsAndTheInf()
    {
        // An elevated ShellExecute launch may start in System32 and ignore WorkingDirectory.
        foreach (var l in Script().Where(l => l.Contains(".exe\"")))
            Assert.StartsWith(@"""%~dp0", l);
        Assert.Contains(@"""%~dp0vmulti.inf""", Joined());
    }

    // ── reading the log back ────────────────────────────────────────────────────────────────────

    private const string CleanRemoval = """
        === DIFxCmd /u vmulti.inf ===
        SUCCESS: uninstalled package vmulti.inf.
        exit=0
        === devcon remove pentablet\hid ===
        ROOT\HIDCLASS\0000                                           : Removed
        1 device(s) were removed.
        exit=0
        === devcon remove djpnewton\vmulti ===
        No matching devices found.
        exit=1
        """;

    [Fact]
    public void ACleanRemoval_IsNotAProblem_EvenThoughOneStepFoundNothingAndExitedNonZero()
    {
        var s = VMultiInstaller.SummarizeRemovalLog(CleanRemoval);
        Assert.False(s.HasProblems);
        Assert.Equal("", s.Details);
    }

    [Fact]
    public void DifxCmdError_IsReported_WithItsStep()
    {
        var log = CleanRemoval.Replace("SUCCESS: uninstalled package vmulti.inf.", "ERROR: failed with error code 0x5");
        var s = VMultiInstaller.SummarizeRemovalLog(log);
        Assert.True(s.HasProblems);
        Assert.Contains("DIFxCmd /u vmulti.inf: ERROR: failed with error code 0x5", s.Details);
        Assert.StartsWith("\n\nDetails:\n", s.Details);
    }

    [Theory]
    [InlineData("Remove failed")]
    [InlineData("Deleting the specified driver package from the machine failed.")]
    public void DevconFailure_IsReported_WithItsStep(string failure)
    {
        var log = CleanRemoval.Replace("1 device(s) were removed.", failure);
        var s = VMultiInstaller.SummarizeRemovalLog(log);
        Assert.True(s.HasProblems);
        Assert.Contains(@"devcon remove pentablet\hid: " + failure, s.Details);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void NoLog_IsNotAProblem(string? log)
        => Assert.False(VMultiInstaller.SummarizeRemovalLog(log).HasProblems);

    // ── keeping the log ─────────────────────────────────────────────────────────────────────────

    // Captures the lines KeepLog writes, by a marker unique to the test: the log event is process-wide and
    // other tests write to it concurrently.
    private static string[] Kept(string marker, string log, bool failed)
    {
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Capture(string line) { if (line.Contains(marker)) lines.Enqueue(line); }
        AppLog.LineWritten += Capture;
        try { VMultiInstaller.KeepLog(marker, log, failed); }
        finally { AppLog.LineWritten -= Capture; }
        return lines.ToArray();
    }

    [Fact]
    public void ASuccessfulRunsLog_IsKeptAtInfo_WithItsOutput()
    {
        var line = Assert.Single(Kept("keeplog-ok-1f3a", "SUCCESS: installed.", failed: false));
        Assert.Contains("[INFO]", line);
        Assert.Contains("keeplog-ok-1f3a output:", line);
        Assert.Contains("SUCCESS: installed.", line);
    }

    [Fact]
    public void AFailedRunsLog_IsKeptAtWarning()
        => Assert.Contains("[WARNING]", Assert.Single(Kept("keeplog-bad-9c21", "Remove failed", failed: true)));

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void NoOutput_WritesNothing(string log)
        => Assert.Empty(Kept("keeplog-empty-77d0", log, failed: true));

    [Fact]
    public void ManyProblems_AreCapped_AndLongLinesAreShortened()
    {
        var log = "=== step ===\n" + string.Join("\n", Enumerable.Range(0, 20).Select(i => "ERROR: " + new string('x', 400)));
        var s = VMultiInstaller.SummarizeRemovalLog(log);
        var lines = s.Details.Split('\n').Skip(3).ToArray();   // after the blank line and "Details:"
        Assert.Equal(6, lines.Length);
        Assert.All(lines, l => Assert.True(l.Length <= "step: ".Length + 200 + 1));
    }
}
