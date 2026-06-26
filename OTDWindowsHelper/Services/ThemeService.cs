using Avalonia;
using Avalonia.Styling;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Light/Dark/System theme selection (#139). The choice is persisted via <see cref="AppSettings"/>
/// and applied by setting <see cref="Application.RequestedThemeVariant"/> — "System" maps to
/// <see cref="ThemeVariant.Default"/>, which follows the OS. Brush resources switch automatically
/// because consumers bind them with DynamicResource (see Themes/Colors.axaml).
/// </summary>
public static class ThemeService
{
    private const string Key = "Theme";

    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    /// <summary>The saved choice, or "System" if none has been set.</summary>
    public static string SavedChoice => AppSettings.Get(Key) ?? System;

    /// <summary>Applies a choice to the running app and persists it.</summary>
    public static void Apply(string choice)
    {
        AppSettings.Set(Key, choice);
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(choice);
    }

    /// <summary>Applies the persisted choice (call once at startup, before showing the window).</summary>
    public static void ApplySaved()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(SavedChoice);
    }

    private static ThemeVariant ToVariant(string choice) => choice switch
    {
        Light => ThemeVariant.Light,
        Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default, // System / unknown → follow the OS
    };
}
