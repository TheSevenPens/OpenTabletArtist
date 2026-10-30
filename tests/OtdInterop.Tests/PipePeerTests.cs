using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using OtdInterop;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// The pipe→pid lookup, checked over real named pipes (#140, #728).
/// </summary>
///
/// <remarks>
/// <para>
/// An in-process server answers with this test's own pid, which shows the lookup works and the
/// plumbing reaches it, but cannot tell "the server" from "me". The child-process test is what does:
/// the pid it expects belongs to a process that is not this one.
/// </para>
/// <para>
/// Nothing here contacts a running OpenTabletDriver; every pipe name is unique to the run.
/// </para>
/// </remarks>
public class PipePeerTests
{
    [Fact]
    public async Task AConnectedPipeNamesItsServer_WhereTheOsCanSay()
    {
        var pipe = $"ota-test-{Guid.NewGuid():N}"[..24];
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var accepted = server.WaitForConnectionAsync(cancel.Token);
        await client.ConnectAsync(cancel.Token);
        await accepted;

        bool found = PipePeer.TryGetServerProcessId(client.SafePipeHandle, out int pid);

        Assert.Equal(PipePeer.IsSupported, found);
        Assert.Equal(PipePeer.IsSupported ? Environment.ProcessId : 0, pid);
    }

    [Fact]
    public void TheLookupIsSupportedOnWindowsAndMacOS_AndNotYetOnLinux() =>
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), PipePeer.IsSupported);

    /// <summary>
    /// The pid reported is the process on the <em>other</em> end, not the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server is <c>nc -lU</c> listening on a socket path. On macOS a .NET named pipe is a
    /// Unix-domain socket and a rooted pipe name is used as the socket path as-is, so the client below
    /// is the same <see cref="NamedPipeClientStream"/> the daemon connection uses. The path is kept
    /// short and under <c>/tmp</c>, well inside the socket-path limit.
    /// </para>
    /// <para>
    /// macOS only: Windows' lookup is the OS's own named-pipe call and has no socket to point a child
    /// at, and Linux has no lookup yet.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OnMacOS_ThePidIsTheServerProcess_NotTheCaller()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The child-process server is a Unix-domain socket; macOS only.");

        var socket = Path.Combine("/tmp", $"ota-{Guid.NewGuid():N}"[..16]);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var nc = Process.Start(new ProcessStartInfo("/usr/bin/nc", ["-lU", socket])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            while (!File.Exists(socket))
                await Task.Delay(20, cancel.Token);

            using var client = new NamedPipeClientStream(".", socket, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(cancel.Token);

            Assert.True(PipePeer.TryGetServerProcessId(client.SafePipeHandle, out int pid));
            Assert.Equal(nc.Id, pid);
            Assert.NotEqual(Environment.ProcessId, pid);
        }
        finally
        {
            if (!nc.HasExited) nc.Kill();
            File.Delete(socket);
        }
    }

    [Fact]
    public async Task DaemonClient_ReportsTheServerWhileConnected_AndNothingAfter()
    {
        var pipe = $"ota-test-{Guid.NewGuid():N}"[..24];
        var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var client = new DaemonClient(NullOtdLog.Instance, pipe);
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnected() => connected.TrySetResult();
        client.Connected += OnConnected;
        try
        {
            Assert.Null(client.GetServerProcessId());

            var accepted = server.WaitForConnectionAsync(cancel.Token);
            _ = client.ConnectAsync(cancel.Token);
            await accepted;
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);

            Assert.Equal(PipePeer.IsSupported ? Environment.ProcessId : null, client.GetServerProcessId());

            client.AutoReconnect = false;
            client.Dispose();
            Assert.Null(client.GetServerProcessId());
        }
        finally
        {
            client.Connected -= OnConnected;
            client.AutoReconnect = false;
            cancel.Cancel();
            client.Dispose();
            cancel.Dispose();
        }
    }

    [Fact]
    public async Task DiagnosticsConnection_NamesTheServer_OrReturnsNullWhereItCannot()
    {
        var pipe = $"ota-test-{Guid.NewGuid():N}"[..24];
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new DiagnosticsConnection(pipe);

        var accepted = server.WaitForConnectionAsync(cancel.Token);
        await client.ConnectAsync(cancel.Token);
        await accepted;

        Assert.Equal(PipePeer.IsSupported ? Environment.ProcessId : null, client.ServerProcessId());
        // The executable is the test host's here; what matters is that it is read rather than skipped.
        Assert.Equal(PipePeer.IsSupported, client.ServerExecutablePath() is not null);
    }
}
