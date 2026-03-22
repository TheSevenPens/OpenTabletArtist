using HidSharp;

namespace Bridge.Services;

public class VMultiDetector
{
    // vmulti registers as HID device with these IDs
    // Source: https://github.com/X9VoiD/VoiDPlugins VMultiInstance.cs
    private const int VMultiVendorId = 0x00FF;  // 255
    private const int VMultiProductId = 0xBACC;  // 47820

    public VMultiStatus Detect()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(
                vendorID: VMultiVendorId,
                productID: VMultiProductId
            ).ToArray();

            if (devices.Length == 0)
                return new VMultiStatus(false, false, "Driver not found");

            // Check for the control channel (65-byte reports) — means driver is loaded
            bool hasControlChannel = devices.Any(d =>
                d.GetMaxOutputReportLength() == 65 && d.GetMaxInputReportLength() == 65);

            // Check for digitizer input (10-byte input report) — means it's functional
            bool hasDigitizer = devices.Any(d => d.GetMaxInputReportLength() == 10);

            if (hasControlChannel)
                return new VMultiStatus(true, true, $"Installed ({devices.Length} devices)");

            return new VMultiStatus(true, false, "Driver found but not functional");
        }
        catch (Exception ex)
        {
            return new VMultiStatus(false, false, $"Detection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Debug: list all HID devices to help diagnose vmulti detection issues.
    /// </summary>
    public List<HidDeviceInfo> ListAllHidDevices()
    {
        var result = new List<HidDeviceInfo>();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            try
            {
                string name;
                try { name = d.GetFriendlyName() ?? d.DevicePath; }
                catch { name = d.DevicePath; }

                result.Add(new HidDeviceInfo(
                    d.VendorID,
                    d.ProductID,
                    name,
                    d.GetMaxInputReportLength(),
                    d.GetMaxOutputReportLength()
                ));
            }
            catch { /* skip devices that throw */ }
        }
        return result;
    }
}

public record VMultiStatus(bool Installed, bool Functional, string Message);
public record HidDeviceInfo(int VendorId, int ProductId, string Name, int MaxInputReport, int MaxOutputReport);
