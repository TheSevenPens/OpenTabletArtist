using OpenTabletDriver.Desktop.Profiles;

namespace OtdHealth.Collector;

/// <summary>Shared interpretation of raw OTD profile evidence; never normalizes or edits profiles.</summary>
public static class ProfileInspector
{
    public static void Validate(IReadOnlyList<ProfileObservation> profiles)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in profiles)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id) || string.IsNullOrWhiteSpace(p.Name)
                || p.Profile?.BindingSettings == null)
                throw new InvalidDataException("Profile identities must be unique and profiles must contain binding settings.");
            if (p.Profile.Filters?.Any(f => f == null) == true)
                throw new InvalidDataException("A profile contains a null filter.");
            foreach (var area in new[] { p.Profile.AbsoluteModeSettings?.Display, p.Profile.AbsoluteModeSettings?.Tablet })
                if (area != null && (!float.IsFinite(area.X) || !float.IsFinite(area.Y) || !float.IsFinite(area.Width)
                    || !float.IsFinite(area.Height) || !float.IsFinite(area.Rotation)))
                    throw new InvalidDataException("A profile contains a non-finite area.");
        }
    }

    /// <summary>OTD profiles have no GUID. Use an escaped config name and occurrence within that name,
    /// independent of unrelated profile ordering. Consumers with durable hardware IDs can supply them
    /// directly in ProfileObservation. Identical-name slots cannot identify physical devices.</summary>
    public static IReadOnlyList<ProfileObservation> Identify(IEnumerable<(Profile Profile, bool Detected)> profiles)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        return profiles.Select(p =>
        {
            string name = p.Profile.Tablet;
            counts.TryGetValue(name, out var ordinal); counts[name] = ordinal + 1;
            return new ProfileObservation($"profile:{Uri.EscapeDataString(name)}:{ordinal}", name, p.Profile, p.Detected);
        }).ToArray();
    }

    public static TabletHealthSnapshot Read(ProfileObservation item, IReadOnlyList<DisplayBounds> displays,
        IReadOnlySet<string> overrides, HealthPolicy policy)
    {
        var p = item.Profile;
        var rotation = p.AbsoluteModeSettings?.Tablet?.Rotation ?? 0;
        double normalized = ((rotation % 360f) + 360f) % 360f;
        return new(item.Name, item.Detected,
            (p.OutputMode?.Path ?? "").Contains("WinInk", StringComparison.OrdinalIgnoreCase),
            item.Detected ? ClassifyMapping(p, displays) : DisplayMappingStatus.None,
            item.Detected && Math.Abs(normalized - Math.Round(normalized / 90) * 90) > 0.5,
            policy.RequiredDynamicsFilter is { } path && p.Filters?.Any(f => f.Path == path && !f.Enable) == true,
            overrides.Contains(item.Name), policy.WinInkOptedOutProfileIds.Contains(item.Id),
            p.BindingSettings.TipButton?.Path == null, p.BindingSettings.DisablePressure,
            p.BindingSettings.DisableTilt, item.Id);
    }

    public static DisplayMappingStatus ClassifyMapping(Profile profile, IReadOnlyList<DisplayBounds> displays)
    {
        var disp = profile.AbsoluteModeSettings?.Display;
        if (disp == null || disp.Width <= 0 || disp.Height <= 0 || displays.Count == 0)
            return DisplayMappingStatus.None;
        float minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        static bool Near(float a, float b) => Math.Abs(a - b) <= 1.5f;
        if (displays.Any(d => Near(disp.Width, d.Width) && Near(disp.Height, d.Height)
            && Near(disp.X, d.X - minX + d.Width / 2f) && Near(disp.Y, d.Y - minY + d.Height / 2f)))
            return DisplayMappingStatus.Clean;
        float left = disp.X - disp.Width / 2f, top = disp.Y - disp.Height / 2f;
        float right = left + disp.Width, bottom = top + disp.Height;
        float covered = 0;
        foreach (var d in displays)
        {
            float dl = d.X - minX, dt = d.Y - minY;
            covered += Math.Max(0, Math.Min(right, dl + d.Width) - Math.Max(left, dl))
                * Math.Max(0, Math.Min(bottom, dt + d.Height) - Math.Max(top, dt));
        }
        float area = disp.Width * disp.Height;
        return covered >= area - Math.Max(1, area * 0.01f) ? DisplayMappingStatus.Custom : DisplayMappingStatus.OffScreen;
    }
}
