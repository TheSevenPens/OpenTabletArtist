using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
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
    [ObservableProperty] private JToken? _settingsJson;
    [ObservableProperty] private JToken? _tabletsJson;

    // OTD Version & Update
    [ObservableProperty] private string _currentOtdVersion = "";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateVersion = "";
    [ObservableProperty] private string _updateDownloadUrl = "";
    [ObservableProperty] private bool _updateDownloading;
    [ObservableProperty] private string _updateDownloadStatus = "";

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
        // Detect vmulti on background thread (both methods)
        var (hidResult, setupResult) = await Task.Run(() =>
            (_vmulti.DetectHid(), _vmulti.DetectSetupApi()));

        VmultiHidStatus = hidResult.Message;
        VmultiSetupApiStatus = setupResult.Message;

        // Overall status: installed if either method sees it
        VmultiInstalled = hidResult.Visible || setupResult.Installed;

        if (hidResult.Functional)
            VmultiMessage = "Installed & active";
        else if (setupResult.Installed && !setupResult.Enabled)
            VmultiMessage = "Installed but disabled";
        else if (setupResult.Installed)
            VmultiMessage = "Installed (not active in HID)";
        else
            VmultiMessage = "Not installed";

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
            await CheckForOtdUpdatesAsync();
        }
        catch { /* Data load failed — will retry on next connection */ }
    }

    private async Task CheckForOtdUpdatesAsync()
    {
        // Get current version from daemon exe
        try
        {
            if (!string.IsNullOrEmpty(OtdInstallPath))
            {
                var daemonPath = Path.Combine(OtdInstallPath, "OpenTabletDriver.Daemon.exe");
                if (File.Exists(daemonPath))
                {
                    var fileInfo = FileVersionInfo.GetVersionInfo(daemonPath);
                    CurrentOtdVersion = fileInfo.FileVersion ?? fileInfo.ProductVersion ?? "";
                }
            }
        }
        catch { /* ignore version detection errors */ }

        // Check for updates via daemon RPC
        try
        {
            var updateInfo = await _daemon.CheckForUpdatesAsync();
            if (updateInfo != null && updateInfo.Type != JTokenType.Null)
            {
                var version = updateInfo["Version"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(version))
                {
                    UpdateAvailable = true;
                    UpdateVersion = version;
                }
                else
                {
                    UpdateAvailable = false;
                }
            }
            else
            {
                UpdateAvailable = false;
            }
        }
        catch
        {
            UpdateAvailable = false;
        }
    }

    [RelayCommand]
    private void OpenOtdReleases()
    {
        Process.Start(new ProcessStartInfo("https://github.com/OpenTabletDriver/OpenTabletDriver/releases") { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task DownloadOtdUpdateAsync()
    {
        if (string.IsNullOrEmpty(UpdateVersion) || UpdateDownloading) return;

        var zipUrl = $"https://github.com/OpenTabletDriver/OpenTabletDriver/releases/download/v{UpdateVersion}/OpenTabletDriver-{UpdateVersion}_win-x64.zip";
        var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var fileName = $"OpenTabletDriver-{UpdateVersion}_win-x64.zip";
        var filePath = Path.Combine(downloadsFolder, fileName);

        try
        {
            UpdateDownloading = true;
            UpdateDownloadStatus = "Downloading...";

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TabletDriverUX/1.0");

            using var response = await client.GetAsync(zipUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (int)(totalRead * 100 / totalBytes.Value);
                    UpdateDownloadStatus = $"Downloading... {pct}%";
                }
            }

            UpdateDownloadStatus = "Download complete";
            UpdateDownloading = false;

            // Open File Explorer with the downloaded file selected
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            UpdateDownloadStatus = $"Download failed: {ex.Message}";
            UpdateDownloading = false;
        }
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
    private void OpenConnectedTabletSettings()
    {
        // Find the profile for the currently connected tablet
        if (!HasTablet || string.IsNullOrEmpty(TabletName)) return;
        var profile = Profiles.FirstOrDefault(p => p["Tablet"]?.ToString() == TabletName);
        if (profile != null)
            OpenTabletSettings(profile);
    }

    [RelayCommand]
    private void OpenTabletSettings(object profileToken)
    {
        if (profileToken is JToken profile)
        {
            var dialog = new Views.TabletSettingsDialog(profile, async updatedProfile =>
            {
                // Replace the matching profile in settings and push to daemon
                if (SettingsJson is JObject settings)
                {
                    var profiles = settings["Profiles"] as JArray;
                    if (profiles != null)
                    {
                        var tabletName = updatedProfile["Tablet"]?.ToString();
                        for (int i = 0; i < profiles.Count; i++)
                        {
                            if (profiles[i]["Tablet"]?.ToString() == tabletName)
                            {
                                profiles[i] = updatedProfile.DeepClone();
                                break;
                            }
                        }
                        await _daemon.SetSettingsAsync(settings);
                        await LoadDataAsync();
                    }
                }
            })
            {
                Owner = App.Current.MainWindow
            };
            dialog.ShowDialog();
        }
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task InstallVmulti()
    {
        // Warn the user about reboot risk
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
                // Refresh detection
                await RefreshVmultiDetection();

                System.Windows.MessageBox.Show(
                    installResult.Message,
                    "VMulti Installation",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    installResult.Message,
                    "VMulti Installation",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
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

                System.Windows.MessageBox.Show(
                    uninstallResult.Message,
                    "VMulti Uninstallation",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    uninstallResult.Message,
                    "VMulti Uninstallation",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
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
