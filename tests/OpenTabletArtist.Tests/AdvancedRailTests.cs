using System.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The ADVANCED rail used to hide its Windows-only subpages off-Windows (#140), through a pure predicate this
/// file tested. Both of them have since left: Windows Ink for PLUGINS, and the VMulti driver for
/// SETTINGS → DRIVERS (#vmulti-to-drivers). Nothing on ADVANCED is OS-specific any more, so the predicate is
/// gone and this guards the claim it rested on — that the enum holds no Windows-only pivot. The OS filtering
/// now lives on the SETTINGS rail (<see cref="ViewModels.SettingsViewModel.TabAppliesToOs"/>), covered by
/// <see cref="SettingsRailTests"/>.
/// </summary>
public class AdvancedRailTests
{
    [Fact]
    public void EveryAdvancedTab_IsCrossPlatform()
    {
        // Named rather than counted, so adding a Windows-only pivot back has to come here and say so.
        Assert.Equal(
            new[]
            {
                AdvancedTab.Daemon, AdvancedTab.CustomTabletConfigs, AdvancedTab.Diagnostics,
                AdvancedTab.Plugins, AdvancedTab.Console,
            }.OrderBy(t => t),
            System.Enum.GetValues<AdvancedTab>().OrderBy(t => t));
    }
}
