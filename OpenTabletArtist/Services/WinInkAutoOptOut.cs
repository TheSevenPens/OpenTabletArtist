using System;
using System.Collections.Generic;

namespace OpenTabletArtist.Services;

/// <summary>
/// Tablets the user has deliberately set to a non–Windows Ink output mode. Auto-setup (#380) skips them.
/// Cleared when the user switches back to a Windows Ink mode.
/// </summary>
public static class WinInkAutoOptOut
{
    private const string Key = "WinInkAutoOptOut";
    private static readonly char[] Separator = ['\n'];

    public static bool IsOptedOut(string tablet) =>
        !string.IsNullOrEmpty(tablet) && Load().Contains(tablet);

    public static void OptOut(string tablet)
    {
        if (string.IsNullOrEmpty(tablet)) return;
        var set = Load();
        if (!set.Add(tablet)) return;
        Save(set);
    }

    public static void Clear(string tablet)
    {
        if (string.IsNullOrEmpty(tablet)) return;
        var set = Load();
        if (!set.Remove(tablet)) return;
        Save(set);
    }

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = AppSettings.Get(Key);
        if (!string.IsNullOrEmpty(raw))
            foreach (var name in raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
                set.Add(name);
        return set;
    }

    private static void Save(HashSet<string> set) =>
        AppSettings.Set(Key, string.Join('\n', set));
}
