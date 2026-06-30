using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Enumerates connected monitors in virtual-desktop pixels (geometry via the GDI monitor APIs) and,
/// best-effort, their friendly names (via the DisplayConfig API — matching what Windows Display
/// Settings shows). Friendly-name lookup is fully fault-tolerant: any failure just yields blank
/// names, never throws.
/// </summary>
public static class DisplayEnumerator
{
    public static IReadOnlyList<DisplayInfo> Enumerate()
    {
        var names = TryGetFriendlyNames();
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
                names.TryGetValue(info.szDevice, out var name);

                monitors.Add(new DisplayInfo(
                    Number: number,
                    Name: name ?? "",
                    Width: devMode.dmPelsWidth,
                    Height: devMode.dmPelsHeight,
                    X: devMode.dmPositionX,
                    Y: devMode.dmPositionY,
                    IsPrimary: isPrimary,
                    RefreshHz: devMode.dmDisplayFrequency));
            }
            return true;
        };
        EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);

        return monitors.OrderBy(m => m.Number).ToList();
    }

    // "\\.\DISPLAY1" → 1 (the Windows display number); fall back to the enumeration order.
    private static int ParseDisplayNumber(string device, int fallback)
    {
        var digits = new string(device.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) && n > 0 ? n : fallback;
    }

    /// <summary>Map GDI device name (\\.\DISPLAYx) → friendly monitor name, via DisplayConfig.</summary>
    private static Dictionary<string, string> TryGetFriendlyNames()
    {
        var map = new Dictionary<string, string>();
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
                var friendly = target.monitorFriendlyDeviceName;
                if (!string.IsNullOrWhiteSpace(gdi) && !string.IsNullOrWhiteSpace(friendly))
                    map[gdi] = friendly;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

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
