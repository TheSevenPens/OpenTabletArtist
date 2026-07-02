using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Services;

public enum HotkeySetResult { Ok, Conflict, Invalid }

/// <summary>Snapshot-hotkey operations the Saved Settings page uses (an interface so the page is
/// testable without Win32/disk). Implemented by <see cref="ProfileHotkeyManager"/>. (#320)</summary>
public interface IProfileHotkeys
{
    HotkeyChord? GetChord(string snapshot);
    HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord);
    void ClearHotkey(string snapshot);
    void Sync(IEnumerable<string> snapshotNames);
    void RenameSnapshot(string oldName, string newName);
}

/// <summary>
/// Ties keyboard hotkeys to profile switching (#320): persists a per-snapshot chord in
/// <see cref="AppSettings"/>, (re)registers them with the <see cref="GlobalHotkeyService"/>, and routes
/// a press to <see cref="ProfileSwitchService"/>. Also keeps mappings in sync as snapshots are added,
/// renamed, or deleted so a hotkey never dangles on a gone snapshot. UI-thread only (hotkey (un)register
/// runs on the UI thread with the service).
/// </summary>
public sealed class ProfileHotkeyManager : IProfileHotkeys, IDisposable
{
    private readonly GlobalHotkeyService _hotkeys;
    private readonly ProfileSwitchService _switch;
    private readonly Dictionary<int, string> _idToSnapshot = new();
    private readonly Dictionary<string, int> _snapshotToId = new(StringComparer.OrdinalIgnoreCase);

    public ProfileHotkeyManager(GlobalHotkeyService hotkeys, ProfileSwitchService profileSwitch)
    {
        _hotkeys = hotkeys;
        _switch = profileSwitch;
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
    }

    private static string MapKey(string snapshot) => $"Hotkey:{snapshot}";

    /// <summary>The chord assigned to a snapshot, or null.</summary>
    public HotkeyChord? GetChord(string snapshot)
        => HotkeyChord.TryParse(AppSettings.Get(MapKey(snapshot)), out var c) ? c : null;

    /// <summary>Assign a chord to a snapshot (persist + register). Conflict = another app owns the chord;
    /// Invalid = the chord isn't registerable.</summary>
    public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord)
    {
        if (!chord.IsRegisterable) return HotkeySetResult.Invalid;
        Unregister(snapshot);
        int id = _hotkeys.TryRegister(chord);
        if (id == 0) return HotkeySetResult.Conflict;
        _idToSnapshot[id] = snapshot;
        _snapshotToId[snapshot] = id;
        AppSettings.Set(MapKey(snapshot), chord.Serialize());
        return HotkeySetResult.Ok;
    }

    /// <summary>Remove a snapshot's hotkey (unregister + forget).</summary>
    public void ClearHotkey(string snapshot)
    {
        Unregister(snapshot);
        AppSettings.Remove(MapKey(snapshot));
    }

    /// <summary>Reconcile registrations with the current snapshot set: register any mapped snapshot not
    /// yet active, and drop mappings for snapshots that no longer exist. Call when the preset list changes.</summary>
    public void Sync(IEnumerable<string> snapshotNames)
    {
        var names = new HashSet<string>(snapshotNames, StringComparer.OrdinalIgnoreCase);

        foreach (var gone in _snapshotToId.Keys.Where(n => !names.Contains(n)).ToList())
            ClearHotkey(gone); // dangling mapping (snapshot deleted/renamed elsewhere)

        foreach (var name in names)
        {
            if (_snapshotToId.ContainsKey(name)) continue;
            var chord = GetChord(name);
            if (chord is { IsRegisterable: true })
            {
                int id = _hotkeys.TryRegister(chord);
                if (id != 0) { _idToSnapshot[id] = name; _snapshotToId[name] = id; }
            }
        }
    }

    /// <summary>Move a hotkey mapping when a snapshot is renamed.</summary>
    public void RenameSnapshot(string oldName, string newName)
    {
        var chord = GetChord(oldName);
        ClearHotkey(oldName);
        if (chord != null) SetHotkey(newName, chord);
    }

    private void Unregister(string snapshot)
    {
        if (!_snapshotToId.TryGetValue(snapshot, out var id)) return;
        _hotkeys.Unregister(id);
        _snapshotToId.Remove(snapshot);
        _idToSnapshot.Remove(id);
    }

    private void OnHotkeyPressed(int id)
    {
        if (_idToSnapshot.TryGetValue(id, out var snapshot))
            _ = _switch.SwitchToAsync(snapshot);
    }

    public void Dispose()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        // Drop our own registrations but NOT the service — it's shared with the monitor-cycle hotkey and
        // owned/disposed by the shell (#89).
        foreach (var id in _idToSnapshot.Keys.ToList()) _hotkeys.Unregister(id);
        _idToSnapshot.Clear();
        _snapshotToId.Clear();
    }
}
