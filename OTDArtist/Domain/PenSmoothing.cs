using System;

namespace OtdArtist.Domain;

/// <summary>
/// Exponential-moving-average smoothing. A <c>factor</c> in [0, <see cref="MaxFactor"/>] (0 =
/// passthrough, higher = heavier lag) blends the new value toward the previous one by
/// <c>alpha = 1 - factor</c>: <c>next = prev + alpha * (current - prev)</c>. State (the previous
/// value) is owned by the caller so it can be reset per stroke.
///
/// The user-facing slider is an <c>amount</c> in [0,1] mapped to the factor by
/// <see cref="FactorFromAmount"/> — a perceptual curve borrowed from Slimy Scylla, because a raw
/// linear factor packs almost all the perceptible change into the very top of the range.
/// </summary>
public static class PenSmoothing
{
    /// <summary>Heaviest smoothing allowed; 1.0 would freeze the output entirely.</summary>
    public const double MaxFactor = 0.99;

    /// <summary>One EMA step. With no previous value (stroke start) or factor &lt;= 0, returns
    /// <paramref name="current"/> unchanged.</summary>
    public static double Ema(double current, double? previous, double factor)
    {
        factor = factor < 0 ? 0 : factor > MaxFactor ? MaxFactor : factor;
        if (factor <= 0 || previous is not double prev) return current;
        double alpha = 1 - factor;
        return prev + alpha * (current - prev);
    }

    /// <summary>Map a slider <c>amount</c> in [0,1] to an EMA factor via Slimy Scylla's
    /// <c>amount^(0.02/amount)</c> curve, so the difference between, say, 0.2 and 0.4 is felt as much
    /// as between 0.6 and 0.8. 0 ⇒ off; 1 ⇒ <see cref="MaxFactor"/>.</summary>
    public static double FactorFromAmount(double amount)
    {
        if (amount <= 0) return 0;
        if (amount >= 1) return MaxFactor;
        double factor = Math.Pow(amount, 0.02 / amount);
        return factor > MaxFactor ? MaxFactor : factor;
    }
}
