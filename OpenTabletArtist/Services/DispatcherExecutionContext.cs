using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// OTA's execution context for OtdInterop: Avalonia's UI thread (#828).
/// </summary>
///
/// <remarks>
/// <para>
/// The library needs somewhere to run work it starts itself — identifying a daemon after a reconnect,
/// and dropping state belonging to one that has gone. That arrives on the transport's thread and touches
/// fields this application reads, so it has to be moved somewhere serialized. For this application that
/// is the dispatcher, which is where everything else touching session state already runs.
/// </para>
/// <para>
/// The whole of the adapter. The library has no business knowing what Avalonia is, and this has no
/// business knowing what the library does with it.
/// </para>
/// </remarks>
public sealed class DispatcherExecutionContext : IOtdExecutionContext
{
    /// <summary>The shared instance. There is one UI thread, so there is one of these.</summary>
    public static readonly DispatcherExecutionContext Instance = new();

    private DispatcherExecutionContext() { }

    /// <inheritdoc />
    public bool IsCurrent => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    /// <remarks>
    /// <c>InvokeAsync</c> rather than <c>Post</c>, because the returned task is the library's only way to
    /// observe that posted work failed. <c>Post</c> would drop the exception on the dispatcher's floor.
    ///
    /// It also runs inline when already on the UI thread, which satisfies the contract's requirement that
    /// posting from the context itself must not deadlock.
    /// </remarks>
    public Task PostAsync(Action work) => Dispatcher.UIThread.InvokeAsync(work).GetTask();
}
