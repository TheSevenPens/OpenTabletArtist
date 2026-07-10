using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>Raises an event when the foreground application changes (#167). Interface so the switch
/// policy is testable without Win32 and for the later macOS seam (#140).</summary>
public interface IForegroundAppWatcher : IDisposable
{
    /// <summary>Fired on the UI thread when the foreground window's process changes.</summary>
    event Action<AppIdentity>? Changed;
    void Start();
    void Stop();
}

/// <summary>
/// Windows foreground watcher via <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> (#167). The hook is
/// installed on the UI thread — OUTOFCONTEXT callbacks are delivered through the installing thread's
/// message queue, which Avalonia's UI thread pumps — so the callback already runs on the UI thread; it's
/// still marshalled defensively. The delegate is pinned (held in a field) so native code doesn't call a
/// collected callback. Resolving the exe path can fail for elevated/UWP apps; the process name is the
/// fallback (see <see cref="AppIdentity"/>).
/// </summary>
public sealed class Win32ForegroundAppWatcher : IForegroundAppWatcher
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild,
        uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc,
        uint idProcess, uint idThread, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private readonly WinEventProc _proc; // pinned: native code holds this for the hook's lifetime
    private IntPtr _hook;

    public event Action<AppIdentity>? Changed;

    public Win32ForegroundAppWatcher() => _proc = OnWinEvent;

    public void Start()
    {
        // Windows-only (SetWinEventHook is user32). Off-Windows this no-ops rather than throwing, so
        // per-app switching degrades to "unavailable" instead of crashing if the feature is ever enabled
        // there before a macOS foreground-watcher backend exists (NSWorkspace notifications). (#140/#167)
        if (!OperatingSystem.IsWindows() || _hook != IntPtr.Zero) return;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc,
            0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        var identity = Resolve(hwnd);
        if (identity == null) return;
        // Already on the UI thread (OUTOFCONTEXT), but marshal defensively.
        Dispatcher.UIThread.Post(() => Changed?.Invoke(identity));
    }

    private static AppIdentity? Resolve(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            var name = proc.ProcessName;
            if (string.IsNullOrEmpty(name)) return null;
            string path = "";
            try { path = proc.MainModule?.FileName ?? ""; } catch { /* elevated/UWP: name-only */ }
            var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
            return new AppIdentity(path, exeName);
        }
        catch
        {
            return null; // process gone / access denied
        }
    }

    public void Dispose() => Stop();
}
