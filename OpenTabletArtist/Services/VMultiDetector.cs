namespace OpenTabletArtist.Services;

/// <summary>Presentation-compatible facade over the shared read-only detector.</summary>
public class VMultiDetector
{
    public static SetupApiDetectionResult ClassifySetupApi(IReadOnlyList<DeviceInfo> devices)
    {
        var r = OtdHealth.Collector.VMultiDetector.ClassifySetupApi(devices.Select(d =>
            new OtdHealth.Collector.DeviceInfo(d.HardwareId, d.Description, d.Enabled, d.Problem)).ToArray());
        return new(r.Installed, r.Enabled, r.Message);
    }
}
public record SetupApiDetectionResult(bool Installed, bool Enabled, string Message);
public record DeviceInfo(string HardwareId, string Description, bool Enabled, uint Problem = 0);
