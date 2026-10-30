using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class TrayPresetMenuTests
{
    private static PresetInfo Preset(string name) => new(name, name + ".json", "{}", "2026-01-01 00:00:00");

    [Fact]
    public void TheTrayListsPresetsByName_NotNewestFirst()
    {
        // The page's order: newest first, so it changes whenever a preset is saved or updated.
        var page = new[] { Preset("sketching"), Preset("Inking"), Preset("Animation") };

        Assert.Equal(new[] { "Animation", "Inking", "sketching" }, AppTray.PresetMenuNames(page));
    }

    [Fact]
    public void NoPresets_NoEntries() => Assert.Empty(AppTray.PresetMenuNames([]));
}
