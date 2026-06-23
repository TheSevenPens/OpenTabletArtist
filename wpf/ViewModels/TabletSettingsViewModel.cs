using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Paired Tablets (Tablet Settings) page. Page-VM split (#14 phase 2).
/// The profile list is derived from shared state (settings + live tablet detection) and
/// pushed in via <see cref="Profiles"/>. Forgetting a profile is a settings mutation done
/// through <see cref="ISettingsCoordinator"/>; opening the per-tablet dialog is UI
/// orchestration shared with the Dashboard's "Open", so it stays a delegate (#37).
/// </summary>
public partial class TabletSettingsViewModel : ObservableObject
{
    private readonly ISettingsCoordinator _settings;
    private readonly Func<Profile, Task> _openSettings;

    [ObservableProperty] private List<ProfileItem> _profiles = [];

    public bool HasProfiles => Profiles.Count > 0;
    partial void OnProfilesChanged(List<ProfileItem> value) => OnPropertyChanged(nameof(HasProfiles));

    // Forget is a settings mutation, so it uses the shared coordinator. Opening the per-tablet
    // dialog is UI orchestration shared with the Dashboard "Open", so it stays a delegate (#37).
    public TabletSettingsViewModel(ISettingsCoordinator settings, Func<Profile, Task> openSettings)
    {
        _settings = settings;
        _openSettings = openSettings;
    }

    [RelayCommand]
    private async Task OpenTabletSettings(object profileObj)
    {
        // Called from XAML (button + double-tap) — may receive a ProfileItem or a Profile.
        if (profileObj is ProfileItem item)
            await _openSettings(item.Profile);
        else if (profileObj is Profile profile)
            await _openSettings(profile);
    }

    [RelayCommand]
    private async Task ForgetProfile(string tabletName)
    {
        var settings = _settings.CurrentSettings;
        if (settings == null || string.IsNullOrEmpty(tabletName)) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        settings.Profiles.Remove(profile);
        await _settings.ApplyAndSaveSettingsAsync(settings);
    }
}
