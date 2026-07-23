using System;
using System.IO;
using System.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// Ensures only one instance of the app runs at a time (#191). The first ("primary") instance owns a
/// named <see cref="Mutex"/> and listens on a named <see cref="EventWaitHandle"/>; a second instance
/// detects the mutex, signals that event (so the primary surfaces its window) and exits immediately —
/// avoiding the duplicate window + duplicate tray icon you'd otherwise get.
///
/// Windows-only: the named-event signalling isn't supported on other platforms, so there it's a no-op
/// (every launch is treated as primary). Cross-platform single-instance is out of scope for now
/// (see the macOS/Linux investigations, #140/#192).
///
/// The name suffix is injectable so tests can use an isolated mutex/event per run.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _mutexName;
    private readonly string _eventName;

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registration;
    private FileStream? _lockFile;

    public SingleInstance(string? key = null)
    {
        var suffix = string.IsNullOrEmpty(key) ? "" : "." + key;
        _mutexName = "OpenTabletArtist.SingleInstance.Mutex" + suffix;
        _eventName = "OpenTabletArtist.SingleInstance.Show" + suffix;
    }

    /// <summary>True once <see cref="TryAcquire"/> has decided this is the primary instance.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>
    /// Claim primary ownership. Returns true if this is the first instance (the caller should run
    /// normally); false if another instance already holds it (the caller should exit — the existing
    /// instance has been signalled to surface its window). Always true off Windows.
    /// </summary>
    public bool TryAcquire()
    {
        if (OperatingSystem.IsLinux())
            return TryAcquireLinux();

        if (!OperatingSystem.IsWindows())
        {
            IsPrimary = true;
            return true;
        }

        _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        IsPrimary = createdNew;

        if (!IsPrimary)
        {
            // A primary instance already exists — nudge it to show its window, then we exit.
            try
            {
                if (EventWaitHandle.TryOpenExisting(_eventName, out var existing))
                    using (existing) existing.Set();
            }
            catch { /* best-effort; worst case the user clicks the tray icon themselves */ }
        }

        return IsPrimary;
    }

    private bool TryAcquireLinux()
    {
        var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                  ?? Path.Combine(Path.GetTempPath(), $"opentabletartist-{Environment.UserName}");
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, _mutexName + ".lock");
        try
        {
            // FileShare.None ensures exclusive access — a second instance's open will throw IOException.
            _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            IsPrimary = true;
        }
        catch (IOException)
        {
            IsPrimary = false; // another instance holds the lock
        }
        return IsPrimary;
    }

    /// <summary>
    /// Primary-only: run <paramref name="onActivate"/> whenever a later instance signals (i.e. the
    /// user relaunched the app). The callback runs on a thread-pool thread, so it must marshal to the
    /// UI thread before touching the window.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        if (!IsPrimary || !OperatingSystem.IsWindows()) return;

        _showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, _eventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onActivate(), state: null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _showEvent?.Dispose();
        _showEvent = null;

        if (_lockFile != null)
        {
            _lockFile.Dispose(); // releasing the stream releases the exclusive file handle
            _lockFile = null;
        }

        if (_mutex != null)
        {
            try { if (IsPrimary) _mutex.ReleaseMutex(); } catch { /* not owned / already released */ }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
