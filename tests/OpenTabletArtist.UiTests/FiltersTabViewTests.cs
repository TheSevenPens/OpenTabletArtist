using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The filters have moved from each tablet's page to the Advanced page (#1049): one section per tablet there, and
/// nothing left on the tablet page.
/// </summary>
public class FiltersTabViewTests
{
    private static Profile Profile(string tablet, bool hover)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { OutputMode = TestOtd.Output(), Tablet = tablet } } };
        PressureCurveProfile.Write(settings, tablet, PenDynamicsSettings.Default, enable: true);
        if (hover) HoverProfile.Write(settings, tablet, 127, enable: true, nearProximityOnly: false);
        return settings.Profiles.First();
    }

    private static List<string?> VisibleTexts(Control view) =>
        view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void EveryTablet_IsASection_WithItsOwnFilters()
    {
        var data = new FakeDeviceData
        {
            Profiles =
            [
                new ProfileItem(Profile("Wacom PTK-870", hover: true), true, null),
                new ProfileItem(Profile("Huion HS611", hover: false), false, null),
            ],
        };
        using var vm = new FiltersViewModel(data);
        var view = new FiltersView { DataContext = vm };
        var window = HarnessTests.Show(view);

        var texts = VisibleTexts(view);

        Assert.Contains("Wacom PTK-870", texts);
        Assert.Contains("Huion HS611", texts);
        Assert.Equal(2, texts.Count(t => t == "Pen Dynamics"));   // one per tablet
        Assert.Equal(1, texts.Count(t => t == "Hover Limit"));    // only the tablet that has it
        window.Close();
    }

    [AvaloniaFact]
    public void WithNoTablets_ItSaysSo()
    {
        using var vm = new FiltersViewModel(new FakeDeviceData());
        var view = new FiltersView { DataContext = vm };
        var window = HarnessTests.Show(view);

        Assert.Contains("No tablets yet. Connect one and its filters will be listed here.", VisibleTexts(view));
        window.Close();
    }

    /// <summary>A tablet's page has no Filters tab any more, even with the developer switch on: it would only
    /// repeat what the Advanced page now lists, and for one tablet.</summary>
    [AvaloniaFact]
    public void TheTabletPage_HasNoFiltersTab_EvenWhenTheDeveloperSwitchIsOn()
    {
        var wasFilters = DeveloperSettings.Instance.ShowFiltersTab;
        var wasJson = DeveloperSettings.Instance.ShowJsonTab;
        try
        {
            DeveloperSettings.Instance.ShowFiltersTab = true;
            DeveloperSettings.Instance.ShowJsonTab = true;
            var profile = Profile("Wacom PTK-870", hover: false);
            var settings = new Settings { Profiles = new ProfileCollection { profile } };
            var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
            var view = new TabletDetailView { DataContext = vm };
            var window = HarnessTests.Show(view);

            var tabs = view.GetVisualDescendants().OfType<RadioButton>()
                .Where(r => r.IsEffectivelyVisible).Select(r => r.Content as string).ToList();

            Assert.DoesNotContain("filters", tabs);
            Assert.Contains("json", tabs);   // the JSON tab is a separate switch, and is on here, so it stays
            window.Close();
        }
        finally
        {
            DeveloperSettings.Instance.ShowFiltersTab = wasFilters;
            DeveloperSettings.Instance.ShowJsonTab = wasJson;
        }
    }
}
