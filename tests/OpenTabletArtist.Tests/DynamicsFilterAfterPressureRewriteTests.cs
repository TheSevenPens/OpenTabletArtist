using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OpenTabletArtist.Dynamics;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What the pressure curve receives now that OpenTabletDriver 0.6.8 applies the tip activation threshold
/// first (<see cref="PressureRewriteFilter"/> is prepended to the pipeline) instead of last, in the binding
/// handler. Before, the curve saw raw pressure and the threshold remapped its output; now the threshold
/// takes its share off the bottom and rescales, and the curve shapes what is left.
///
/// Runs the two real filters in the order the daemon is ASSUMED to build them: these tests wire the filters
/// themselves and never touch the daemon's pipeline, so on their own they say nothing about what it does. The
/// assumption is pinned by <see cref="TheDaemonPutsTheRewriteFilterFirst"/>, which reads the pinned daemon's
/// source (#1060). OTA exposes no activation threshold, so
/// the default (1%) is what nearly every profile has; these tests use a clearly non-default one so the
/// order is visible.
///
/// Two of them (<see cref="PressureUnderTheThreshold_NeverReachesTheCurve"/> and
/// <see cref="InputMinimum_IsMeasuredAfterTheThreshold"/>) fail if the filters run curve-first; checked by
/// running them that way. The other two hold in either order (a linear curve commutes with the rescale),
/// and are here for the plain contract, not the order.
/// </summary>
public class DynamicsFilterAfterPressureRewriteTests
{
    private const uint MaxPressure = 1000;

    private static uint Run(float thresholdPercent, uint rawPressure, Action<DynamicsFilter> configure)
    {
        var rewrite = new PressureRewriteFilter
        {
            TipPressureThreshold = thresholdPercent,
            EraserPressureThreshold = thresholdPercent,
            MaxPenPressure = MaxPressure,
        };

        var tablet = new TabletReference(new TabletConfiguration
        {
            Name = "Test tablet",
            Specifications = new TabletSpecifications
            {
                Digitizer = new DigitizerSpecifications { Width = 100, Height = 50, MaxX = 1000, MaxY = 500 },
                Pen = new PenSpecifications { MaxPressure = MaxPressure, ButtonCount = 2 },
            },
            DigitizerIdentifiers = [],
        }, []);

        var dynamics = new DynamicsFilter { Tablet = tablet };
        configure(dynamics);

        // The report is a struct reached through an interface, so the boxed copy is what both filters
        // mutate — the same by-reference behaviour the daemon relies on.
        ITabletReport report = new TabletReport(new byte[8]) { Pressure = rawPressure };
        IDeviceReport? emitted = null;
        dynamics.Emit += r => emitted = r;
        rewrite.Emit += dynamics.Consume;

        rewrite.Consume(report);

        return ((ITabletReport)emitted!).Pressure;
    }

    /// <summary>
    /// The order the tests above assume, read from the daemon that is pinned. This is the daemon's source text,
    /// not its behaviour: it fails when the submodule moves to a daemon that builds the pipeline differently,
    /// which is the moment the assumption needs a person to look at it.
    /// </summary>
    [Fact]
    public void TheDaemonPutsTheRewriteFilterFirst()
    {
        var path = Path.Combine(RepoRoot(), "external", "OpenTabletDriver", "OpenTabletDriver.Daemon", "DriverDaemon.cs");

        Assert.True(BuildsRewriteFirst(File.ReadAllText(path)),
            "OpenTabletDriver no longer prepends PressureRewriteFilter to the filters; re-check these tests' order");
    }

    /// <summary>And the check itself can say no: the same line with the filter last does not pass.</summary>
    [Theory]
    [InlineData("outputMode.Elements = elements.Prepend(pressureRewriteFilter).Append(bindingHandler).ToList();", true)]
    [InlineData("outputMode.Elements = elements.Append(pressureRewriteFilter).Append(bindingHandler).ToList();", false)]
    [InlineData("outputMode.Elements = elements.Prepend(bindingHandler).Append(pressureRewriteFilter).ToList();", false)]
    [InlineData("", false)]
    public void TheOrderCheck_AcceptsOnlyTheRewriteFilterFirst(string source, bool expected) =>
        Assert.Equal(expected, BuildsRewriteFirst(source));

    private static bool BuildsRewriteFirst(string daemonSource) =>
        Regex.IsMatch(daemonSource, @"Elements\s*=\s*elements\s*\.Prepend\(\s*pressureRewriteFilter\s*\)");

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    [Fact]
    public void LinearCurve_PassesOnWhatTheThresholdLeft()
    {
        // 10% threshold: raw 55% is (0.55 - 0.10) / 0.90 = 50% of the remaining range.
        Assert.Equal(500u, Run(10, 550, _ => { }));
    }

    [Fact]
    public void PressureUnderTheThreshold_NeverReachesTheCurve()
    {
        Assert.Equal(0u, Run(10, 90, d => { d.Minimum = 0.3f; }));
    }

    [Fact]
    public void FullPressure_IsStillFullOutput()
    {
        Assert.Equal(MaxPressure, Run(10, MaxPressure, _ => { }));
    }

    [Fact]
    public void InputMinimum_IsMeasuredAfterTheThreshold()
    {
        // Input minimum 0.5 holding at an output minimum of 0.3. Raw 52% clears 0.5 on its own, but the
        // threshold has already taken it down to (0.52 - 0.10) / 0.90 = 46.7%, so the curve holds it at the
        // output minimum: exactly 300. Curve-first it would have come out near 253 (curve, then rescale).
        // Raw 60% becomes 55.6%, which is past the minimum, so it rises above it.
        void Hold(DynamicsFilter d) { d.InputMinimum = 0.5f; d.Minimum = 0.3f; }

        Assert.Equal(300u, Run(10, 520, Hold));
        Assert.True(Run(10, 600, Hold) > 300u);
    }
}
