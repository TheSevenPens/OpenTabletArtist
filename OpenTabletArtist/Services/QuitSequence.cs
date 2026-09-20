using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>
/// What the application does, in what order, between the user choosing Quit and the process going.
/// </summary>
///
/// <remarks>
/// <para>
/// Lifted out of the tray so it can be tested. <c>AppTray</c> needs a real <c>TrayIcon</c> and a desktop
/// lifetime, so nothing about this sequence was reachable from a test — and the order is the whole of it.
/// Getting it wrong does not fail loudly; the step quietly does nothing, or does it to the wrong thing.
/// </para>
/// <para>
/// The ordering, and why:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Restore the per-app default</b> (#167), while the daemon is still connected and still running, so
/// no per-app snapshot is left applied after exit.
/// </description></item>
/// <item><description>
/// <b>Settle and close the settings session</b> (#828). A write that reached the daemon but not yet disk
/// finishes here instead of being cut off.
/// </description></item>
/// <item><description>
/// <b>Stop the daemon</b> (#596), if asked — <b>last</b>. It used to run before the close, which took the
/// connection away from the very writes the close then tried to settle: an apply in flight when the user
/// chose "quit and stop" lost its daemon and the edit with it. The close could see the failed task
/// finish, which is not the same as the edit surviving.
/// </description></item>
/// </list>
/// <para>
/// The stop's <em>target</em> is still decided before the close, by the caller — see
/// <c>AppSession.PrepareDaemonStopAsync</c>. Deferring the decision along with the action is the trap:
/// the live session is what knows which process is answering, and after a close there is nothing to ask,
/// so it would fall through to stopping every daemon on the machine.
/// </para>
/// <para>
/// <b>One budget for the exit, not one per step.</b> Three five-second steps meant a wholly unresponsive
/// daemon could hold an artist's application open for fifteen seconds with nothing on screen explaining
/// it. The budget below covers the sequence, and each step gets what is left of it.
/// </para>
/// </remarks>
internal static class QuitSequence
{
    /// <summary>
    /// How long the whole graceful exit may take before the rest of it is abandoned.
    /// </summary>
    /// <remarks>
    /// This application's choice, and not a wall-clock guarantee: the library documents that closing may
    /// wait on a host handoff already under way, which for an execution context that runs posted work
    /// inline is the callback itself. What it does guarantee is that nothing here waits on a daemon for
    /// longer than this in total.
    /// </remarks>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <param name="restorePerApp">Puts the user's default back, or null when there is nothing to restore.</param>
    /// <param name="closeSession">Settles work in flight and closes; answers false if it gave up.</param>
    /// <param name="stopDaemon">
    /// Stops the target captured before this ran, or null when the daemon is being left running.
    /// </param>
    /// <param name="warn">Where a step that did not finish, or failed, is reported.</param>
    /// <param name="budget">
    /// Overrides <see cref="Budget"/>, so a test about giving up need not spend the real one. The
    /// application never passes it; one test uses the default deliberately, to pin what that is.
    /// </param>
    public static async Task RunAsync(Func<Task>? restorePerApp, Func<TimeSpan, Task<bool>>? closeSession,
        Func<Task>? stopDaemon, Action<string> warn, TimeSpan? budget = null)
    {
        var total = budget ?? Budget;
        var clock = Stopwatch.StartNew();
        TimeSpan Left()
        {
            var left = total - clock.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        await BoundedAsync(restorePerApp, Left(), warn, "the per-app restore", "a snapshot may remain applied")
            .ConfigureAwait(true);

        if (closeSession != null)
        {
            try
            {
                if (!await closeSession(Left()).ConfigureAwait(true))
                {
                    warn("Closing the settings session on quit gave up with work still in flight; "
                         + "whether it reached disk is unknown.");
                }
            }
            catch (Exception ex)
            {
                // Narrower than the steps around it, which swallow everything because a stuck daemon is
                // ordinary. A close that throws is a defect rather than a stuck daemon, and exiting
                // silently on one would hide it.
                warn($"Closing the settings session on quit failed: {ex}");
            }
        }

        await BoundedAsync(stopDaemon, Left(), warn, "stopping the daemon", "it may still be running")
            .ConfigureAwait(true);
    }

    /// <summary>Runs a step that cannot bound itself, and gives up on it when the budget runs out.</summary>
    /// <remarks>
    /// <para>
    /// A step given up on is <b>abandoned, not cancelled</b>: it goes on running, and on its own it would
    /// go on running against a connection the exit is closing. That is why the close is ordered where it
    /// is and why the stop target is captured in advance — abandonment is the fallback, so the sequence
    /// has to be safe when it happens rather than merely unlikely to need it.
    /// </para>
    /// <para>
    /// Its failure is observed either way. A winning task is awaited rather than merely compared, or a
    /// step that returns an already-faulted task would be treated as having succeeded; an abandoned one
    /// is left a continuation, so its fault is reported when it arrives instead of going unobserved.
    /// </para>
    /// </remarks>
    private static async Task BoundedAsync(Func<Task>? step, TimeSpan within, Action<string> warn,
        string what, string consequence)
    {
        if (step == null) return;

        Task running;
        try
        {
            running = step();
        }
        catch (Exception ex)
        {
            // Threw before returning a task, so there is nothing to wait for or observe later.
            warn($"Quit: {what} failed: {ex}");
            return;
        }

        if (await Task.WhenAny(running, Task.Delay(within)).ConfigureAwait(true) == running)
        {
            try
            {
                await running.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                warn($"Quit: {what} failed: {ex}");
            }

            return;
        }

        warn($"Quit: {what} did not finish within the exit budget; {consequence}.");

        // Abandoned, and its fault still observed -- without waiting for it.
        _ = running.ContinueWith(t => warn($"Quit: {what} failed after being abandoned: {t.Exception}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
}
