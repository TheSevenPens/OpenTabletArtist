using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// Ensures only one instance of the app runs at a time (#191). The first ("primary") instance holds the
/// claim and listens for activation; a second instance detects the primary, signals it to surface its
/// window, and exits immediately — avoiding the duplicate window + duplicate tray icon you'd otherwise get.
///
/// The signalling is platform-specific:
/// <list type="bullet">
/// <item><b>Windows</b> — a named <see cref="Mutex"/> for the claim + a named <see cref="EventWaitHandle"/>
/// the second instance sets.</item>
/// <item><b>Linux</b> — an exclusive lock file for the claim + a Unix-domain socket the second instance
/// connects to; the connection is the whole signal. Fully raises the window on X11; on Wayland the
/// compositor may only flag the window for attention rather than foreground it, since it forbids an app
/// from raising itself (#192).</item>
/// <item><b>Other (macOS)</b> — a no-op: every launch is treated as primary (#140).</item>
/// </list>
///
/// The name suffix is injectable so tests can use an isolated claim/channel per run.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _mutexName;
    private readonly string _eventName;

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registration;
    private FileStream? _lockFile;

    // Linux activation channel: the primary binds/listens on this socket, a second instance connects to it.
    private string? _socketPath;
    private Socket? _listenSocket;
    private Thread? _acceptThread;

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
        _socketPath = Path.Combine(dir, _eventName + ".sock"); // where the primary listens for a nudge
        try
        {
            // FileShare.None ensures exclusive access — a second instance's open will throw IOException.
            _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            IsPrimary = true;
        }
        catch (IOException)
        {
            IsPrimary = false;   // another instance holds the lock
            NudgePrimaryLinux(); // ask it to surface its window (best-effort)
        }
        return IsPrimary;
    }

    /// <summary>Second-instance side: connect to the primary's socket to signal it. The connection itself
    /// is the signal (no payload); the primary's accept loop reacts. Best-effort — if the primary is still
    /// starting up (socket not yet bound) or the file is stale, the connect just fails and we exit anyway.</summary>
    private void NudgePrimaryLinux()
    {
        if (_socketPath == null) return;
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_socketPath));
        }
        catch { /* primary not listening (yet) — worst case the user surfaces the window themselves */ }
    }

    /// <summary>
    /// Primary-only: run <paramref name="onActivate"/> whenever a later instance signals (i.e. the
    /// user relaunched the app). The callback runs on a thread-pool thread, so it must marshal to the
    /// UI thread before touching the window.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        if (!IsPrimary) return;

        if (OperatingSystem.IsLinux())
        {
            ListenForActivationLinux(onActivate);
            return;
        }

        if (!OperatingSystem.IsWindows()) return;

        _showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, _eventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onActivate(), state: null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private void ListenForActivationLinux(Action onActivate)
    {
        if (_socketPath == null) return;
        try
        {
            // We hold the lock, so any leftover socket file is from a crashed prior primary — remove it so
            // Bind doesn't fail with "address already in use".
            if (File.Exists(_socketPath)) File.Delete(_socketPath);

            _listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listenSocket.Bind(new UnixDomainSocketEndPoint(_socketPath));
            _listenSocket.Listen(backlog: 4);
        }
        catch
        {
            _listenSocket?.Dispose();
            _listenSocket = null;
            return; // dedup still works; we just can't be nudged to the front
        }

        // Blocking accept loop on a background thread. Disposing the listener unblocks Accept (it throws),
        // which ends the loop. Each connection is one "surface the window" request.
        _acceptThread = new Thread(() => AcceptLoop(_listenSocket, onActivate))
        {
            IsBackground = true,
            Name = "OTA-single-instance",
        };
        _acceptThread.Start();
    }

    private static void AcceptLoop(Socket listener, Action onActivate)
    {
        while (true)
        {
            Socket client;
            try { client = listener.Accept(); }
            catch { break; } // listener disposed on shutdown (or a fatal socket error) — stop listening
            try { onActivate(); } catch { /* activation callback must never kill the loop */ }
            try { client.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _showEvent?.Dispose();
        _showEvent = null;

        // Closing the listener unblocks the accept loop (Accept throws → the thread exits).
        if (_listenSocket != null)
        {
            try { _listenSocket.Dispose(); } catch { }
            _listenSocket = null;
            _acceptThread?.Join(500); // best-effort; it's a background thread so exit isn't blocked regardless
            _acceptThread = null;
        }
        if (_socketPath != null && IsPrimary)
        {
            try { if (File.Exists(_socketPath)) File.Delete(_socketPath); } catch { }
        }

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
