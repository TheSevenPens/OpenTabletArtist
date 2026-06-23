using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OtdWindowsHelper.Services;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class DiagnosticsViewModelTests
{
    private sealed class FakeDebugSession : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public List<bool> DebugCalls { get; } = new();
        public Task SetTabletDebugAsync(bool enabled) { DebugCalls.Add(enabled); return Task.CompletedTask; }
        public void Raise(JObject data) => DeviceReport?.Invoke(data);
    }

    [Fact]
    public async Task Toggle_WhenConnected_StartsDebugging()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null);

        Assert.True(vm.IsDebugging);
        Assert.Equal(new[] { true }, fake.DebugCalls);
    }

    [Fact]
    public async Task Toggle_WhenNotConnected_DoesNothing()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake); // IsConnected defaults false

        await vm.ToggleDebuggingCommand.ExecuteAsync(null);

        Assert.False(vm.IsDebugging);
        Assert.Empty(fake.DebugCalls);
    }

    [Fact]
    public async Task Toggle_Twice_StopsDebuggingAndDisablesStream()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // start
        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // stop

        Assert.False(vm.IsDebugging);
        Assert.Equal(new[] { true, false }, fake.DebugCalls);
    }

    [Fact]
    public async Task StopDebuggingAsync_WhenIdle_IsNoOp()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.StopDebuggingAsync();

        Assert.False(vm.IsDebugging);
        Assert.Empty(fake.DebugCalls);
    }

    [Fact]
    public async Task Dispose_WhileDebugging_DisablesStream()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };
        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // start

        vm.Dispose();

        Assert.Contains(false, fake.DebugCalls);
    }
}
