using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

/// <summary>
/// A tablet profile wrapped with detection status for the Paired Tablets list. Produced by
/// the session's data load and consumed by the view models, so it lives in Domain (no UI deps).
/// </summary>
public record ProfileItem(Profile Profile, bool IsDetected, DateTime? LastSeen)
{
    public string Tablet => Profile.Tablet;

    public string StatusText
    {
        get
        {
            if (IsDetected) return "Detected";
            if (LastSeen == null) return "Not detected";
            return $"Not detected — {FormatRelativeTime(LastSeen.Value)}";
        }
    }

    /// <summary>
    /// The card's quiet third line: CONNECTED for a tablet that is here, when it was last seen for one
    /// that is not.
    /// </summary>
    /// <remarks>
    /// A connected tablet has no last-seen time to show — it is being seen — so the line used to be
    /// blank, and the card lost a row while its neighbours kept theirs. Saying so outright also puts the
    /// state in words on the card itself, where previously it was carried only by the link icon and its
    /// tooltip.
    /// </remarks>
    public string? StatusDetail
    {
        get
        {
            if (IsDetected) return "CONNECTED";
            if (LastSeen == null) return null;
            return $"Last seen {LastSeen.Value:yyyy-MM-dd} at {LastSeen.Value:h:mm tt}";
        }
    }

    private static string FormatRelativeTime(DateTime lastSeen)
    {
        var elapsed = DateTime.Now - lastSeen;

        if (elapsed.TotalMinutes < 1) return "seen just now";
        if (elapsed.TotalMinutes < 60) return $"seen {(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"seen {(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 2) return "seen yesterday";
        if (elapsed.TotalDays < 7) return $"seen {(int)elapsed.TotalDays} days ago";
        if (elapsed.TotalDays < 30) return $"seen {(int)(elapsed.TotalDays / 7)} weeks ago";
        return "seen a long time ago";
    }
}
