namespace OtdWindowsHelper.Domain;

/// <summary>
/// One connected monitor, in virtual-desktop pixels. <see cref="Number"/> is the Windows display
/// number; <see cref="Name"/> is the friendly monitor name (may be empty if it couldn't be read);
/// <see cref="RefreshHz"/> is the current refresh rate in Hz (0 if unknown).
/// </summary>
public record DisplayInfo(int Number, string Name, int Width, int Height, int X, int Y, bool IsPrimary, int RefreshHz = 0)
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
}
