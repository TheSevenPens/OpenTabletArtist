using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;
/// <summary>After resolving unsaved edits, close the session then stop the captured daemon if requested.</summary>
internal static class QuitSequence
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    public static async Task RunAsync(Func<TimeSpan, Task<bool>>? closeSession,
        Func<Task>? stopDaemon, Action<string> warn, TimeSpan? budget = null)
    {
        var total = budget ?? Budget;
        var clock = Stopwatch.StartNew();
        TimeSpan Left()
        {
            var left = total - clock.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }


        if (closeSession != null)
        {
            try
            {
                if (!await closeSession(Left()).ConfigureAwait(true))
                {
                    warn("Closing the settings session on quit gave up with work still in flight; "
                         + "pending operations were cancelled.");
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
