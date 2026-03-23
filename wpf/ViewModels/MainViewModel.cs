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

    public MainViewModel()
    {
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
                HasTablet = true;
                TabletName = first["Name"]?.ToString() ?? "Unknown";

                var specs = first["Specifications"];
                var digi = specs?["Digitizer"];
                var pen = specs?["Pen"];
                TabletArea = $"{digi?["Width"]} x {digi?["Height"]} mm";
                TabletPressure = pen?["MaxPressure"]?.ToString() ?? "?";
                TabletButtons = pen?["ButtonCount"]?.ToString() ?? "?";
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
