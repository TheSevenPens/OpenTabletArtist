using System;

namespace OtdWindowsHelper.Domain;

/// <summary>How the Extended curve behaves below <see cref="PressureCurveSettings.InputMinimum"/>.</summary>
public enum PressureMinApproach
{
    /// <summary>Output holds at Minimum below InputMinimum.</summary>
    Clamp,
    /// <summary>Output is 0 below InputMinimum (a dead zone), then jumps to Minimum.</summary>
    Cut,
}

/// <summary>
/// Parameters for the "Extended" pressure curve — a power curve (shaped by <see cref="Softness"/>)
/// with input/output remapping and a Clamp/Cut min approach. Ported from PenDynamicsLab. This type
/// + <see cref="PressureCurve"/> are the single source of truth, source-linked into the OTD plugin
/// so the daemon-side filter and the app's preview compute identical results.
/// </summary>
public readonly record struct PressureCurveSettings(
    double Softness,                   // -0.9..0.9 ; 0 = linear, >0 concave (lighter), <0 convex (heavier)
    double InputMinimum,               // 0..1
    double InputMaximum,               // 0..1
    double Minimum,                    // 0..1 output floor
    double Maximum,                    // 0..1 output ceiling (lower to cap how hard the pen registers)
    PressureMinApproach MinApproach)
{
    public static PressureCurveSettings Default { get; } =
        new(Softness: 0, InputMinimum: 0, InputMaximum: 1, Minimum: 0, Maximum: 1, MinApproach: PressureMinApproach.Clamp);
}

/// <summary>The Extended pressure curve: maps a normalized pressure in [0,1] to [0,1].</summary>
public static class PressureCurve
{
    public static double Apply(double x, PressureCurveSettings p)
    {
        // Cut: below the input minimum there is no output (dead zone).
        if (p.MinApproach == PressureMinApproach.Cut && x < p.InputMinimum)
            return 0;

        // Remap input into [InputMinimum, InputMaximum]; below it clamps to 0 (→ Output Minimum).
        var inputRange = p.InputMaximum - p.InputMinimum;
        var xNorm = inputRange > 0 ? Clamp01((x - p.InputMinimum) / inputRange) : 0;

        // Power curve. softness>=0 → exponent 1-softness (concave); softness<0 → 1/(1+softness) (convex).
        var exponent = p.Softness >= 0 ? 1 - p.Softness : 1 / (1 + p.Softness);
        var curved = Math.Pow(Math.Max(0, xNorm), exponent);

        // Scale into the output range.
        return Clamp01(p.Minimum + curved * (p.Maximum - p.Minimum));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
