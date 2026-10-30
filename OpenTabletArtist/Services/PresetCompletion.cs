using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Services;

/// <summary>
/// A preset made to fit the tablets that are here now.
/// </summary>
/// <remarks>
/// <para>
/// A preset is the driver's whole document as it was when it was saved, so it only holds profiles for the
/// tablets that were known then. Applied raw to a driver with a tablet it has no profile for, the daemon
/// generates one on the spot (<c>ProfileCollection.GetProfile</c> adds the missing profile as
/// <c>SetSettings</c> walks the detected tablets), the read-back no longer equals what was sent, and the
/// apply is reported as rejected. That pauses editing across the whole app, for a load the artist asked
/// for and nothing else had touched.
/// </para>
/// <para>
/// So a tablet the preset has nothing to say about keeps the profile it has now. Every such tablet, not
/// only the plugged-in ones: a remembered tablet's setup would otherwise go because an old preset predates
/// it, and nothing here knows which tablets are plugged in anyway.
/// </para>
/// </remarks>
internal static class PresetCompletion
{
    /// <summary>
    /// Adds to <paramref name="preset"/> the profile <paramref name="current"/> holds for each tablet the
    /// preset has none for, and says how many that was. Nothing the preset does hold is changed.
    /// </summary>
    public static int AddMissingProfiles(Settings preset, Settings? current)
    {
        if (current?.Profiles is null) return 0;
        preset.Profiles ??= new ProfileCollection();
        var added = 0;
        foreach (var profile in current.Profiles)
        {
            if (profile.Tablet is not { } tablet || preset.Profiles.GetProfile(tablet) is not null) continue;
            preset.Profiles.Add(profile);
            added++;
        }
        return added;
    }
}
