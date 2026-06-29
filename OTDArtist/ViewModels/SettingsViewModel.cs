using CommunityToolkit.Mvvm.ComponentModel;
using OtdArtist.Services;

namespace OtdArtist.ViewModels;

/// <summary>
/// View model for the Settings page. Owns app-level preferences that aren't tied to a tablet —
/// currently the Light / Dark / System theme (#139), persisted via <see cref="ThemeService"/>.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    public string[] ThemeOptions { get; } = { ThemeService.System, ThemeService.Light, ThemeService.Dark };

    [ObservableProperty] private string _selectedTheme = ThemeService.SavedChoice;

    // Fires only on user-driven changes (not the field initializer), so it both applies and persists.
    partial void OnSelectedThemeChanged(string value) => ThemeService.Apply(value);
}
