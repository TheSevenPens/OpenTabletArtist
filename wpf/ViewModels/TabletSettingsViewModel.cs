using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop.Profiles;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Paired Tablets (Tablet Settings) page. Page-VM split (#14 phase 2).
/// The profile list is derived from shell state (settings + live tablet detection), so the
/// shell computes it and pushes it via <see cref="Profiles"/>. Opening the per-tablet
/// settings dialog and forgetting a profile both touch shared shell state (and the dialog
/// is also opened from the Dashboard's "Open"), so they're provided as delegates rather
/// than duplicated here.
/// </summary>
public partial class TabletSettingsViewModel : ObservableObject
{
    private readonly Func<Profile, Task> _openSettings;
    private readonly Func<string, Task> _forgetProfile;

    [ObservableProperty] private List<ProfileItem> _profiles = [];

    public bool HasProfiles => Profiles.Count > 0;
    partial void OnProfilesChanged(List<ProfileItem> value) => OnPropertyChanged(nameof(HasProfiles));

    public TabletSettingsViewModel(Func<Profile, Task> openSettings, Func<string, Task> forgetProfile)
    {
        _openSettings = openSettings;
        _forgetProfile = forgetProfile;
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
    private Task ForgetProfile(string tabletName) => _forgetProfile(tabletName);
}
