using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OtdHealth.Collector;

public static class HostProbes
{
    public static HealthPlatform Platform => OperatingSystem.IsWindows() ? HealthPlatform.Windows
        : OperatingSystem.IsLinux() ? HealthPlatform.Linux : OperatingSystem.IsMacOS() ? HealthPlatform.MacOS : HealthPlatform.Unspecified;
    public static bool VMulti() => VMultiDetector.ReadInstalled();
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) throw new ProbeUnavailableException("Windows process elevation only.", true);
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static IReadOnlyList<DisplayBounds> Displays()
    {
        if (!OperatingSystem.IsWindows()) throw new ProbeUnavailableException("Headless display enumeration is currently implemented on Windows only. Supply a display source on other platforms.", true);
        // OTD stores physical pixels. A console host may be DPI-unaware; temporarily make this
        // worker thread per-monitor aware so Windows cannot virtualize the returned coordinates.
        nint previous = SetThreadDpiAwarenessContext(new nint(-4));
        if (previous == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<DisplayBounds>();
        MonitorCallback callback = (nint monitor, nint dc, ref Rect bounds, nint data) =>
        {
            result.Add(new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            return true;
        };
        try
        {
            if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            GC.KeepAlive(callback);
            return result;
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect bounds, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);

    private static readonly string[] Modules = ["wacom", "hid_uclogic"];
    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux()) throw new ProbeUnavailableException("This source requires a Linux host.", true);
    }
    public static bool LinuxUdev()
    {
        RequireLinux();
        return FileProbes.Exists("/etc/udev/rules.d/99-opentabletdriver.rules")
            || FileProbes.Exists("/usr/lib/udev/rules.d/70-opentabletdriver.rules");
    }
    public static LinuxModulesObservation LinuxModules()
    {
        RequireLinux();
        string[] paths = ["/etc/modprobe.d/99-opentabletdriver.conf", "/etc/modprobe.d/blacklist.conf"];
        var configs = paths.Where(FileProbes.Exists).Select(File.ReadAllText);
        return new(LoadedFrom(File.ReadAllText("/proc/modules"), Modules), !AllModulesBlacklisted(string.Join("\n", configs), Modules));
    }
    public static LinuxAccessObservation LinuxAccess()
    {
        RequireLinux();
        var devices = Directory.GetFiles("/dev", "hidraw*");
        var state = HidAccessStatus.NoProblemReported;
        if (devices.Length > 0 && !devices.Any(CanOpen))
        {
            var groups = File.ReadAllText("/etc/group");
            string? groupId = groups.Split('\n').Select(l => l.Split(':')).FirstOrDefault(p => p.Length >= 4 && p[0] == "input")?[2];
            var active = File.ReadAllLines("/proc/self/status").Where(l => l.StartsWith("Groups:", StringComparison.Ordinal) || l.StartsWith("Gid:", StringComparison.Ordinal))
                .SelectMany(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            state = GroupContains(groups, "input", Environment.UserName) && !active.Contains(groupId)
                ? HidAccessStatus.PendingRestart : HidAccessStatus.Blocked;
        }
        return new(state, UserManagerRunning());
    }
    private static bool CanOpen(string device)
    {
        try
        {
            using var stream = new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
            return true; // Permission check only; no report is read or written.
        }
        catch (UnauthorizedAccessException) { return false; }
        // Device removal or I/O failure is not proof of permission denial.
    }
    public static bool UserManagerRunning()
    {
        var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrEmpty(dir) && FileProbes.Exists(Path.Combine(dir, "systemd"));
    }
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
