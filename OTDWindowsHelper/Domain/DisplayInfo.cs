namespace OtdWindowsHelper.Domain;

/// <summary>
/// One connected monitor, in virtual-desktop pixels. <see cref="Number"/> is the Windows display
/// number; <see cref="Name"/> is the friendly monitor name (may be empty if it couldn't be read).
/// </summary>
public record DisplayInfo(int Number, string Name, int Width, int Height, int X, int Y, bool IsPrimary)
{
    public string Resolution => $"{Width}×{Height}";

    /// <summary>Heading line, e.g. "Display 1 (Primary)".</summary>
    public string Caption => IsPrimary ? $"Display {Number} (Primary)" : $"Display {Number}";
}
