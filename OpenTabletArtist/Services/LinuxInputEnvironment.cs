using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>What a probe of the Linux tablet prerequisites found.</summary>
public sealed record LinuxInputStatus
{
    public bool UdevRulesInstalled { get; init; } = true;
    public bool ConflictingModulesBlacklisted { get; init; } = true;
    public IReadOnlyList<string> LoadedConflictingModules { get; init; } = [];
    public LinuxHidAccess HidAccess { get; init; } = LinuxHidAccess.Ok;

    /// <summary>Every default is "fine", so a platform that isn't Linux — or a probe that couldn't read
    /// what it needed — raises nothing. A check that can't see is not a check that failed.</summary>
    public static readonly LinuxInputStatus Fine = new();
}

/// <summary>
/// Probes the things a Linux box needs before a tablet works at all: OpenTabletDriver's udev rules, the
/// kernel modules that grab tablets before OTD can, and permission to open the HID devices.
///
/// These were in <c>tools/OtdLinuxSetup</c>, a separate Avalonia app that nothing built and nothing
/// shipped (#779). The knowledge was good and the place was wrong — a user whose tablet doesn't work on
/// Linux looks at the app, not at a tools directory in the source tree.
///
/// Detection only. The fixes are privileged (writing udev rules, blacklisting kernel modules,
/// regenerating the initramfs, adding the user to a group), which is a different decision from surfacing
/// the problem, so the health copy tells the user what to do rather than doing it.
/// </summary>
public static class LinuxInputEnvironment
{
    private const string UdevRulesPath = "/etc/udev/rules.d/99-opentabletdriver.rules";
    private const string PackagedUdevRulesPath = "/usr/lib/udev/rules.d/70-opentabletdriver.rules";

    /// <summary>The modules that bind tablet HID devices before OpenTabletDriver can.</summary>
    private static readonly string[] ConflictingModules = ["wacom", "hid_uclogic"];

    // Health re-evaluates every 3 seconds, and this reads several files and opens every hidraw node — far
    // too heavy for that cadence. Unlike the tray probe it can't be resolved once for the session either:
    // the whole point is that the user goes and fixes something and comes back. So: cache, briefly.
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);
    private static readonly object Gate = new();
    private static LinuxInputStatus? _cached;
    private static long _cachedAtTicks;

    /// <summary>The current state, re-probed at most every <see cref="CacheFor"/>.</summary>
    public static LinuxInputStatus Current
    {
        get
        {
            if (!OperatingSystem.IsLinux()) return LinuxInputStatus.Fine;

            lock (Gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (_cached != null && Stopwatch.GetElapsedTime(_cachedAtTicks, now) < CacheFor)
                    return _cached;

                _cached = Probe();
                _cachedAtTicks = now;
                return _cached;
            }
        }
    }

    /// <summary>Drops the cache so the next read re-probes — for after the user says they've fixed
    /// something and expects the app to notice.</summary>
    public static void Invalidate()
    {
        lock (Gate) _cached = null;
    }

    private static LinuxInputStatus Probe()
    {
        try
        {
            return new LinuxInputStatus
            {
                UdevRulesInstalled = File.Exists(UdevRulesPath) || File.Exists(PackagedUdevRulesPath),
                ConflictingModulesBlacklisted = ProbeBlacklisted(),
                LoadedConflictingModules = ProbeLoadedModules(),
                HidAccess = ProbeHidAccess(),
            };
        }
        catch (Exception ex)
        {
            // An unreadable /proc or /etc is not evidence of a misconfigured machine.
            AppLog.Warn("Couldn't probe the Linux tablet prerequisites; reporting nothing.", ex);
            return LinuxInputStatus.Fine;
        }
    }

    private static bool ProbeBlacklisted()
    {
        string[] paths = ["/etc/modprobe.d/99-opentabletdriver.conf", "/etc/modprobe.d/blacklist.conf"];
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path) && AllModulesBlacklisted(File.ReadAllText(path), ConflictingModules))
                    return true;
            }
            catch { /* unreadable file: try the next */ }
        }
        return false;
    }

    private static IReadOnlyList<string> ProbeLoadedModules()
    {
        try { return LoadedFrom(File.ReadAllText("/proc/modules"), ConflictingModules); }
        catch { return []; }
    }

    private static LinuxHidAccess ProbeHidAccess()
    {
        string[] devices;
        try { devices = Directory.GetFiles("/dev", "hidraw*"); }
        catch { return LinuxHidAccess.Ok; }

        // No HID devices at all means nothing to be denied — "plug in a tablet" is not a permissions fault.
        if (devices.Length == 0) return LinuxHidAccess.Ok;
        if (devices.Any(CanOpen)) return LinuxHidAccess.Ok;

        // Nothing openable. If the group is already granted on disk, the grant just isn't live in this
        // session yet, which is a different message and a much less alarming one.
        return UserIsInGroupOnDisk("input") && !ProcessIsInGroup("input")
            ? LinuxHidAccess.PendingReboot
            : LinuxHidAccess.Blocked;
    }

    private static bool CanOpen(string device)
    {
        try
        {
            // Share read/write: the daemon holds these open too, and hidraw allows multiple readers. We
            // only want to know whether the open is permitted, so nothing is read.
            using var _ = new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1,
                FileOptions.None);
            return true;
        }
        catch { return false; }
    }

    private static bool UserIsInGroupOnDisk(string group)
    {
        try { return GroupContains(File.ReadAllText("/etc/group"), group, Environment.UserName); }
        catch { return false; }
    }

    private static bool ProcessIsInGroup(string group)
    {
        try
        {
            var psi = new ProcessStartInfo("id", "-Gn")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(3000)) return false;
            return output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Any(g => g == group);
        }
        catch { return false; }
    }

    /// <summary>
    /// True while a per-user systemd manager is running, which is why the pending-grant message says
    /// reboot rather than log out: the manager survives a logout and hands its stale group set to every
    /// app it launches afterwards, so the new group never takes effect. This detail cost someone a
    /// confusing afternoon once; it is the main thing worth carrying over from the old tool.
    /// </summary>
    public static bool UserManagerRunning()
    {
        try
        {
            // $XDG_RUNTIME_DIR/systemd exists exactly while the user manager is up.
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return !string.IsNullOrEmpty(runtimeDir)
                   && Directory.Exists(Path.Combine(runtimeDir, "systemd"));
        }
        catch { return false; }
    }

    // --- Pure parsers, separated so they can be tested off Linux --------------------------------

    /// <summary>True when every one of <paramref name="modules"/> is blacklisted by
    /// <paramref name="modprobeConf"/>. Matches whole words, so "blacklist wacom_foo" does not count as
    /// blacklisting "wacom", and tolerates the leading whitespace and comments real files carry.</summary>
    public static bool AllModulesBlacklisted(string modprobeConf, IEnumerable<string> modules)
    {
        var blacklisted = modprobeConf
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[0] == "blacklist")
            .Select(parts => parts[1])
            .ToHashSet(StringComparer.Ordinal);

        return modules.All(blacklisted.Contains);
    }

    /// <summary>Which of <paramref name="modules"/> appear in <c>/proc/modules</c> content. The module
    /// name is the first field of each line; substring matching would report "wacom" for "wacom_w8001"
    /// and for any module merely listing it as a dependency.</summary>
    public static IReadOnlyList<string> LoadedFrom(string procModules, IEnumerable<string> modules)
    {
        var loaded = procModules
            .Split('\n')
            .Select(line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal!);

        return modules.Where(loaded.Contains).ToList();
    }

    /// <summary>True when <paramref name="user"/> is a member of <paramref name="group"/> per the
    /// <c>/etc/group</c> content given.</summary>
    public static bool GroupContains(string etcGroup, string group, string user)
    {
        foreach (var line in etcGroup.Split('\n'))
        {
            var parts = line.TrimEnd('\r').Split(':');
            if (parts.Length < 4 || parts[0] != group) continue;
            return parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(m => m.Trim() == user);
        }
        return false;
    }
}
