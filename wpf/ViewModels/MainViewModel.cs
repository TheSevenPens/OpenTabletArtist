using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using TabletDriverUX.Services;

namespace TabletDriverUX.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DaemonClient _daemon = new();
    private readonly VMultiDetector _vmulti = new();
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private string _currentPage = "Dashboard";
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private bool _isConnected;

    // Dashboard data
    [ObservableProperty] private string _tabletName = "";
    [ObservableProperty] private bool _hasTablet;
    [ObservableProperty] private bool _vmultiInstalled;
    [ObservableProperty] private string _vmultiMessage = "Checking...";
    [ObservableProperty] private bool _hasWindowsInk;
    [ObservableProperty] private JToken? _settingsJson;
    [ObservableProperty] private JToken? _tabletsJson;

    // Tablet specs
    [ObservableProperty] private string _tabletArea = "";
    [ObservableProperty] private string _tabletPressure = "";
    [ObservableProperty] private string _tabletButtons = "";
    [ObservableProperty] private string _outputMode = "";

    // Profiles
    [ObservableProperty] private JArray _profiles = new();

    // Presets
    [ObservableProperty] private JArray _presets = new();
    [ObservableProperty] private string _presetDirectory = "";

    // OTD install location
    [ObservableProperty] private string _otdInstallPath = "";
    [ObservableProperty] private bool _hasOtdInstallPath;

    private const string OtdInstallPathKey = "OtdInstallPath";

    public MainViewModel()
    {
        // Load saved OTD install path
        var saved = AppSettings.Get(OtdInstallPathKey);
        if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved))
        {
            OtdInstallPath = saved;
            HasOtdInstallPath = true;
        }

        _daemon.Connected += () => App.Current.Dispatcher.Invoke(() =>
        {
            ConnectionStatus = "Connected";
            IsConnected = true;
            _ = LoadDataAsync();
        });
        _daemon.Disconnected += () => App.Current.Dispatcher.Invoke(() =>
        {
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            HasTablet = false;
            TabletName = "";
        });

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        // Detect vmulti on background thread
        var status = await Task.Run(() => _vmulti.Detect());
        VmultiInstalled = status.Installed;
        VmultiMessage = status.Message;

        // Connect to daemon
        ConnectionStatus = "Connecting...";
        await _daemon.ConnectAsync(_cts.Token);

        // Poll for data changes (tablet connect/disconnect, settings changes)
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
                    await App.Current.Dispatcher.InvokeAsync(() => _ = LoadDataAsync());
                }
                catch { /* ignore polling errors */ }
            }
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var tablets = await _daemon.GetTabletsAsync();
            TabletsJson = tablets;

            if (tablets is JArray arr && arr.Count > 0)
            {
                var first = arr[0];
                // OTD nests tablet info under "Properties"
                var props = first["Properties"] ?? first;
                HasTablet = true;
                TabletName = props["Name"]?.ToString() ?? "Unknown";

                var specs = props["Specifications"];
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

            var settings = await _daemon.GetSettingsAsync();
            SettingsJson = settings;

            if (settings is JObject settingsObj)
            {
                var profiles = settingsObj["Profiles"] as JArray ?? new JArray();
                Profiles = profiles;

                if (profiles.Count > 0)
                {
                    var mode = profiles[0]?["OutputMode"]?["Path"]?.ToString();
                    OutputMode = mode?.Split('.').LastOrDefault() ?? "Unknown";
                    HasWindowsInk = mode?.ToLower().Contains("winink") ?? false;
                }
            }

            // Load presets
            var appInfo = await _daemon.GetAppInfoAsync();
            if (appInfo is JObject info)
            {
                PresetDirectory = info["PresetDirectory"]?.ToString() ?? "";
            }
            await LoadPresetsAsync();
        }
        catch { /* Data load failed — will retry on next connection */ }
    }

    private async Task LoadPresetsAsync()
    {
        if (string.IsNullOrEmpty(PresetDirectory) || !Directory.Exists(PresetDirectory))
        {
            Presets = new JArray();
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
            catch { /* skip unreadable files */ }
        }
        Presets = presetList;
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPage = page;
    }

    [RelayCommand]
    private void BrowseOtdInstallPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select OpenTabletDriver install folder",
            InitialDirectory = HasOtdInstallPath ? OtdInstallPath : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };

        if (dialog.ShowDialog() == true)
        {
            OtdInstallPath = dialog.FolderName;
            HasOtdInstallPath = true;
            AppSettings.Set(OtdInstallPathKey, dialog.FolderName);
        }
    }

    [RelayCommand]
    private async Task RefreshConnection()
    {
        if (IsConnected)
        {
            await LoadDataAsync();
        }
        else
        {
            // Try to connect (non-blocking single attempt)
            ConnectionStatus = "Connecting...";
            _ = _daemon.ConnectAsync(_cts.Token);
        }
    }

    [RelayCommand]
    private void StartDaemon()
    {
        if (!HasOtdInstallPath) return;
        var daemonExe = Path.Combine(OtdInstallPath, "OpenTabletDriver.Daemon.exe");
        if (File.Exists(daemonExe))
        {
            Process.Start(new ProcessStartInfo(daemonExe)
            {
                CreateNoWindow = true,
                WorkingDirectory = OtdInstallPath,
            });
        }
    }

    [RelayCommand]
    private async Task RestartDaemon()
    {
        // Find the daemon exe path BEFORE killing it (connection still alive)
        string? daemonPath = null;
        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { daemonPath = proc.MainModule?.FileName; } catch { }
            if (daemonPath != null) break;
        }

        // Kill all daemon processes
        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { proc.Kill(); } catch { }
        }

        // Wait briefly for the process to exit
        await Task.Delay(500);

        // Relaunch if we found the path
        if (daemonPath != null && File.Exists(daemonPath))
        {
            Process.Start(new ProcessStartInfo(daemonPath)
            {
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? "",
            });
        }

        // DaemonClient's Disconnected event auto-reconnects
    }

    [RelayCommand]
    private async Task SavePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SettingsJson == null) return;
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        await File.WriteAllTextAsync(path, SettingsJson.ToString());
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private async Task LoadPreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(path)) return;
        var json = await File.ReadAllTextAsync(path);
        var settings = JToken.Parse(json);
        await _daemon.SetSettingsAsync(settings);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task DeletePreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (File.Exists(path)) File.Delete(path);
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _daemon.Dispose();
    }
}
