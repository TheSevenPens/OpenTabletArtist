using System;
using Avalonia.Input;

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
    /// <summary>
    /// Where this hotkey is persisted. Public because it shares the <c>Hotkey:</c> prefix with the
    /// per-preset mappings, so <see cref="ProfileHotkeyManager.OrphanedMappingKeys"/> has to know to skip
    /// it — reconciling that prefix against the presets on disk would otherwise reap this key, which has
    /// no preset behind it by design.
    /// </summary>
    public const string MapKey = "Hotkey:CycleMonitor";
    private readonly GlobalHotkeyService _hotkeys;
    private readonly MonitorCycleService _cycle;
    private int _id;
    private int _backId; // the same chord with Shift added: the previous display instead of the next

    /// <summary>The chord that cycles the other way: <paramref name="chord"/> with Shift added. Null when it
    /// already holds Shift, since there is then no modifier left to tell the two directions apart.</summary>
    public static HotkeyChord? BackChord(HotkeyChord chord) =>
        chord.Modifiers.HasFlag(KeyModifiers.Shift) ? null : chord with { Modifiers = chord.Modifiers | KeyModifiers.Shift };

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
        RegisterBack(chord);
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
            if (id != 0) { _id = id; RegisterBack(chord); }
        }
    }

    // Best-effort: if another app owns the Shift chord, the forward hotkey still works on its own. A chord
    // the user gave a preset is left to the preset: the backwards hotkey is a bonus nobody asked for by
    // name, and registering it first would quietly take a shortcut they did ask for.
    private void RegisterBack(HotkeyChord chord)
    {
        if (BackChord(chord) is { } back && !IsAssignedElsewhere(back)) _backId = _hotkeys.TryRegister(back);
    }

    private static bool IsAssignedElsewhere(HotkeyChord chord)
    {
        foreach (var key in AppSettings.Keys(ProfileHotkeyManager.MappingKeyPrefix))
            if (key != MapKey && HotkeyChord.TryParse(AppSettings.Get(key), out var other) && other == chord)
                return true;
        return false;
    }

    /// <summary>Whether <paramref name="claimed"/> is the Shift variant this hotkey registered for
    /// <paramref name="monitorChord"/>. Pure, so the decision is testable without Win32.</summary>
    internal static bool OwnsBackChord(HotkeyChord? monitorChord, HotkeyChord claimed) =>
        monitorChord is not null && BackChord(monitorChord) == claimed;

    /// <summary>Let go of the backwards hotkey when a preset is being given the same chord. The backwards
    /// hotkey is registered without being asked for, so it gives way to a shortcut that is.</summary>
    public void ReleaseBackChordIfClaimed(HotkeyChord claimed)
    {
        if (_backId == 0 || !OwnsBackChord(GetChord(), claimed)) return;
        _hotkeys.Unregister(_backId);
        _backId = 0;
    }

    private void Unregister()
    {
        if (_id != 0) { _hotkeys.Unregister(_id); _id = 0; }
        if (_backId != 0) { _hotkeys.Unregister(_backId); _backId = 0; }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == 0) return;
        if (id == _id) _ = _cycle.CycleAsync();
        else if (id == _backId) _ = _cycle.CycleAsync(backwards: true);
    }

    public void Dispose()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        Unregister(); // NOT _hotkeys.Dispose() — the shared service is owned by the shell
    }
}
