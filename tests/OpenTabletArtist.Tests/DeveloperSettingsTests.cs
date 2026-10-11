using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DeveloperSettingsTests
{
    [Fact]
    public void ShowDiagnosticsTab_IsATabVisibilityToggle_NotAHealthFlag()
    {
        // Like the Filters / JSON tab toggles it changes what is shown, never the health catalog — so
        // flipping it must not make the health service re-evaluate.
        Assert.False(DeveloperSettings.AffectsHealth(nameof(DeveloperSettings.ShowDiagnosticsTab)));
        Assert.True(DeveloperSettings.AffectsHealth(nameof(DeveloperSettings.ForceDriverConflict)));
    }
}
