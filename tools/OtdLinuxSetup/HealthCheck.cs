using System.Diagnostics;
using System.Text;

namespace OtdLinuxSetup;

public enum CheckStatus { Unknown, Healthy, Unhealthy, PendingRelogin }

public sealed class HealthItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; set; } = "";
    public CheckStatus Status { get; set; } = CheckStatus.Unknown;
    public string? FixLabel { get; init; }
    public Func<IProgress<string>, Task<string>>? Fix { get; init; }
    public bool CanFix => Status == CheckStatus.Unhealthy && Fix != null;
    public bool IsPendingRelogin => Status == CheckStatus.PendingRelogin;
}

public static class HealthChecker
{
    /// <summary>Path where OTD's udev rules should be installed.</summary>
    private const string UdevRulesPath = "/etc/udev/rules.d/99-opentabletdriver.rules";
    private const string BlacklistPath = "/etc/modprobe.d/99-opentabletdriver.conf";

    public static List<HealthItem> CreateChecks()
    {
        return
        [
            new HealthItem
            {
                Id = "udev",
                Title = "Udev rules",
                FixLabel = "Install rules",
                Fix = p => FixUdevRules(p),
            },
            new HealthItem
            {
                Id = "blacklist",
                Title = "Kernel module blacklist",
                FixLabel = "Blacklist modules",
                Fix = p => FixBlacklist(p),
            },
            new HealthItem
            {
                Id = "modules",
                Title = "Conflicting modules unloaded",
                FixLabel = "Unload now",
                Fix = p => FixUnloadModules(p),
            },
            new HealthItem
            {
                Id = "hidraw",
                Title = "HID device access",
                FixLabel = "Fix permissions",
                Fix = p => FixHidrawAccess(p),
            },
            new HealthItem
            {
                Id = "tablet",
                Title = "Tablet detected",
            },
        ];
    }

    public static void Evaluate(List<HealthItem> checks)
    {
        foreach (var c in checks)
        {
            switch (c.Id)
            {
                case "udev":
                    EvaluateUdev(c);
                    break;
                case "blacklist":
                    EvaluateBlacklist(c);
                    break;
                case "modules":
                    EvaluateModules(c);
                    break;
                case "hidraw":
                    EvaluateHidraw(c);
                    break;
                case "tablet":
                    EvaluateTablet(c);
                    break;
            }
        }
    }

    // --- Evaluators ---

    private static void EvaluateUdev(HealthItem c)
    {
        if (File.Exists(UdevRulesPath))
        {
            var content = File.ReadAllText(UdevRulesPath);
            if (content.Contains("opentabletdriver", StringComparison.OrdinalIgnoreCase))
            {
                c.Status = CheckStatus.Healthy;
                c.Description = $"Rules installed at {UdevRulesPath}";
                return;
            }
        }
        // Also check /usr/lib/udev/rules.d/ (package-installed location)
        const string altPath = "/usr/lib/udev/rules.d/70-opentabletdriver.rules";
        if (File.Exists(altPath))
        {
            c.Status = CheckStatus.Healthy;
            c.Description = $"Rules installed at {altPath}";
            return;
        }
        c.Status = CheckStatus.Unhealthy;
        c.Description = "OTD udev rules not found. Tablets won't be accessible.";
    }

    private static void EvaluateBlacklist(HealthItem c)
    {
        bool blacklisted = false;
        // Check common locations
        string[] paths = [BlacklistPath, "/etc/modprobe.d/blacklist.conf"];
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var content = File.ReadAllText(path);
            if (content.Contains("blacklist wacom") && content.Contains("blacklist hid_uclogic"))
            {
                blacklisted = true;
                c.Description = $"wacom and hid_uclogic blacklisted in {path}";
                break;
            }
        }
        c.Status = blacklisted ? CheckStatus.Healthy : CheckStatus.Unhealthy;
        if (!blacklisted)
            c.Description = "wacom/hid_uclogic not blacklisted — they may grab your tablet before OTD can.";
    }

    private static void EvaluateModules(HealthItem c)
    {
        var loaded = new List<string>();
        if (IsModuleLoaded("wacom")) loaded.Add("wacom");
        if (IsModuleLoaded("hid_uclogic")) loaded.Add("hid_uclogic");

        if (loaded.Count == 0)
        {
            c.Status = CheckStatus.Healthy;
            c.Description = "No conflicting kernel modules loaded.";
        }
        else
        {
            c.Status = CheckStatus.Unhealthy;
            c.Description = $"Loaded: {string.Join(", ", loaded)} — these grab tablet devices before OTD.";
        }
    }

    private static void EvaluateHidraw(HealthItem c)
    {
        var devices = Directory.GetFiles("/dev", "hidraw*");
        if (devices.Length == 0)
        {
            c.Status = CheckStatus.Healthy;
            c.Description = "No HID devices present (plug in a tablet to test).";
            return;
        }
        var inaccessible = devices.Where(d =>
        {
            try
            {
                // Use low-level open check via access() syscall equivalent — avoids blocking on device reads.
                using var fs = new FileStream(d, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1,
                    FileOptions.None);
                return false;
            }
            catch { return true; }
        }).ToList();

        if (inaccessible.Count == 0)
        {
            c.Status = CheckStatus.Healthy;
            c.Description = $"All {devices.Length} HID devices are accessible.";
        }
        else
        {
            // Check if the fix was already applied (user in input group) but isn't active yet.
            if (IsUserInGroupOnDisk("input") && !IsUserInActiveGroup("input"))
            {
                c.Status = CheckStatus.PendingRelogin;
                c.Description = IsUserManagerRunning()
                    ? "Permissions configured — reboot to activate the input group. A logout likely won't "
                      + "suffice: your systemd user manager is still running and hands its old group set to "
                      + "every app it launches."
                    : "Permissions configured — reboot to activate the input group.";
            }
            else
            {
                c.Status = CheckStatus.Unhealthy;
                c.Description = $"{inaccessible.Count}/{devices.Length} HID devices not accessible: {string.Join(", ", inaccessible.Select(Path.GetFileName))}";
            }
        }
    }

    private static void EvaluateTablet(HealthItem c)
    {
        // Look for known tablet vendor IDs in /sys/class/hidraw/*/device/uevent
        var tabletVendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "056A", // Wacom
            "0483", // UC-Logic / Huion / XP-Pen / Gaomon (UC-Logic chipset)
            "28BD", // XP-Pen
            "256C", // Huion
            "3233", // VEIKK
            "5543", // UGEE
            "0458", // Genius
        };

        var found = new List<string>();
        foreach (var hidraw in Directory.GetDirectories("/sys/class/hidraw"))
        {
            var uevent = Path.Combine(hidraw, "device", "uevent");
            if (!File.Exists(uevent)) continue;
            var content = File.ReadAllText(uevent);
            // HID_ID=0003:0000056A:000003F9
            var match = System.Text.RegularExpressions.Regex.Match(content, @"HID_ID=\w+:(\w{8}):(\w{8})");
            if (!match.Success) continue;
            var vid = match.Groups[1].Value.TrimStart('0');
            if (vid.Length < 4) vid = vid.PadLeft(4, '0');
            if (tabletVendors.Contains(vid))
            {
                var name = System.Text.RegularExpressions.Regex.Match(content, @"HID_NAME=(.+)");
                found.Add(name.Success ? name.Groups[1].Value : $"VID:{vid}");
            }
        }

        if (found.Count > 0)
        {
            c.Status = CheckStatus.Healthy;
            c.Description = $"Found: {string.Join(", ", found.Distinct())}";
        }
        else
        {
            c.Status = CheckStatus.Unhealthy;
            c.Description = "No known tablet hardware detected. Is a tablet plugged in?";
        }
    }

    // --- Fixers (run privileged commands via pkexec) ---

    private static async Task<string> FixUdevRules(IProgress<string> progress)
    {
        var repoRoot = FindRepoRoot();
        var script = repoRoot != null
            ? Path.Combine(repoRoot, "external", "OpenTabletDriver", "generate-rules.sh")
            : null;

        if (script == null || !File.Exists(script))
            return "Error: Could not find generate-rules.sh. Are you running from the repo?";

        progress.Report("Step 1/3: Generating udev rules...");
        var tempRules = Path.GetTempFileName();
        var genResult = await RunAsync("bash", script);
        if (genResult.ExitCode != 0)
            return $"Error generating rules: {genResult.StdErr}";
        await File.WriteAllTextAsync(tempRules, genResult.StdOut);

        progress.Report("Step 2/3: Installing rules (authentication required)...");
        var installResult = await RunAsync("pkexec", "bash", "-c",
            $"cp '{tempRules}' '{UdevRulesPath}'");
        File.Delete(tempRules);
        if (installResult.ExitCode != 0)
            return $"Error: {installResult.StdErr}";

        progress.Report("Step 3/3: Reloading udev rules...");
        await RunAsync("pkexec", "bash", "-c",
            "udevadm control --reload-rules && udevadm trigger");

        return "Udev rules installed and reloaded. Replug your tablet.";
    }

    private static async Task<string> FixBlacklist(IProgress<string> progress)
    {
        progress.Report("Step 1/3: Writing module blacklist (authentication required)...");
        var result = await RunAsync("pkexec", "bash", "-c",
            $"printf 'blacklist wacom\\nblacklist hid_uclogic\\n' > '{BlacklistPath}'");
        if (result.ExitCode != 0)
            return $"Error: {result.StdErr}";

        progress.Report("Step 2/3: Regenerating initramfs (this may take a minute)...");
        if (File.Exists("/usr/bin/dracut"))
            await RunAsync("pkexec", "dracut", "--regenerate-all", "--force");
        else if (File.Exists("/usr/sbin/update-initramfs"))
            await RunAsync("pkexec", "update-initramfs", "-u");
        else if (File.Exists("/usr/bin/mkinitcpio"))
            await RunAsync("pkexec", "mkinitcpio", "-P");

        progress.Report("Step 3/3: Done.");
        return "Kernel modules blacklisted. Initramfs updated. Takes effect on next boot.";
    }

    private static async Task<string> FixUnloadModules(IProgress<string> progress)
    {
        progress.Report("Step 1/2: Unloading wacom module...");
        await RunAsync("pkexec", "bash", "-c", "rmmod wacom 2>/dev/null; true");

        progress.Report("Step 2/2: Unloading hid_uclogic module...");
        await RunAsync("pkexec", "bash", "-c", "rmmod hid_uclogic 2>/dev/null; true");

        return "Conflicting modules unloaded. Replug your tablet.";
    }

    private static async Task<string> FixHidrawAccess(IProgress<string> progress)
    {
        var user = Environment.UserName;

        progress.Report("Step 1/3: Writing HID permissions rule (authentication required)...");
        var result = await RunAsync("pkexec", "bash", "-c",
            "echo 'KERNEL==\"hidraw*\", SUBSYSTEM==\"hidraw\", MODE=\"0660\", GROUP=\"input\"' > /etc/udev/rules.d/98-hidraw-permissions.rules");
        if (result.ExitCode != 0)
            return $"Error: {result.StdErr}";

        progress.Report("Step 2/3: Reloading udev rules...");
        await RunAsync("pkexec", "bash", "-c",
            "udevadm control --reload-rules && udevadm trigger");

        progress.Report($"Step 3/3: Adding user '{user}' to input group...");
        var groupResult = await RunAsync("pkexec", "usermod", "-aG", "input", user);
        if (groupResult.ExitCode != 0)
            return $"Error adding to group: {groupResult.StdErr}";

        return $"Permissions fixed. User '{user}' added to input group. Reboot to apply.";
    }

    // --- Helpers ---

    /// <summary>Check if the current user is listed in a group in /etc/group (may not be active yet).</summary>
    private static bool IsUserInGroupOnDisk(string group)
    {
        try
        {
            var user = Environment.UserName;
            foreach (var line in File.ReadLines("/etc/group"))
            {
                var parts = line.Split(':');
                if (parts.Length >= 4 && parts[0] == group)
                {
                    var members = parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    return members.Contains(user, StringComparer.Ordinal);
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>Check if the current process actually has a group in its active group list.</summary>
    private static bool IsUserInActiveGroup(string group)
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
            proc.WaitForExit(3000);
            return output.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(g => g.Trim() == group);
        }
        catch { return false; }
    }

    /// <summary>
    /// True if a per-user systemd manager (<c>systemd --user</c>) is running for this session.
    /// When it is, a plain logout/login often won't refresh group membership — the manager
    /// survives the logout and hands its stale group set to newly launched apps — so a reboot
    /// is the reliable way to activate a freshly added group.
    /// </summary>
    private static bool IsUserManagerRunning()
    {
        try
        {
            // $XDG_RUNTIME_DIR/systemd exists exactly while the user manager is up.
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(runtimeDir))
                return Directory.Exists(Path.Combine(runtimeDir, "systemd"));
        }
        catch { }
        return false;
    }

    private static bool IsModuleLoaded(string module)
    {
        try
        {
            var modules = File.ReadAllText("/proc/modules");
            return modules.Contains(module, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc == null) return (-1, "", "Failed to start process");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, stdout, stderr);
    }
}
