using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
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

    // Profiles — typed
    [ObservableProperty] private List<Profile> _profiles = [];

    // Presets
    [ObservableProperty] private JArray _presets = [];
    [ObservableProperty] private string _presetDirectory = "";
    [ObservableProperty] private string _settingsFilePath = "";

    /// <summary>Current OTD Settings object (typed). Use for reads and modifications.</summary>
    public Settings? CurrentSettings => _settings;

    public MainViewModel()
    {
        _daemon.Connected += () => App.Current.Dispatcher.Invoke(() =>
        {
            ConnectionStatus = "Connected";
            IsConnected = true;
            IsDaemonRunning = true;
            OnPropertyChanged(nameof(CanStartDaemon));
            _ = LoadDataAsync();
        });
        _daemon.Disconnected += () => App.Current.Dispatcher.Invoke(() =>
        {
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            IsDaemonRunning = false;
            OnPropertyChanged(nameof(CanStartDaemon));
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
                    await App.Current.Dispatcher.InvokeAsync(() => _ = LoadDataAsync());
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

            if (tablets.Count > 0)
            {
                var first = tablets[0];
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

            // Load settings — TYPED
            _settings = await _daemon.GetSettingsAsync();

            if (_settings != null)
            {
                Profiles = _settings.Profiles.ToList();

                if (Profiles.Count > 0)
                {
                    var mode = Profiles[0].OutputMode?.Path;
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

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

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
    private async Task SavePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || _settings == null) return;
        var path = Path.Combine(PresetDirectory, $"{name}.json");
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
    private async Task DeletePreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (File.Exists(path)) File.Delete(path);
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private void OpenConnectedTabletSettings()
    {
        if (!HasTablet || string.IsNullOrEmpty(TabletName) || _settings == null) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Tablet == TabletName);
        if (profile != null)
            OpenTabletSettingsForProfile(profile);
    }

    [RelayCommand]
    private void OpenTabletSettings(object profileObj)
    {
        // Called from XAML with a Profile binding
        if (profileObj is Profile profile)
            OpenTabletSettingsForProfile(profile);
    }

    private void OpenTabletSettingsForProfile(Profile profile)
    {
        var tabletName = profile.Tablet;
        var dialog = new Views.TabletSettingsDialog(
            profile,
            _settings,
            async updatedSettings => await ApplyAndSaveSettingsAsync(updatedSettings),
            async () =>
            {
                // Reload settings from daemon and return the updated profile
                _settings = await _daemon.GetSettingsAsync();
                if (_settings != null)
                    return _settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
                return null;
            })
        {
            Owner = App.Current.MainWindow
        };
        dialog.ShowDialog();
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
        var result = System.Windows.MessageBox.Show(
            "VMulti driver installation may restart your computer.\n\n" +
            "Please save all work in other applications before continuing.\n\n" +
            "Do you want to proceed?",
            "Install VMulti Driver",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        VmultiInstalling = true;
        VmultiInstallStatus = "Starting...";

        _vmultiInstaller.StatusChanged += status =>
            App.Current.Dispatcher.Invoke(() => VmultiInstallStatus = status);

        try
        {
            var installResult = await Task.Run(() => _vmultiInstaller.InstallAsync(_cts.Token));
            VmultiInstallStatus = installResult.Message;

            if (installResult.Success)
            {
                await RefreshVmultiDetection();
                System.Windows.MessageBox.Show(installResult.Message, "VMulti Installation",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(installResult.Message, "VMulti Installation",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
        var result = System.Windows.MessageBox.Show(
            "This will uninstall the VMulti virtual driver.\n\n" +
            "Your computer may need to restart to complete the removal.\n" +
            "Please save all work in other applications before continuing.\n\n" +
            "Do you want to proceed?",
            "Uninstall VMulti Driver",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        VmultiInstalling = true;
        VmultiInstallStatus = "Starting uninstall...";

        _vmultiInstaller.StatusChanged += status =>
            App.Current.Dispatcher.Invoke(() => VmultiInstallStatus = status);

        try
        {
            var uninstallResult = await Task.Run(() => _vmultiInstaller.UninstallAsync(_cts.Token));
            VmultiInstallStatus = uninstallResult.Message;

            if (uninstallResult.Success)
            {
                await RefreshVmultiDetection();
                System.Windows.MessageBox.Show(uninstallResult.Message, "VMulti Uninstallation",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(uninstallResult.Message, "VMulti Uninstallation",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
        _cts.Cancel();
        _daemon.Dispose();
    }
}
