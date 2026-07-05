using System;
using Microsoft.Win32;

namespace OpenTabletArtist.Services;

/// <summary>
/// Manages whether OpenTabletArtist launches when the user signs in to Windows, via the per-user
/// <c>HKCU\…\CurrentVersion\Run</c> registry key (no admin needed). Registers
/// <c>"{exe}" --background</c> so sign-in starts tray-only (#381). Best-effort: registry failures
/// return false so a policy-locked machine never crashes the app.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenTabletArtist";

    /// <summary>Command-line flag for tray-only launch at sign-in (#381).</summary>
    public const string BackgroundArgument = "--background";

    /// <summary>Whether this platform supports the run-at-startup toggle (Windows only).</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>True when the Run entry points at the current executable (with or without --background).</summary>
    public static bool IsEnabled => GetState() == StartupRegistryState.Enabled;

    /// <summary>Registry state for the startup toggle (#382).</summary>
    public static StartupRegistryState GetState()
    {
        if (!OperatingSystem.IsWindows()) return StartupRegistryState.Unsupported;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(value)) return StartupRegistryState.Disabled;
            return RunValueMatchesCurrent(value) ? StartupRegistryState.Enabled : StartupRegistryState.StalePath;
        }
        catch
        {
            return StartupRegistryState.Disabled;
        }
    }

    /// <summary>Builds the Run-key value: quoted exe + background flag (#381).</summary>
    public static string FormatRunValue(string exePath) => $"\"{exePath}\" {BackgroundArgument}";

    /// <summary>Parses a Run-key value into exe path and whether --background is present.</summary>
    public static bool TryParseRunValue(string? value, out string exePath, out bool hasBackground)
    {
        exePath = "";
        hasBackground = false;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end < 0) return false;
            exePath = trimmed[1..end];
            var rest = trimmed[(end + 1)..].Trim();
            hasBackground = rest.Contains(BackgroundArgument, StringComparison.OrdinalIgnoreCase);
            return !string.IsNullOrEmpty(exePath);
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        exePath = parts[0].Trim('"');
        hasBackground = trimmed.Contains(BackgroundArgument, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrEmpty(exePath);
    }

    /// <summary>Whether a stored Run value targets the current process executable (#382).</summary>
    public static bool RunValueMatchesCurrent(string? value)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current) || !TryParseRunValue(value, out var exe, out _)) return false;
        return string.Equals(exe, current, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds or removes the startup entry. No-op (returns false) if it can't be written.</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return false;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, FormatRunValue(exe));
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Run-key state for the startup toggle (#382).</summary>
public enum StartupRegistryState
{
    Unsupported,
    Disabled,
    Enabled,
    StalePath,
}
