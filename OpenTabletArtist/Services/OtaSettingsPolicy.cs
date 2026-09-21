using OpenTabletDriver.Desktop;

namespace OpenTabletArtist.Services;

/// <summary>Artist-specific rules run before admitting a settings operation to OTD Interop.</summary>
public static class OtaSettingsPolicy
{
    public static void Prepare(Settings workingCopy, bool ownedDaemon)
    {
        ProfileFilterMaintenance.CleanLegacyFilters(workingCopy);
        if (ownedDaemon) ProfileFilterMaintenance.DisableUnapprovedFilters(workingCopy);
    }
}
