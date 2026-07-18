using System;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.ViewModels;

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

    /// <summary>Builds a <see cref="TabletDetailViewModel"/> for the in-app Tablets page, wired to the
    /// session (apply/save, reload, detection, live pen input, calibration owned by the main window).
    /// <paramref name="onForget"/> removes the tablet's profile and is invoked by the page's Forget.</summary>
    TabletDetailViewModel CreateTabletDetail(Profile profile, Func<Task> onForget, Action? openConfigsPage = null);

    /// <summary>Shows an informational message with an OK button.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Shows a yes/no confirmation; returns true if confirmed.</summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>Prompts for a line of text; returns null if cancelled.</summary>
    Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "");

    /// <summary>Opens the on-screen hotkey picker (optionally pre-filled with <paramref name="initial"/>);
    /// returns the chosen chord, or null if cancelled. (#320)</summary>
    Task<HotkeyChord?> ShowHotkeyCaptureAsync(HotkeyChord? initial = null);

    /// <summary>Opens a read-only, scrollable monospace viewer (used for config JSON).</summary>
    Task ShowTextViewerAsync(string title, string content);

    /// <summary>Lets the user pick a running windowed application; returns its identity, or null if
    /// cancelled. Used to add a per-app profile mapping (#167).</summary>
    Task<Domain.AppIdentity?> ShowProcessPickerAsync();

    /// <summary>Shows the built-in, searchable list of tablets OpenTabletDriver supports, highlighting
    /// the connected tablet when <paramref name="detectedName"/> matches one (#155).</summary>
    Task ShowSupportedTabletsAsync(string? detectedName);
}

/// <inheritdoc />
public class DialogService : IDialogService
{
    private readonly AppSession _session;

    public DialogService(AppSession session) => _session = session;

    /// <inheritdoc />
    public TabletDetailViewModel CreateTabletDetail(Profile profile, Func<Task> onForget, Action? openConfigsPage = null)
    {
        var tabletName = profile.Tablet;
        return new TabletDetailViewModel(
            profile,
            _session.CurrentSettings,
            applyAction: async updated => await _session.ApplyAndSaveSettingsAsync(updated),
            refreshAction: async () =>
            {
                // Authoritative reload through the session so its cache stays coherent; return the
                // reloaded settings + this tablet's profile (a reference inside them) together (#124).
                await _session.ReloadAsync();
                var settings = _session.CurrentSettings;
                return (settings, settings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName));
            },
            tabletDigitizer: _session.GetTabletDigitizer(tabletName),
            penInput: _session.Daemon, // live pen-pressure dot on the Dynamics tab (#102)
            isDetected: () => _session.Profiles.Any(p => p.IsDetected && p.Profile.Tablet == tabletName),
            dynamicsOnly: false,
            deviceData: _session, // live detection updates while the page is open (#177)
            forgetAction: onForget,
            // Calibration overlay is owned by the main window (the page has no window of its own, #127).
            onCalibrate: options =>
            {
                var owner = Dialogs.GetMainWindow();
                if (owner == null) return Task.CompletedTask;
                // Resolve the profile live (by tablet name) so the overlay follows the CURRENT display
                // mapping. The VM swaps its _profile on refresh (#124), so a captured reference goes stale
                // after a remap and would open the overlay on the previously-mapped display.
                var current = _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName) ?? profile;
                return ShowCalibrationAsync(current, owner, options);
            },
            // Express-key / wheel binding editor, owned by the main window (the page has no window).
            editBinding: async (binding, title) =>
            {
                var owner = Dialogs.GetMainWindow();
                return owner != null
                    ? await Views.BindingEditorDialog.ShowAsync(owner, binding, title)
                    : null;
            },
            // ABOUT tab → the config-override card's Review button navigates to the CONFIGS page (#467).
            openConfigsPage: openConfigsPage);
    }

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
            // capture mode the user picked (corners → homography, or a finer grid; #195/#196). Resolve
            // the profile live (by name) so the overlay follows the current display mapping (#124).
            (owner, options) => ShowCalibrationAsync(
                _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName) ?? profile,
                owner, options),
            // Live-refresh the detection banner + tablet-dependent actions while the dialog is open
            // (#177): the session reloads on the daemon's TabletsChanged push (#170) and raises DataLoaded.
            _session);

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

        // Find the mapped monitor with the SAME 0-based-desktop-aware matching the rest of the app uses
        // (DisplayMappingApplier.CurrentlyMapped / MappedCenter). The previous ad-hoc origin compare assumed
        // the desktop origin was (0,0), so it broke whenever a monitor sits at a negative virtual-desktop
        // coordinate — e.g. a display to the left of / above the primary (common with 3+ monitors, and seen
        // on macOS): the stored area is in min-shifted coords, so it no longer equalled a raw display
        // position and calibration wrongly reported "couldn't match". (#140)
        var displays = DisplayEnumerator.Enumerate();
        var display = Domain.DisplayMappingApplier.CurrentlyMapped(profile, displays);
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

    public async Task<HotkeyChord?> ShowHotkeyCaptureAsync(HotkeyChord? initial = null)
    {
        var owner = Dialogs.GetMainWindow();
        return owner != null ? await Views.HotkeyCaptureDialog.ShowAsync(owner, initial) : null;
    }

    public Task<Domain.AppIdentity?> ShowProcessPickerAsync() => Dialogs.ShowProcessPickerAsync();

    public async Task ShowSupportedTabletsAsync(string? detectedName)
    {
        var owner = Dialogs.GetMainWindow();
        if (owner != null) await Views.SupportedTabletsDialog.ShowAsync(owner, detectedName);
    }

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
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
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
