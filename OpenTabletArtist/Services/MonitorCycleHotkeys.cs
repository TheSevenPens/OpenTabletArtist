using System;

namespace OpenTabletArtist.Services;

/// <summary>The single "cycle mapped monitor" hotkey the Hotkeys page manages (an interface so the page
/// is testable without Win32/disk). Implemented by <see cref="MonitorCycleHotkeys"/>. (#89)</summary>
public interface IMonitorCycleHotkey
{
    HotkeyChord? GetChord();
    HotkeySetResult SetHotkey(HotkeyChord chord);
    void ClearHotkey();
}

/// <summary>
/// Registers the single global "cycle mapped monitor" hotkey (#89) on the shared
/// <see cref="GlobalHotkeyService"/> and routes a press to <see cref="MonitorCycleService"/>. Persists
/// the chord in <see cref="AppSettings"/>. Shares the one hotkey window with the profile hotkeys, so it
/// filters presses to its own id and, on dispose, drops only its own registration + event hook — the
/// shell owns and disposes the shared service. UI-thread only.
/// </summary>
public sealed class MonitorCycleHotkeys : IMonitorCycleHotkey, IDisposable
{
    private const string MapKey = "Hotkey:CycleMonitor";
    private readonly GlobalHotkeyService _hotkeys;
    private readonly MonitorCycleService _cycle;
    private int _id;

    public MonitorCycleHotkeys(GlobalHotkeyService hotkeys, MonitorCycleService cycle)
    {
        _hotkeys = hotkeys;
        _cycle = cycle;
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        Register(GetChord()); // register a persisted chord on startup, independent of the page being opened
    }

    /// <summary>The assigned chord, or null.</summary>
    public HotkeyChord? GetChord()
        => HotkeyChord.TryParse(AppSettings.Get(MapKey), out var c) ? c : null;

    /// <summary>Assign the chord (persist + register). Conflict = another app owns it; Invalid = not registerable.</summary>
    public HotkeySetResult SetHotkey(HotkeyChord chord)
    {
        if (!chord.IsRegisterable) return HotkeySetResult.Invalid;
        Unregister();
        int id = _hotkeys.TryRegister(chord);
        if (id == 0) return HotkeySetResult.Conflict;
        _id = id;
        AppSettings.Set(MapKey, chord.Serialize());
        return HotkeySetResult.Ok;
    }

    /// <summary>Remove the hotkey (unregister + forget).</summary>
    public void ClearHotkey()
    {
        Unregister();
        AppSettings.Remove(MapKey);
    }

    private void Register(HotkeyChord? chord)
    {
        if (chord is { IsRegisterable: true })
        {
            int id = _hotkeys.TryRegister(chord);
            if (id != 0) _id = id;
        }
    }

    private void Unregister()
    {
        if (_id != 0) { _hotkeys.Unregister(_id); _id = 0; }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id != 0 && id == _id) _ = _cycle.CycleAsync();
    }

    public void Dispose()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        Unregister(); // NOT _hotkeys.Dispose() — the shared service is owned by the shell
    }
}
