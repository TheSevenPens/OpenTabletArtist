using System.Threading.Tasks;
using OtdArtist.Concurrency;
using Xunit;

namespace OtdArtist.Tests;

public class LatestOnlyGateTests
{
    [Fact]
    public async Task SingleCall_Runs()
    {
        using var gate = new LatestOnlyGate();
        var ran = false;
        await gate.RunAsync(() => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
    }

    [Fact]
    public async Task SequentialCalls_AllRun()
    {
        using var gate = new LatestOnlyGate();
        var count = 0;
        await gate.RunAsync(() => { count++; return Task.CompletedTask; });
        await gate.RunAsync(() => { count++; return Task.CompletedTask; });
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task WhileOneRuns_OnlyTheLatestQueuedAlsoRuns()
    {
        using var gate = new LatestOnlyGate();
        var block = new TaskCompletionSource();
        var aEntered = new TaskCompletionSource();
        bool aRan = false, bRan = false, cRan = false;

        // A acquires the gate and holds it until we release `block`.
        var tA = gate.RunAsync(async () =>
        {
            aRan = true;
            aEntered.SetResult();
            await block.Task;
        });

        await aEntered.Task; // A is now inside its body, holding the gate.

        // B then C are requested while A holds the gate → both queue (generations 2, 3).
        var tB = gate.RunAsync(() => { bRan = true; return Task.CompletedTask; });
        var tC = gate.RunAsync(() => { cRan = true; return Task.CompletedTask; });

        // Release A. B acquires first (FIFO) but is superseded by C → skips; C runs.
        block.SetResult();
        await Task.WhenAll(tA, tB, tC);

        Assert.True(aRan);
        Assert.False(bRan); // superseded
        Assert.True(cRan);  // latest wins
    }
}
