using System;
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
/// Each step needs the connection the next one takes away, so getting them the wrong way round does not
/// fail loudly; it just means the step quietly did nothing.
/// </para>
/// <para>
/// The ordering, and why:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Restore the per-app default</b> (#167), while the daemon is still connected, so no per-app snapshot
/// is left applied after exit.
/// </description></item>
/// <item><description>
/// <b>Stop the daemon</b> (#596), if asked — after the restore, which needs it running.
/// </description></item>
/// <item><description>
/// <b>Settle and close the settings session</b> (#828) — last, because it is what takes the connection
/// away. A write that reached the daemon but not yet disk finishes here instead of being cut off by a
/// transport disposed underneath it.
/// </description></item>
/// </list>
/// <para>
/// Every step is bounded, because quitting has to be possible against a daemon that has stopped
/// answering. The first two are raced against a timer since the calls themselves are unbounded; the close
/// takes a window and returns when it expires, so racing it as well would only obscure which of the two
/// bounds fired.
/// </para>
/// </remarks>
internal static class QuitSequence
{
    /// <summary>
    /// How long any one shutdown step may take before the exit gives up on it.
    /// </summary>
    /// <remarks>
    /// This application's choice, and deliberately one number rather than three: a Quit against a wholly
    /// unresponsive daemon should take a predictable time. It is not the library's own ten-second default,
    /// which is a value for a host that has not decided — and OTA has.
    /// </remarks>
    public static readonly TimeSpan StepBound = TimeSpan.FromSeconds(5);

    /// <param name="restorePerApp">Puts the user's default back, or null when there is nothing to restore.</param>
    /// <param name="stopDaemon">Stops the daemon, or null when it is being left running.</param>
    /// <param name="closeSession">Settles work in flight and closes; answers false if it gave up.</param>
    /// <param name="warn">Where a step that did not finish is reported.</param>
    public static async Task RunAsync(Func<Task>? restorePerApp, Func<Task>? stopDaemon,
        Func<TimeSpan, Task<bool>>? closeSession, Action<string> warn)
    {
        await BoundedAsync(restorePerApp, warn,
            "Per-app restore on quit timed out after 5s; a snapshot may remain applied.")
            .ConfigureAwait(true);

        await BoundedAsync(stopDaemon, warn, "Stopping the daemon on quit timed out after 5s.")
            .ConfigureAwait(true);

        if (closeSession == null) return;

        try
        {
            if (!await closeSession(StepBound).ConfigureAwait(true))
            {
                warn("Closing the settings session on quit gave up after 5s with work still in flight; "
                     + "whether it reached disk is unknown.");
            }
        }
        catch (Exception ex)
        {
            // Narrower than the two above, which swallow everything because a stuck daemon is ordinary.
            // A close that throws is a defect rather than a stuck daemon, and exiting silently on one
            // would hide it.
            warn($"Closing the settings session on quit failed: {ex}");
        }
    }

    /// <summary>Runs a step that cannot bound itself, and gives up on it after <see cref="StepBound"/>.</summary>
    /// <remarks>
    /// The timed-out step is <em>abandoned, not cancelled</em>: it goes on running against a connection
    /// the next step is about to close. That is what it did before this was lifted out, and it is why the
    /// close is last rather than first.
    /// </remarks>
    private static async Task BoundedAsync(Func<Task>? step, Action<string> warn, string onTimeout)
    {
        if (step == null) return;

        try
        {
            var running = step();
            if (await Task.WhenAny(running, Task.Delay(StepBound)).ConfigureAwait(true) != running)
                warn(onTimeout);
        }
        catch
        {
            // A step that throws must not stop the ones after it, or a failed restore would leave the
            // session unclosed and the application still running.
        }
    }
}
