using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Windows implementation of <see cref="IDisplayEnumerator"/>. Enumerates connected monitors in
/// virtual-desktop pixels (geometry via the GDI monitor APIs) and, best-effort, their friendly names
/// (via the DisplayConfig API — matching what Windows Display Settings shows), connector/port, and the
/// driving GPU. Friendly-name / port / GPU lookup is fully fault-tolerant: any failure just yields blank
/// values, never throws.
/// </summary>
public sealed class WindowsDisplayEnumerator : IDisplayEnumerator
{
    public IReadOnlyList<DisplayInfo> Enumerate()
    {
        var targets = TryGetTargetInfo();   // \\.\DISPLAYx → (friendly name, connector/port)
        var gpus = TryGetGpuNames();        // \\.\DISPLAYx → adapter (GPU) name
        var monitors = new List<DisplayInfo>();

        MonitorEnumProc callback = (nint hMonitor, nint hdc, ref RECT rect, nint data) =>
        {
            var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                var devMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                EnumDisplaySettings(info.szDevice, -1, ref devMode);

                int number = ParseDisplayNumber(info.szDevice, monitors.Count + 1);
                bool isPrimary = (info.dwFlags & 1) != 0;
                targets.TryGetValue(info.szDevice, out var t);
                gpus.TryGetValue(info.szDevice, out var gpu);

                monitors.Add(new DisplayInfo(
                    Number: number,
                    Name: t.Name ?? "",
                    Width: devMode.dmPelsWidth,
                    Height: devMode.dmPelsHeight,
                    X: devMode.dmPositionX,
                    Y: devMode.dmPositionY,
                    IsPrimary: isPrimary,
                    RefreshHz: devMode.dmDisplayFrequency,
                    Port: t.Port ?? "",
                    Gpu: gpu ?? ""));
            }
            return true;
        };
        EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);

        return monitors.OrderBy(m => m.Number).ToList();
    }

    /// <summary>GDI device name (\\.\DISPLAYx) → the adapter (GPU) that drives it, via EnumDisplayDevices.
    /// The adapter's DeviceString is the card name Windows shows in Device Manager. Best-effort.</summary>
    private static Dictionary<string, string> TryGetGpuNames()
    {
        var map = new Dictionary<string, string>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if (!string.IsNullOrWhiteSpace(dd.DeviceName) && !string.IsNullOrWhiteSpace(dd.DeviceString))
                    map[dd.DeviceName] = dd.DeviceString.Trim();
            }
        }
        catch { /* best-effort — blank GPU is fine */ }
        return map;
    }

    /// <summary>Map a DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY value to a short connector label.</summary>
    private static string PortName(uint tech) => tech switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 => "Internal",            // LVDS (built-in panel)
        9 => "SDI",
        10 => "DisplayPort",        // external
        11 => "Internal (eDP)",     // embedded DisplayPort
        12 => "USB-C",              // UDI external
        13 => "Internal",           // UDI embedded
        15 => "Wireless",           // Miracast
        0x80000000 => "Internal",
        _ => "",
    };

    // "\\.\DISPLAY1" → 1 (the Windows display number); fall back to the enumeration order.
    private static int ParseDisplayNumber(string device, int fallback)
    {
        var digits = new string(device.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) && n > 0 ? n : fallback;
    }

    /// <summary>Map GDI device name (\\.\DISPLAYx) → (friendly monitor name, connector/port), via
    /// DisplayConfig. Both are best-effort; either may be empty.</summary>
    private static Dictionary<string, (string Name, string Port)> TryGetTargetInfo()
    {
        var map = new Dictionary<string, (string Name, string Port)>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                return map;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, nint.Zero) != 0)
                return map;

            for (int i = 0; i < pathCount; i++)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id,
                    }
                };
                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id,
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                var gdi = source.viewGdiDeviceName;
                if (string.IsNullOrWhiteSpace(gdi)) continue;
                var friendly = target.monitorFriendlyDeviceName;
                map[gdi] = (string.IsNullOrWhiteSpace(friendly) ? "" : friendly, PortName(target.outputTechnology));
            }
        }
        catch { /* best-effort — blank names are fine */ }
        return map;
    }

    #region P/Invoke — monitor geometry

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    #endregion

    #region P/Invoke — DisplayConfig friendly names

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPaths,
        [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes,
        [Out] DISPLAYCONFIG_MODE_INFO[] modes, nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // We never read mode contents — only the paths drive the name lookup. Keep the element size
    // correct (union is 48 bytes) so QueryDisplayConfig fills the array properly.
    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION { }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    #endregion
}
