using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Win32;

namespace OtdHealth.Collector;

public class VMultiDetector
{
    /// <summary>The health probe: the install verdict plus what it was based on, from one enumeration so
    /// the two cannot disagree. Optional property reads never fail the probe; a missing value stays null.</summary>
    public static VMultiObservation Observe()
    {
        if (!OperatingSystem.IsWindows()) throw new ProbeUnavailableException("VMulti requires Windows.", true);

        var present = ReadNodes(DIGCF_ALLCLASSES | DIGCF_PRESENT, present: true);
        var presentIds = present.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = ReadNodes(DIGCF_ALLCLASSES, present: false)
            .Count(n => MatchHardwareId(n.HardwareIds) != null && !presentIds.Contains(n.InstanceId));

        return VMultiInspector.Build(present, stale, ReadStagedPackage(), ObserveHid());
    }

    private const int VMultiVendorId = 0x00FF;
    private const int VMultiProductId = 0xBACC;

    /// <summary>The hardware IDs a VMulti device node enumerates under. The package OTA installs
    /// (<c>devcon install vmulti.inf "pentablet\hid"</c>; its INF lists only this model) creates a node
    /// with <c>pentablet\hid</c>, so that is what a working install carries. <c>djpnewton\vmulti</c> is
    /// the upstream package's ID and is where the driverless leftovers after an uninstall show up. The
    /// probe used to look only for the latter, so a healthy install read as "not installed".</summary>
    private static readonly string[] VMultiHardwareIds = [@"pentablet\hid", @"djpnewton\vmulti"];

    /// <summary>The VMulti hardware ID among a device's hardware IDs, or null if it carries none. Exact,
    /// case-insensitive match: the HID child nodes (<c>HID\hid&amp;Col01</c>, ...) are not VMulti nodes.</summary>
    public static string? MatchHardwareId(IEnumerable<string> hardwareIds) =>
        hardwareIds.Select(id => VMultiHardwareIds.FirstOrDefault(v => v.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(v => v != null);

    /// <summary>
    /// Detect vmulti via HID enumeration (only sees enabled, running devices).
    /// </summary>
    public HidDetectionResult DetectHid()
    {
        var h = ObserveHid();
        if (h.Error != null) return new HidDetectionResult(false, false, $"Error: {h.Error}");
        if (!h.Visible) return new HidDetectionResult(false, false, "Not visible to HID");
        return h.ControlChannel
            ? new HidDetectionResult(true, true, $"Active ({h.DeviceCount} devices)")
            : new HidDetectionResult(true, false, "Visible but no control channel");
    }

    /// <summary>The HID view of the virtual pen. A failure is recorded in <c>Error</c>, never thrown: it is
    /// supporting evidence and must not take the install verdict down with it.</summary>
    public static VMultiHidObservation ObserveHid()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(
                vendorID: VMultiVendorId,
                productID: VMultiProductId
            ).ToArray();

            if (devices.Length == 0) return new VMultiHidObservation(false, false, 0, null);

            bool hasControlChannel = devices.Any(d =>
                d.GetMaxOutputReportLength() == 65 && d.GetMaxInputReportLength() == 65);
            return new VMultiHidObservation(true, hasControlChannel, devices.Length, null);
        }
        catch (Exception ex)
        {
            return new VMultiHidObservation(false, false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Detect vmulti via Windows Setup API (sees all devices including disabled ones).
    /// </summary>
    public SetupApiDetectionResult DetectSetupApi()
    {
        try
        {
            return ClassifySetupApi(FindVMultiDevices());
        }
        catch (Exception ex)
        {
            return new SetupApiDetectionResult(false, false, $"Error: {ex.Message}");
        }
    }

    /// <summary>Pure classification of present vmulti device nodes into an install state, by their
    /// CM problem code. A driverless leftover (e.g. Code 28 after an uninstall) is present but not a
    /// working install, so it reports as not installed rather than installed.</summary>
    public static SetupApiDetectionResult ClassifySetupApi(IReadOnlyList<DeviceInfo> devices)
    {
        if (devices.Count == 0)
            return new SetupApiDetectionResult(false, false, "Not installed");

        var functional = devices.Where(d => d.Problem == 0).ToList();
        var disabled = devices.Where(d => d.Problem == CM_PROB_DISABLED).ToList();
        var orphaned = devices.Where(d => d.Problem != 0 && d.Problem != CM_PROB_DISABLED).ToList();

        if (functional.Count > 0)
            return functional.Count == devices.Count
                ? new SetupApiDetectionResult(true, true, $"Installed & enabled ({functional.Count} devices)")
                : new SetupApiDetectionResult(true, true, $"Installed ({devices.Count} devices, some inactive)");

        if (disabled.Count > 0 && orphaned.Count == 0)
            return new SetupApiDetectionResult(true, false, $"Installed but disabled ({disabled.Count} devices)");

        // Only driverless/problem nodes remain — the driver is gone; these are leftover nodes.
        return new SetupApiDetectionResult(false, false,
            $"Not installed ({orphaned.Count} leftover device node{(orphaned.Count == 1 ? "" : "s")}, no driver)");
    }

    private static List<DeviceInfo> FindVMultiDevices()
    {
        var results = new List<DeviceInfo>();
        var guid = Guid.Empty;

        var devInfoSet = SetupDiGetClassDevs(
            ref guid, null, nint.Zero,
            DIGCF_ALLCLASSES | DIGCF_PRESENT);

        if (devInfoSet == INVALID_HANDLE)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var devInfoData = new SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

            for (uint i = 0; SetupDiEnumDeviceInfo(devInfoSet, i, ref devInfoData); i++)
            {
                string? hardwareIds = GetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                if (hardwareIds == null) continue;

                // Hardware IDs are multi-sz (null-separated); a node counts if any is a known VMulti ID.
                if (MatchHardwareId(hardwareIds.Split('\0', StringSplitOptions.RemoveEmptyEntries)) is { } id)
                {
                    string? description = GetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_DEVICEDESC);

                    var (enabled, problem) = GetDevNodeState(devInfoSet, ref devInfoData);

                    results.Add(new DeviceInfo(
                        id,
                        description ?? "Unknown",
                        enabled,
                        problem
                    ));
                }
            }
            if (Marshal.GetLastWin32Error() != 259) // ERROR_NO_MORE_ITEMS
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return results;
    }

    /// <summary>Every device that is a VMulti node or merely looks like one (by hardware ID, name or
    /// service), with the properties a reader needs to tell which. <paramref name="present"/> marks the rows
    /// as coming from the present-devices pass.</summary>
    private static List<VMultiNode> ReadNodes(uint flags, bool present)
    {
        var results = new List<VMultiNode>();
        var guid = Guid.Empty;

        var devInfoSet = SetupDiGetClassDevs(ref guid, null, nint.Zero, flags);
        if (devInfoSet == INVALID_HANDLE) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var devInfoData = new SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

            for (uint i = 0; SetupDiEnumDeviceInfo(devInfoSet, i, ref devInfoData); i++)
            {
                string? hardwareIds = TryGetProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                string[] ids = hardwareIds?.Split('\0', StringSplitOptions.RemoveEmptyEntries) ?? [];
                string? description = TryGetProperty(devInfoSet, ref devInfoData, SPDRP_DEVICEDESC);
                string? service = TryGetProperty(devInfoSet, ref devInfoData, SPDRP_SERVICE);
                if (MatchHardwareId(ids) == null && !VMultiInspector.Mentions(ids, description, service)) continue;

                var (enabled, problem) = GetDevNodeState(devInfoSet, ref devInfoData);
                var driver = ReadDriverKey(TryGetProperty(devInfoSet, ref devInfoData, SPDRP_DRIVER));
                results.Add(new VMultiNode(InstanceId(devInfoSet, ref devInfoData), ids, description, service,
                    present, enabled, problem, driver.Inf, driver.Version, driver.Provider));
            }
            if (Marshal.GetLastWin32Error() != 259) // ERROR_NO_MORE_ITEMS
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return results;
    }

    private static string? TryGetProperty(nint devInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
    {
        try { return GetDeviceRegistryProperty(devInfoSet, ref devInfoData, property); }
        catch (System.ComponentModel.Win32Exception) { return null; } // an optional read, never fatal
    }

    private static string InstanceId(nint devInfoSet, ref SP_DEVINFO_DATA devInfoData)
    {
        var sb = new System.Text.StringBuilder(512);
        return SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, sb, sb.Capacity, out _) ? sb.ToString() : "";
    }

    /// <summary>The installed driver's INF, version and provider, from the node's class key. The device's
    /// <c>Driver</c> value names that key (e.g. <c>{745a17a0-...}\0229</c>).</summary>
    private static (string? Inf, string? Version, string? Provider) ReadDriverKey(string? driverValue)
    {
        if (string.IsNullOrEmpty(driverValue) || !OperatingSystem.IsWindows()) return default;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driverValue);
            return (key?.GetValue("InfPath") as string, key?.GetValue("DriverVersion") as string,
                key?.GetValue("ProviderName") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return default; }
    }

    /// <summary>The VMulti package in the driver store, whether or not any device uses it. Read from the
    /// registry (no <c>pnputil</c>): a package key is named for its INF, its default value is the published
    /// name (<c>oemNN.inf</c>) and it records the original name and provider.</summary>
    private static VMultiDriverPackage? ReadStagedPackage()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\DriverDatabase\DriverPackages");
            if (root == null) return null;
            foreach (var name in root.GetSubKeyNames())
            {
                if (!name.StartsWith("vmulti.inf_", StringComparison.OrdinalIgnoreCase)) continue;
                using var key = root.OpenSubKey(name);
                return new VMultiDriverPackage(key?.GetValue("") as string ?? name,
                    key?.GetValue("InfName") as string ?? "vmulti.inf", key?.GetValue("Provider") as string ?? "");
            }
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return null; }
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
        if (devInfoSet == INVALID_HANDLE) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

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
                        var (enabled, problem) = GetDevNodeState(devInfoSet, ref devInfoData);
                        results.Add(new DeviceInfo(id, description ?? "Unknown", enabled, problem));
                        break;
                    }
                }
            }
            if (Marshal.GetLastWin32Error() != 259) // ERROR_NO_MORE_ITEMS
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return results;
    }

    // CM device problem codes we care about.
    private const uint CM_PROB_DISABLED = 0x16; // 22 — user-disabled (driver still present)
    private const uint DN_HAS_PROBLEM = 0x00000400;

    /// <summary>Returns whether the device node is enabled and its CM problem code (0 = no problem).
    /// A driverless leftover reports a problem (e.g. Code 28) while still being "present".</summary>
    private static (bool Enabled, uint Problem) GetDevNodeState(nint devInfoSet, ref SP_DEVINFO_DATA devInfoData)
    {
        uint status = 0, problem = 0;
        if (CM_Get_DevNode_Status(ref status, ref problem, devInfoData.devInst, 0) != 0)
            return (false, 0); // Can't get status — treat as disabled, unknown problem

        uint prob = (status & DN_HAS_PROBLEM) != 0 ? problem : 0;
        bool enabled = prob != CM_PROB_DISABLED;
        return (enabled, prob);
    }

    private static string? GetDeviceRegistryProperty(nint devInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
    {
        SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property,
            out _, null, 0, out uint requiredSize);

        if (requiredSize == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 13) return null; // ERROR_INVALID_DATA: this device has no such optional property.
            throw new System.ComponentModel.Win32Exception(error);
        }

        byte[] buffer = new byte[requiredSize];
        if (!SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property,
            out _, buffer, requiredSize, out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    #region P/Invoke

    private static readonly nint INVALID_HANDLE = new(-1);
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_ALLCLASSES = 0x04;
    private const uint SPDRP_HARDWAREID = 0x01;
    private const uint SPDRP_DEVICEDESC = 0x00;
    private const uint SPDRP_SERVICE = 0x04;
    private const uint SPDRP_DRIVER = 0x09;

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

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(
        nint deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        System.Text.StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(
        ref uint status, ref uint problemNumber, uint devInst, uint flags);

    #endregion
}

public record HidDetectionResult(bool Visible, bool Functional, string Message);
public record SetupApiDetectionResult(bool Installed, bool Enabled, string Message);
public record DeviceInfo(string HardwareId, string Description, bool Enabled, uint Problem = 0);
