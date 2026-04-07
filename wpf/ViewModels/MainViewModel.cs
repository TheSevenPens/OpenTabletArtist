using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using TabletDriverUX.Helpers;
using TabletDriverUX.Services;

namespace TabletDriverUX.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DaemonClient _daemon = new();
    private readonly VMultiDetector _vmulti = new();
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private string _currentPage = "Dashboard";
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private bool _isConnected;

    // Diagnostics
    [ObservableProperty] private bool _isDebugging;
    [ObservableProperty] private string _lastReportRaw = "";
    [ObservableProperty] private string _lastReportFormatted = "";
    [ObservableProperty] private int _reportCount;
    [ObservableProperty] private string _debugReportRate = "";
    [ObservableProperty] private string _debugTabletName = "";
    [ObservableProperty] private string _debugReportType = "";
    [ObservableProperty] private double _debugPenX;
    [ObservableProperty] private double _debugPenY;
    [ObservableProperty] private double _debugPenPressure;
    [ObservableProperty] private double _debugMaxX;
    [ObservableProperty] private double _debugMaxY;
    [ObservableProperty] private double _debugMaxPressure;
    [ObservableProperty] private double _debugDigitizerWidth;
    [ObservableProperty] private double _debugDigitizerHeight;
    [ObservableProperty] private bool _debugHasPosition;
    [ObservableProperty] private double _debugTiltX;
    [ObservableProperty] private double _debugTiltY;
    [ObservableProperty] private double _debugPressurePercent;
    [ObservableProperty] private double _debugTiltAzimuth;
    [ObservableProperty] private double _debugTiltAltitude;
    [ObservableProperty] private string _debugPenButtons = "";
    [ObservableProperty] private string _debugNearProximity = "";
    [ObservableProperty] private string _debugHoverDistance = "";
    private double _reportPeriodEma;
    private DateTime _lastReportTime;

    // Dashboard data
    [ObservableProperty] private string _tabletName = "";
    [ObservableProperty] private bool _hasTablet;
    [ObservableProperty] private bool _vmultiInstalled;
    [ObservableProperty] private string _vmultiMessage = "Checking...";
    [ObservableProperty] private string _vmultiHidStatus = "Checking...";
    [ObservableProperty] private string _vmultiSetupApiStatus = "Checking...";
    [ObservableProperty] private bool _vmultiInstalling;
    [ObservableProperty] private string _vmultiInstallStatus = "";
    [ObservableProperty] private bool _hasWindowsInk;

    // Computed properties for Avalonia bindings (replacing DataTriggers)
    [ObservableProperty] private string _daemonStatusText = "Not connected";
    [ObservableProperty] private string _tabletStatusText = "No tablet detected";
    [ObservableProperty] private string _windowsInkStatusText = "Not configured";
    [ObservableProperty] private bool _showInstallButton;
    [ObservableProperty] private bool _showUninstallButton;

    partial void OnIsConnectedChanged(bool value)
    {
        DaemonStatusText = value ? "Daemon running" : "Not connected";
        OnPropertyChanged(nameof(CanStartDaemon));
    }

    partial void OnHasTabletChanged(bool value)
    {
        TabletStatusText = value ? TabletName : "No tablet detected";
    }

    partial void OnTabletNameChanged(string value)
    {
        if (HasTablet) TabletStatusText = value;
    }

    partial void OnHasWindowsInkChanged(bool value)
    {
        WindowsInkStatusText = value ? "Plugin active" : "Not configured";
    }

    partial void OnVmultiInstalledChanged(bool value)
    {
        ShowInstallButton = !value && !VmultiInstalling;
        ShowUninstallButton = value && !VmultiInstalling;
    }

    partial void OnVmultiInstallingChanged(bool value)
    {
        ShowInstallButton = !VmultiInstalled && !value;
        ShowUninstallButton = VmultiInstalled && !value;
    }

    // Typed OTD data
    private Settings? _settings;
    [ObservableProperty] private JToken? _tabletsJson; // tablets remain JToken (complex runtime type)

    // OTD Version (from referenced assembly)
    public string CurrentOtdVersion { get; } = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

    // Daemon path — built from submodule
    private static string? FindDaemonExe()
    {
        // Look relative to our exe: up to repo root, then into the daemon build output
        var baseDir = AppContext.BaseDirectory;
        // Try the daemon's build output (Debug and Release)
        foreach (var config in new[] { "Debug", "Release" })
        {
            var candidate = Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "external", "OpenTabletDriver", "OpenTabletDriver.Daemon",
                "bin", config, "net8.0", "OpenTabletDriver.Daemon.exe"));
            if (File.Exists(candidate)) return candidate;
        }
        // Fallback: check if it's running and get path from process
        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { var p = proc.MainModule?.FileName; if (p != null) return p; } catch { }
        }
        return null;
    }

    public bool CanStartDaemon => !IsConnected && FindDaemonExe() != null;
    [ObservableProperty] private bool _isDaemonRunning;

    // Tablet specs
    [ObservableProperty] private string _tabletArea = "";
    [ObservableProperty] private string _tabletPressure = "";
    [ObservableProperty] private string _tabletButtons = "";
    [ObservableProperty] private string _outputMode = "";

    // Profiles — wrapped with detection status
    [ObservableProperty] private List<ProfileItem> _profiles = [];

    // Presets
    [ObservableProperty] private JArray _presets = [];
    [ObservableProperty] private string _presetDirectory = "";
    [ObservableProperty] private string _settingsFilePath = "";

    // Tablet Configurations (folder peer of OpenTabletDriver.Daemon.exe)
    [ObservableProperty] private string _configurationsDirectory = "";
    [ObservableProperty] private List<ConfigurationItem> _configurations = [];
    [ObservableProperty] private bool _hasConfigurations;

    /// <summary>Current OTD Settings object (typed). Use for reads and modifications.</summary>
    public Settings? CurrentSettings => _settings;

    public MainViewModel()
    {
        _daemon.Connected += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Connected";
            IsConnected = true;
            IsDaemonRunning = true;
            _ = LoadDataAsync();
        });
        _daemon.Disconnected += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            IsDaemonRunning = false;
            HasTablet = false;
            TabletName = "";
        });

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var (hidResult, setupResult) = await Task.Run(() =>
            (_vmulti.DetectHid(), _vmulti.DetectSetupApi()));

        VmultiHidStatus = hidResult.Message;
        VmultiSetupApiStatus = setupResult.Message;
        VmultiInstalled = hidResult.Visible || setupResult.Installed;

        if (hidResult.Functional)
            VmultiMessage = "Installed & active";
        else if (setupResult.Installed && !setupResult.Enabled)
            VmultiMessage = "Installed but disabled";
        else if (setupResult.Installed)
            VmultiMessage = "Installed (not active in HID)";
        else
            VmultiMessage = "Not installed";

        // Ensure the tablet Configurations folder exists next to the daemon exe,
        // and load the current list for the dashboard.
        InitializeConfigurationsFolder();
        LoadConfigurations();

        // Auto-start daemon if not running
        IsDaemonRunning = Process.GetProcessesByName("OpenTabletDriver.Daemon").Length > 0;
        if (!IsDaemonRunning && FindDaemonExe() != null)
        {
            LaunchDaemonProcess();
            await Task.Delay(1000);
        }

        ConnectionStatus = "Connecting...";
        await _daemon.ConnectAsync(_cts.Token);
        _ = PollDataAsync();
    }

    private async Task PollDataAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            await Task.Delay(3000, _cts.Token).ConfigureAwait(false);
            if (IsConnected)
            {
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => _ = LoadDataAsync());
                }
                catch { }
            }
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Load tablets (JToken — complex runtime type)
            var tablets = await _daemon.GetTabletsAsync();
            TabletsJson = tablets;

            // Build set of detected tablet names and record last-seen timestamps
            var detectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tablets.Count > 0)
            {
                foreach (var t in tablets)
                {
                    var props = t["Properties"] ?? t;
                    var name = props["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        detectedNames.Add(name);
                        AppSettings.Set($"LastSeen:{name}", DateTime.Now.ToString("o"));
                    }
                }

                var first = tablets[0];
                var firstProps = first["Properties"] ?? first;
                HasTablet = true;
                TabletName = firstProps["Name"]?.ToString() ?? "Unknown";

                var specs = firstProps["Specifications"];
                var digi = specs?["Digitizer"];
                var pen = specs?["Pen"];
                TabletArea = $"{digi?["Width"]} x {digi?["Height"]} mm";
                TabletPressure = pen?["MaxPressure"]?.ToString() ?? "?";
                TabletButtons = pen?["ButtonCount"]?.ToString() ?? "?";
            }
            else
            {
                HasTablet = false;
                TabletName = "";
                TabletArea = "";
                TabletPressure = "";
                TabletButtons = "";
            }

            // Load settings — TYPED
            _settings = await _daemon.GetSettingsAsync();

            if (_settings != null)
            {
                Profiles = _settings.Profiles
                    .Select(p =>
                    {
                        bool detected = detectedNames.Contains(p.Tablet);
                        DateTime? lastSeen = null;
                        var stored = AppSettings.Get($"LastSeen:{p.Tablet}");
                        if (stored != null && DateTime.TryParse(stored, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                            lastSeen = dt;
                        if (detected)
                            lastSeen = DateTime.Now;
                        return new ProfileItem(p, detected, lastSeen);
                    })
                    .ToList();

                if (Profiles.Count > 0)
                {
                    var mode = Profiles[0].Profile.OutputMode?.Path;
                    OutputMode = mode?.Split('.').LastOrDefault() ?? "Unknown";
                    HasWindowsInk = mode?.Contains("WinInk", StringComparison.OrdinalIgnoreCase) ?? false;
                }
            }

            // Load app info — TYPED
            var appInfo = await _daemon.GetAppInfoAsync();
            if (appInfo != null)
            {
                PresetDirectory = appInfo.PresetDirectory ?? "";
                SettingsFilePath = appInfo.SettingsFile ?? "";
            }
            await LoadPresetsAsync();
        }
        catch { /* Data load failed — will retry on next connection */ }
    }

    /// <summary>
    /// Apply settings to the daemon AND save to disk (like OTD's own Save + Apply).
    /// </summary>
    public async Task ApplyAndSaveSettingsAsync(Settings settings)
    {
        _settings = settings;
        await _daemon.SetSettingsAsync(settings);

        // Persist to disk (same as OTD's own UX Save button)
        if (!string.IsNullOrEmpty(SettingsFilePath))
        {
            try
            {
                settings.Serialize(new FileInfo(SettingsFilePath));
            }
            catch { /* ignore save errors */ }
        }

        await LoadDataAsync();
    }

    private async Task LoadPresetsAsync()
    {
        if (string.IsNullOrEmpty(PresetDirectory) || !Directory.Exists(PresetDirectory))
        {
            Presets = [];
            return;
        }

        var presetList = new JArray();
        foreach (var file in Directory.GetFiles(PresetDirectory, "*.json"))
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var obj = new JObject
                {
                    ["Name"] = Path.GetFileNameWithoutExtension(file),
                    ["Path"] = file,
                    ["Content"] = content
                };
                presetList.Add(obj);
            }
            catch { }
        }
        Presets = presetList;
    }

    private void InitializeConfigurationsFolder()
    {
        // OTD reads tablet configs from %AppData%\OpenTabletDriver\Configurations
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) { ConfigurationsDirectory = ""; return; }
        var configDir = Path.Combine(appData, "OpenTabletDriver", "Configurations");
        try
        {
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
        }
        catch { }
        ConfigurationsDirectory = configDir;
    }

    private void LoadConfigurations()
    {
        var dir = ConfigurationsDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Configurations = [];
            HasConfigurations = false;
            return;
        }

        var items = new List<ConfigurationItem>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                                       .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(file);

                // Friendly name: prefer the JSON "Name" field (e.g. "Huion H640P"),
                // then a "Manufacturer Model" combo, then the parent-folder + filename,
                // and finally the bare filename.
                string displayName = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var raw = File.ReadAllText(file).TrimStart('\uFEFF');
                    var token = JToken.Parse(raw);
                    var jsonName = token["Name"]?.ToString();
                    var manufacturer = token["Manufacturer"]?.ToString()
                                       ?? token["Vendor"]?.ToString();
                    var model = token["Model"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(jsonName))
                        displayName = jsonName!;
                    else if (!string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(model))
                        displayName = $"{manufacturer} {model}";
                    else
                    {
                        // Fall back to "<parent folder> <filename>" so files placed in
                        // manufacturer subfolders still get a vendor prefix.
                        var parent = Path.GetFileName(Path.GetDirectoryName(file));
                        var stem = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrEmpty(parent) &&
                            !string.Equals(parent, "Configurations", StringComparison.OrdinalIgnoreCase))
                            displayName = $"{parent} {stem}";
                    }
                }
                catch { }

                items.Add(new ConfigurationItem(
                    displayName,
                    Path.GetFileName(file),
                    file,
                    $"{info.Length:N0} bytes"));
            }
            catch { }
        }
        Configurations = items;
        HasConfigurations = items.Count > 0;
    }

    [RelayCommand]
    private void RefreshConfigurations() => LoadConfigurations();

    [RelayCommand]
    private void OpenConfigurationsFolder()
    {
        if (!string.IsNullOrEmpty(ConfigurationsDirectory) && Directory.Exists(ConfigurationsDirectory))
            Process.Start("explorer.exe", ConfigurationsDirectory);
    }

    [RelayCommand]
    private async Task ViewConfiguration(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        string content;
        try
        {
            var raw = await File.ReadAllTextAsync(path);
            try { content = JToken.Parse(raw).ToString(Newtonsoft.Json.Formatting.Indented); }
            catch { content = raw; }
        }
        catch (Exception ex) { content = $"Failed to read file:\n{ex.Message}"; }

        await ShowConfigurationDetailsDialogAsync(Path.GetFileName(path), content);
    }

    [RelayCommand]
    private async Task DeleteConfiguration(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var confirmed = await Dialogs.ShowConfirmAsync(
            "Delete Configuration",
            $"Delete \"{Path.GetFileName(path)}\"?\n\nThis cannot be undone.");
        if (!confirmed) return;
        try { File.Delete(path); } catch { }
        LoadConfigurations();
    }

    private static async Task ShowConfigurationDetailsDialogAsync(string title, string content)
    {
        var parent = Dialogs.GetMainWindow();
        if (parent == null) return;

        var textBox = new Avalonia.Controls.TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
        };

        var scroll = new Avalonia.Controls.ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = textBox,
        };

        var closeBtn = new Avalonia.Controls.Button
        {
            Content = "Close",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(24, 8),
            FontSize = 13,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
        };

        var grid = new Avalonia.Controls.Grid
        {
            Margin = new Avalonia.Thickness(20),
            RowDefinitions = new Avalonia.Controls.RowDefinitions("*,Auto"),
        };
        Avalonia.Controls.Grid.SetRow(scroll, 0);
        Avalonia.Controls.Grid.SetRow(closeBtn, 1);
        grid.Children.Add(scroll);
        grid.Children.Add(closeBtn);

        var dialog = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 720,
            Height = 600,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = grid,
        };
        closeBtn.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(parent);
    }

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    partial void OnCurrentPageChanged(string? oldValue, string newValue)
    {
        if (oldValue == "Diagnostics" && newValue != "Diagnostics")
            _ = StopDebuggingAsync();
    }

    private async Task StartDebuggingAsync()
    {
        if (IsDebugging || !IsConnected) return;
        _daemon.DeviceReport += OnDeviceReport;
        await _daemon.SetTabletDebugAsync(true);
        IsDebugging = true;
        ReportCount = 0;
        LastReportRaw = "";
        LastReportFormatted = "";
        DebugReportRate = "";
        DebugTabletName = "";
        DebugReportType = "";
        DebugPenX = 0; DebugPenY = 0; DebugPenPressure = 0; DebugPressurePercent = 0;
        DebugMaxX = 0; DebugMaxY = 0; DebugMaxPressure = 0;
        DebugDigitizerWidth = 0; DebugDigitizerHeight = 0;
        DebugHasPosition = false;
        DebugTiltX = 0; DebugTiltY = 0; DebugTiltAzimuth = 0; DebugTiltAltitude = 0;
        DebugPenButtons = ""; DebugNearProximity = ""; DebugHoverDistance = "";
        _reportPeriodEma = 0;
        _lastReportTime = DateTime.MinValue;
    }

    private async Task StopDebuggingAsync()
    {
        if (!IsDebugging) return;
        _daemon.DeviceReport -= OnDeviceReport;
        IsDebugging = false;
        try { await _daemon.SetTabletDebugAsync(false); } catch { }
    }

    private void OnDeviceReport(JObject data)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReportCount++;

            // Report rate (EMA)
            var now = DateTime.UtcNow;
            if (_lastReportTime != DateTime.MinValue)
            {
                var deltaMs = (now - _lastReportTime).TotalMilliseconds;
                if (_reportPeriodEma == 0)
                    _reportPeriodEma = deltaMs;
                else
                    _reportPeriodEma += (deltaMs - _reportPeriodEma) * 0.05;
                if (_reportPeriodEma > 0)
                    DebugReportRate = $"{Math.Round(1000.0 / _reportPeriodEma)} Hz";
            }
            _lastReportTime = now;

            // Tablet name
            var tabletName = data["Tablet"]?["Properties"]?["Name"]?.ToString();
            if (tabletName != null) DebugTabletName = tabletName;

            // Report type
            var path = data["Path"]?.ToString();
            if (path != null) DebugReportType = path.Split('.').LastOrDefault() ?? path;

            // Digitizer specs (for visualizer scaling)
            var digi = data["Tablet"]?["Properties"]?["Specifications"]?["Digitizer"];
            if (digi != null)
            {
                var maxX = digi["MaxX"]?.Value<double>() ?? 0;
                var maxY = digi["MaxY"]?.Value<double>() ?? 0;
                var digiW = digi["Width"]?.Value<double>() ?? 0;
                var digiH = digi["Height"]?.Value<double>() ?? 0;
                if (maxX > 0) DebugMaxX = maxX;
                if (maxY > 0) DebugMaxY = maxY;
                if (digiW > 0) DebugDigitizerWidth = digiW;
                if (digiH > 0) DebugDigitizerHeight = digiH;
            }

            // Max pressure from pen specs
            var pen = data["Tablet"]?["Properties"]?["Specifications"]?["Pen"];
            if (pen != null)
            {
                var maxP = pen["MaxPressure"]?.Value<double>() ?? 0;
                if (maxP > 0) DebugMaxPressure = maxP;
            }

            var reportData = data["Data"];
            if (reportData == null) return;

            // Raw bytes
            var rawBase64 = reportData["Raw"]?.ToString();
            if (rawBase64 != null)
            {
                try
                {
                    var bytes = Convert.FromBase64String(rawBase64);
                    LastReportRaw = BitConverter.ToString(bytes).Replace('-', ' ');
                }
                catch { LastReportRaw = rawBase64; }
            }

            // Position for visualizer
            var pos = reportData["Position"];
            if (pos != null)
            {
                DebugPenX = pos["X"]?.Value<double>() ?? 0;
                DebugPenY = pos["Y"]?.Value<double>() ?? 0;
                DebugHasPosition = true;
            }

            // Pressure
            var pressure = reportData["Pressure"];
            if (pressure != null)
            {
                DebugPenPressure = pressure.Value<double>();
                DebugPressurePercent = DebugMaxPressure > 0 ? (DebugPenPressure / DebugMaxPressure) * 100.0 : 0;
            }

            // Tilt
            var tilt = reportData["Tilt"];
            if (tilt != null)
            {
                DebugTiltX = tilt["X"]?.Value<double>() ?? 0;
                DebugTiltY = tilt["Y"]?.Value<double>() ?? 0;
                DebugTiltAzimuth = Math.Atan2(DebugTiltX, DebugTiltY) * (180.0 / Math.PI);
                if (DebugTiltAzimuth < 0) DebugTiltAzimuth += 360;
                DebugTiltAltitude = 90.0 - Math.Sqrt(DebugTiltX * DebugTiltX + DebugTiltY * DebugTiltY);
            }

            // Pen buttons
            var buttons = reportData["PenButtons"];
            if (buttons is JArray btnArr)
                DebugPenButtons = string.Join("  ", btnArr.Select((b, i) => $"{i + 1}: {b}"));

            // Formatted fields
            var lines = new List<string>();
            if (pos != null) lines.Add($"Position: [{pos["X"]}, {pos["Y"]}]");
            if (pressure != null) lines.Add($"Pressure: {pressure}");
            if (buttons != null) lines.Add($"PenButtons: {buttons}");
            if (tilt != null)
            {
                lines.Add($"Tilt: [{tilt["X"]}, {tilt["Y"]}]");
                lines.Add($"Azimuth: {DebugTiltAzimuth:F1}°  Altitude: {DebugTiltAltitude:F1}°");
            }
            var aux = reportData["AuxButtons"];
            if (aux != null) lines.Add($"AuxButtons: {aux}");
            var proximity = reportData["NearProximity"];
            if (proximity != null)
            {
                DebugNearProximity = proximity.Value<bool>() ? "Yes" : "No";
                lines.Add($"NearProximity: {proximity}");
            }
            var hover = reportData["HoverDistance"];
            if (hover != null)
            {
                DebugHoverDistance = hover.ToString();
                lines.Add($"HoverDistance: {hover}");
            }

            LastReportFormatted = string.Join("\n", lines);
        });
    }

    [RelayCommand]
    private async Task ToggleDebugging()
    {
        if (IsDebugging)
            await StopDebuggingAsync();
        else
            await StartDebuggingAsync();
    }

    [RelayCommand]
    private async Task RefreshConnection()
    {
        if (IsConnected)
            await LoadDataAsync();
        else
        {
            ConnectionStatus = "Connecting...";
            _ = _daemon.ConnectAsync(_cts.Token);
        }
    }

    [RelayCommand]
    private void LaunchOtdUx()
    {
        // Launch the OTD WPF UX from the submodule via dotnet run
        var otdUxProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "external", "OpenTabletDriver", "OpenTabletDriver.UX.Wpf"));

        if (Directory.Exists(otdUxProject))
        {
            Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{otdUxProject}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
    }

    private void LaunchDaemonProcess()
    {
        var daemonPath = FindDaemonExe();
        if (daemonPath != null)
        {
            Process.Start(new ProcessStartInfo(daemonPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? "",
            });
        }
    }

    [RelayCommand]
    private void StopDaemon()
    {
        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { proc.Kill(); } catch { }
        }
    }

    [RelayCommand]
    private async Task StartDaemon()
    {
        LaunchDaemonProcess();
        await Task.Delay(1000);
        OnPropertyChanged(nameof(CanStartDaemon));
        if (!IsConnected)
        {
            ConnectionStatus = "Connecting...";
            await _daemon.ConnectAsync(_cts.Token);
        }
    }

    [RelayCommand]
    private async Task RestartDaemon()
    {
        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { proc.Kill(); } catch { }
        }

        await Task.Delay(500);
        LaunchDaemonProcess();
        await Task.Delay(1000);

        if (!IsConnected)
        {
            ConnectionStatus = "Connecting...";
            await _daemon.ConnectAsync(_cts.Token);
        }
    }

    [RelayCommand]
    private async Task SavePreset()
    {
        if (_settings == null) return;
        var name = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (!Directory.Exists(PresetDirectory)) Directory.CreateDirectory(PresetDirectory);
        _settings.Serialize(new FileInfo(path));
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private async Task LoadPreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(path)) return;
        if (Settings.TryDeserialize(new FileInfo(path), out var settings))
        {
            await ApplyAndSaveSettingsAsync(settings);
        }
    }

    [RelayCommand]
    private async Task UpdatePreset(string name)
    {
        if (_settings == null) return;
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        _settings.Serialize(new FileInfo(path));
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private async Task RenamePreset(string name)
    {
        var oldPath = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(oldPath)) return;

        var newName = await Dialogs.ShowInputAsync(
            "Rename Snapshot",
            "Enter a new name for this snapshot:",
            name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != name)
        {
            var newPath = Path.Combine(PresetDirectory, $"{newName}.json");
            if (!File.Exists(newPath))
            {
                File.Move(oldPath, newPath);
                await LoadPresetsAsync();
            }
            else
            {
                await Dialogs.ShowMessageAsync("Rename",
                    $"A snapshot named \"{newName}\" already exists.");
            }
        }
    }

    [RelayCommand]
    private async Task DeletePreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (File.Exists(path)) File.Delete(path);
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private async Task OpenConnectedTabletSettings()
    {
        if (!HasTablet || string.IsNullOrEmpty(TabletName) || _settings == null) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Tablet == TabletName);
        if (profile != null)
            await OpenTabletSettingsForProfile(profile);
    }

    [RelayCommand]
    private async Task OpenTabletSettings(object profileObj)
    {
        // Called from XAML — may receive ProfileItem or Profile
        if (profileObj is ProfileItem item)
            await OpenTabletSettingsForProfile(item.Profile);
        else if (profileObj is Profile profile)
            await OpenTabletSettingsForProfile(profile);
    }

    [RelayCommand]
    private async Task ForgetProfile(string tabletName)
    {
        if (_settings == null || string.IsNullOrEmpty(tabletName)) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        _settings.Profiles.Remove(profile);
        await ApplyAndSaveSettingsAsync(_settings);
    }

    private (float Width, float Height)? GetTabletDigitizer(string tabletName)
    {
        if (TabletsJson is not JArray tablets) return null;
        foreach (var t in tablets)
        {
            var props = t["Properties"] ?? t;
            if (props["Name"]?.ToString() == tabletName)
            {
                var digi = props["Specifications"]?["Digitizer"];
                if (digi != null)
                {
                    var w = digi["Width"]?.Value<float>() ?? 0;
                    var h = digi["Height"]?.Value<float>() ?? 0;
                    if (w > 0 && h > 0) return (w, h);
                }
            }
        }
        return null;
    }

    private async Task OpenTabletSettingsForProfile(Profile profile)
    {
        var tabletName = profile.Tablet;
        var digitizer = GetTabletDigitizer(tabletName);
        var dialog = new Views.TabletSettingsDialog(
            profile,
            _settings,
            async updatedSettings => await ApplyAndSaveSettingsAsync(updatedSettings),
            async () =>
            {
                _settings = await _daemon.GetSettingsAsync();
                if (_settings != null)
                    return _settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
                return null;
            },
            digitizer);

        var mainWindow = Dialogs.GetMainWindow();
        if (mainWindow != null)
            await dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task InstallVmulti()
    {
        var confirmed = await Dialogs.ShowConfirmAsync(
            "Install VMulti Driver",
            "VMulti driver installation may restart your computer.\n\n" +
            "Please save all work in other applications before continuing.\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        VmultiInstalling = true;
        VmultiInstallStatus = "Starting...";

        _vmultiInstaller.StatusChanged += status =>
            Dispatcher.UIThread.InvokeAsync(() => VmultiInstallStatus = status);

        try
        {
            var installResult = await Task.Run(() => _vmultiInstaller.InstallAsync(_cts.Token));
            VmultiInstallStatus = installResult.Message;

            if (installResult.Success)
            {
                await RefreshVmultiDetection();
                await Dialogs.ShowMessageAsync("VMulti Installation", installResult.Message);
            }
            else
            {
                await Dialogs.ShowMessageAsync("VMulti Installation", installResult.Message);
            }
        }
        catch (Exception ex)
        {
            VmultiInstallStatus = $"Error: {ex.Message}";
        }
        finally
        {
            VmultiInstalling = false;
        }
    }

    [RelayCommand]
    private async Task UninstallVmulti()
    {
        var confirmed = await Dialogs.ShowConfirmAsync(
            "Uninstall VMulti Driver",
            "This will uninstall the VMulti virtual driver.\n\n" +
            "Your computer may need to restart to complete the removal.\n" +
            "Please save all work in other applications before continuing.\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        VmultiInstalling = true;
        VmultiInstallStatus = "Starting uninstall...";

        _vmultiInstaller.StatusChanged += status =>
            Dispatcher.UIThread.InvokeAsync(() => VmultiInstallStatus = status);

        try
        {
            var uninstallResult = await Task.Run(() => _vmultiInstaller.UninstallAsync(_cts.Token));
            VmultiInstallStatus = uninstallResult.Message;

            if (uninstallResult.Success)
            {
                await RefreshVmultiDetection();
                await Dialogs.ShowMessageAsync("VMulti Uninstallation", uninstallResult.Message);
            }
            else
            {
                await Dialogs.ShowMessageAsync("VMulti Uninstallation", uninstallResult.Message);
            }
        }
        catch (Exception ex)
        {
            VmultiInstallStatus = $"Error: {ex.Message}";
        }
        finally
        {
            VmultiInstalling = false;
        }
    }

    [RelayCommand]
    private async Task RefreshVmultiDetection()
    {
        var (hidResult, setupResult) = await Task.Run(() =>
            (_vmulti.DetectHid(), _vmulti.DetectSetupApi()));

        VmultiHidStatus = hidResult.Message;
        VmultiSetupApiStatus = setupResult.Message;
        VmultiInstalled = hidResult.Visible || setupResult.Installed;

        if (hidResult.Functional)
            VmultiMessage = "Installed & active";
        else if (setupResult.Installed && !setupResult.Enabled)
            VmultiMessage = "Installed but disabled";
        else if (setupResult.Installed)
            VmultiMessage = "Installed (not active in HID)";
        else
            VmultiMessage = "Not installed";
    }

    public void Dispose()
    {
        if (IsDebugging)
        {
            _daemon.DeviceReport -= OnDeviceReport;
            try { _daemon.SetTabletDebugAsync(false).Wait(2000); } catch { }
        }
        _cts.Cancel();
        _daemon.Dispose();
    }
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

public record ProfileItem(Profile Profile, bool IsDetected, DateTime? LastSeen)
{
    public string Tablet => Profile.Tablet;

    public string StatusText
    {
        get
        {
            if (IsDetected) return "Detected";
            if (LastSeen == null) return "Not detected";
            return $"Not detected — {FormatRelativeTime(LastSeen.Value)}";
        }
    }

    public string? LastSeenDetail
    {
        get
        {
            if (IsDetected || LastSeen == null) return null;
            return $"Last seen {LastSeen.Value:yyyy-MM-dd} at {LastSeen.Value:h:mm tt}";
        }
    }

    private static string FormatRelativeTime(DateTime lastSeen)
    {
        var elapsed = DateTime.Now - lastSeen;

        if (elapsed.TotalMinutes < 1) return "seen just now";
        if (elapsed.TotalMinutes < 60) return $"seen {(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"seen {(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 2) return "seen yesterday";
        if (elapsed.TotalDays < 7) return $"seen {(int)elapsed.TotalDays} days ago";
        if (elapsed.TotalDays < 30) return $"seen {(int)(elapsed.TotalDays / 7)} weeks ago";
        return "seen a long time ago";
    }
}
