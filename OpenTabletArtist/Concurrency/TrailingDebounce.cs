using OpenTabletArtist.Services;

namespace OpenTabletArtist.Concurrency;

/// <summary>One pending input edit. Save can flush it; reload and disposal can discard it.</summary>
public sealed class TrailingDebounce(int delayMs, string name) : IDisposable
{
    private CancellationTokenSource? _delay;
    private Func<Task>? _pending;
    private int _version;
    private Task _running = Task.CompletedTask;
    public bool IsDisposed { get; private set; }
    public bool IsPending => _pending is not null;

    public void Schedule(Func<Task> work)
    {
        if (IsDisposed) return;
        CancelPending();
        _pending = work;
        var delay = _delay = new CancellationTokenSource();
        _ = WaitAsync(delay.Token);
    }

    private async Task WaitAsync(CancellationToken token)
    {
        try { await Task.Delay(delayMs, token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested || IsDisposed) return;
        try { await FlushAsync(); }
        catch (Exception ex) { AppLog.Warn($"A {name} edit failed to apply.", ex); }
    }

    public async Task FlushAsync()
    {
        var work = _pending;
        CancelPending();
        if (work is not null && !IsDisposed)
        {
            var previous = _running;
            _running = RunAsync(previous, work, _version);
        }
        await _running;
    }

    private async Task RunAsync(Task previous, Func<Task> work, int version)
    {
        try { await previous; }
        catch { /* a later edit may recover after an earlier one failed */ }
        if (IsDisposed || version != _version) return;
        await work();
    }

    public void CancelPending()
    {
        ++_version;
        _pending = null;
        _delay?.Cancel();
        _delay?.Dispose();
        _delay = null;
    }

    public void Dispose()
    {
        IsDisposed = true;
        CancelPending();
    }
}
