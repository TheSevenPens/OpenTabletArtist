using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>
/// Controls the OpenTabletDriver systemd <b>user</b> service that OTD's packaged install ships
/// (<c>opentabletdriver.service</c>, <c>ExecStart=otd-daemon</c>). It runs as the current user, so starting
/// it needs no root/pkexec — a plain <c>systemctl --user start</c>. Linux-only; off Linux (or if systemctl
/// is absent) <see cref="IsActive"/> reports false and <see cref="StartAsync"/> returns a failure, never
/// throwing.
/// </summary>
public static class OtdSystemdService
{
    /// <summary>The user unit the OTD RPM/DEB installs.</summary>
    public const string Unit = "opentabletdriver.service";

    /// <summary>True if the user service is currently active (running). False off Linux, if systemctl is
    /// missing, or on any error.</summary>
    public static bool IsActive()
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            // is-active exits 0 only when the unit is fully active (an auto-restart loop reads as not-active).
            return Run("is-active", timeoutMs: 2000).exit == 0;
        }
        catch { return false; }
    }

    /// <summary>Start the user service (off the calling thread). Returns (true, null) on success,
    /// (false, message) on failure.</summary>
    public static Task<(bool ok, string? error)> StartAsync() => Task.Run(() =>
    {
        if (!OperatingSystem.IsLinux())
            return (false, (string?)"The OpenTabletDriver service is only available on Linux.");
        try
        {
            var (exit, output) = Run("start", timeoutMs: 15000);
            if (exit == 0) return (true, (string?)null);
            return (false, string.IsNullOrWhiteSpace(output) ? $"systemctl exited with code {exit}." : output.Trim());
        }
        catch (Exception ex)
        {
            return (false, (string?)ex.Message);
        }
    });

    // Run `systemctl --user <verb> opentabletdriver.service`, returning the exit code and combined
    // stdout+stderr (small, so reading after WaitForExit can't deadlock the pipe).
    private static (int exit, string output) Run(string verb, int timeoutMs)
    {
        var psi = new ProcessStartInfo("systemctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--user");
        psi.ArgumentList.Add(verb);
        psi.ArgumentList.Add(Unit);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch systemctl.");
        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(); } catch { /* best effort */ }
            throw new TimeoutException("systemctl did not respond in time.");
        }
        var output = proc.StandardError.ReadToEnd() + proc.StandardOutput.ReadToEnd();
        return (proc.ExitCode, output);
    }
}
