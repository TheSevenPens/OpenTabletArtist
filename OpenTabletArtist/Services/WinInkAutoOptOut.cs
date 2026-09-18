using System;
using System.Collections.Generic;

namespace OpenTabletArtist.Services;

/// <summary>
/// The opt-out set's behaviour, over an injected read/write pair (#738). Separated from the static
/// <see cref="WinInkAutoOptOut"/> facade so it can be tested against an in-memory value instead of
/// writing to the developer's real preference file under <c>%LOCALAPPDATA%</c>.
///
/// Stored as one newline-separated string under a single key, and compared case-insensitively — the
/// daemon's reported tablet casing can drift from the profile's.
/// </summary>
public sealed class WinInkOptOutSet
{
    private static readonly char[] Separator = ['\n'];

    private readonly Func<string?> _read;
    private readonly Action<string> _write;

    public WinInkOptOutSet(Func<string?> read, Action<string> write)
    {
        _read = read;
        _write = write;
    }

    public bool IsOptedOut(string tablet) =>
        !string.IsNullOrEmpty(tablet) && Load().Contains(tablet);

    public void OptOut(string tablet)
    {
        if (string.IsNullOrEmpty(tablet)) return;
        var set = Load();
        if (!set.Add(tablet)) return;
        Save(set);
    }

    public void Clear(string tablet)
    {
        if (string.IsNullOrEmpty(tablet)) return;
        var set = Load();
        if (!set.Remove(tablet)) return;
        Save(set);
    }

    private HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = _read();
        if (!string.IsNullOrEmpty(raw))
            foreach (var name in raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
                set.Add(name);
        return set;
    }

    private void Save(HashSet<string> set) => _write(string.Join('\n', set));
}

/// <summary>
/// Tablets the user has deliberately set to a non–Windows Ink output mode. Auto-setup (#380) skips them.
/// Cleared when the user switches back to a Windows Ink mode.
///
/// A thin facade over <see cref="WinInkOptOutSet"/> bound to the real preference store; the behaviour
/// lives there so it is testable without touching a real install.
/// </summary>
public static class WinInkAutoOptOut
{
    private const string Key = "WinInkAutoOptOut";

    private static readonly WinInkOptOutSet Set =
        new(() => AppSettings.Get(Key), value => AppSettings.Set(Key, value));

    public static bool IsOptedOut(string tablet) => Set.IsOptedOut(tablet);
    public static void OptOut(string tablet) => Set.OptOut(tablet);
    public static void Clear(string tablet) => Set.Clear(tablet);
}
