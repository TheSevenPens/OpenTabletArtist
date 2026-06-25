namespace OtdWindowsHelper.Domain;

/// <summary>
/// Exponential-moving-average smoothing, matching PenDynamicsLab: a <c>smoothing</c> factor in
/// [0, <see cref="MaxFactor"/>] where 0 = passthrough and higher = heavier lag. Each step blends
/// the new value toward the previous one by <c>alpha = 1 - factor</c>:
/// <c>next = prev + alpha * (current - prev)</c>. State (the previous value) is owned by the caller
/// so it can be reset per stroke.
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
}
