using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Paired Tablets (Tablet Settings) page. Page-VM split (#14 phase 2).
/// The profile list is derived from shared state and refreshed by self-subscribing to the
/// session's <see cref="IDeviceData.DataLoaded"/> event. Forgetting a profile is a settings
/// mutation done through <see cref="ISettingsCoordinator"/>; opening the per-tablet dialog
/// goes through <see cref="IDialogService"/> (also used by the Dashboard's "Open", #37).
/// </summary>
public partial class TabletSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsCoordinator _settings;
    private readonly IDeviceData _deviceData;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private List<ProfileItem> _profiles = [];

    public bool HasProfiles => Profiles.Count > 0;
    partial void OnProfilesChanged(List<ProfileItem> value) => OnPropertyChanged(nameof(HasProfiles));

    public TabletSettingsViewModel(ISettingsCoordinator settings, IDeviceData deviceData, IDialogService dialogs)
    {
        _settings = settings;
        _deviceData = deviceData;
        _dialogs = dialogs;

        // Self-subscribe to the session's data load instead of being pushed to by the shell.
        _deviceData.DataLoaded += OnDataLoaded;
        Profiles = Ordered(_deviceData.Profiles);
    }

    private void OnDataLoaded() => Profiles = Ordered(_deviceData.Profiles);

    /// <summary>Ordering for the Paired Tablets list (#137): the currently-detected tablet(s) first,
    /// then the rest by most-recently-seen, with never-seen tablets last (alphabetical). Last-seen is
    /// persisted across restarts (see <see cref="ProfileItem.LastSeen"/>), so the order is stable.</summary>
    private static List<ProfileItem> Ordered(IEnumerable<ProfileItem> items) =>
        items.OrderByDescending(p => p.IsDetected)
             .ThenByDescending(p => p.LastSeen ?? DateTime.MinValue)
             .ThenBy(p => p.Tablet, StringComparer.OrdinalIgnoreCase)
             .ToList();

    [RelayCommand]
    private async Task OpenTabletSettings(object profileObj)
    {
        // Called from XAML (button + double-tap) — may receive a ProfileItem or a Profile.
        if (profileObj is ProfileItem item)
            await _dialogs.ShowTabletSettingsAsync(item.Profile);
        else if (profileObj is Profile profile)
            await _dialogs.ShowTabletSettingsAsync(profile);
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

    public void Dispose() => _deviceData.DataLoaded -= OnDataLoaded;
}
