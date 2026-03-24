using System.Runtime.InteropServices;
using HidSharp;

namespace TabletDriverUX.Services;

public class VMultiDetector
{
    private const int VMultiVendorId = 0x00FF;
    private const int VMultiProductId = 0xBACC;
    private const string VMultiHardwareId = @"djpnewton\vmulti";

    /// <summary>
    /// Detect vmulti via HID enumeration (only sees enabled, running devices).
    /// </summary>
    public HidDetectionResult DetectHid()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(
                vendorID: VMultiVendorId,
                productID: VMultiProductId
            ).ToArray();

            if (devices.Length == 0)
                return new HidDetectionResult(false, false, "Not visible to HID");

            bool hasControlChannel = devices.Any(d =>
                d.GetMaxOutputReportLength() == 65 && d.GetMaxInputReportLength() == 65);

            if (hasControlChannel)
                return new HidDetectionResult(true, true, $"Active ({devices.Length} devices)");

            return new HidDetectionResult(true, false, "Visible but no control channel");
        }
        catch (Exception ex)
        {
            return new HidDetectionResult(false, false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detect vmulti via Windows Setup API (sees all devices including disabled ones).
    /// </summary>
    public SetupApiDetectionResult DetectSetupApi()
    {
        try
        {
            var devices = FindDevicesByHardwareId(VMultiHardwareId);

            if (devices.Count == 0)
                return new SetupApiDetectionResult(false, false, "Not installed");

            bool anyEnabled = devices.Any(d => d.Enabled);
            bool anyDisabled = devices.Any(d => !d.Enabled);

            if (anyEnabled && !anyDisabled)
                return new SetupApiDetectionResult(true, true, $"Installed & enabled ({devices.Count} devices)");
            if (anyEnabled && anyDisabled)
                return new SetupApiDetectionResult(true, true, $"Installed ({devices.Count} devices, some disabled)");
            // All disabled
            return new SetupApiDetectionResult(true, false, $"Installed but disabled ({devices.Count} devices)");
        }
        catch (Exception ex)
        {
            return new SetupApiDetectionResult(false, false, $"Error: {ex.Message}");
        }
    }

    private static List<DeviceInfo> FindDevicesByHardwareId(string targetHardwareId)
    {
        var results = new List<DeviceInfo>();
        var guid = Guid.Empty;

        var devInfoSet = SetupDiGetClassDevs(
            ref guid, null, nint.Zero,
            DIGCF_ALLCLASSES | DIGCF_PRESENT);

        if (devInfoSet == INVALID_HANDLE)
            return results;

        try
        {
            var devInfoData = new SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

            for (uint i = 0; SetupDiEnumDeviceInfo(devInfoSet, i, ref devInfoData); i++)
            {
                string? hardwareIds = GetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                if (hardwareIds == null) continue;

                // Hardware IDs are multi-sz (null-separated), check each
                foreach (var id in hardwareIds.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (id.Equals(targetHardwareId, StringComparison.OrdinalIgnoreCase))
                    {
                        string? description = GetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_DEVICEDESC);

                        // Check if device is enabled
                        bool enabled = IsDeviceEnabled(devInfoSet, ref devInfoData);

                        results.Add(new DeviceInfo(
                            id,
                            description ?? "Unknown",
                            enabled
                        ));
                        break;
                    }
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return results;
    }

    // Also search for disabled devices by using DIGCF without DIGCF_PRESENT
    public static List<DeviceInfo> FindAllDevicesByHardwareId(string targetHardwareId)
    {
        var results = new List<DeviceInfo>();
        var guid = Guid.Empty;

        // First pass: present devices
        results.AddRange(FindDevicesWithFlags(targetHardwareId, DIGCF_ALLCLASSES | DIGCF_PRESENT));

        // Second pass: all devices (includes non-present/disabled)
        var allDevices = FindDevicesWithFlags(targetHardwareId, DIGCF_ALLCLASSES);
        foreach (var dev in allDevices)
        {
            if (!results.Any(r => r.HardwareId.Equals(dev.HardwareId, StringComparison.OrdinalIgnoreCase)
                                  && r.Description == dev.Description))
            {
                results.Add(dev);
            }
        }

        return results;
    }

    private static List<DeviceInfo> FindDevicesWithFlags(string targetHardwareId, uint flags)
    {
        var results = new List<DeviceInfo>();
        var guid = Guid.Empty;

        var devInfoSet = SetupDiGetClassDevs(ref guid, null, nint.Zero, flags);
        if (devInfoSet == INVALID_HANDLE) return results;

        try
        {
            var devInfoData = new SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

            for (uint i = 0; SetupDiEnumDeviceInfo(devInfoSet, i, ref devInfoData); i++)
            {
                string? hardwareIds = GetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                if (hardwareIds == null) continue;

                foreach (var id in hardwareIds.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (id.Equals(targetHardwareId, StringComparison.OrdinalIgnoreCase))
                    {
                        string? description = GetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_DEVICEDESC);
                        bool enabled = IsDeviceEnabled(devInfoSet, ref devInfoData);
                        results.Add(new DeviceInfo(id, description ?? "Unknown", enabled));
                        break;
                    }
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return results;
    }

    private static bool IsDeviceEnabled(nint devInfoSet, ref SP_DEVINFO_DATA devInfoData)
    {
        uint status = 0, problem = 0;
        if (CM_Get_DevNode_Status(ref status, ref problem, devInfoData.devInst, 0) != 0)
            return false; // Can't get status — treat as disabled

        // DN_DISABLEABLE check isn't needed; if CM_Get_DevNode_Status succeeds
        // and there's no DN_HAS_PROBLEM with CM_PROB_DISABLED, it's enabled
        const uint CM_PROB_DISABLED = 0x16;
        const uint DN_HAS_PROBLEM = 0x00000400;

        if ((status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_DISABLED)
            return false;

        return true;
    }

    private static string? GetDeviceRegistryProperty(nint devInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
    {
        SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property,
            out _, null, 0, out uint requiredSize);

        if (requiredSize == 0) return null;

        byte[] buffer = new byte[requiredSize];
        if (!SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property,
            out _, buffer, requiredSize, out _))
            return null;

        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    #region P/Invoke

    private static readonly nint INVALID_HANDLE = new(-1);
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_ALLCLASSES = 0x04;
    private const uint SPDRP_HARDWAREID = 0x01;
    private const uint SPDRP_DEVICEDESC = 0x00;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid classGuid;
        public uint devInst;
        public nint reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        nint deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        nint deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, out uint propertyRegDataType,
        byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(
        ref uint status, ref uint problemNumber, uint devInst, uint flags);

    #endregion
}

public record HidDetectionResult(bool Visible, bool Functional, string Message);
public record SetupApiDetectionResult(bool Installed, bool Enabled, string Message);
public record DeviceInfo(string HardwareId, string Description, bool Enabled);
