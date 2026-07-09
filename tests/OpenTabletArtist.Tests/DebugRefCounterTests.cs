using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>The daemon debug-stream reference counting (#121): the enable/disable RPC fires only on a
/// 0↔1 transition; a failed enable rolls back; disconnect resets.</summary>
public class DebugRefCounterTests
{
    [Fact]
    public void FirstAcquire_Signals_Enable()
    {
        var c = new DebugRefCounter();
        Assert.True(c.Acquire());   // 0 → 1: send enable
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void SecondAcquire_DoesNotSignal()
    {
        var c = new DebugRefCounter();
        c.Acquire();
        Assert.False(c.Acquire());  // 1 → 2: already streaming, no RPC
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void LastRelease_Signals_Disable()
    {
        var c = new DebugRefCounter();
        c.Acquire();
        c.Acquire();
        Assert.False(c.Release());  // 2 → 1: still a consumer, no RPC
        Assert.True(c.Release());   // 1 → 0: send disable
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void ReleaseAtZero_IsIgnored()
    {
        var c = new DebugRefCounter();
        Assert.False(c.Release());  // no underflow, no RPC
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void FailedEnable_RollsBack_SoNextAcquireReEnables()
    {
        var c = new DebugRefCounter();
        Assert.True(c.Acquire());   // took 0 → 1, but the enable RPC "failed"…
        c.RollbackAcquire();        // …so undo it
        Assert.Equal(0, c.Count);
        Assert.True(c.Acquire());   // the next acquire re-asserts the enable (not suppressed)
    }

    [Fact]
    public void RollbackAtZero_IsNoOp()
    {
        var c = new DebugRefCounter();
        c.RollbackAcquire();
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void Reset_ClearsCount_SoNextAcquireReEnables()
    {
        var c = new DebugRefCounter();
        c.Acquire();
        c.Acquire();                // two consumers active
        c.Reset();                  // disconnect: daemon forgot the flag
        Assert.Equal(0, c.Count);
        Assert.True(c.Acquire());   // 0 → 1 again: send enable
    }
}
