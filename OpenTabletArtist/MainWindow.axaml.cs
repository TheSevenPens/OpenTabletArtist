using System.Threading.Tasks;
using Avalonia.Controls;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist;

public partial class MainWindow : Window
{
    private bool _allowClose;
    // One-time "still running in the tray" hint, persisted so it only shows on the first close (#72).
    private bool _trayHintShown = AppSettings.Get("TrayHintShown") == "true";

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    /// <summary>Permit a real close — used by the tray's Quit. Without it, closing hides to the tray.</summary>
    public void AllowCloseForQuit() => _allowClose = true;

    /// <summary>Surface the window from a hidden/minimized state and focus it. Used by the tray click
    /// and by a second app instance asking the running one to come forward (#191).</summary>
    public void BringToFront()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            // Close minimizes to the tray instead of exiting; Quit is via the tray menu (#72).
            e.Cancel = true;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                AppSettings.Set("TrayHintShown", "true");
                _ = ShowTrayHintThenHide();
            }
            else
            {
                Hide();
            }
        }
        base.OnClosing(e);
    }

    private async Task ShowTrayHintThenHide()
    {
        await Dialogs.ShowMessageAsync(
            "Still running in the tray",
            "OpenTabletArtist is still running in the system tray. Click the tray icon to reopen it, " +
            "or right-click it and choose Quit to exit.",
            this);
        Hide();
    }
}
