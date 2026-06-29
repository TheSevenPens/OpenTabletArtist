using Avalonia;
using OtdArtist.Services;

namespace OtdArtist;

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
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
