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
    /// <summary>
    /// How many checks failed. Incremented atomically because <see cref="MustBeOnTheContext"/> exists to
    /// be called when the caller may NOT be on the one thread, which is exactly when a plain <c>++</c>
    /// would be racing — and losing a failure is the one outcome this counter must not produce.
    /// </summary>
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

        // One context for the whole run, and the scenarios run ON it rather than merely posting to it.
        // Starting the session's work here while the tool's own settings calls ran on console
        // continuations would confine the half that needs it least: the library posts its own work, but
        // OpenSettings, ReloadFromDaemonAsync and everything resumed after an await would still be
        // wherever the console left them, which is the thread pool.
        var context = new PumpContext();
        try
        {
            await context.RunAsync(async () =>
            {
                if (scenario is "all" or "1") await SwitchWithPendingEdit(a, b, context);
                if (scenario is "all" or "2") await SwitchWithNothingPending(a, b, context);
                if (scenario is "all" or "3") await SameDaemonRestart(a, context);
                if (scenario is "all" or "4") await UnreadableDaemon(a, context);
            });
        }
        finally
        {
            // Outside the pump deliberately: this is process cleanup, it touches no session state, and it
            // has to run even when the pump is the thing that failed.
            KillDaemons();

            // Disposed here rather than by `using`, so that whether it shut down cleanly can be counted.
            // Abandoning accepted work is a failed shutdown, and a tool whose exit code is its whole
            // output should not report one as a pass.
            context.Dispose();
            Check("the execution context settled its work on shutdown", context.ShutDownCleanly,
                context.ShutDownCleanly);
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>The case the notice exists for: a different daemon, and an edit the disk never took.</summary>
    private static async Task SwitchWithPendingEdit(string a, string b, PumpContext context)
    {
        Head("1. switch to a different daemon with an unsaved edit pending");
        await KillDaemonsAsync();
        using var h = await Open(a, context);

        // Nothing to ask: the session identified the daemon as it connected, and Open printed which one.
        h.BlockWrites(true);
        Check("applied but not saved", await h.EditStatus() == SettingsApplyStatus.AppliedNotSaved, "-");

        var change = await h.SwitchTo(b);

        Check("switch detected", change.Changed, change.Changed);
        Check("identified the second daemon", PathEquality.Same(change.ExecutablePath, b), change.ExecutablePath);
        Check("the unsaved edit was discarded", change.DiscardedUnsavedChange, change.DiscardedUnsavedChange);

        var retry = (await h.Settings.RetryPersistAsync()).Status;
        Check("nothing is left to write into the new daemon's file",
            retry == SettingsApplyStatus.NoChange, retry);

        h.BlockWrites(false);
    }

    /// <summary>A switch that costs nothing must say so. A spurious notice is its own bug.</summary>
    private static async Task SwitchWithNothingPending(string a, string b, PumpContext context)
    {
        Head("2. switch to a different daemon with nothing pending");
        await KillDaemonsAsync();
        using var h = await Open(a, context);
        Check("applied and saved", await h.EditStatus() == SettingsApplyStatus.AppliedAndSaved, "-");

        var change = await h.SwitchTo(b);

        Check("switch detected", change.Changed, change.Changed);
        Check("nothing was discarded", !change.DiscardedUnsavedChange, change.DiscardedUnsavedChange);
    }

    /// <summary>
    /// The same binary restarting is the same daemon. Identity is the executable, not the process — what
    /// the protected state describes is a settings file and an installation, and both survive a restart.
    /// </summary>
    private static async Task SameDaemonRestart(string a, PumpContext context)
    {
        Head("3. restart the SAME daemon with an unsaved edit pending");
        await KillDaemonsAsync();
        using var h = await Open(a, context);
        h.BlockWrites(true);
        Check("applied but not saved", await h.EditStatus() == SettingsApplyStatus.AppliedNotSaved, "-");

        var change = await h.SwitchTo(a);               // same binary, new process

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
    private static async Task UnreadableDaemon(string a, PumpContext context)
    {
        Head("4. a daemon whose executable can't be read (what elevation looks like)");
        await KillDaemonsAsync();

        var real = new DaemonLifecycleService();
        var system = Process.GetProcessesByName("csrss").FirstOrDefault()
                     ?? Process.GetProcessesByName("services").FirstOrDefault();
        if (system == null)
            Console.WriteLine("   [SKIP] no SYSTEM-owned process found to probe");
        else
            Check($"the real locator can't read {system.ProcessName} (pid {system.Id})",
                real.PathOf(system.Id) == null, real.PathOf(system.Id) ?? "<null>");

        var blind = new BlindLocator(real);
        using var h = await Open(a, context, blind);

        Check("a real pipe still reports a process id",
            h.Session.ConnectedProcessId() != null, h.Session.ConnectedProcessId());

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
    private sealed class Live(OtdSession session, IOtdSettingsSession settings, string settingsFile,
        PumpContext context) : IDisposable
    {
        // The context is held to check against, not to dispose: it outlives every scenario, and Main owns it.

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
        /// <remarks>
        /// The checks are the point as much as the edit is. Adopting the reload's result and applying it
        /// are host accesses to state the library also touches when a daemon switches, and both here
        /// happen after an await, which is exactly where a context that only receives posts would have
        /// already let go.
        /// </remarks>
        public async Task<SettingsApplyStatus> EditStatus()
        {
            MustBeOnTheContext(context, "EditStatus entry");
            var reload = await Settings.ReloadFromDaemonAsync();
            MustBeOnTheContext(context, "adopting the reload result");
            var s = reload.Adopted?.Settings
                    ?? throw new InvalidOperationException("the daemon returned no settings");
            var profile = s.Profiles[0];
            // Round-trips cleanly and is visible in the file: flip the pressure-disable flag.
            profile.BindingSettings.DisablePressure = !profile.BindingSettings.DisablePressure;
            var status = (await Settings.ApplyAndSaveAsync(s)).Status;
            MustBeOnTheContext(context, "after applying and saving");
            return status;
        }

        /// <summary>
        /// Stops whatever is running, brings up <paramref name="exe"/>, and reports what the session made
        /// of the daemon that answered.
        /// </summary>
        /// <remarks>
        /// The answer comes from the session's own notification rather than from asking it afterwards.
        /// Since #828 the session identifies the daemon itself the moment the transport reconnects, so a
        /// host that asked later would be told nothing had changed -- the change having already been
        /// noticed and acted on. That is the behaviour this tool exists to exercise, so it observes it the
        /// way a host now has to.
        /// </remarks>
        public async Task<DaemonChange> SwitchTo(string exe)
        {
            var reconnected = new TaskCompletionSource<DaemonChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void Once(DaemonChange c) => reconnected.TrySetResult(c);
            Session.Connected += Once;
            try
            {
                await KillDaemonsAsync();
                await Task.Delay(1500);
                await StartDaemonAsync(exe);
                var change = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(30));
                // The reconnect half of the same question: the session raised this from the context, and
                // the host resumed on it.
                MustBeOnTheContext(context, "resuming after a reconnect");
                return change;
            }
            finally { Session.Connected -= Once; }
        }

        public void Dispose()
        {
            BlockWrites(false);
            Session.Dispose();
        }
    }

    private static async Task<Live> Open(string exe, PumpContext context,
        IDaemonProcessLocator? locator = null)
    {
        MustBeOnTheContext(context, "Open entry");
        await StartDaemonAsync(exe);
        var session = OtdSession.Create(AppLogBridge.Instance, OtaSettingsPolicy.Instance,
            locator ?? new DaemonLifecycleService(), context);

        var connected = new TaskCompletionSource<DaemonChange>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Connected += c => connected.TrySetResult(c);
        await session.ConnectAsync(CancellationToken.None);
        var first = await connected.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Console.WriteLine($"   connected to: {first.ExecutablePath ?? "<unidentifiable>"}");

        var file = (await session.Capabilities.GetAppInfoAsync())?.SettingsFile ?? "";
        Console.WriteLine($"   settings file: {file}");

        MustBeOnTheContext(context, "opening a settings session");
        var settings = session.OpenSettings(() => file, () => true, _ => { });
        await settings.ReloadFromDaemonAsync();
        MustBeOnTheContext(context, "after the first reload");
        return new Live(session, settings, file, context);
    }

    /// <remarks>
    /// The settle waits are awaited rather than slept, because these now run ON the context. A blocking
    /// sleep there holds the only thread the session has, so a connection raised during the wait would sit
    /// in the queue behind us: working, but only because nothing needed the context meanwhile.
    /// </remarks>
    private static async Task StartDaemonAsync(string exe)
    {
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true });
        await Task.Delay(2500);
    }

    private static async Task KillDaemonsAsync()
    {
        KillDaemons();
        await Task.Delay(500);
    }

    /// <summary>Kills the daemons and returns. For cleanup, where there is nothing left to settle for.</summary>
    private static void KillDaemons()
    {
        foreach (var p in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch { /* already gone, or not ours to kill */ }
        }
    }

    private static void Head(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title}");
    }

    /// <summary>
    /// Reports a host access that happened off the context the session was given.
    /// </summary>
    /// <remarks>
    /// Counted as a failure like any other check, so a regression shows up in the exit code rather than in
    /// a line someone has to notice. This is the assertion the arrangement is for: every other check here
    /// would pass just as well with the settings calls running on the thread pool, which is precisely the
    /// arrangement being ruled out.
    /// </remarks>
    private static void MustBeOnTheContext(PumpContext context, string where)
    {
        if (context.IsCurrent) return;
        Interlocked.Increment(ref _failures);
        Console.WriteLine($"   [FAIL] {where} ran off the execution context "
                          + $"(thread {Environment.CurrentManagedThreadId})");
    }

    private static void Check(string what, bool ok, object? actual)
    {
        if (!ok) Interlocked.Increment(ref _failures);
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
