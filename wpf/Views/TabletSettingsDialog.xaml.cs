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

        // Output mode
        OutputModePath = profile.OutputMode?.Path ?? "Not set";
        OutputModeShort = OutputModePath.Split('.').LastOrDefault() ?? OutputModePath;

        IsNotWinInk = !OutputModePath.Equals(WinInkAbsoluteModePath, StringComparison.OrdinalIgnoreCase);
        CanFixOutputMode = IsNotWinInk && applyAction != null;

        // Area mapping
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        // Enumerate displays and select primary
        Displays = EnumerateDisplays();
        SelectedDisplay = Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();

        // Bindings
        var bindings = profile.BindingSettings;
        HasBindings = true;

        TipBinding = GetBindingName(bindings.TipButton);
        TipPressure = bindings.TipActivationThreshold.ToString("F1");
        EraserBinding = GetBindingName(bindings.EraserButton);
        EraserPressure = bindings.EraserActivationThreshold.ToString("F1");
        PenButtonCount = bindings.PenButtons.Count.ToString();
        AuxButtonCount = bindings.AuxButtons.Count.ToString();

        // Check if bindings use recommended AdaptiveBinding
        TipIsAdaptive = IsAdaptive(bindings.TipButton);
        EraserIsAdaptive = IsAdaptive(bindings.EraserButton);
        CanFixTip = !TipIsAdaptive && applyAction != null;
        CanFixEraser = !EraserIsAdaptive && applyAction != null;

        // Pen buttons
        bool allPenButtonsAdaptive = true;
        for (int i = 0; i < bindings.PenButtons.Count; i++)
        {
            var btn = bindings.PenButtons[i];
            PenButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(btn) });
            if (!IsAdaptive(btn)) allPenButtonsAdaptive = false;
        }
        PenButtonsAllAdaptive = bindings.PenButtons.Count == 0 || allPenButtonsAdaptive;
        CanFixPenButtons = !PenButtonsAllAdaptive && applyAction != null;

        for (int i = 0; i < bindings.AuxButtons.Count; i++)
        {
            var btn = bindings.AuxButtons[i];
            AuxButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(btn) });
        }

        // Filters
        if (profile.Filters.Count > 0)
        {
            var filterNames = profile.Filters.Select(f =>
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

        // Raw JSON via Newtonsoft serialization
        RawJson = JsonConvert.SerializeObject(profile, Formatting.Indented);
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
    public string OutputModeShort { get; }
    public string OutputModePath { get; }
    public bool IsNotWinInk { get; }
    public bool CanFixOutputMode { get; }
    public bool HasAreaMapping { get; }
    public ObservableCollection<DisplayInfo> Displays { get; }
    public bool HasBindings { get; }
    public string TipBinding { get; } = "None";
    public string TipPressure { get; } = "";
    public bool TipIsAdaptive { get; }
    public bool CanFixTip { get; }
    public string EraserBinding { get; } = "None";
    public string EraserPressure { get; } = "";
    public bool EraserIsAdaptive { get; }
    public bool CanFixEraser { get; }
    public string PenButtonCount { get; } = "0";
    public string AuxButtonCount { get; } = "0";
    public bool PenButtonsAllAdaptive { get; } = true;
    public bool CanFixPenButtons { get; }
    public List<ButtonBinding> PenButtons { get; } = [];
    public List<ButtonBinding> AuxButtons { get; } = [];
    public string FiltersText { get; }
    public string RawJson { get; }
}

public class ButtonBinding
{
    public int Index { get; set; }
    public string Name { get; set; } = "None";
    public string Label => $"Button {Index}";
}
