using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileFilterMaintenanceTests
{
    // Build a filter store at a given type path the same way the *Profile writers do (the app can't
    // reference the plugin type, so it deserializes an empty store and sets the path).
    private static PluginSettingStore StoreAt(string path)
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        store.Path = path;
        return store;
    }

    private static Settings WithFilters(params string[] filterPaths)
    {
        var profile = new Profile { Tablet = "T" };
        foreach (var p in filterPaths)
            profile.Filters.Add(StoreAt(p));
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static string[] Paths(Settings s) =>
        s.Profiles.First().Filters.Select(f => f.Path).ToArray();

    // The reported bug (#…): a pre-rename OtdArtist.* DynamicsFilter lingers beside the current
    // OpenTabletArtist one, so the Filters tab shows "DynamicsFilter" twice. Cleanup drops the orphan.
    [Fact]
    public void RemovesPreRenameOrphan_KeepsCurrent()
    {
        var settings = WithFilters(
            "OtdArtist.Dynamics.DynamicsFilter",          // stale orphan from before the rename
            PressureCurveProfile.FilterTypeName);          // current

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(new[] { PressureCurveProfile.FilterTypeName }, Paths(settings));
    }

    [Fact]
    public void RemovesAllKnownLegacyNamespaces_AndLegacyClassName()
    {
        var settings = WithFilters(
            "OtdArtist.Dynamics.DynamicsFilter",
            "OtdWindowsHelper.Dynamics.DynamicsFilter",     // real-world earliest name (mixed case)
            "OtdWindowsHelper.Dynamics.CalibrationFilter",  // its calibration sibling
            "OtdArtist.PressureCurve.PressureCurveFilter",  // even-older class name
            PressureCurveProfile.FilterTypeName,
            HoverProfile.FilterTypeName);

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(
            new[] { PressureCurveProfile.FilterTypeName, HoverProfile.FilterTypeName },
            Paths(settings));
    }

    // The orphan that shipped: an OtdWindowsHelper DynamicsFilter sitting beside the current one,
    // matched case-insensitively (the namespace appears as both "Otd..." and "OTD..." in the wild).
    [Theory]
    [InlineData("OtdWindowsHelper.Dynamics.DynamicsFilter")]
    [InlineData("OTDWindowsHelper.Dynamics.DynamicsFilter")]
    [InlineData("otdwindowshelper.dynamics.DynamicsFilter")]
    public void RemovesEarliestNamespaceOrphan_RegardlessOfCase(string legacyPath)
    {
        var settings = WithFilters(legacyPath, PressureCurveProfile.FilterTypeName);

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(new[] { PressureCurveProfile.FilterTypeName }, Paths(settings));
    }

    // An exact duplicate of a current store (somehow written twice) collapses to one.
    [Fact]
    public void CollapsesExactDuplicateOfCurrentStore()
    {
        var settings = WithFilters(
            PressureCurveProfile.FilterTypeName,
            PressureCurveProfile.FilterTypeName);

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(new[] { PressureCurveProfile.FilterTypeName }, Paths(settings));
    }

    // A clean profile is left untouched and reports no change (so we don't needlessly re-save).
    [Fact]
    public void NoChange_WhenOnlyCurrentFilters()
    {
        var settings = WithFilters(
            PressureCurveProfile.FilterTypeName,
            HoverProfile.FilterTypeName,
            CalibrationProfile.FilterTypeName);

        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(3, settings.Profiles.First().Filters.Count);
    }

    // Third-party filters — even one that coincidentally ends in a name we use — are never removed.
    [Fact]
    public void LeavesThirdPartyFiltersAlone()
    {
        var settings = WithFilters(
            "SomeVendor.Cool.DynamicsFilter", // same class name, but not our namespace → keep
            "OpenTabletDriver.Filters.Noise.NoiseReduction",
            PressureCurveProfile.FilterTypeName);

        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(3, settings.Profiles.First().Filters.Count);
    }

    [Fact]
    public void HandlesNullSettings()
    {
        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(null));
    }

    [Theory]
    [InlineData(null, ProfileFilterMaintenance.FilterOrigin.Unknown)]
    [InlineData("", ProfileFilterMaintenance.FilterOrigin.Unknown)]
    [InlineData("OpenTabletArtist.Dynamics.DynamicsFilter", ProfileFilterMaintenance.FilterOrigin.Current)]
    [InlineData("OtdWindowsHelper.Dynamics.DynamicsFilter", ProfileFilterMaintenance.FilterOrigin.Legacy)]
    [InlineData("OtdArtist.PressureCurve.PressureCurveFilter", ProfileFilterMaintenance.FilterOrigin.Legacy)]
    [InlineData("OpenTabletDriver.Filters.Noise.NoiseReduction", ProfileFilterMaintenance.FilterOrigin.Unknown)]
    public void Classify_TagsCurrentLegacyAndThirdParty(string? path, ProfileFilterMaintenance.FilterOrigin expected)
    {
        Assert.Equal(expected, ProfileFilterMaintenance.Classify(path));
    }
}
