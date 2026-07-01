using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Theme page. Owns app-level preferences that aren't tied to a tablet —
/// currently the appearance theme (Light / Dark / System + the Sakura/Anime skin, #139/#207),
/// persisted via <see cref="ThemeService"/>. The picker shows a colour swatch + label per option
/// and a one-line description of the current choice.
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
    };

    [ObservableProperty] private ThemeOption _selectedTheme;

    /// <summary>Description of the currently-selected theme (shown under the picker).</summary>
    public string SelectedDescription => SelectedTheme?.Description ?? "";

    /// <summary>The falling-petals toggle + frost slider only make sense for the Sakura skin.</summary>
    public bool ShowPetalsToggle => SelectedTheme?.Id == ThemeService.Anime;
    public bool ShowFrostControls => SelectedTheme?.Id == ThemeService.Anime;

    /// <summary>Falling-petal animation for the Sakura skin (#207). Persisted; the overlay reacts live.</summary>
    [ObservableProperty] private bool _petalsEnabled = AnimationSettings.PetalsEnabled;

    // ── Sakura frosted-glass (card translucency) ──
    // Avalonia can't blur in-app content behind a control, so "frosted glass" is a tunable translucent
    // tint: the sakura backdrop shows through the cards. Driving the global GlassBgBrush resource
    // re-frosts every card live. Scoped to the Sakura skin — cleared for other themes so the pink
    // frost never carries over (top-level resource shadows the theme dictionary entry).
    private static readonly Color FrostTint = Color.Parse("#FDF1F7"); // soft sakura white
    [ObservableProperty] private double _cardOpacity = AcrylicSettings.MaterialOpacity;

    partial void OnCardOpacityChanged(double value)
    {
        AcrylicSettings.MaterialOpacity = value;
        ApplyOrClearFrost();
    }

    /// <summary>Applies the frost override in the Sakura skin, or removes it (restoring the theme's own
    /// glass) for any other theme.</summary>
    private void ApplyOrClearFrost()
    {
        if (Application.Current is not { } app) return;
        if (SelectedTheme?.Id == ThemeService.Anime)
        {
            var a = (byte)(System.Math.Clamp(CardOpacity, 0, 1) * 255);
            app.Resources["GlassBgBrush"] = new SolidColorBrush(Color.FromArgb(a, FrostTint.R, FrostTint.G, FrostTint.B));
        }
        else
        {
            app.Resources.Remove("GlassBgBrush");
        }
    }

    public ThemeViewModel()
    {
        // Assign the backing field directly so selecting the persisted choice here doesn't re-fire
        // Apply at construction (the theme is already applied at startup via ThemeService.ApplySaved).
        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Id == ThemeService.SavedChoice) ?? ThemeOptions[0];
        ApplyOrClearFrost(); // restore the persisted frost level (Sakura only)
    }

    // Fires only on user-driven changes, so it both applies and persists.
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(ShowPetalsToggle));
        OnPropertyChanged(nameof(ShowFrostControls));
        if (value != null) ThemeService.Apply(value.Id);
        ApplyOrClearFrost(); // re-apply for Sakura, clear for other themes
    }

    partial void OnPetalsEnabledChanged(bool value) => AnimationSettings.PetalsEnabled = value;

    /// <summary>Sakura swatch: the same pink→rose gradient as the skin's accent buttons.</summary>
    private static IBrush SakuraSwatch() => Gradient("#FF7EC4", "#E0218A");

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

    private static IBrush Gradient(string from, string to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(from), 0),
            new GradientStop(Color.Parse(to), 1),
        },
    };
}
