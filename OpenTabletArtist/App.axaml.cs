using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenTabletArtist.Services;

namespace OpenTabletArtist;

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
                _tray = new AppTray(desktop, window, vm.Connection, vm.DeviceData, vm.SettingsCoordinator,
                    vm.Dialogs, vm.ShutdownRestorePerAppAsync);

            // Remove the tray icon as part of a clean exit, so it doesn't ghost in the notification
            // area after Quit (Windows only clears an orphaned icon on hover otherwise). (#58)
            desktop.Exit += (_, _) => _tray?.Dispose();

            // When a second instance is launched, it signals this (primary) one to surface instead of
            // opening a duplicate window (#191). The signal arrives off the UI thread — marshal first.
            Program.Instance.ListenForActivation(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(window.BringToFront));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
