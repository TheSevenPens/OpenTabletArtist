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
        new(ThemeService.DarkSakura, "Dark Sakura", "A moody dark cherry-blossom skin: pink accents and falling petals over a dimmed backdrop.", DarkSakuraSwatch()),
        new(ThemeService.Custom, "Custom", "A translucent skin you tune: pick the accent colour and a background image.", CustomSwatch()),
    };

    [ObservableProperty] private ThemeOption _selectedTheme;

    /// <summary>Description of the currently-selected theme (shown under the picker).</summary>
    public string SelectedDescription => SelectedTheme?.Description ?? "";

    private bool IsSakura => SelectedTheme?.Id == ThemeService.Anime;
    private bool IsDarkSakura => SelectedTheme?.Id == ThemeService.DarkSakura;
    private bool IsCustom => SelectedTheme?.Id == ThemeService.Custom;

    /// <summary>Falling petals apply to every blossom skin (Sakura, Dark Sakura, Custom).</summary>
    public bool ShowPetalsToggle => IsSakura || IsDarkSakura || IsCustom;
    public bool ShowFrostControls => IsSakura || IsDarkSakura || IsCustom;
    /// <summary>The accent-colour + background-image controls are Custom-only.</summary>
    public bool ShowCustomControls => IsCustom;
    /// <summary>The left-pane colour picker applies to Sakura / Dark Sakura (#554); Custom derives its
    /// sidebar from the Base colour, so it's hidden there.</summary>
    public bool ShowSidebarColor => IsSakura || IsDarkSakura;
    /// <summary>The highlight/accent colour is pickable on every translucent skin (#557).</summary>
    public bool ShowAccentControl => IsSakura || IsDarkSakura || IsCustom;

    // Per-skin highlight/accent (#557). Custom stores its accent in CustomThemeSettings; the blossom skins
    // in SkinColorSettings. Their default reproduces each skin's original pink.
    private string ActiveAccentHex() => IsCustom ? CustomThemeSettings.AccentHex
        : IsDarkSakura ? SkinColorSettings.DarkSakuraAccentHex : SkinColorSettings.SakuraAccentHex;
    private string DefaultAccentHexForSkin => IsCustom ? CustomThemeSettings.DefaultAccentHex
        : IsDarkSakura ? SkinColorSettings.DarkSakuraAccentDefault : SkinColorSettings.SakuraAccentDefault;
    private Color DefaultAccentColorForSkin => ParseColorOr(DefaultAccentHexForSkin, Colors.DeepPink);

    // The active translucent skin's storage bucket + its per-skin defaults, so each skin keeps its own
    // card tint/opacity and sidebar opacity — changing one never touches another (#241).
    private string SkinKey => IsCustom ? "Custom" : IsDarkSakura ? "DarkSakura" : "Sakura";
    private double DefaultCardOpacityForSkin => IsDarkSakura ? 0.35 : AcrylicSettings.DefaultMaterialOpacity;
    private double DefaultSidebarOpacityForSkin => IsDarkSakura ? 0.55 : AcrylicSettings.DefaultSidebarOpacity;
    private string DefaultCardHexForSkin => IsCustom ? SkinColorSettings.CustomCardDefault
        : IsDarkSakura ? SkinColorSettings.DarkSakuraCardDefault : SkinColorSettings.SakuraCardDefault;

    // Per-skin left-pane (sidebar) tint (#554), stored/defaulted per skin. Used as the gradient's top stop;
    // the bottom is a slightly darker shade of it for a subtle vertical gradient.
    private string ActiveSidebarHex() => IsDarkSakura
        ? SkinColorSettings.DarkSakuraSidebarHex : SkinColorSettings.SakuraSidebarHex;
    private string DefaultSidebarHexForSkin => IsDarkSakura
        ? SkinColorSettings.DarkSakuraSidebarDefault : SkinColorSettings.SakuraSidebarDefault;

    /// <summary>Falling-petal animation (#207), reused by the Custom skin. Persisted; the overlay reacts live.</summary>
    [ObservableProperty] private bool _petalsEnabled = AnimationSettings.PetalsEnabled;

    /// <summary>Overall opacity of the falling petals (0..1). Persisted; the overlay reacts live.</summary>
    [ObservableProperty] private double _petalsOpacity = AnimationSettings.PetalsOpacity;

    // ── Card translucency ("frosted glass") ──
    // Avalonia can't blur in-app content behind a control, so "frosted glass" is a tunable translucent
    // tint: the backdrop shows through the cards. Driving the global GlassBgBrush resource re-frosts
    // every card live. Scoped to the translucent skins — cleared for other themes. The card tint now
    // comes from CardColor (per-skin, user-tunable); a single "Card opacity" sets its alpha.
    // Seeded with the generic default; the ctor overwrites it with the active skin's stored value.
    [ObservableProperty] private double _cardOpacity = AcrylicSettings.DefaultMaterialOpacity;

    // Sidebar (left pane) background: a vertical gradient rebuilt live at the chosen opacity. Sakura /
    // Dark Sakura use the per-skin SidebarColor (#554); Custom derives its stops from BaseColor.
    [ObservableProperty] private double _sidebarOpacity = AcrylicSettings.DefaultSidebarOpacity;

    /// <summary>Per-skin left-pane tint for Sakura / Dark Sakura. Persisted; drives the sidebar gradient (#554).</summary>
    [ObservableProperty] private Color _sidebarColor;

    // ── Translucent-skin colours ──
    // CardColor is the frosted-card tint for the active skin (persisted per-skin). BaseColor + AccentColor
    // + BackgroundImagePath are the Custom skin's background, scheme, and backdrop.
    [ObservableProperty] private Color _cardColor;
    [ObservableProperty] private Color _baseColor;
    [ObservableProperty] private Color _accentColor;
    [ObservableProperty] private string? _backgroundImagePath;

    /// <summary>Opacity (0..1) of the Custom background image over the base colour. Lower = more base
    /// colour shows through. Persisted; the backdrop reacts live.</summary>
    [ObservableProperty] private double _backgroundImageOpacity = CustomThemeSettings.DefaultBackgroundImageOpacity;

    /// <summary>The per-skin card tint stored for the active translucent skin.</summary>
    private string ActiveCardHex() =>
        IsCustom ? SkinColorSettings.CustomCardHex
        : IsDarkSakura ? SkinColorSettings.DarkSakuraCardHex
        : SkinColorSettings.SakuraCardHex;

    public bool HasBackgroundImage => !string.IsNullOrWhiteSpace(BackgroundImagePath);
    /// <summary>Filename of the chosen image, or a placeholder when none is set.</summary>
    public string BackgroundImageLabel =>
        HasBackgroundImage ? Path.GetFileName(BackgroundImagePath!) : "No image chosen";
    /// <summary>The image-opacity slider only makes sense once an image is chosen (Custom skin).</summary>
    public bool ShowBackgroundImageOpacity => IsCustom && HasBackgroundImage;

    public ThemeViewModel()
    {
        // Assign backing fields directly so restoring the persisted values here doesn't re-fire the
        // change handlers at construction (the theme is already applied at startup via ApplySaved).
        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Id == ThemeService.SavedChoice) ?? ThemeOptions[0];
        _accentColor = ParseColorOr(ActiveAccentHex(), Color.Parse(DefaultAccentHexForSkin));
        _cardColor = ParseColorOr(ActiveCardHex(), Color.Parse(DefaultCardHexForSkin));
        _cardOpacity = AcrylicSettings.MaterialOpacity(SkinKey, DefaultCardOpacityForSkin);
        _sidebarOpacity = AcrylicSettings.SidebarOpacity(SkinKey, DefaultSidebarOpacityForSkin);
        _sidebarColor = ParseColorOr(ActiveSidebarHex(), Color.Parse(DefaultSidebarHexForSkin));
        _baseColor = ParseColorOr(SkinColorSettings.CustomBaseHex, Color.Parse(SkinColorSettings.CustomBaseDefault));
        _backgroundImagePath = CustomThemeSettings.BackgroundImagePath;
        _backgroundImageOpacity = CustomThemeSettings.BackgroundImageOpacity;
        RefreshSkin(); // restore the persisted skin overrides (frost / accent / base / backdrop)
    }

    partial void OnCardOpacityChanged(double value)
    {
        AcrylicSettings.SetMaterialOpacity(SkinKey, value);
        RefreshSkin();
    }

    partial void OnCardColorChanged(Color value)
    {
        // Persist to the active skin's card-tint slot so each translucent skin keeps its own tint.
        if (IsCustom) SkinColorSettings.CustomCardHex = value.ToString();
        else if (IsDarkSakura) SkinColorSettings.DarkSakuraCardHex = value.ToString();
        else SkinColorSettings.SakuraCardHex = value.ToString();
        RefreshSkin();
    }

    partial void OnBaseColorChanged(Color value)
    {
        SkinColorSettings.CustomBaseHex = value.ToString();
        RefreshSkin();
    }

    partial void OnSidebarOpacityChanged(double value)
    {
        AcrylicSettings.SetSidebarOpacity(SkinKey, value);
        RefreshSkin();
    }

    partial void OnSidebarColorChanged(Color value)
    {
        // Per-skin left-pane tint (#554); Custom derives its sidebar from BaseColor, so it isn't stored here.
        if (IsDarkSakura) SkinColorSettings.DarkSakuraSidebarHex = value.ToString();
        else if (IsSakura) SkinColorSettings.SakuraSidebarHex = value.ToString();
        RefreshSkin();
    }

    partial void OnAccentColorChanged(Color value)
    {
        // Persist to the active skin's accent slot so each translucent skin keeps its own highlight (#557).
        if (IsCustom) CustomThemeSettings.AccentHex = value.ToString();
        else if (IsDarkSakura) SkinColorSettings.DarkSakuraAccentHex = value.ToString();
        else if (IsSakura) SkinColorSettings.SakuraAccentHex = value.ToString();
        RefreshSkin();
    }

    partial void OnBackgroundImagePathChanged(string? value)
    {
        CustomThemeSettings.BackgroundImagePath = value;
        OnPropertyChanged(nameof(HasBackgroundImage));
        OnPropertyChanged(nameof(BackgroundImageLabel));
        OnPropertyChanged(nameof(ShowBackgroundImageOpacity));
        RefreshSkin();
    }

    partial void OnBackgroundImageOpacityChanged(double value)
    {
        CustomThemeSettings.BackgroundImageOpacity = value;
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
        OnPropertyChanged(nameof(ShowBackgroundImageOpacity));
        OnPropertyChanged(nameof(ShowSidebarColor));
        OnPropertyChanged(nameof(ShowAccentControl));
        // Point the frost controls at the new skin's own stored values (backing fields directly, so this
        // doesn't re-persist — it's a display sync, not a user edit). Each skin keeps separate settings.
        _cardColor = ParseColorOr(ActiveCardHex(), Color.Parse(DefaultCardHexForSkin));
        _cardOpacity = AcrylicSettings.MaterialOpacity(SkinKey, DefaultCardOpacityForSkin);
        _sidebarOpacity = AcrylicSettings.SidebarOpacity(SkinKey, DefaultSidebarOpacityForSkin);
        _sidebarColor = ParseColorOr(ActiveSidebarHex(), Color.Parse(DefaultSidebarHexForSkin));
        _accentColor = ParseColorOr(ActiveAccentHex(), Color.Parse(DefaultAccentHexForSkin));
        OnPropertyChanged(nameof(CardColor));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(SidebarOpacity));
        OnPropertyChanged(nameof(SidebarColor));
        OnPropertyChanged(nameof(AccentColor));
        if (value != null) ThemeService.Apply(value.Id);
        RefreshSkin(); // apply the new skin's overrides, clear the old skin's
    }

    partial void OnPetalsEnabledChanged(bool value) => AnimationSettings.PetalsEnabled = value;
    partial void OnPetalsOpacityChanged(double value) => AnimationSettings.PetalsOpacity = value;

    // ── Live resource overrides ────────────────────────────────────────────────────────────────
    // The translucent skins customise app brushes at runtime by writing directly into
    // Application.Resources, which shadows the theme-dictionary entries of the same key. Everything set
    // here must be removed when leaving the skin so it never bleeds into Light/Dark/other skins.

    /// <summary>Applies the current skin's live overrides, clearing any from a previously-selected skin.
    /// The override keys live in <see cref="SkinOverrides"/> (shared with the screenshot tool, #437).</summary>
    private void RefreshSkin()
    {
        if (Application.Current is not { } app) return;
        SkinOverrides.Clear(app);
        if (!(IsSakura || IsDarkSakura || IsCustom)) return;

        // Highlight/accent scheme (#557): always for Custom (it has no theme-dictionary accent); for Sakura /
        // Dark Sakura only when the user picked a non-default accent, so the skins' hand-tuned default look
        // (e.g. Sakura's pink→rose button gradient) is preserved until they choose otherwise.
        if (IsCustom || AccentColor != DefaultAccentColorForSkin)
            ApplyAccentScheme(app, AccentColor);

        app.Resources["GlassBgBrush"] = FrostBrush(CardColor);

        if (IsCustom)
        {
            app.Resources["SidebarBgBrush"] = SidebarBrush(BaseColor, Darken(BaseColor, 0.35));
            ApplyCustomBackdrop(app);
        }
        else
        {
            app.Resources["SidebarBgBrush"] = SidebarBrush(SidebarColor, Darken(SidebarColor, IsDarkSakura ? 0.25 : 0.08));
        }
    }

    /// <summary>Override the accent-derived brushes (buttons, radios, selected nav, system accent shades)
    /// from a single accent colour. Shared by every translucent skin (#557).</summary>
    private void ApplyAccentScheme(Application app, Color accent)
    {
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

        // The checkmark glyph, the "on" toggle knob, and the radio dot all sit on a solid-accent fill and
        // are hard-coded white by Fluent — invisible on a light accent (e.g. pink). Pick white / near-black
        // by contrast against the accent so each stays legible in every checked/on state.
        var onAccentInk = new SolidColorBrush(ContrastingInk(accent));
        app.Resources["CheckBoxCheckGlyphForegroundChecked"] = onAccentInk;
        app.Resources["CheckBoxCheckGlyphForegroundCheckedPointerOver"] = onAccentInk;
        app.Resources["CheckBoxCheckGlyphForegroundCheckedPressed"] = onAccentInk;

        app.Resources["ToggleSwitchKnobFillOn"] = onAccentInk;
        app.Resources["ToggleSwitchKnobFillOnPointerOver"] = onAccentInk;
        app.Resources["ToggleSwitchKnobFillOnPressed"] = onAccentInk;

        app.Resources["RadioButtonCheckGlyphFill"] = onAccentInk;
        app.Resources["RadioButtonCheckGlyphFillPointerOver"] = onAccentInk;
        app.Resources["RadioButtonCheckGlyphFillPressed"] = onAccentInk;

        app.Resources["AccentButtonFillBrush"] = Gradient(light1, accent);
        app.Resources["AccentButtonFillHoverBrush"] = Gradient(Lighten(accent, 0.22), light1);
        // Accent buttons hard-coded white text, which is unreadable on a light accent (e.g. pink). Pick
        // white or near-black by contrast instead, evaluated against the LIGHTEST stop the label ever sits
        // on (the hover gradient's top, Lighten(accent, 0.22)) so it stays legible at rest and on hover.
        // Both rest + hover use the same ink so the label colour doesn't flip mid-interaction.
        var buttonInk = new SolidColorBrush(ContrastingInk(Lighten(accent, 0.22)));
        app.Resources["AccentButtonForegroundBrush"] = buttonInk;
        app.Resources["AccentButtonForegroundHoverBrush"] = buttonInk;
        app.Resources["GlassBorderBrush"] = new SolidColorBrush(WithAlpha(accent, 0x40));
        app.Resources["CardShadow"] = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 4, Blur = 16, Spread = 0, Color = WithAlpha(accent, 0x40),
        });

    }

    /// <summary>Custom-only backdrop: the base colour always fills the window; a chosen image layers over it
    /// at the user's opacity (lower = more base colour shows through). The base fill also means an
    /// unset/unreadable image just falls back to the user's chosen base instead of a hard-coded black.</summary>
    private void ApplyCustomBackdrop(Application app)
    {
        app.Resources["AppBackdropBrush"] = new SolidColorBrush(BaseColor);

        var path = BackgroundImagePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var opacity = Math.Clamp(BackgroundImageOpacity, 0, 1);
                app.Resources["AppBackdropImageBrush"] = new ImageBrush(new Bitmap(path))
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                    Opacity = opacity,
                };
                // Legibility scrim, faded out with the image so a faint image (low opacity) doesn't leave
                // the base colour needlessly darkened.
                app.Resources["BackdropScrimBrush"] = new SolidColorBrush(Color.FromArgb((byte)(0x66 * opacity), 0, 0, 0));
                return;
            }
            catch
            {
                // Unreadable/missing image → just the flat base colour, no overlay/scrim.
            }
        }

        app.Resources.Remove("AppBackdropImageBrush");
        app.Resources.Remove("BackdropScrimBrush");
    }

    /// <summary>Restore the active translucent skin's tunables (card tint/opacity, left-pane opacity,
    /// petals, and — for Custom — accent, base colour, and background image) to their defaults.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        // Each assignment goes through its change handler, so this both persists and re-applies live.
        // Defaults are per-skin, so a reset only restores the active skin's own look (#241).
        CardOpacity = DefaultCardOpacityForSkin;
        SidebarOpacity = DefaultSidebarOpacityForSkin;
        PetalsEnabled = true;
        PetalsOpacity = 1;
        CardColor = Color.Parse(DefaultCardHexForSkin);
        if (ShowSidebarColor) SidebarColor = Color.Parse(DefaultSidebarHexForSkin);
        AccentColor = Color.Parse(DefaultAccentHexForSkin); // per-skin highlight (#557)
        if (IsCustom)
        {
            BaseColor = Color.Parse(SkinColorSettings.CustomBaseDefault);
            BackgroundImagePath = null;
            BackgroundImageOpacity = CustomThemeSettings.DefaultBackgroundImageOpacity;
        }
    }

    /// <summary>A translucent card-frost brush: the card tint at the current card opacity (its alpha).</summary>
    private SolidColorBrush FrostBrush(Color tint)
    {
        var a = (byte)(Math.Clamp(CardOpacity, 0, 1) * 255);
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

    /// <summary>White or near-black — whichever stays legible as label text on the given button colour.
    /// Uses WCAG relative luminance so light fills (e.g. a pink accent) get dark text instead of white.</summary>
    private static Color ContrastingInk(Color buttonColor) =>
        RelativeLuminance(buttonColor) > 0.42 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;

    /// <summary>WCAG relative luminance (0 = black, 1 = white) of an sRGB colour.</summary>
    private static double RelativeLuminance(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

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

    /// <summary>Dark Sakura swatch: pink accent emerging from a dark plum, hinting the dark scheme.</summary>
    private static IBrush DarkSakuraSwatch() => Gradient(Color.Parse("#2A1420"), Color.Parse("#E0218A"));

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
