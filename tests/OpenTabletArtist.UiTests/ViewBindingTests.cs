using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Views rendered against a real view model, with binding diagnostics treated as failures (#741).
///
/// <see cref="ViewLoadTests"/> proves a view can be built; this proves its bindings still reach
/// something. With compiled bindings off, those are different questions: a view whose view model was
/// renamed out from under it loads perfectly and shows nothing.
/// </summary>
public class ViewBindingTests
{
    private static Settings SettingsWith(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    /// <summary>
    /// The tablet editor is the largest view in the app and the one the redesign will disturb most —
    /// mapping, pen bindings, dynamics, calibration and hover all bind to one view model.
    /// </summary>
    [AvaloniaFact]
    public void TabletDetailView_BindsAgainstItsViewModel()
    {
        var settings = SettingsWith("Test Tablet");
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(),
            settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live),
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>(
                (settings, settings.Profiles.First())));

        using var errors = BindingErrors.Capture();
        HarnessTests.Show(new TabletDetailView { DataContext = vm });

        errors.AssertNone(nameof(TabletDetailView));
    }

    /// <summary>Each of the editor's tabs, since a tab's content is only realised once selected —
    /// binding errors in an unselected tab would otherwise go unseen.</summary>
    [AvaloniaTheory]
    [InlineData(TabletDetailTab.PenBehavior)]
    [InlineData(TabletDetailTab.DisplayMapping)]
    [InlineData(TabletDetailTab.PenInputs)]
    [InlineData(TabletDetailTab.PenDynamics)]
    public void TabletDetailView_BindsOnEveryTab(TabletDetailTab tab)
    {
        var settings = SettingsWith("Test Tablet");
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(),
            settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live),
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>(
                (settings, settings.Profiles.First())));
        // The view consumes the pending tab when it attaches, so ask before showing it.
        vm.RequestTab(tab);

        using var errors = BindingErrors.Capture();
        HarnessTests.Show(new TabletDetailView { DataContext = vm });

        errors.AssertNone($"{nameof(TabletDetailView)} on the {tab} tab");
    }

    /// <summary>A view whose DataContext is the wrong shape entirely. Proves these tests would notice —
    /// the same failure a renamed or split view model produces.</summary>
    [AvaloniaFact]
    public void AViewGivenTheWrongViewModel_IsCaught()
    {
        using var errors = BindingErrors.Capture();
        HarnessTests.Show(new TabletDetailView { DataContext = new { Nothing = "useful" } });

        Assert.NotEmpty(errors.Messages);
    }
}
