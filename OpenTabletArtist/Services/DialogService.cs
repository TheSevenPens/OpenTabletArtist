using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Helpers;

namespace OpenTabletArtist.Services;

/// <summary>
/// The single seam for app dialogs. Page view models depend on this interface instead of
/// constructing Windows or calling the static <see cref="Dialogs"/> helper, so every dialog
/// flow (confirm/message/input, the per-tablet settings dialog, the read-only config viewer)
/// is mockable in tests and the shell stays free of UI orchestration (#37).
/// </summary>
public interface IDialogService
{
    /// <summary>Opens the per-tablet settings dialog for <paramref name="profile"/> and awaits its
    /// close. When <paramref name="dynamicsOnly"/> is true, the dialog opens as a focused Pen Dynamics
    /// editor — the tab bar is hidden and only the Dynamics panel shows (#133).</summary>
    Task ShowTabletSettingsAsync(Profile profile, bool dynamicsOnly = false);

    /// <summary>Shows an informational message with an OK button.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Shows a yes/no confirmation; returns true if confirmed.</summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>Prompts for a line of text; returns null if cancelled.</summary>
    Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "");

    /// <summary>Opens a read-only, scrollable monospace viewer (used for config JSON).</summary>
    Task ShowTextViewerAsync(string title, string content);
}

/// <inheritdoc />
public class DialogService : IDialogService
{
    private readonly AppSession _session;

    public DialogService(AppSession session) => _session = session;

    public async Task ShowTabletSettingsAsync(Profile profile, bool dynamicsOnly = false)
    {
        var tabletName = profile.Tablet;
        var digitizer = _session.GetTabletDigitizer(tabletName);
        var dialog = new Views.TabletSettingsDialog(
            profile,
            _session.CurrentSettings,
            async updatedSettings => await _session.ApplyAndSaveSettingsAsync(updatedSettings),
            async () =>
            {
                // Authoritative refresh through the session so its CurrentSettings cache stays
                // coherent (and the rest of the UI updates too). (Codex #43.) Return the reloaded
                // settings together with the profile so the dialog keeps both in sync — the profile
                // is a reference inside these settings, and later edits persist through them (#124).
                await _session.ReloadAsync();
                var settings = _session.CurrentSettings;
                return (settings, settings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName));
            },
            digitizer,
            dynamicsOnly,
            _session.Daemon, // live pen-pressure dot on the Dynamics tab (#102)
            // Is this tablet the currently-connected one? Drives the detected/connected banner (#132).
            () => _session.Profiles.Any(p => p.IsDetected && p.Profile.Tablet == tabletName),
            // Open the pointer-calibration overlay owned by the settings dialog (#127), using the
            // capture mode the user picked (corners → homography, or a finer grid; #195/#196).
            (owner, options) => ShowCalibrationAsync(profile, owner, options));

        var mainWindow = Dialogs.GetMainWindow();
        if (mainWindow != null)
            await dialog.ShowDialog(mainWindow);
    }

    /// <summary>Opens the 4-tap pointer-calibration overlay on the display this tablet is mapped to
    /// (#127). Validates that the profile is in an Absolute mapping with a known digitizer + display.</summary>
    public async Task ShowCalibrationAsync(Profile profile, Avalonia.Controls.Window owner,
        ViewModels.CalibrationOptions options = default)
    {
        var tabletName = profile.Tablet;

        // Calibration captures live pen taps, so the tablet must actually be connected. Check this
        // first and say so plainly — otherwise a missing tablet falls through to the generic
        // "set Windows Ink Absolute" message below, which is misleading (#178).
        var isDetected = _session.Profiles.Any(p => p.IsDetected && p.Profile.Tablet == tabletName);
        if (!isDetected)
        {
            await ShowMessageAsync("Tablet not detected",
                "Calibration needs this tablet connected so it can read where the pen actually lands. " +
                "Plug in / reconnect the tablet, wait for it to be detected, then try again.");
            return;
        }

        var digi = _session.GetDigitizerSpec(tabletName);
        var abs = profile.AbsoluteModeSettings;
        if (digi is null || abs?.Tablet is not { } t || abs.Display is not { } disp
            || t.Width <= 0 || t.Height <= 0 || disp.Width <= 0 || disp.Height <= 0
            || _session.CurrentSettings is not { } settings)
        {
            await ShowMessageAsync("Calibration unavailable",
                "Calibration needs an Absolute output mode with a known tablet area and display. " +
                "Set this tablet to Windows Ink Absolute and map it to a display first.");
            return;
        }

        var input = new Domain.MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation);
        var output = new Domain.MappingArea(disp.X, disp.Y, disp.Width, disp.Height);

        // The mapped display = the monitor whose top-left matches the output area's origin.
        var originX = disp.X - disp.Width / 2;
        var originY = disp.Y - disp.Height / 2;
        var displays = DisplayEnumerator.Enumerate();
        var display = displays.FirstOrDefault(d => System.Math.Abs(d.X - originX) <= 2 && System.Math.Abs(d.Y - originY) <= 2)
                      ?? displays.FirstOrDefault(d => disp.X >= d.X && disp.X <= d.X + d.Width && disp.Y >= d.Y && disp.Y <= d.Y + d.Height);
        if (display is null)
        {
            await ShowMessageAsync("Calibration unavailable",
                "Couldn't match this tablet's mapped area to a connected display. Re-apply the display mapping and try again.");
            return;
        }

        var ctx = new ViewModels.CalibrationViewModel.Context(
            tabletName, digi.Value, input, output, display, settings,
            s => _session.ApplyAndSaveSettingsAsync(s), _session.Daemon,
            options.Mode, options.Cols, options.Rows);

        var window = new Views.CalibrationOverlayWindow(new ViewModels.CalibrationViewModel(ctx), display);
        await window.ShowDialog(owner);
    }

    // The confirm/message/input flows delegate to the static helper, which owns the actual
    // Avalonia dialog building. Routing them through the interface is what makes them fakeable.
    public Task ShowMessageAsync(string title, string message) => Dialogs.ShowMessageAsync(title, message);
    public Task<bool> ShowConfirmAsync(string title, string message) => Dialogs.ShowConfirmAsync(title, message);
    public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
        => Dialogs.ShowInputAsync(title, prompt, defaultValue);

    public async Task ShowTextViewerAsync(string title, string content)
    {
        var parent = Dialogs.GetMainWindow();
        if (parent == null) return;

        var textBox = new Avalonia.Controls.TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
        };

        var scroll = new Avalonia.Controls.ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = textBox,
        };

        var closeBtn = new Avalonia.Controls.Button
        {
            Content = "Close",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(24, 8),
            FontSize = 13,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
        };

        var grid = new Avalonia.Controls.Grid
        {
            Margin = new Avalonia.Thickness(20),
            RowDefinitions = new Avalonia.Controls.RowDefinitions("*,Auto"),
        };
        Avalonia.Controls.Grid.SetRow(scroll, 0);
        Avalonia.Controls.Grid.SetRow(closeBtn, 1);
        grid.Children.Add(scroll);
        grid.Children.Add(closeBtn);

        var dialog = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 720,
            Height = 600,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = grid,
        };
        closeBtn.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(parent);
    }
}
