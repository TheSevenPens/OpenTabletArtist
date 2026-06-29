using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Xunit;

namespace OtdArtist.Tests;

/// <summary>
/// Guards the assumption behind the poll loop's `await Dispatcher.UIThread.InvokeAsync(LoadDataAsync)`
/// (#33): that the call binds to Avalonia's non-generic <c>InvokeAsync(Func&lt;Task&gt;)</c> overload,
/// which returns an unwrapped <see cref="Task"/> — so a single await awaits the inner load and its
/// exceptions propagate. (WPF lacks this overload, which is why the pattern is a footgun there.)
/// </summary>
public class DispatcherOverloadTests
{
    [Fact]
    public void InvokeAsync_FuncTask_Overload_ReturnsUnwrappedTask()
    {
        var method = typeof(Dispatcher).GetMethod(
            nameof(Dispatcher.InvokeAsync), new[] { typeof(Func<Task>) });

        Assert.NotNull(method);
        // If this were the generic Func<TResult> overload it would return DispatcherOperation<Task>,
        // and a single await would NOT await the inner load.
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    // Compile-time proof that overload resolution actually binds the LoadDataAsync-shaped call to
    // that overload: if it bound to the generic overload this would return DispatcherOperation<Task>
    // and the test project would fail to compile. Never executed (no real dispatcher needed).
    private static Task OverloadBindingProbe(Func<Task> load)
        => Dispatcher.UIThread.InvokeAsync(load);

    [Fact]
    public void BindingProbe_IsTaskReturning()
        => Assert.Equal(typeof(Task), ((Delegate)OverloadBindingProbe).Method.ReturnType);
}
