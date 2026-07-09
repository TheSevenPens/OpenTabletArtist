using Avalonia;

namespace OpenTabletArtist.Services;

/// <summary>
/// The <see cref="Application.Resources"/> keys the translucent skins (Sakura / Custom) override live at
/// runtime — <see cref="ViewModels.ThemeViewModel"/> writes them so they shadow the theme-dictionary
/// entries of the same key. Centralised so both the theme code and the screenshot tool (#437) can clear
/// them wholesale to reveal a theme's plain dictionary look.
/// </summary>
public static class SkinOverrides
{
    public static readonly string[] Keys =
    {
        "Accent", "AccentBrush", "AccentMutedBrush", "NavActiveBrush",
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
        "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver",
        "RadioButtonOuterEllipseCheckedFillPressed", "RadioButtonOuterEllipseCheckedStroke",
        "RadioButtonOuterEllipseCheckedStrokePointerOver", "RadioButtonOuterEllipseCheckedStrokePressed",
        "AccentButtonFillBrush", "AccentButtonFillHoverBrush", "AccentButtonForegroundBrush",
        "GlassBorderBrush", "CardShadow", "GlassBgBrush", "SidebarBgBrush",
        "AppBackdropBrush", "BackdropScrimBrush",
    };

    /// <summary>Remove every live skin override so the current theme dictionary renders unshadowed.</summary>
    public static void Clear(Application app)
    {
        foreach (var k in Keys) app.Resources.Remove(k);
    }
}
