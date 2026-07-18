namespace OpenTabletArtist.Domain;

/// <summary>
/// One connected monitor, in virtual-desktop pixels. <see cref="Number"/> is the Windows display
/// number; <see cref="Name"/> is the friendly monitor name (may be empty if it couldn't be read);
/// <see cref="RefreshHz"/> is the current refresh rate in Hz (0 if unknown). <see cref="Port"/> is the
/// connector type (HDMI/DisplayPort/USB-C/Internal/…) and <see cref="Gpu"/> the adapter that drives it;
/// both are best-effort and may be empty.
/// </summary>
public record DisplayInfo(int Number, string Name, int Width, int Height, int X, int Y, bool IsPrimary,
    int RefreshHz = 0, string Port = "", string Gpu = "")
{
    public string Resolution => $"{Width}×{Height}";

    /// <summary>True when a real refresh rate was read (Windows reports 0 or 1 for "default/unknown").</summary>
    public bool HasRefreshRate => RefreshHz > 1;

    /// <summary>Refresh rate as a short label, e.g. "144 Hz" (empty when unknown).</summary>
    public string RefreshRateText => HasRefreshRate ? $"{RefreshHz} Hz" : "";

    /// <summary>Resolution plus refresh rate when known, e.g. "2560×1440 · 144 Hz".</summary>
    public string ResolutionWithRefresh => HasRefreshRate ? $"{Resolution} · {RefreshRateText}" : Resolution;

    /// <summary>Heading line, e.g. "Display 1 (Primary)".</summary>
    public string Caption => IsPrimary ? $"Display {Number} (Primary)" : $"Display {Number}";

    public bool HasPort => !string.IsNullOrWhiteSpace(Port);
    public bool HasGpu => !string.IsNullOrWhiteSpace(Gpu);

    /// <summary>Row title for the detail list: the monitor's friendly name, or "Display N" if unknown.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? $"Display {Number}" : Name;

    /// <summary>Row title with the primary marker appended next to the name, e.g. "ASUS PA329CV (PRIMARY)".
    /// Used by the per-display list so the designation reads with the name rather than in the detail line.</summary>
    public string DisplayTitleWithPrimary => IsPrimary ? $"{DisplayTitle} (PRIMARY)" : DisplayTitle;

    /// <summary>Detail line beneath the title: resolution · refresh · port · GPU (blanks skipped). The
    /// primary marker lives on <see cref="DisplayTitleWithPrimary"/> next to the name instead.</summary>
    public string DetailSubLine
    {
        get
        {
            var s = ResolutionWithRefresh;
            if (HasPort) s += "  ·  " + Port;
            if (HasGpu) s += "  ·  " + Gpu;
            return s;
        }
    }
}
