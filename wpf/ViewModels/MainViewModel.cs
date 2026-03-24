using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

    // OTD Version & Update
    [ObservableProperty] private string _currentOtdVersion = "";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateVersion = "";
    [ObservableProperty] private string _updateCheckStatus = "";
    [ObservableProperty] private bool _updateDownloading;
    [ObservableProperty] private string _updateDownloadStatus = "";
    private bool _updateChecked;

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

    // OTD install location
    [ObservableProperty] private string _otdInstallPath = "";
    [ObservableProperty] private bool _hasOtdInstallPath;

    private const string OtdInstallPathKey = "OtdInstallPath";

    /// <summary>Current OTD Settings object (typed). Use for reads and modifications.</summary>
    public Settings? CurrentSettings => _settings;

    public MainViewModel()
    {
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
            await CheckForOtdUpdatesAsync();
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

    private async Task CheckForOtdUpdatesAsync()
    {
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
        catch { }

        if (_updateChecked) return;
        _updateChecked = true;
        UpdateCheckStatus = "";

        // Strategy 1: daemon RPC
        string? latestTag = null;
        try
        {
            var updateInfo = await _daemon.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                latestTag = updateInfo.Version?.ToString();
            }
        }
        catch { }

        // Strategy 2: direct GitHub API
        if (string.IsNullOrEmpty(latestTag))
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TabletDriverUX/1.0");
                var response = await client.GetAsync("https://api.github.com/repos/OpenTabletDriver/OpenTabletDriver/releases/latest");

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    UpdateCheckStatus = "Unable to check for updates — GitHub rate limit reached. This resets automatically within the hour.";
                    return;
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var release = JObject.Parse(json);
                latestTag = release["tag_name"]?.ToString() ?? "";
                if (latestTag.StartsWith("v")) latestTag = latestTag[1..];
            }
            catch (System.Net.Http.HttpRequestException)
            {
                UpdateCheckStatus = "Unable to check for updates — network error.";
                return;
            }
            catch
            {
                UpdateCheckStatus = "Unable to check for updates.";
                return;
            }
        }

        if (!string.IsNullOrEmpty(latestTag) && !string.IsNullOrEmpty(CurrentOtdVersion))
        {
            try
            {
                var latestVersion = new Version(latestTag);
                var currentVersion = new Version(CurrentOtdVersion);
                if (latestVersion > currentVersion)
                {
                    UpdateAvailable = true;
                    UpdateVersion = latestTag;
                    UpdateCheckStatus = "";
                }
                else
                {
                    UpdateAvailable = false;
                    UpdateCheckStatus = "Up to date";
                }
            }
            catch
            {
                UpdateCheckStatus = "Unable to compare versions.";
            }
        }
        else if (string.IsNullOrEmpty(CurrentOtdVersion))
        {
            UpdateCheckStatus = "Set OTD install path to check for updates.";
        }
    }

    [RelayCommand]
    private void OpenOtdReleases()
    {
        Process.Start(new ProcessStartInfo("https://github.com/OpenTabletDriver/OpenTabletDriver/releases") { UseShellExecute = true });
    }

    public static string OtdBinRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTabletDriverBin");

    [RelayCommand]
    private async Task DownloadOtdUpdateAsync()
    {
        if (string.IsNullOrEmpty(UpdateVersion) || UpdateDownloading) return;

        var zipUrl = $"https://github.com/OpenTabletDriver/OpenTabletDriver/releases/download/v{UpdateVersion}/OpenTabletDriver-{UpdateVersion}_win-x64.zip";
        var versionDir = Path.Combine(OtdBinRoot, UpdateVersion);
        var tempZip = Path.Combine(Path.GetTempPath(), $"OTD-{UpdateVersion}.zip");

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
            await using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
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
            }

            UpdateDownloadStatus = "Extracting...";
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, true);
            Directory.CreateDirectory(versionDir);
            ZipFile.ExtractToDirectory(tempZip, versionDir);

            try { File.Delete(tempZip); } catch { }

            UpdateDownloadStatus = $"Installed to {versionDir}";
            UpdateDownloading = false;

            OtdInstallPath = versionDir;
            HasOtdInstallPath = true;
            AppSettings.Set(OtdInstallPathKey, versionDir);

            var daemonPath = Path.Combine(versionDir, "OpenTabletDriver.Daemon.exe");
            if (File.Exists(daemonPath))
            {
                var fileInfo = FileVersionInfo.GetVersionInfo(daemonPath);
                CurrentOtdVersion = fileInfo.FileVersion ?? fileInfo.ProductVersion ?? "";
            }

            Process.Start("explorer.exe", $"\"{versionDir}\"");
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
            await LoadDataAsync();
        else
        {
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
        string? daemonPath = null;
        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { daemonPath = proc.MainModule?.FileName; } catch { }
            if (daemonPath != null) break;
        }

        foreach (var proc in Process.GetProcessesByName("OpenTabletDriver.Daemon"))
        {
            try { proc.Kill(); } catch { }
        }

        await Task.Delay(500);

        if (daemonPath != null && File.Exists(daemonPath))
        {
            Process.Start(new ProcessStartInfo(daemonPath)
            {
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? "",
            });
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
        var dialog = new Views.TabletSettingsDialog(profile, _settings, async updatedSettings =>
        {
            await ApplyAndSaveSettingsAsync(updatedSettings);
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
