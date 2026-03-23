using HidSharp;

namespace TabletDriverUX.Services;

public class VMultiDetector
{
    private const int VMultiVendorId = 0x00FF;
    private const int VMultiProductId = 0xBACC;

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

            bool hasControlChannel = devices.Any(d =>
                d.GetMaxOutputReportLength() == 65 && d.GetMaxInputReportLength() == 65);

            if (hasControlChannel)
                return new VMultiStatus(true, true, $"Installed ({devices.Length} devices)");

            return new VMultiStatus(true, false, "Driver found but not functional");
        }
        catch (Exception ex)
        {
            return new VMultiStatus(false, false, $"Detection error: {ex.Message}");
        }
    }
}

public record VMultiStatus(bool Installed, bool Functional, string Message);
