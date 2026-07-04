using System;
using Microsoft.Win32;

namespace OpenTabletArtist.Services;

/// <summary>
/// Manages whether OpenTabletArtist launches when the user signs in to Windows, via the per-user
/// <c>HKCU\…\CurrentVersion\Run</c> registry key (no admin needed). Because the app must be running for
/// hotkeys and per-app profile switching to work, this lets it start with Windows (minimized to the
/// tray). Best-effort: registry failures are swallowed so a policy-locked machine never crashes the app.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenTabletArtist";

    /// <summary>Whether this platform supports the run-at-startup toggle (Windows only).</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>True when the Run entry is present and points at the current executable.</summary>
    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string v && !string.IsNullOrWhiteSpace(v);
            }
            catch
            {
                return false;
            }
        }
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
                key.SetValue(ValueName, $"\"{exe}\""); // quoted so a spaced path still launches
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
