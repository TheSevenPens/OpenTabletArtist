using System.Diagnostics;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OtdDaemonSwitchCheck;

/// <summary>
/// Checks the daemon-switch behaviour against two real OpenTabletDriver installs.
/// </summary>
///
/// <remarks>
/// <para>
/// What a daemon switch costs has been fixed five times in this codebase (#774, #777, #787, #789, #803)
/// and, until this existed, was only ever verified against fakes. Everything the unit tests substitute is
/// real here: the named pipe, the Win32 pipe-to-process-id lookup, the process path resolution, the OTA
/// settings policy, and the disk.
/// </para>
/// <para>
/// The interesting cases are the ones where nothing should happen. A build that discarded state on every
/// reconnect would pass the first scenario and silently eat an artist's unsaved edit in ordinary use, so
/// three of the four here assert that a change was <em>not</em> reported.
/// </para>
/// </remarks>
internal static class Program
{
    private static int _failures;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("""
                Usage: OtdDaemonSwitchCheck <daemon-a.exe> <daemon-b.exe> [scenario]

                Two DIFFERENT OpenTabletDriver.Daemon executables. Scenario is 1-4, or omitted for all.

                This starts and kills daemon processes and toggles the read-only flag on the settings file
                the daemon reports. It restores the flag; it does not restore which daemon was running.
                """);
            return 2;
        }

        var a = Path.GetFullPath(args[0]);
        var b = Path.GetFullPath(args[1]);
        var scenario = args.Length > 2 ? args[2] : "all";

        if (!File.Exists(a) || !File.Exists(b)) { Console.Error.WriteLine("Both daemon paths must exist."); return 2; }
        if (PathEquality.Same(a, b)) { Console.Error.WriteLine("The two daemons must be different installs."); return 2; }

        // Refused rather than documented. A running OpenTabletArtist connects to the same daemon, reloads,
        // applies its filter policy and writes settings on every load -- so it races this and makes every
        // result unattributable. A check that runs anyway and reports a pass is worse than one that stops.
        if (Process.GetProcessesByName("OpenTabletArtist").Length > 0)
        {
            Console.Error.WriteLine(
                "OpenTabletArtist is running. Close it first: it connects to the same daemon and writes "
                + "settings on every reload, which would race this check.");
            return 2;
        }

        try
        {
            if (scenario is "all" or "1") await SwitchWithPendingEdit(a, b);
            if (scenario is "all" or "2") await SwitchWithNothingPending(a, b);
            if (scenario is "all" or "3") await SameDaemonRestart(a);
            if (scenario is "all" or "4") await UnreadableDaemon(a);
        }
        finally
        {
            KillDaemons();
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>The case the notice exists for: a different daemon, and an edit the disk never took.</summary>
    private static async Task SwitchWithPendingEdit(string a, string b)
    {
        Head("1. switch to a different daemon with an unsaved edit pending");
        KillDaemons();
        using var h = await Open(a);

        var first = h.Session.NoteConnectedDaemon();
        Check("identified the first daemon", PathEquality.Same(first.ExecutablePath, a), first.ExecutablePath);
        Check("the first look is not a change", !first.Changed, first.Changed);

        h.BlockWrites(true);
        Check("applied but not saved", await h.EditStatus() == SettingsApplyStatus.AppliedNotSaved, "-");

        await h.SwitchTo(b);
        var change = h.Session.NoteConnectedDaemon();

        Check("switch detected", change.Changed, change.Changed);
        Check("identified the second daemon", PathEquality.Same(change.ExecutablePath, b), change.ExecutablePath);
        Check("the unsaved edit was discarded", change.DiscardedUnsavedChange, change.DiscardedUnsavedChange);

        var retry = (await h.Settings.RetryPersistAsync()).Status;
        Check("nothing is left to write into the new daemon's file",
            retry == SettingsApplyStatus.NoChange, retry);

        h.BlockWrites(false);
    }

    /// <summary>A switch that costs nothing must say so. A spurious notice is its own bug.</summary>
    private static async Task SwitchWithNothingPending(string a, string b)
    {
        Head("2. switch to a different daemon with nothing pending");
        KillDaemons();
        using var h = await Open(a);
        h.Session.NoteConnectedDaemon();

        Check("applied and saved", await h.EditStatus() == SettingsApplyStatus.AppliedAndSaved, "-");

        await h.SwitchTo(b);
        var change = h.Session.NoteConnectedDaemon();

        Check("switch detected", change.Changed, change.Changed);
        Check("nothing was discarded", !change.DiscardedUnsavedChange, change.DiscardedUnsavedChange);
    }

    /// <summary>
    /// The same binary restarting is the same daemon. Identity is the executable, not the process — what
    /// the protected state describes is a settings file and an installation, and both survive a restart.
    /// </summary>
    private static async Task SameDaemonRestart(string a)
    {
        Head("3. restart the SAME daemon with an unsaved edit pending");
        KillDaemons();
        using var h = await Open(a);
        h.Session.NoteConnectedDaemon();

        h.BlockWrites(true);
        Check("applied but not saved", await h.EditStatus() == SettingsApplyStatus.AppliedNotSaved, "-");

        await h.SwitchTo(a);                            // same binary, new process
        var change = h.Session.NoteConnectedDaemon();

        Check("NOT reported as a change", !change.Changed, change.Changed);
        Check("nothing was discarded", !change.DiscardedUnsavedChange, change.DiscardedUnsavedChange);

        var pending = (await h.Settings.RetryPersistAsync()).Status;
        Check("the edit survived and is still unsaved", pending == SettingsApplyStatus.AppliedNotSaved, pending);

        h.BlockWrites(false);
        var saved = (await h.Settings.RetryPersistAsync()).Status;
        Check("and the retry lands it once the file is writable",
            saved == SettingsApplyStatus.AppliedAndSaved, saved);
    }

    /// <summary>
    /// The case that costs an artist their work if it is wrong.
    ///
    /// An elevated daemon, or another user's, is unreadable on <em>every</em> reconnect — so treating
    /// "cannot see" as "it changed" would discard an unsaved edit over and over while looking like the
    /// app working correctly.
    ///
    /// Elevation itself needs a UAC prompt, so this composes the two facts instead: that the real locator
    /// genuinely returns null for a process it cannot read, and that a real daemon over a real pipe
    /// behaves correctly when its id cannot be resolved. What stays unproven is that an elevated daemon
    /// still yields a process id from the pipe — likely, since the pipe is
    /// <c>PipeOptions.CurrentUserOnly</c> and an elevated daemon runs as the same user, but unobserved.
    /// </summary>
    private static async Task UnreadableDaemon(string a)
    {
        Head("4. a daemon whose executable can't be read (what elevation looks like)");
        KillDaemons();

        var real = new DaemonLifecycleService();
        var system = Process.GetProcessesByName("csrss").FirstOrDefault()
                     ?? Process.GetProcessesByName("services").FirstOrDefault();
        if (system == null)
            Console.WriteLine("   [SKIP] no SYSTEM-owned process found to probe");
        else
            Check($"the real locator can't read {system.ProcessName} (pid {system.Id})",
                real.PathOf(system.Id) == null, real.PathOf(system.Id) ?? "<null>");

        var blind = new BlindLocator(real);
        using var h = await Open(a, blind);

        Check("a real pipe still reports a process id",
            h.Session.Connection.GetServerProcessId() != null, h.Session.Connection.GetServerProcessId());

        var seen = h.Session.NoteConnectedDaemon();
        Check("identified while readable", PathEquality.Same(seen.ExecutablePath, a), seen.ExecutablePath);

        h.BlockWrites(true);
        Check("applied but not saved", await h.EditStatus() == SettingsApplyStatus.AppliedNotSaved, "-");

        blind.Blind = true;                             // the daemon comes back elevated
        var change = h.Session.NoteConnectedDaemon();

        Check("unreadable is NOT a change", !change.Changed, change.Changed);
        Check("nothing was discarded", !change.DiscardedUnsavedChange, change.DiscardedUnsavedChange);
        Check("no path is reported", change.ExecutablePath == null, change.ExecutablePath ?? "<null>");

        var pending = (await h.Settings.RetryPersistAsync()).Status;
        Check("the edit survived", pending == SettingsApplyStatus.AppliedNotSaved, pending);

        h.BlockWrites(false);
        var saved = (await h.Settings.RetryPersistAsync()).Status;
        Check("and saves once writable", saved == SettingsApplyStatus.AppliedAndSaved, saved);
    }

    // --- harness ---------------------------------------------------------------------------------

    /// <summary>A live session over a started daemon, plus the settings file it reported.</summary>
    private sealed class Live(OtdSession session, IOtdSettingsSession settings, string settingsFile)
        : IDisposable
    {
        public OtdSession Session { get; } = session;
        public IOtdSettingsSession Settings { get; } = settings;

        /// <summary>
        /// Makes the daemon's settings file refuse writes, so an apply is live and nowhere else.
        ///
        /// The path comes from the daemon's own AppInfo rather than a guess at where OTD keeps it: a
        /// portable install puts it beside the executable, and checking the wrong file would make every
        /// write succeed and every assertion below it meaningless.
        /// </summary>
        public void BlockWrites(bool on)
        {
            if (File.Exists(settingsFile)) new FileInfo(settingsFile).IsReadOnly = on;
        }

        /// <summary>Makes one real edit and reports what happened to it.</summary>
        public async Task<SettingsApplyStatus> EditStatus()
        {
            var reload = await Settings.ReloadFromDaemonAsync();
            var s = reload.Adopted?.Settings
                    ?? throw new InvalidOperationException("the daemon returned no settings");
            var profile = s.Profiles[0];
            // Round-trips cleanly and is visible in the file: flip the pressure-disable flag.
            profile.BindingSettings.DisablePressure = !profile.BindingSettings.DisablePressure;
            return (await Settings.ApplyAndSaveAsync(s)).Status;
        }

        /// <summary>Stops whatever is running and brings up <paramref name="exe"/>, waiting for reconnect.</summary>
        public async Task SwitchTo(string exe)
        {
            var reconnected = new TaskCompletionSource();
            Session.Connection.Connected += () => reconnected.TrySetResult();
            KillDaemons();
            await Task.Delay(1500);
            StartDaemon(exe);
            await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(500);
        }

        public void Dispose()
        {
            BlockWrites(false);
            Session.Dispose();
        }
    }

    private static async Task<Live> Open(string exe, IDaemonProcessLocator? locator = null)
    {
        StartDaemon(exe);
        var session = OtdSession.Create(AppLogBridge.Instance, OtaSettingsPolicy.Instance,
            locator ?? new DaemonLifecycleService());

        var connected = new TaskCompletionSource();
        session.Connection.Connected += () => connected.TrySetResult();
        await session.Connection.ConnectAsync(CancellationToken.None);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var file = (await session.Connection.GetAppInfoAsync())?.SettingsFile ?? "";
        Console.WriteLine($"   settings file: {file}");

        var settings = session.OpenSettings(() => file, () => true, _ => { });
        await settings.ReloadFromDaemonAsync();
        return new Live(session, settings, file);
    }

    private static void StartDaemon(string exe)
    {
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true });
        Thread.Sleep(2500);
    }

    private static void KillDaemons()
    {
        foreach (var p in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch { /* already gone, or not ours to kill */ }
        }
        Thread.Sleep(500);
    }

    private static void Head(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title}");
    }

    private static void Check(string what, bool ok, object? actual)
    {
        if (!ok) _failures++;
        Console.WriteLine($"   [{(ok ? "PASS" : "FAIL")}] {what}   (actual: {actual})");
    }

    /// <summary>The real locator with its sight switchable off — what elevation does to it.</summary>
    private sealed class BlindLocator(IDaemonProcessLocator inner) : IDaemonProcessLocator
    {
        public bool Blind { get; set; }

        public string? PathOf(int processId) => Blind ? null : inner.PathOf(processId);

        public string? SingleRunningDaemonPath() => Blind ? null : inner.SingleRunningDaemonPath();
    }
}
