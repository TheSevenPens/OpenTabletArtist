using System.Linq;
using OtdHealth.Collector;

namespace OpenTabletArtist.Services;

/// <summary>Whether a report has said anything about VMulti yet.</summary>
public enum VMultiCheck
{
    /// <summary>No report yet, so nothing is known. Not the same as "not installed".</summary>
    Pending,
    /// <summary>A report arrived but its VMulti probe did not complete (timed out, failed, not applicable).</summary>
    Unreadable,
    /// <summary>The probe completed.</summary>
    Read,
}

/// <summary>
/// What the latest health report says about VMulti, in the terms the VMulti page and Windows Ink
/// auto-setup need. The only place that turns a report into an answer, so Home, the page and auto-setup
/// cannot disagree about the install state: <see cref="Installed"/> is the snapshot's own
/// <c>VMultiInstalled</c> (the setup-API verdict), the value Home's finding reads.
/// </summary>
/// <remarks>
/// <para>"Installed" and "functional" are different questions with different sources. Installed is the
/// device node, and it is what a finding is made from. Functional is the HID control channel, which is
/// what Windows Ink needs before switching a tablet to it (without it the pointer stops), and it is
/// recorded on the probe as supporting evidence, never as the verdict. A virtual pen that Windows shows
/// in HID but whose node carries no known hardware ID is therefore not installed here, as on Home; the
/// probe notes that disagreement as <c>signals-disagree</c> for whoever reads the report.</para>
/// <para>Findings still come only from the snapshot. This reads <see cref="ProbeResult.Details"/> for
/// wording and for the functional check, and nothing it reads feeds the evaluator.</para>
/// </remarks>
public sealed record VMultiStatus(
    VMultiCheck Check, bool Installed, bool Enabled, bool Functional,
    string Summary, string SetupApiText, string HidText)
{
    public bool Known => Check == VMultiCheck.Read;

    public static VMultiStatus Pending { get; } =
        new(VMultiCheck.Pending, false, false, false, "Checking...", "Checking...", "Checking...");

    /// <summary>The check ran (or was asked to) and produced no answer; says so rather than guessing.</summary>
    public static VMultiStatus Unreadable(string why) =>
        new(VMultiCheck.Unreadable, false, false, false, $"Couldn't check: {why}", why, why);

    public static VMultiStatus From(HealthAnalysisReport? report)
    {
        if (report == null) return Pending;

        var probe = report.Probes.FirstOrDefault(p => p.Id == ProbeId.VMulti);
        if (probe is not { Outcome: ProbeOutcome.Completed } || report.Snapshot.VMultiInstalled is not { } installed)
        {
            var why = probe?.Outcome == ProbeOutcome.NotApplicable ? "Not applicable on this platform"
                : probe?.Failure?.Message ?? "The check did not run";
            return Unreadable(why);
        }

        var details = probe.Details as VMultiDetails;
        // Disabled means installed with no node working; a leftover beside a working node doesn't count.
        bool enabled = installed && (details == null || details.Nodes.Any(n => n.ProblemCode == 0));
        bool functional = details?.Hid is { Error: null, ControlChannel: true };
        bool hidVisible = details?.Hid is { Error: null, Visible: true };

        var summary =
            installed && functional ? "Installed & active"
            : installed && !enabled ? "Installed but disabled"
            : installed ? "Installed (not active in HID)"
            : hidVisible ? "Not installed (a virtual pen is still visible to HID)"
            : "Not installed";

        return new(VMultiCheck.Read, installed, enabled, functional, summary,
            details?.SetupApiVerdict ?? (installed ? "Installed" : "Not installed"),
            DescribeHid(details?.Hid));
    }

    private static string DescribeHid(VMultiHidObservation? hid) => hid switch
    {
        null => "Not read",
        { Error: { } error } => $"Error: {error}",
        { Visible: false } => "Not visible to HID",
        { ControlChannel: true } h => $"Active ({h.DeviceCount} devices)",
        _ => "Visible but no control channel",
    };
}
