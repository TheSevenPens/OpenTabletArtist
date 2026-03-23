using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace TabletDriverUX.Views;

public partial class TabletSettingsDialog : Window
{
    public TabletSettingsDialog(JToken profile, Func<JToken, Task>? onApplyChanges = null)
    {
        InitializeComponent();
        DataContext = new TabletSettingsDialogViewModel(profile, async updatedProfile =>
        {
            if (onApplyChanges != null)
            {
                await onApplyChanges(updatedProfile);
            }
        });
    }
}

public record DisplayInfo(int Index, string Label, int Width, int Height, int X, int Y, bool IsPrimary);

public partial class TabletSettingsDialogViewModel : ObservableObject
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";

    private readonly JToken _profile;
    private readonly Func<JToken, Task>? _applyAction;

    [ObservableProperty] private DisplayInfo? _selectedDisplay;

    public TabletSettingsDialogViewModel(JToken profile, Func<JToken, Task>? applyAction = null)
    {
        _profile = profile;
        _applyAction = applyAction;

        TabletName = profile["Tablet"]?.ToString() ?? "Unknown Tablet";

        // Output mode
        var outputMode = profile["OutputMode"];
        OutputModePath = outputMode?["Path"]?.ToString() ?? "Not set";
        OutputModeShort = OutputModePath.Split('.').LastOrDefault() ?? OutputModePath;

        IsNotWinInk = !OutputModePath.Equals(WinInkAbsoluteModePath, StringComparison.OrdinalIgnoreCase);
        CanFixOutputMode = IsNotWinInk && applyAction != null;

        // Area mapping
        var abs = profile["AbsoluteModeSettings"];
        HasAreaMapping = abs != null;

        // Enumerate displays and select primary
        Displays = EnumerateDisplays();
        SelectedDisplay = Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();

        // Bindings
        var bindings = profile["BindingSettings"];
        HasBindings = bindings != null;
        if (bindings != null)
        {
            TipBinding = GetBindingName(bindings["TipButton"]);
            EraserBinding = GetBindingName(bindings["EraserButton"]);
            PenButtonCount = (bindings["PenButtons"] as JArray)?.Count.ToString() ?? "0";
            AuxButtonCount = (bindings["AuxButtons"] as JArray)?.Count.ToString() ?? "0";
        }

        // Filters
        var filters = profile["Filters"] as JArray;
        if (filters != null && filters.Count > 0)
        {
            var filterNames = filters.Select(f =>
            {
                var path = f["Path"]?.ToString() ?? "Unknown";
                var name = path.Split('.').LastOrDefault() ?? path;
                var enabled = f["Enable"]?.Value<bool>() ?? true;
                return enabled ? name : $"{name} (disabled)";
            });
            FiltersText = string.Join("\n", filterNames);
        }
        else
        {
            FiltersText = "No filters configured";
        }

        RawJson = _profile.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    [RelayCommand]
    private async Task FixOutputMode()
    {
        if (_applyAction == null) return;

        var result = MessageBox.Show(
            $"This will change the output mode from:\n\n" +
            $"  {OutputModeShort}\n\nto:\n\n" +
            $"  WinInkAbsoluteMode\n\n" +
            "This enables pressure and tilt support in drawing apps.\n\n" +
            "Proceed?",
            "Change Output Mode",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var updated = _profile.DeepClone();
        updated["OutputMode"]!["Path"] = WinInkAbsoluteModePath;
        await _applyAction(updated);
    }

    [RelayCommand]
    private async Task SetToDisplay()
    {
        if (_applyAction == null || SelectedDisplay == null) return;

        var display = SelectedDisplay;
        var updated = _profile.DeepClone();
        var abs = updated["AbsoluteModeSettings"];
        if (abs == null) return;

        // Set the display area to match the selected monitor
        var displayArea = abs["Display"];
        if (displayArea != null)
        {
            displayArea["Width"] = display.Width;
            displayArea["Height"] = display.Height;
            displayArea["X"] = display.X + display.Width / 2.0;
            displayArea["Y"] = display.Y + display.Height / 2.0;
        }

        // Always enforce aspect ratio lock: adjust tablet area to match display aspect ratio
        var tabletArea = abs["Tablet"];
        if (tabletArea != null)
        {
            double tabletWidth = tabletArea["Width"]?.Value<double>() ?? 0;
            double displayAspect = (double)display.Width / display.Height;

            // Scale tablet height to match display aspect ratio, keeping tablet width
            double scaledHeight = tabletWidth / displayAspect;
            tabletArea["Height"] = scaledHeight;
        }

        // Set the LockAspectRatio flag
        abs["LockAspectRatio"] = true;

        await _applyAction(updated);
    }

    private static string GetBindingName(JToken? binding)
    {
        if (binding == null) return "None";
        var path = binding["Path"]?.ToString();
        if (string.IsNullOrEmpty(path)) return "None";
        return path.Split('.').LastOrDefault() ?? path;
    }

    private static ObservableCollection<DisplayInfo> EnumerateDisplays()
    {
        var displays = new ObservableCollection<DisplayInfo>();
        int index = 0;

        MonitorEnumProc callback = (nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = (uint)Marshal.SizeOf(info);

            if (GetMonitorInfo(hMonitor, ref info))
            {
                var devMode = new DEVMODE();
                devMode.dmSize = (short)Marshal.SizeOf(devMode);
                EnumDisplaySettings(info.szDevice, -1, ref devMode);

                int w = devMode.dmPelsWidth;
                int h = devMode.dmPelsHeight;
                int x = devMode.dmPositionX;
                int y = devMode.dmPositionY;
                bool isPrimary = (info.dwFlags & 1) != 0;
                index++;

                string label = isPrimary
                    ? $"Display {index} (Primary) — {w}x{h}"
                    : $"Display {index} — {w}x{h}";

                displays.Add(new DisplayInfo(index, label, w, h, x, y, isPrimary));
            }
            return true;
        };
        EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);

        return displays;
    }

    #region Win32 P/Invoke

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

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

    public string TabletName { get; }
    public string OutputModeShort { get; }
    public string OutputModePath { get; }
    public bool IsNotWinInk { get; }
    public bool CanFixOutputMode { get; }
    public bool HasAreaMapping { get; }
    public ObservableCollection<DisplayInfo> Displays { get; }
    public bool HasBindings { get; }
    public string TipBinding { get; } = "None";
    public string EraserBinding { get; } = "None";
    public string PenButtonCount { get; } = "0";
    public string AuxButtonCount { get; } = "0";
    public string FiltersText { get; }
    public string RawJson { get; }
}
