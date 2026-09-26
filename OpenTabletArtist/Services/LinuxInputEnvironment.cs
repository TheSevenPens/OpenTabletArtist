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
            var modules = OtdHealth.Collector.HostProbes.LinuxModules();
            var access = OtdHealth.Collector.HostProbes.LinuxAccess();
            return new LinuxInputStatus
            {
                UdevRulesInstalled = OtdHealth.Collector.HostProbes.LinuxUdev(),
                ConflictingModulesBlacklisted = !modules.NotBlacklisted,
                LoadedConflictingModules = modules.Loaded,
                HidAccess = access.Access switch
                {
                    OtdHealth.HidAccessStatus.Blocked => LinuxHidAccess.Blocked,
                    OtdHealth.HidAccessStatus.PendingRestart => LinuxHidAccess.PendingReboot,
                    _ => LinuxHidAccess.Ok,
                },
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't probe the Linux tablet prerequisites.", ex);
            return LinuxInputStatus.Fine;
        }
    }

    public static bool UserManagerRunning()
    {
        try { return OtdHealth.Collector.HostProbes.UserManagerRunning(); }
        catch { return false; }
    }
    public static bool AllModulesBlacklisted(string text, IEnumerable<string> modules) =>
        OtdHealth.Collector.HostProbes.AllModulesBlacklisted(text, modules);
    public static IReadOnlyList<string> LoadedFrom(string text, IEnumerable<string> modules) =>
        OtdHealth.Collector.HostProbes.LoadedFrom(text, modules);
    public static bool GroupContains(string text, string group, string user) =>
        OtdHealth.Collector.HostProbes.GroupContains(text, group, user);
}
