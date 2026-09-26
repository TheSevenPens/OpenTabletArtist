namespace OpenTabletArtist.Services;

/// <summary>Presentation-compatible facade over the shared read-only detector.</summary>
public class VMultiDetector
{
    private readonly OtdHealth.Collector.VMultiDetector _detector = new();
    public HidDetectionResult DetectHid()
    {
        var r = _detector.DetectHid(); return new(r.Visible, r.Functional, r.Message);
    }
    public SetupApiDetectionResult DetectSetupApi()
    {
        var r = _detector.DetectSetupApi(); return new(r.Installed, r.Enabled, r.Message);
    }
    public static SetupApiDetectionResult ClassifySetupApi(IReadOnlyList<DeviceInfo> devices)
    {
        var r = OtdHealth.Collector.VMultiDetector.ClassifySetupApi(devices.Select(d =>
            new OtdHealth.Collector.DeviceInfo(d.HardwareId, d.Description, d.Enabled, d.Problem)).ToArray());
        return new(r.Installed, r.Enabled, r.Message);
    }
    public static List<DeviceInfo> FindAllDevicesByHardwareId(string id) =>
        OtdHealth.Collector.VMultiDetector.FindAllDevicesByHardwareId(id)
            .Select(d => new DeviceInfo(d.HardwareId, d.Description, d.Enabled, d.Problem)).ToList();
}
public record HidDetectionResult(bool Visible, bool Functional, string Message);
public record SetupApiDetectionResult(bool Installed, bool Enabled, string Message);
public record DeviceInfo(string HardwareId, string Description, bool Enabled, uint Problem = 0);
