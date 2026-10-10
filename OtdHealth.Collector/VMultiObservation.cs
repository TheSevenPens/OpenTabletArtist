using System.Text.Json.Serialization;

namespace OtdHealth.Collector;

/// <summary>What a probe saw, as context for people reading a report. Never an input to findings or to
/// <see cref="HealthAnalysisReport.IsComplete"/>: findings still come from the snapshot alone.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(VMultiDetails), "vmulti")]
public abstract record ProbeDetails;

/// <summary>A probe read that carries its observations along with the value the snapshot needs.</summary>
public interface IHasProbeDetails { ProbeDetails? Details { get; } }

/// <summary>The VMulti probe's result: the verdict the snapshot uses, plus what it was based on.</summary>
public sealed record VMultiObservation(bool Installed, VMultiDetails Details) : IHasProbeDetails
{
    ProbeDetails? IHasProbeDetails.Details => Details;
}

/// <summary>One device node, as read from Windows.</summary>
public sealed record VMultiNode(
    string InstanceId, IReadOnlyList<string> HardwareIds, string? Description, string? Service,
    bool Present, bool Enabled, uint ProblemCode,
    string? DriverInf, string? DriverVersion, string? DriverProvider);

/// <summary>A VMulti driver package staged in the Windows driver store (present with or without a device).</summary>
public sealed record VMultiDriverPackage(string PublishedName, string OriginalName, string Provider);

/// <summary>The HID view of the virtual pen: visible only while the device is enabled and running.</summary>
public sealed record VMultiHidObservation(bool Visible, bool ControlChannel, int DeviceCount, string? Error);

/// <param name="SetupApiVerdict">The classifier's text, e.g. "Installed &amp; enabled (1 devices)".</param>
/// <param name="MatchedBy">Which hardware ID counted, e.g. <c>hardware-id:pentablet\hid</c>; null if none did.</param>
/// <param name="Nodes">Present nodes carrying a known VMulti hardware ID.</param>
/// <param name="NearMisses">Present devices that mention vmulti/pentablet but did NOT match. Capped.</param>
/// <param name="NearMissTotal">The real count behind <paramref name="NearMisses"/>.</param>
/// <param name="StaleNodeCount">Non-present nodes with a known VMulti ID (leftovers of a removed device).</param>
/// <param name="DriverPackage">The staged package, if any.</param>
/// <param name="Hid">The second signal; recorded, never decisive.</param>
/// <param name="Notes">Cross-check codes. Informal, informational only, and free to change.</param>
public sealed record VMultiDetails(
    string SetupApiVerdict, string? MatchedBy,
    IReadOnlyList<VMultiNode> Nodes, IReadOnlyList<VMultiNode> NearMisses, int NearMissTotal,
    int StaleNodeCount, VMultiDriverPackage? DriverPackage, VMultiHidObservation? Hid,
    IReadOnlyList<string> Notes) : ProbeDetails;

/// <summary>Turns raw device rows into an observation. Pure: Windows gathers the rows, this decides what
/// they mean, so the logic can be tested without a machine that has (or lacks) the driver.</summary>
public static class VMultiInspector
{
    /// <summary>Keeps the report small: a machine can carry dozens of stale "PenTablet" HID entries.</summary>
    public const int MaxListed = 8;

    private static readonly string[] Keywords = ["vmulti", "pentablet"];

    /// <param name="present">Every present device, with its properties read.</param>
    /// <param name="staleNodeCount">Non-present nodes carrying a known VMulti ID.</param>
    public static VMultiObservation Build(IReadOnlyList<VMultiNode> present, int staleNodeCount,
        VMultiDriverPackage? package, VMultiHidObservation? hid)
    {
        var matched = present.Where(d => VMultiDetector.MatchHardwareId(d.HardwareIds) != null).ToList();
        var verdict = VMultiDetector.ClassifySetupApi(matched.Select(d =>
            new DeviceInfo(VMultiDetector.MatchHardwareId(d.HardwareIds)!, d.Description ?? "Unknown", d.Enabled, d.ProblemCode)).ToList());

        var nearMisses = present.Except(matched).Where(d => Mentions(d.HardwareIds, d.Description, d.Service)).ToList();
        string? matchedBy = matched
            .Select(d => VMultiDetector.MatchHardwareId(d.HardwareIds)!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() is { } id ? $"hardware-id:{id}" : null;

        var details = new VMultiDetails(verdict.Message, matchedBy,
            matched.Take(MaxListed).ToList(), nearMisses.Take(MaxListed).ToList(), nearMisses.Count,
            staleNodeCount, package, hid, Notes: []);
        return new VMultiObservation(verdict.Installed, details);
    }

    /// <summary>A device that looks like VMulti by name, service or any hardware ID without carrying a
    /// known one: the shape of the bug where a real install was missed.</summary>
    public static bool Mentions(IReadOnlyList<string> hardwareIds, string? description, string? service) =>
        Keywords.Any(k => hardwareIds.Any(id => id.Contains(k, StringComparison.OrdinalIgnoreCase))
            || (description?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
            || (service?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false));
}
