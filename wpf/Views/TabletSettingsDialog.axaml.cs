using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OtdWindowsHelper.Views;

public partial class TabletSettingsDialog : Window
{
    public TabletSettingsDialog(Profile profile, Settings? settings,
        Func<Settings, Task>? onApplyChanges = null,
        Func<Task<Profile?>>? onRefresh = null,
        (float Width, float Height)? tabletDigitizer = null)
    {
        InitializeComponent();
        DataContext = new TabletSettingsDialogViewModel(profile, settings, onApplyChanges, onRefresh, tabletDigitizer);
    }

    // Parameterless constructor required by Avalonia XAML loader
    public TabletSettingsDialog() { InitializeComponent(); }
}

public record DisplayInfo(int Index, string Label, int Width, int Height, int X, int Y, bool IsPrimary);

public partial class TabletSettingsDialogViewModel : ObservableObject
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string WinInkRelativeModePath = "VoiDPlugins.OutputMode.WinInkRelativeMode";
    private const string AdaptiveBindingPath = "OpenTabletDriver.Desktop.Binding.AdaptiveBinding";

    private Profile _profile;
    private Settings? _settings;
    private readonly Func<Settings, Task>? _applyAction;
    private readonly Func<Task<Profile?>>? _refreshAction;
    private readonly (float Width, float Height)? _tabletDigitizer;

    [ObservableProperty] private DisplayInfo? _selectedDisplay;
    private bool _skipDisplayChange;

    partial void OnSelectedDisplayChanged(DisplayInfo? value)
    {
        if (!_skipDisplayChange && value != null)
            _ = SetToDisplay();
    }

    partial void OnIsWinInkAbsoluteChanged(bool value)
    {
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkAbsoluteModePath);
    }

    partial void OnIsWinInkRelativeChanged(bool value)
    {
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkRelativeModePath);
    }

    private async Task SetOutputMode(string path)
    {
        await ApplySettingsChange(p =>
        {
            p.OutputMode ??= new PluginSettingStore(path, true);
            p.OutputMode.Path = path;
        });
    }

    public TabletSettingsDialogViewModel(Profile profile, Settings? settings,
        Func<Settings, Task>? applyAction = null,
        Func<Task<Profile?>>? refreshAction = null,
        (float Width, float Height)? tabletDigitizer = null)
    {
        _profile = profile;
        _settings = settings;
        _applyAction = applyAction;
        _refreshAction = refreshAction;
        _tabletDigitizer = tabletDigitizer;

        TabletName = profile.Tablet ?? "Unknown Tablet";
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        Displays = EnumerateDisplays();
        RefreshFromProfile();
        // Set initial selection AFTER RefreshFromProfile so the change handler can fire
        _skipDisplayChange = true;
        SelectedDisplay = Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();
        _skipDisplayChange = false;
    }

    // Parameterless constructor for design-time
    public TabletSettingsDialogViewModel()
    {
        _profile = new Profile();
        TabletName = "Design Tablet";
        Displays = new ObservableCollection<DisplayInfo>();
    }

    private static string GetBindingName(PluginSettingStore? store)
    {
        if (store?.Path == null) return "None";
        if (store.Path == AdaptiveBindingPath)
        {
            var bindingSetting = store.Settings.FirstOrDefault(s => s.Property == "Binding");
            var value = bindingSetting?.Value?.ToString();
            return string.IsNullOrEmpty(value) ? "Adaptive Binding" : $"Adaptive Binding ({value})";
        }
        return store.Path.Split('.').LastOrDefault() ?? store.Path;
    }

    private static bool IsAdaptive(PluginSettingStore? store)
        => store?.Path == AdaptiveBindingPath;

    private static PluginSettingStore MakeAdaptiveBinding(string value)
    {
        var store = new PluginSettingStore(typeof(AdaptiveBinding), true);
        // Set the "Binding" property to the desired value
        var bindingSetting = store.Settings.FirstOrDefault(s => s.Property == "Binding");
        if (bindingSetting != null)
            bindingSetting.SetValue(value);
        else
            store.Settings.Add(new PluginSetting("Binding", value));
        return store;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_refreshAction == null) return;
        var updated = await _refreshAction();
        if (updated != null)
        {
            _profile = updated;
            RefreshFromProfile();
        }
    }

    private async Task ApplySettingsChange(Action<Profile> modify)
    {
        if (_applyAction == null || _settings == null) return;
        modify(_profile);
        await _applyAction(_settings);
        RefreshFromProfile();
    }

    private void RefreshFromProfile()
    {
        // Output mode
        OutputModePath = _profile.OutputMode?.Path ?? "Not set";
        OutputModeShort = OutputModePath.Split('.').LastOrDefault() ?? OutputModePath;

        _skipOutputModeChange = true;
        IsWinInkAbsolute = OutputModePath.Equals(WinInkAbsoluteModePath, StringComparison.OrdinalIgnoreCase);
        IsWinInkRelative = OutputModePath.Equals(WinInkRelativeModePath, StringComparison.OrdinalIgnoreCase);
        _skipOutputModeChange = false;

        var isWinInk = IsWinInkAbsolute || IsWinInkRelative;
        CanFixOutputMode = !isWinInk && _applyAction != null;

        // Bindings
        var bindings = _profile.BindingSettings;
        TipBinding = GetBindingName(bindings.TipButton);
        TipPressure = bindings.TipActivationThreshold.ToString("F1");
        EraserBinding = GetBindingName(bindings.EraserButton);
        EraserPressure = bindings.EraserActivationThreshold.ToString("F1");
        CanFixTip = !IsAdaptive(bindings.TipButton) && _applyAction != null;
        CanFixEraser = !IsAdaptive(bindings.EraserButton) && _applyAction != null;

        // Pen buttons
        PenButtonCount = bindings.PenButtons.Count.ToString();
        AuxButtonCount = bindings.AuxButtons.Count.ToString();
        bool allAdaptive = true;
        var newPenButtons = new List<ButtonBinding>();
        for (int i = 0; i < bindings.PenButtons.Count; i++)
        {
            newPenButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(bindings.PenButtons[i]) });
            if (!IsAdaptive(bindings.PenButtons[i])) allAdaptive = false;
        }
        PenButtons = newPenButtons;
        NoPenButtons = newPenButtons.Count == 0;
        CanFixPenButtons = !(bindings.PenButtons.Count == 0 || allAdaptive) && _applyAction != null;

        var newAuxButtons = new List<ButtonBinding>();
        for (int i = 0; i < bindings.AuxButtons.Count; i++)
            newAuxButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(bindings.AuxButtons[i]) });
        AuxButtons = newAuxButtons;
        NoAuxButtons = newAuxButtons.Count == 0;

        // Filters
        if (_profile.Filters.Count > 0)
        {
            var filterNames = _profile.Filters.Select(f =>
            {
                var name = (f?.Path ?? "Unknown").Split('.').LastOrDefault() ?? "Unknown";
                var enabled = f?.Enable ?? true;
                return enabled ? name : $"{name} (disabled)";
            });
            FiltersText = string.Join("\n", filterNames);
        }
        else
        {
            FiltersText = "No filters configured";
        }

        // Raw JSON
        RawJson = JsonConvert.SerializeObject(_profile, Formatting.Indented);
    }

    [RelayCommand]
    private async Task FixOutputMode()
    {
        await SetOutputMode(WinInkAbsoluteModePath);
    }

    [RelayCommand]
    private async Task SetToDisplay()
    {
        if (_applyAction == null || _settings == null || SelectedDisplay == null) return;

        var display = SelectedDisplay;
        await ApplySettingsChange(p =>
        {
            var abs = p.AbsoluteModeSettings;
            if (abs == null) return;

            // Set display area to the full selected monitor
            abs.Display.Width = display.Width;
            abs.Display.Height = display.Height;
            abs.Display.X = display.X + display.Width / 2f;
            abs.Display.Y = display.Y + display.Height / 2f;

            // Start from the FULL tablet digitizer area
            float fullWidth = _tabletDigitizer?.Width ?? abs.Tablet.Width;
            float fullHeight = _tabletDigitizer?.Height ?? abs.Tablet.Height;

            // Calculate the largest sub-area that matches the display's aspect ratio
            double displayAspect = (double)display.Width / display.Height;
            double tabletAspect = (double)fullWidth / fullHeight;

            float tabletWidth, tabletHeight;
            if (displayAspect > tabletAspect)
            {
                // Display is wider than tablet — use full tablet width, reduce height
                tabletWidth = fullWidth;
                tabletHeight = (float)(fullWidth / displayAspect);
            }
            else
            {
                // Display is taller than tablet — use full tablet height, reduce width
                tabletHeight = fullHeight;
                tabletWidth = (float)(fullHeight * displayAspect);
            }

            abs.Tablet.Width = tabletWidth;
            abs.Tablet.Height = tabletHeight;
            // Center the tablet area on the digitizer
            abs.Tablet.X = fullWidth / 2f;
            abs.Tablet.Y = fullHeight / 2f;
            abs.LockAspectRatio = true;
        });
    }

    [RelayCommand]
    private async Task FixTipBinding()
    {
        await ApplySettingsChange(p => p.BindingSettings.TipButton = MakeAdaptiveBinding("Tip"));
    }

    [RelayCommand]
    private async Task FixEraserBinding()
    {
        await ApplySettingsChange(p => p.BindingSettings.EraserButton = MakeAdaptiveBinding("Eraser"));
    }

    [RelayCommand]
    private async Task FixPenButtons()
    {
        await ApplySettingsChange(p =>
        {
            for (int i = 0; i < p.BindingSettings.PenButtons.Count; i++)
                p.BindingSettings.PenButtons[i] = MakeAdaptiveBinding($"Button {i + 1}");
        });
    }

    private static ObservableCollection<DisplayInfo> EnumerateDisplays()
    {
        var monitors = new List<DisplayInfo>();
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

                monitors.Add(new DisplayInfo(index, label, w, h, x, y, isPrimary));
            }
            return true;
        };
        EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);

        var displays = new ObservableCollection<DisplayInfo>();

        // Add "All displays" — bounding box spanning all monitors
        if (monitors.Count > 1)
        {
            int minX = monitors.Min(m => m.X);
            int minY = monitors.Min(m => m.Y);
            int maxRight = monitors.Max(m => m.X + m.Width);
            int maxBottom = monitors.Max(m => m.Y + m.Height);
            int totalW = maxRight - minX;
            int totalH = maxBottom - minY;

            displays.Add(new DisplayInfo(0, $"All displays — {totalW}x{totalH}", totalW, totalH, minX, minY, false));
        }

        foreach (var m in monitors)
            displays.Add(m);

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
    [ObservableProperty] private string _outputModeShort = "";
    [ObservableProperty] private string _outputModePath = "";
    [ObservableProperty] private bool _canFixOutputMode;
    [ObservableProperty] private bool _isWinInkAbsolute;
    [ObservableProperty] private bool _isWinInkRelative;
    private bool _skipOutputModeChange;
    public bool HasAreaMapping { get; }
    public ObservableCollection<DisplayInfo> Displays { get; }
    [ObservableProperty] private string _tipBinding = "None";
    [ObservableProperty] private string _tipPressure = "";
    [ObservableProperty] private bool _canFixTip;
    [ObservableProperty] private string _eraserBinding = "None";
    [ObservableProperty] private string _eraserPressure = "";
    [ObservableProperty] private bool _canFixEraser;
    [ObservableProperty] private string _penButtonCount = "0";
    [ObservableProperty] private string _auxButtonCount = "0";
    [ObservableProperty] private bool _canFixPenButtons;
    [ObservableProperty] private bool _noPenButtons;
    [ObservableProperty] private bool _noAuxButtons;
    [ObservableProperty] private List<ButtonBinding> _penButtons = [];
    [ObservableProperty] private List<ButtonBinding> _auxButtons = [];
    [ObservableProperty] private string _filtersText = "";
    [ObservableProperty] private string _rawJson = "";
}

public class ButtonBinding
{
    public int Index { get; set; }
    public string Name { get; set; } = "None";
    public string Label => $"Button {Index}";
}
