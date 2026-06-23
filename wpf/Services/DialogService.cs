using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Helpers;

namespace OtdWindowsHelper.Services;

/// <summary>
/// The single seam for app dialogs. Page view models depend on this interface instead of
/// constructing Windows or calling the static <see cref="Dialogs"/> helper, so every dialog
/// flow (confirm/message/input, the per-tablet settings dialog, the read-only config viewer)
/// is mockable in tests and the shell stays free of UI orchestration (#37).
/// </summary>
public interface IDialogService
{
    /// <summary>Opens the per-tablet settings dialog for <paramref name="profile"/> and awaits its close.</summary>
    Task ShowTabletSettingsAsync(Profile profile);

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

    public async Task ShowTabletSettingsAsync(Profile profile)
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
                // coherent (and the rest of the UI updates too). (Codex #43.)
                await _session.ReloadAsync();
                return _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
            },
            digitizer);

        var mainWindow = Dialogs.GetMainWindow();
        if (mainWindow != null)
            await dialog.ShowDialog(mainWindow);
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
