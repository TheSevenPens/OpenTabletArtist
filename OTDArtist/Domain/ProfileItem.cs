using OpenTabletDriver.Desktop.Profiles;

namespace OtdArtist.Domain;

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

    public string? LastSeenDetail
    {
        get
        {
            if (IsDetected || LastSeen == null) return null;
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
