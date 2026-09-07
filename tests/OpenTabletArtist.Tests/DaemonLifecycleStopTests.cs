using System;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// <see cref="DaemonLifecycleService.Stop(int)"/> — stopping the ONE daemon we're connected to instead of
/// every process sharing its name (#601). These cover the process-name guard, which is the whole reason
/// killing by pid is safer than killing by name: pids get recycled, so without it a stale pid would turn
/// "Stop the daemon" into "kill whatever inherited that number".
/// </summary>
public class DaemonLifecycleStopTests
{
    [Fact]
    public void Stop_RefusesAPidThatIsNotADaemon()
    {
        var svc = new DaemonLifecycleService();

        // The test host itself: a real, live process that is definitely not OpenTabletDriver.Daemon —
        // exactly what a recycled pid looks like. If the name guard were missing this would kill the test
        // run, so a passing suite is itself part of the assertion.
        Assert.False(svc.Stop(Environment.ProcessId));
    }

    [Fact]
    public void Stop_RefusesPidZero()
    {
        var svc = new DaemonLifecycleService();

        // Pid 0 is not "no process": it is the System Idle Process on Windows and the kernel on Linux,
        // and it resolves rather than throwing. Only the name guard stands between a bad pid and a kill
        // attempt on it, so pin that here rather than relying on the pid never being 0.
        Assert.False(svc.Stop(0));
    }
}
