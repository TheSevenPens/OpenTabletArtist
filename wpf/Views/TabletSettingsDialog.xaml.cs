using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace TabletDriverUX.Views;

public partial class TabletSettingsDialog : Window
{
    public TabletSettingsDialog(Profile profile, Settings? settings, Func<Settings, Task>? onApplyChanges = null)
    {
        InitializeComponent();
        DataContext = new TabletSettingsDialogViewModel(profile, settings, onApplyChanges);
    }
}

public record DisplayInfo(int Index, string Label, int Width, int Height, int X, int Y, bool IsPrimary);

public partial class TabletSettingsDialogViewModel : ObservableObject
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string AdaptiveBindingPath = "OpenTabletDriver.Desktop.Binding.AdaptiveBinding";

    private readonly Profile _profile;
    private readonly Settings? _settings;
    private readonly Func<Settings, Task>? _applyAction;

    [ObservableProperty] private DisplayInfo? _selectedDisplay;

    public TabletSettingsDialogViewModel(Profile profile, Settings? settings, Func<Settings, Task>? applyAction = null)
    {
        _profile = profile;
        _settings = settings;
        _applyAction = applyAction;

        TabletName = profile.Tablet ?? "Unknown Tablet";
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        Displays = EnumerateDisplays();
        SelectedDisplay = Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();

        RefreshFromProfile();
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
        var isNotWinInk = !OutputModePath.Equals(WinInkAbsoluteModePath, StringComparison.OrdinalIgnoreCase);
        CanFixOutputMode = isNotWinInk && _applyAction != null;

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
        CanFixPenButtons = !(bindings.PenButtons.Count == 0 || allAdaptive) && _applyAction != null;

        var newAuxButtons = new List<ButtonBinding>();
        for (int i = 0; i < bindings.AuxButtons.Count; i++)
            newAuxButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(bindings.AuxButtons[i]) });
        AuxButtons = newAuxButtons;

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

        await ApplySettingsChange(p =>
        {
            p.OutputMode ??= new PluginSettingStore(WinInkAbsoluteModePath, true);
            p.OutputMode.Path = WinInkAbsoluteModePath;
        });
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

            // Set display area
            abs.Display.Width = display.Width;
            abs.Display.Height = display.Height;
            abs.Display.X = display.X + display.Width / 2f;
            abs.Display.Y = display.Y + display.Height / 2f;

            // Enforce aspect ratio lock
            double displayAspect = (double)display.Width / display.Height;
            abs.Tablet.Height = (float)(abs.Tablet.Width / displayAspect);
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
    [ObservableProperty] private string _outputModeShort = "";
    [ObservableProperty] private string _outputModePath = "";
    [ObservableProperty] private bool _canFixOutputMode;
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
