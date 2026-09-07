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

    /// <summary>Prefix every hotkey mapping is persisted under — shared with the monitor-cycle hotkey,
    /// which owns the one reserved key under it (see <see cref="OrphanedMappingKeys"/>).</summary>
    public const string MappingKeyPrefix = "Hotkey:";

    private static string MapKey(string snapshot) => MappingKeyPrefix + snapshot;

    /// <summary>
    /// Which of <paramref name="mappingKeys"/> no longer have a preset behind them. Pure, so the rule is
    /// testable without Win32 or a settings file — <see cref="Sync"/> is the only caller.
    /// <para>
    /// Two things it must get right. The monitor-cycle hotkey sits under the same prefix and is not a
    /// preset, so it is skipped by name; reaping it would silently delete the display-toggle shortcut.
    /// And snapshot names are matched case-insensitively, as everywhere else here, because the file
    /// system is.
    /// </para>
    /// </summary>
    public static List<string> OrphanedMappingKeys(
        IEnumerable<string> mappingKeys, IEnumerable<string> snapshotNames)
    {
        var names = new HashSet<string>(snapshotNames, StringComparer.OrdinalIgnoreCase);
        var orphans = new List<string>();
        foreach (var key in mappingKeys)
        {
            if (!key.StartsWith(MappingKeyPrefix, StringComparison.Ordinal)) continue;
            if (string.Equals(key, MonitorCycleHotkeys.MapKey, StringComparison.Ordinal)) continue;

            var snapshot = key[MappingKeyPrefix.Length..];
            if (snapshot.Length > 0 && !names.Contains(snapshot)) orphans.Add(key);
        }
        return orphans;
    }

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

        // Reconcile what is PERSISTED against what is on disk. This used to walk _snapshotToId — the
        // registrations this process had made — which misses a mapping in two ordinary cases: the app
        // was closed when the preset was deleted (the table is empty on a fresh start, so the loop had
        // nothing to reap), or the chord never registered because another application owns it. Either
        // way the key survived in settings.json forever, and a NEW preset later saved under the same
        // name silently inherited the deleted one's hotkey. The persisted set is a superset of the
        // registered one, so this only ever reaps more than before, never less.
        foreach (var key in OrphanedMappingKeys(AppSettings.Keys(MappingKeyPrefix), names))
            ClearHotkey(key[MappingKeyPrefix.Length..]);

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
        // The result is discarded on purpose: ProfileSwitchService raises Switched on success and
        // SwitchFailed on failure, and the shell toasts both. Discarding it used to mean a press whose
        // preset had been deleted did nothing and said nothing.
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
