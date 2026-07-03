using System;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Feature-scoped pen down/up signal for per-app defer-until-pen-up (#167). Owns its own
/// <see cref="DaemonPenInputSource"/> so it works in tray/background mode (the page-scoped pen sources
/// are off then); the daemon debug stream it turns on is refcounted in <see cref="DaemonClient"/>, so it
/// coexists with Diagnostics/Test rather than fighting them. Collapses the report stream to just the
/// down↔up edges. UI-thread only.
/// </summary>
public sealed class DaemonPenStateProvider : IPenStateProvider
{
    private readonly DaemonPenInputSource _source;
    private bool _isDown;

    public bool IsDown => _isDown;
    public event Action<bool>? PenStateChanged;

    public DaemonPenStateProvider(IDaemonDebugSession daemon)
    {
        _source = new DaemonPenInputSource(daemon);
        _source.Sample += OnSample;
    }

    private void OnSample(PenSample s)
    {
        if (s.IsDown == _isDown) return;   // only fire on a transition
        _isDown = s.IsDown;
        PenStateChanged?.Invoke(_isDown);
    }

    public void Start() => _ = _source.StartAsync();

    public void Stop()
    {
        _isDown = false;
        _ = _source.StopAsync();
    }

    public void Dispose()
    {
        _source.Sample -= OnSample;
        _ = _source.StopAsync();
    }
}
