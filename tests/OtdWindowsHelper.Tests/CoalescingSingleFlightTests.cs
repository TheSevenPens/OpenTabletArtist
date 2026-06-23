using System.Threading;
using System.Threading.Tasks;
using OtdWindowsHelper.Concurrency;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class CoalescingSingleFlightTests
{
    [Fact]
    public async Task Trigger_WhenIdle_RunsOnce()
    {
        var sf = new CoalescingSingleFlight();
        var done = new TaskCompletionSource();
        var runs = 0;

        sf.Trigger(() => { Interlocked.Increment(ref runs); done.SetResult(); return Task.CompletedTask; });

        await done.Task;
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task TriggersDuringRun_CoalesceToExactlyOneRerun()
    {
        var sf = new CoalescingSingleFlight();
        var runs = 0;
        var firstEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var secondDone = new TaskCompletionSource();

        Func<Task> op = async () =>
        {
            var n = Interlocked.Increment(ref runs);
            if (n == 1)
            {
                firstEntered.SetResult();
                await release.Task;       // hold the single slot
            }
            else if (n == 2)
            {
                secondDone.SetResult();
            }
        };

        sf.Trigger(op);
        await firstEntered.Task;          // run #1 is in-flight, holding the slot

        // Three requests arrive during run #1 — they must collapse into a single rerun.
        sf.Trigger(op);
        sf.Trigger(op);
        sf.Trigger(op);

        release.SetResult();              // let run #1 finish; the coalesced rerun (run #2) follows
        await secondDone.Task;

        Assert.Equal(2, runs);            // exactly one rerun, not three
    }

    [Fact]
    public async Task CoalescedRerun_UsesMostRecentOperation()
    {
        var sf = new CoalescingSingleFlight();
        var firstEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var ranB = new TaskCompletionSource();
        var aReran = false;

        Func<Task> a1 = async () => { firstEntered.SetResult(); await release.Task; };
        Func<Task> aRerun = () => { aReran = true; return Task.CompletedTask; };
        Func<Task> b = () => { ranB.SetResult(); return Task.CompletedTask; };

        sf.Trigger(a1);
        await firstEntered.Task;     // a1 holds the slot

        sf.Trigger(aRerun);          // pending = aRerun
        sf.Trigger(b);               // pending replaced by b (latest wins)

        release.SetResult();
        await ranB.Task;             // the rerun executed b, not aRerun

        Assert.False(aReran);
    }

    [Fact]
    public async Task Trigger_AfterCompletion_RunsAgain()
    {
        var sf = new CoalescingSingleFlight();

        var first = new TaskCompletionSource();
        sf.Trigger(() => { first.SetResult(); return Task.CompletedTask; });
        await first.Task;

        var second = new TaskCompletionSource();
        sf.Trigger(() => { second.SetResult(); return Task.CompletedTask; });
        await second.Task; // completes only if a fresh run started after the first finished
    }

    [Fact]
    public async Task FailedRun_DoesNotStrandCoordinator()
    {
        var sf = new CoalescingSingleFlight();

        var firstStarted = new TaskCompletionSource();
        sf.Trigger(() => { firstStarted.SetResult(); throw new InvalidOperationException("boom"); });
        await firstStarted.Task;

        // A later trigger must still run despite the previous run throwing.
        var recovered = new TaskCompletionSource();
        sf.Trigger(() => { recovered.SetResult(); return Task.CompletedTask; });
        await recovered.Task;
    }
}
