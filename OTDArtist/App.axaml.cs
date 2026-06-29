using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OtdArtist.Services;

namespace OtdArtist;

public partial class App : Application
{
    // Hold a reference so the tray icon isn't garbage-collected (#72).
    private AppTray? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Apply the saved Light/Dark/System theme before the window is shown (#139).
        ThemeService.ApplySaved();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the window hides it to the tray and the app keeps running, so we own exit
            // explicitly (the tray's Quit calls Shutdown). (#72)
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            var window = new MainWindow();
            desktop.MainWindow = window;
            window.Show();

            if (window.DataContext is ViewModels.MainViewModel vm)
                _tray = new AppTray(desktop, window, vm.Connection);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
