using Avalonia;
using OpenTabletArtist.Services;

namespace OpenTabletArtist;

class Program
{
    /// <summary>Single-instance guard (#191); App reads this to listen for second-instance activation.</summary>
    public static SingleInstance Instance { get; } = new();

    [STAThread]
    public static void Main(string[] args)
    {
        // If another instance is already running, it's been signalled to surface its window — exit
        // now so we don't spawn a duplicate window + tray icon.
        if (!Instance.TryAcquire())
        {
            Instance.Dispose();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance.Dispose();
        }

        // By here the app has shut down gracefully: the window's Closed handler already ran the full
        // MainViewModel.Dispose() chain (cancelling the connect/poll loops, disposing the daemon RPC +
        // pipe, destroying the hotkey window, stopping the foreground watcher, etc.). Force immediate
        // process termination so the .exe file lock is released at once, instead of lingering for a
        // few seconds while the CLR drains background threads (StreamJsonRpc reader, thread-pool waits)
        // and runs finalizers — which was blocking rebuilds right after Quit (#58). The bundled OTD
        // daemon is a separate process and is unaffected.
        Environment.Exit(0);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
