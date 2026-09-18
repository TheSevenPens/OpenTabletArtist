using Avalonia;
using OpenTabletArtist.Services;

namespace OpenTabletArtist;

class Program
{
    /// <summary>Single-instance guard (#191); App reads this to listen for second-instance activation.</summary>
    public static SingleInstance Instance { get; } = new();

    /// <summary>Tray-only launch from the Windows Run key (#381).</summary>
    public static bool LaunchInBackground { get; private set; }

    /// <summary>Argument that runs the packaged-bundle check and exits, without starting the UI (#741).</summary>
    public const string VerifyBundleArgument = "--verify-bundle";

    [STAThread]
    public static void Main(string[] args)
    {
        // Runs before the single-instance guard and before any Avalonia setup: this is a check the
        // release workflow runs against the packaged output, and it must not depend on — or interfere
        // with — a copy of the app the developer happens to have open.
        //
        // The report goes to a file rather than stdout because this is a WinExe: on Windows it has no
        // console attached, so anything written to Console would vanish in CI.
        if (args.Contains(VerifyBundleArgument, StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunBundleVerification(args));
            return;
        }

        LaunchInBackground = args.Contains(StartupService.BackgroundArgument, StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Writes the bundle report next to the argument that follows <see cref="VerifyBundleArgument"/>
    /// (or to <c>bundle-verification.txt</c> beside the app), and returns 0 only when every component is
    /// present and consistent.</summary>
    private static int RunBundleVerification(string[] args)
    {
        var index = Array.FindIndex(args,
            a => string.Equals(a, VerifyBundleArgument, StringComparison.OrdinalIgnoreCase));
        var outputPath = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith('-')
            ? args[index + 1]
            : Path.Combine(AppContext.BaseDirectory, "bundle-verification.txt");

        var checks = BundleVerification.Run(AppContext.BaseDirectory);
        var report = BundleVerification.Report(checks);

        try { File.WriteAllText(outputPath, report); }
        catch (Exception ex)
        {
            // Nowhere to report the report. Say so through the exit code at least.
            System.Diagnostics.Debug.WriteLine($"Couldn't write {outputPath}: {ex.Message}");
            return 2;
        }

        return checks.All(c => c.Ok) ? 0 : 1;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
