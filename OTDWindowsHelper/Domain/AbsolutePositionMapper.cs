using System;
using System.Numerics;

namespace OtdWindowsHelper.Domain;

/// <summary>Tablet digitizer geometry needed to scale raw report units to millimetres.</summary>
public readonly record struct TabletDigitizerSpec(float Width, float Height, float MaxX, float MaxY);

/// <summary>An OTD area (tablet input or display output), positioned by its <em>centre</em> — the
/// same convention as OTD's <c>Area</c> (its <c>Position</c> is the centre).</summary>
public readonly record struct MappingArea(float CenterX, float CenterY, float Width, float Height, float Rotation = 0);

/// <summary>
/// Replicates OpenTabletDriver's Absolute-mode transform (<c>AbsoluteOutputMode</c>) so the Test tab
/// can map a raw tablet position to the on-screen pixel where the pen points — independently of the
/// Windows Ink output stage. Mirrors OTD's exact matrix (pinned in the submodule); kept pure so it's
/// unit-tested against known cases to guard against drift.
/// </summary>
public static class AbsolutePositionMapper
{
    /// <summary>Build the raw-tablet-units → virtual-desktop-pixels matrix (OTD's
    /// <c>CalculateTransformation</c>, byte for byte).</summary>
    public static Matrix3x2 CreateMatrix(TabletDigitizerSpec digitizer, MappingArea input, MappingArea output)
    {
        // Raw tablet units → millimetres.
        var res = Matrix3x2.CreateScale(digitizer.Width / digitizer.MaxX, digitizer.Height / digitizer.MaxY);
        // Centre on the input (tablet) area.
        res *= Matrix3x2.CreateTranslation(-input.CenterX, -input.CenterY);
        // Tablet-area rotation.
        res *= Matrix3x2.CreateRotation((float)(-input.Rotation * Math.PI / 180));
        // Millimetres → output (display) pixels.
        res *= Matrix3x2.CreateScale(output.Width / input.Width, output.Height / input.Height);
        // Into virtual-desktop coordinates.
        res *= Matrix3x2.CreateTranslation(output.CenterX, output.CenterY);
        return res;
    }

    /// <summary>Map a raw tablet point to a virtual-desktop pixel. Returns null when the inputs are
    /// degenerate, or when <paramref name="areaLimiting"/> is on and the point falls outside the
    /// output rect (matching OTD, where limiting drops the report). Clamps to the output rect when
    /// <paramref name="areaClipping"/> is on.</summary>
    public static Vector2? MapToDesktop(Vector2 raw, TabletDigitizerSpec digitizer,
        MappingArea input, MappingArea output, bool areaClipping, bool areaLimiting)
    {
        if (digitizer.MaxX <= 0 || digitizer.MaxY <= 0 || input.Width <= 0 || input.Height <= 0) return null;

        var pos = Vector2.Transform(raw, CreateMatrix(digitizer, input, output));

        var half = new Vector2(output.Width / 2, output.Height / 2);
        var center = new Vector2(output.CenterX, output.CenterY);
        var clamped = Vector2.Clamp(pos, center - half, center + half);

        if (areaLimiting && clamped != pos) return null;
        if (areaClipping) pos = clamped;
        return pos;
    }

    /// <summary>Map a virtual-desktop pixel back to raw tablet units — the inverse of
    /// <see cref="MapToDesktop"/> (no clipping/limiting). Used by calibration (#127) to find the raw
    /// position that *should* land on a given on-screen target. Returns null when the inputs are
    /// degenerate or the transform isn't invertible.</summary>
    public static Vector2? MapFromDesktop(Vector2 desktopPx, TabletDigitizerSpec digitizer,
        MappingArea input, MappingArea output)
    {
        if (digitizer.MaxX <= 0 || digitizer.MaxY <= 0 || input.Width <= 0 || input.Height <= 0) return null;
        if (!Matrix3x2.Invert(CreateMatrix(digitizer, input, output), out var inverse)) return null;
        return Vector2.Transform(desktopPx, inverse);
    }
}
