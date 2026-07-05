using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Theme page. Owns app-level appearance preferences that aren't tied to a tablet:
/// the theme (Light / Dark / System + the Sakura/Anime skin, #139/#207) and the user-tunable Custom
/// skin (accent colour + background image). Persisted via <see cref="ThemeService"/> /
/// <see cref="CustomThemeSettings"/>. The picker shows a colour swatch + label per option and a
/// one-line description; skin-specific controls appear only for the skin they apply to.
/// </summary>
public partial class ThemeViewModel : ObservableObject
{
    /// <summary>One selectable appearance. <see cref="Id"/> is the persisted <see cref="ThemeService"/>
    /// value; <see cref="Label"/> is what the user sees (e.g. "Anime" surfaces as "Sakura").</summary>
    public sealed record ThemeOption(string Id, string Label, string Description, IBrush Swatch);

    public ThemeOption[] ThemeOptions { get; } =
    {
        new(ThemeService.System, "System", "Follows your Windows light/dark setting.", SystemSwatch()),
        new(ThemeService.Light,  "Light",  "A clean, bright theme.", new SolidColorBrush(Color.Parse("#F0F0F6"))),
        new(ThemeService.Dark,   "Dark",   "Easy on the eyes in low light.", new SolidColorBrush(Color.Parse("#13131C"))),
        new(ThemeService.Anime,  "Sakura", "Pink skin with a cherry-blossom backdrop and frosted-glass panels.", SakuraSwatch()),
        new(ThemeService.Custom, "Custom", "A translucent skin you tune: pick the accent colour and a background image.", CustomSwatch()),
    };

    [ObservableProperty] private ThemeOption _selectedTheme;

    /// <summary>Description of the currently-selected theme (shown under the picker).</summary>
    public string SelectedDescription => SelectedTheme?.Description ?? "";

    private bool IsSakura => SelectedTheme?.Id == ThemeService.Anime;
    private bool IsCustom => SelectedTheme?.Id == ThemeService.Custom;

    /// <summary>Falling petals + the card-opacity slider apply to both translucent skins (Sakura + Custom).</summary>
    public bool ShowPetalsToggle => IsSakura || IsCustom;
    public bool ShowFrostControls => IsSakura || IsCustom;
    /// <summary>The accent-colour + background-image controls are Custom-only.</summary>
    public bool ShowCustomControls => IsCustom;

    /// <summary>Falling-petal animation (#207), reused by the Custom skin. Persisted; the overlay reacts live.</summary>
    [ObservableProperty] private bool _petalsEnabled = AnimationSettings.PetalsEnabled;

    // ── Card translucency ("frosted glass") ──
    // Avalonia can't blur in-app content behind a control, so "frosted glass" is a tunable translucent
    // tint: the backdrop shows through the cards. Driving the global GlassBgBrush resource re-frosts
    // every card live. Scoped to the translucent skins — cleared for other themes.
    private static readonly Color SakuraFrostTint = Color.Parse("#FDF1F7"); // soft sakura white
    private static readonly Color CustomFrostTint = Color.Parse("#202430"); // neutral dark
    [ObservableProperty] private double _cardOpacity = AcrylicSettings.MaterialOpacity;
    [ObservableProperty] private double _tintOpacity = AcrylicSettings.TintOpacity;

    // Sidebar (left pane) background: a vertical gradient rebuilt live at the chosen opacity. The base
    // stop colours differ per skin (soft pink for Sakura, near-black for Custom).
    private static readonly Color SakuraSidebarTop = Color.Parse("#FCDCEC");
    private static readonly Color SakuraSidebarBottom = Color.Parse("#F7C2DC");
    private static readonly Color CustomSidebarTop = Color.Parse("#181820");
    private static readonly Color CustomSidebarBottom = Color.Parse("#101018");
    [ObservableProperty] private double _sidebarOpacity = AcrylicSettings.SidebarOpacity;

    // ── Custom skin: accent colour + background image ──
    [ObservableProperty] private Color _accentColor;
    [ObservableProperty] private string? _backgroundImagePath;

    public bool HasBackgroundImage => !string.IsNullOrWhiteSpace(BackgroundImagePath);
    /// <summary>Filename of the chosen image, or a placeholder when none is set.</summary>
    public string BackgroundImageLabel =>
        HasBackgroundImage ? Path.GetFileName(BackgroundImagePath!) : "No image chosen";

    public ThemeViewModel()
    {
        // Assign backing fields directly so restoring the persisted values here doesn't re-fire the
        // change handlers at construction (the theme is already applied at startup via ApplySaved).
        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Id == ThemeService.SavedChoice) ?? ThemeOptions[0];
        _accentColor = ParseColorOr(CustomThemeSettings.AccentHex, Color.Parse(CustomThemeSettings.DefaultAccentHex));
        _backgroundImagePath = CustomThemeSettings.BackgroundImagePath;
        RefreshSkin(); // restore the persisted skin overrides (frost / accent / backdrop)
    }

    partial void OnCardOpacityChanged(double value)
    {
        AcrylicSettings.MaterialOpacity = value;
        RefreshSkin();
    }

    partial void OnTintOpacityChanged(double value)
    {
        AcrylicSettings.TintOpacity = value;
        RefreshSkin();
    }

    partial void OnSidebarOpacityChanged(double value)
    {
        AcrylicSettings.SidebarOpacity = value;
        RefreshSkin();
    }

    partial void OnAccentColorChanged(Color value)
    {
        CustomThemeSettings.AccentHex = value.ToString();
        RefreshSkin();
    }

    partial void OnBackgroundImagePathChanged(string? value)
    {
        CustomThemeSettings.BackgroundImagePath = value;
        OnPropertyChanged(nameof(HasBackgroundImage));
        OnPropertyChanged(nameof(BackgroundImageLabel));
        RefreshSkin();
    }

    /// <summary>Clears the Custom background image (called by the "Clear" button).</summary>
    [RelayCommand]
    private void ClearBackgroundImage() => BackgroundImagePath = null;

    // Fires only on user-driven changes, so it both applies and persists.
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(ShowPetalsToggle));
        OnPropertyChanged(nameof(ShowFrostControls));
        OnPropertyChanged(nameof(ShowCustomControls));
        if (value != null) ThemeService.Apply(value.Id);
        RefreshSkin(); // apply the new skin's overrides, clear the old skin's
    }

    partial void OnPetalsEnabledChanged(bool value) => AnimationSettings.PetalsEnabled = value;

    // ── Live resource overrides ────────────────────────────────────────────────────────────────
    // The translucent skins customise app brushes at runtime by writing directly into
    // Application.Resources, which shadows the theme-dictionary entries of the same key. Everything set
    // here must be removed when leaving the skin so it never bleeds into Light/Dark/other skins.

    // Keys ApplyCustom writes; cleared wholesale when switching skins.
    private static readonly string[] CustomOverrideKeys =
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

    /// <summary>Applies the current skin's live overrides, clearing any from a previously-selected skin.</summary>
    private void RefreshSkin()
    {
        if (Application.Current is not { } app) return;
        foreach (var k in CustomOverrideKeys) app.Resources.Remove(k);

        if (IsSakura)
        {
            app.Resources["GlassBgBrush"] = FrostBrush(SakuraFrostTint);
            app.Resources["SidebarBgBrush"] = SidebarBrush(SakuraSidebarTop, SakuraSidebarBottom);
        }
        else if (IsCustom)
        {
            ApplyCustom(app);
        }
    }

    private void ApplyCustom(Application app)
    {
        var accent = AccentColor;
        var light1 = Lighten(accent, 0.14);
        var light2 = Lighten(accent, 0.28);
        var light3 = Lighten(accent, 0.50);
        var dark1 = Darken(accent, 0.14);
        var dark2 = Darken(accent, 0.30);
        var dark3 = Darken(accent, 0.46);

        app.Resources["Accent"] = accent;
        app.Resources["AccentBrush"] = new SolidColorBrush(accent);
        app.Resources["AccentMutedBrush"] = new SolidColorBrush(WithAlpha(accent, 0x1F));
        app.Resources["NavActiveBrush"] = new SolidColorBrush(WithAlpha(accent, 0x30));

        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = light1;
        app.Resources["SystemAccentColorLight2"] = light2;
        app.Resources["SystemAccentColorLight3"] = light3;
        app.Resources["SystemAccentColorDark1"] = dark1;
        app.Resources["SystemAccentColorDark2"] = dark2;
        app.Resources["SystemAccentColorDark3"] = dark3;

        app.Resources["RadioButtonOuterEllipseCheckedFill"] = new SolidColorBrush(accent);
        app.Resources["RadioButtonOuterEllipseCheckedFillPointerOver"] = new SolidColorBrush(light1);
        app.Resources["RadioButtonOuterEllipseCheckedFillPressed"] = new SolidColorBrush(dark1);
        app.Resources["RadioButtonOuterEllipseCheckedStroke"] = new SolidColorBrush(accent);
        app.Resources["RadioButtonOuterEllipseCheckedStrokePointerOver"] = new SolidColorBrush(light1);
        app.Resources["RadioButtonOuterEllipseCheckedStrokePressed"] = new SolidColorBrush(dark1);

        app.Resources["AccentButtonFillBrush"] = Gradient(light1, accent);
        app.Resources["AccentButtonFillHoverBrush"] = Gradient(Lighten(accent, 0.22), light1);
        app.Resources["AccentButtonForegroundBrush"] = new SolidColorBrush(Colors.White);
        app.Resources["GlassBorderBrush"] = new SolidColorBrush(WithAlpha(accent, 0x40));
        app.Resources["CardShadow"] = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 4, Blur = 16, Spread = 0, Color = WithAlpha(accent, 0x40),
        });

        // Card frost + sidebar (neutral dark tints at the chosen opacities).
        app.Resources["GlassBgBrush"] = FrostBrush(CustomFrostTint);
        app.Resources["SidebarBgBrush"] = SidebarBrush(CustomSidebarTop, CustomSidebarBottom);

        // Background image + dimming scrim, when one is set and readable.
        var path = BackgroundImagePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                app.Resources["AppBackdropBrush"] = new ImageBrush(new Bitmap(path))
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                };
                app.Resources["BackdropScrimBrush"] = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
            }
            catch
            {
                // Unreadable/missing image → leave the transparent default backdrop, no scrim.
                app.Resources.Remove("AppBackdropBrush");
                app.Resources.Remove("BackdropScrimBrush");
            }
        }
    }

    /// <summary>A translucent card-frost brush: the given tint at the current card opacity.</summary>
    private SolidColorBrush FrostBrush(Color tint)
    {
        var material = Math.Clamp(CardOpacity, 0, 1);
        var tintStrength = Math.Clamp(TintOpacity, 0, 1);
        var a = (byte)(material * tintStrength * 255);
        return new SolidColorBrush(Color.FromArgb(a, tint.R, tint.G, tint.B));
    }

    /// <summary>The sidebar's vertical gradient (top→bottom) at the current sidebar opacity.</summary>
    private LinearGradientBrush SidebarBrush(Color top, Color bottom)
    {
        var a = (byte)(Math.Clamp(SidebarOpacity, 0, 1) * 255);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(top, a), 0),
                new GradientStop(WithAlpha(bottom, a), 1),
            },
        };
    }

    private static Color ParseColorOr(string? hex, Color fallback) =>
        Color.TryParse(hex, out var c) ? c : fallback;

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color Lighten(Color c, double amount) => Mix(c, Colors.White, amount);
    private static Color Darken(Color c, double amount) => Mix(c, Colors.Black, amount);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Sakura swatch: the same pink→rose gradient as the skin's accent buttons.</summary>
    private static IBrush SakuraSwatch() => Gradient(Color.Parse("#FF7EC4"), Color.Parse("#E0218A"));

    /// <summary>Custom swatch: the default indigo, hinting the accent is user-chosen.</summary>
    private static IBrush CustomSwatch() => Gradient(Color.Parse("#7C7EF4"), Color.Parse("#6366F1"));

    /// <summary>System swatch: a half-light/half-dark split hinting it follows the OS.</summary>
    private static IBrush SystemSwatch() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#F0F0F6"), 0.5),
            new GradientStop(Color.Parse("#13131C"), 0.5),
        },
    };

    private static LinearGradientBrush Gradient(Color from, Color to) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(from, 0),
            new GradientStop(to, 1),
        },
    };
}
