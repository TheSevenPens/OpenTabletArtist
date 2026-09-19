using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// This app's rules about what may be written to a tablet's profile, supplied to OtdInterop (#807).
///
/// These are product decisions about this app's users, not facts about OpenTabletDriver, which is why
/// they live here rather than in the library. Which filters an application is willing to leave enabled is
/// exactly the sort of thing two applications could reasonably disagree about.
///
/// Both rules are forward guards: they run on the way out, every time, rather than on load. The shared
/// <c>settings.json</c> is also OpenTabletDriver's own file, so a malformed profile written from here
/// would break their interface too — repairing on the way out means it never gets written, instead of
/// being cleaned up on a load that may never happen.
/// </summary>
public sealed class OtaSettingsPolicy : IOtdSettingsPolicy
{
    /// <summary>The shared instance. The rules are static, so there is nothing to configure.</summary>
    public static readonly OtaSettingsPolicy Instance = new();

    private OtaSettingsPolicy() { }

    /// <inheritdoc />
    public void Apply(Settings workingCopy, SettingsPolicyContext context)
    {
        // Never write back a stale or duplicate filter store — one left behind by a rename, for example.
        ProfileFilterMaintenance.CleanLegacyFilters(workingCopy);

        // Keep only approved filters enabled, and only on a daemon this app positively owns (#465/#742).
        //
        // The ownership test is deliberately "is ours" rather than "is not theirs". Provenance is
        // three-valued, and a daemon we cannot identify is neither ours nor known to be someone else's.
        // Disabling a stranger's filters because we could not tell whose they were is worse than leaving
        // them alone: it is an unasked-for change to settings that may not belong to this user at all.
        if (context.IsOwnedDaemon)
            ProfileFilterMaintenance.DisableUnapprovedFilters(workingCopy);
    }
}
