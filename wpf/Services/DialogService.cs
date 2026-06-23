using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Helpers;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Opens app dialogs that page view models can't construct themselves (they're Windows).
/// Lets the VMs depend on an abstraction instead of a hand-passed delegate, and keeps the
/// shell free of UI orchestration (#37).
/// </summary>
public interface IDialogService
{
    /// <summary>Opens the per-tablet settings dialog for <paramref name="profile"/> and awaits its close.</summary>
    Task ShowTabletSettingsAsync(Profile profile);
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
}
