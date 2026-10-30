using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System.Threading;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OtdInterop;

namespace OpenTabletArtist.Tests;

/// <summary>Values OpenTabletDriver 0.6.8 made required members of the types tests build by hand.</summary>
internal static class TestOtd
{
    /// <summary><c>Profile.OutputMode</c> for a fixture that does not care about it. Deliberately neither
    /// Absolute nor Relative: these fixtures used to leave it null, which the app reads as "no mode chosen",
    /// and a test that wants a mode sets its own.</summary>
    public static OpenTabletDriver.Desktop.Reflection.PluginSettingStore Output() =>
        new(typeof(object), true);
}

/// <summary>Records dialog requests and lets tests script confirm/input results.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public Profile? ShownProfile { get; private set; }
    public int ShowCount { get; private set; }
    public List<string> Messages { get; } = new();
    public (string Title, string Content)? LastTextViewer { get; private set; }

    /// <summary>Result returned by <see cref="ShowConfirmAsync"/> (default false).</summary>
    public bool ConfirmResult { get; set; }
    /// <summary>Result returned by <see cref="ShowInputAsync"/> (default null = cancelled).</summary>
    public string? InputResult { get; set; }
    /// <summary>Result returned by <see cref="ShowHotkeyCaptureAsync"/> (default null = cancelled).</summary>
    public HotkeyChord? HotkeyResult { get; set; }

    public bool ShownDynamicsOnly { get; set; }

    public Task ShowTabletSettingsAsync(Profile profile, bool dynamicsOnly = false)
    {
        ShownProfile = profile;
        ShownDynamicsOnly = dynamicsOnly;
        ShowCount++;
        return Task.CompletedTask;
    }

    /// <summary>Settings and profile from one object, as the real factory now guarantees.</summary>
    public OpenTabletArtist.ViewModels.TabletDetailViewModel? CreateTabletDetail(
        string tabletName, Func<Task> onForget, Action? openConfigsPage = null)
    {
        var settings = new Settings
        {
            Profiles = new OpenTabletDriver.Desktop.Profiles.ProfileCollection
            {
                new Profile { OutputMode = TestOtd.Output(), Tablet = tabletName },
            },
        };
        return new(settings.Profiles[0], settings);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(ConfirmResult);

    public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
        => Task.FromResult(InputResult);

    public Task<HotkeyChord?> ShowHotkeyCaptureAsync(HotkeyChord? initial = null) => Task.FromResult(HotkeyResult);



    public Task ShowTextViewerAsync(string title, string content)
    {
        LastTextViewer = (title, content);
        return Task.CompletedTask;
    }

    /// <summary>The detected-tablet name passed to the last <see cref="ShowSupportedTabletsAsync"/> call.</summary>
    public string? LastSupportedTabletsDetectedName { get; private set; }
    public Task ShowSupportedTabletsAsync(string? detectedName)
    {
        LastSupportedTabletsDetectedName = detectedName;
        return Task.CompletedTask;
    }

    /// <summary>The recordings the review window was asked to show.</summary>
    public List<OpenTabletArtist.ViewModels.StrokeRecordingViewModel> ReviewedRecordings { get; } = new();
    public Task ShowRecordingReviewAsync(OpenTabletArtist.ViewModels.StrokeRecordingViewModel recording)
    {
        ReviewedRecordings.Add(recording);
        return Task.CompletedTask;
    }
}

/// <summary>Minimal <see cref="IDeviceData"/> with settable data and a manual DataLoaded trigger.</summary>
internal sealed class FakeDeviceData : IDeviceData
{
    public JToken? Tablets => null;
    public IReadOnlyList<DetectedTablet> DetectedTablets { get; set; } = new List<DetectedTablet>();
    public string? ActiveTabletName { get; set; }
    public void SetActiveTablet(string? name) => ActiveTabletName = name;
    public bool HasTablet { get; set; }
    public string TabletName { get; set; } = "";
    public string TabletArea => "";
    public string TabletPressure => "";
    public string TabletButtons => "";
    public IReadOnlyList<ProfileItem> Profiles { get; set; } = new List<ProfileItem>();
    public string OutputMode => "";
    public bool HasWindowsInk { get; set; }
    public string PresetDirectory { get; set; } = "";
    public string PluginDirectory { get; set; } = "";
    public string ConfigurationDirectory { get; set; } = "";
    public Dictionary<string, (float Width, float Height)> Digitizers { get; } = new();
    public (float Width, float Height)? GetTabletDigitizer(string tabletName) =>
        Digitizers.TryGetValue(tabletName, out var d) ? d : null;
    public Dictionary<string, OpenTabletArtist.Domain.TabletDigitizerSpec> DigitizerSpecs { get; } = new();
    public OpenTabletArtist.Domain.TabletDigitizerSpec? GetDigitizerSpec(string tabletName) =>
        DigitizerSpecs.TryGetValue(tabletName, out var d) ? d : (OpenTabletArtist.Domain.TabletDigitizerSpec?)null;

    public event Action? DataLoaded;
    public void RaiseDataLoaded() => DataLoaded?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Switch the active tablet the way the shell's switcher does — set it, then notify. Pages
    /// that follow the app-wide selection subscribe to this rather than being told directly.</summary>
    public void RaiseActiveTabletChanged(string? name)
    {
        ActiveTabletName = name;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveTabletName)));
    }
}

/// <summary>Points the Custom Tablet Configs page at a test-controlled directory.</summary>
internal sealed class FakeConfigurationsDirectoryProvider : IConfigurationsDirectoryProvider
{
    private readonly string _dir;
    public FakeConfigurationsDirectoryProvider(string dir) => _dir = dir;
    public string GetOrCreate() => _dir;
}

/// <summary>Minimal <see cref="IConnectionState"/>; only IsConnected is interesting (raises PropertyChanged).</summary>
internal sealed class FakeConnectionState : IConnectionState
{
    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected))); }
    }

    /// <summary>
    /// Whose daemon this pretends to be (#742). Defaults to <see cref="DaemonOwnership.Owned"/> so tests
    /// exercise the normal case; set it to check a guard's behaviour on someone else's daemon or on one
    /// that couldn't be identified.
    ///
    /// It used to return false for both ownership booleans, which is the Unknown state — so every
    /// <c>!IsForeignDaemon</c> path was being tested as if OTA owned the daemon, which is precisely the
    /// conflation #742 fixed. The tests agreed with the bug.
    /// </summary>
    public DaemonOwnership Ownership
    {
        get => _ownership;
        set
        {
            _ownership = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ownership)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAppOwnedDaemon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsForeignDaemon)));
        }
    }
    private DaemonOwnership _ownership = DaemonOwnership.Owned;

    /// <summary>
    /// The daemon is somewhere the app manages, but is not the selected one (#882).
    /// </summary>
    /// <remarks>
    /// Raises its change notification, because the real one does and because a listener that reacts to
    /// it is exactly what is under test. As a silent auto-property this fake would have made a card that
    /// never re-evaluates look correct.
    /// </remarks>
    public bool DaemonIsManagedButNotSelected
    {
        get => _daemonIsManagedButNotSelected;
        set
        {
            _daemonIsManagedButNotSelected = value;
            PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(DaemonIsManagedButNotSelected)));
        }
    }
    private bool _daemonIsManagedButNotSelected;

    public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";
    public bool IsDaemonRunning => _isConnected;
    public bool IsAppOwnedDaemon => Ownership == DaemonOwnership.Owned;
    public bool IsForeignDaemon => Ownership == DaemonOwnership.External;
    /// <summary>Which daemon is answering. Raises a change, because the page's driver card is computed
    /// from it and a silent setter cannot show that the card fails to follow (#936).</summary>
    public string DaemonSourcePath
    {
        get => _daemonSourcePath;
        set { _daemonSourcePath = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DaemonSourcePath))); }
    }
    private string _daemonSourcePath = "";
    /// <summary>The version the pretend daemon reports. The one this build ships with by default, so a
    /// test that is not about the version rule is not refused by it (#1042); set it to exercise the rule.</summary>
    public string DaemonVersion { get; set; } = HealthService.ExpectedOtdVersion;
    public bool HasDaemonVersion => DaemonVersion.Length > 0;

    // Settable and notifying, like the rest the daemon page is derived from: a silent setter could not
    // show a page that fails to follow a refusal (#1042).
    private bool _isDaemonRefused;
    public bool IsDaemonRefused
    {
        get => _isDaemonRefused;
        set => Set(ref _isDaemonRefused, value, nameof(IsDaemonRefused));
    }

    private string _daemonRefusalReason = "";
    public string DaemonRefusalReason
    {
        get => _daemonRefusalReason;
        set => Set(ref _daemonRefusalReason, value, nameof(DaemonRefusalReason));
    }

    private string _daemonVersionNote = "";
    public string DaemonVersionNote
    {
        get => _daemonVersionNote;
        set
        {
            Set(ref _daemonVersionNote, value, nameof(DaemonVersionNote));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDaemonVersionNote)));
        }
    }
    public bool HasDaemonVersionNote => _daemonVersionNote.Length > 0;
    public bool SaveFailed => false;
    public string SaveStatusText => "";
    public bool ShowAppOwnedDaemon => false;

    // Settable, and notifying, because the daemon page is derived from them: a silent auto-property here
    // would let a view model that never hears about a change look correct (#900).
    private bool _showForeignDaemonWarning;
    public bool ShowForeignDaemonWarning
    {
        get => _showForeignDaemonWarning;
        set => Set(ref _showForeignDaemonWarning, value, nameof(ShowForeignDaemonWarning));
    }

    private bool _hasBundledDaemon;
    public bool HasBundledDaemon
    {
        get => _hasBundledDaemon;
        set => Set(ref _hasBundledDaemon, value, nameof(HasBundledDaemon));
    }

    private bool _connectStalled;
    public bool ConnectStalled
    {
        get => _connectStalled;
        set => Set(ref _connectStalled, value, nameof(ConnectStalled));
    }

    private bool _showDaemonActivity;
    public bool ShowDaemonActivity
    {
        get => _showDaemonActivity;
        set => Set(ref _showDaemonActivity, value, nameof(ShowDaemonActivity));
    }

    private string _daemonActivityText = "";
    public string DaemonActivityText
    {
        get => _daemonActivityText;
        set => Set(ref _daemonActivityText, value, nameof(DaemonActivityText));
    }

    private string _discardedChangeNotice = "";
    public string DiscardedChangeNotice
    {
        get => _discardedChangeNotice;
        set
        {
            _discardedChangeNotice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscardedChangeNotice)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDiscardedChangeNotice)));
        }
    }

    /// <summary>Derived, as the real one is: a test cannot set "there is a notice" without a notice.</summary>
    public bool HasDiscardedChangeNotice => !string.IsNullOrEmpty(_discardedChangeNotice);

    private string _settingsRefreshedNotice = "";
    public string SettingsRefreshedNotice
    {
        get => _settingsRefreshedNotice;
        set
        {
            _settingsRefreshedNotice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SettingsRefreshedNotice)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSettingsRefreshedNotice)));
        }
    }

    /// <summary>Derived for the same reason.</summary>
    public bool HasSettingsRefreshedNotice => !string.IsNullOrEmpty(_settingsRefreshedNotice);

    public bool HasSettingsNotice => HasDiscardedChangeNotice || HasSettingsRefreshedNotice;

    public IRelayCommand DismissSettingsNoticesCommand => new RelayCommand(() =>
    {
        DiscardedChangeNotice = "";
        SettingsRefreshedNotice = "";
    });

    /// <summary>How many times each was asked for, so a test can assert which one a refresh chose.</summary>
    public int Reloads { get; private set; }
    public int Connects { get; private set; }

    public Task ReloadAsync()
    {
        Reloads++;
        return Task.CompletedTask;
    }

    public Task ConnectAsync()
    {
        Connects++;
        return Task.CompletedTask;
    }

    public int Refreshes { get; private set; }

    /// <summary>The real one decides between reload, reconnect and neither (#912); this only records
    /// that the page asked, because the deciding is what <c>AppSession</c>'s own tests cover.</summary>
    public Task RefreshAsync()
    {
        Refreshes++;
        return Task.CompletedTask;
    }

    private void Set<T>(ref T field, T value, string name)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public bool HasDaemonSourcePath => false;
    public bool DaemonCannotOpenTablet => false;
    public bool ShowDaemonSourceUnknown => false;
    public bool CanStartDaemon => !_isConnected;
    public bool IsDaemonExeMissing => false;
    public bool ShowStartButton => !_isConnected;
    public string DaemonStatusText => ConnectionStatus;
    public bool IsDaemonBusy => false;
    public string DaemonOperationStatus => "";
    public string DaemonOperationError => "";
    public bool HasDaemonOperationError => false;
    public IAsyncRelayCommand StartDaemonCommand => null!;
    public IAsyncRelayCommand StopDaemonCommand => null!;
    public IAsyncRelayCommand RestartDaemonCommand => null!;
    public IRelayCommand LaunchOtdUxCommand => null!;
    public bool CanLaunchOtdUx => false;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The shared <see cref="ISettingsCoordinator"/> stand-in. Four near-identical copies of this used to
/// live in four test files, so every change to the interface meant editing all of them — which is what
/// #734, #737 and #740 each had to do in turn. Records what was applied and how, and lets a test script
/// a failure.
/// </summary>
internal sealed class FakeSettingsCoordinator : ISettingsCoordinator
{
    public Settings? CurrentSettings { get; set; }
    public Settings? Applied { get; private set; }
    public int ApplyCalls { get; private set; }
    public int RestoreCalls { get; private set; }
    public SettingsApplyOutcome ApplyResult { get; set; } = SettingsApplyOutcome.Live;
    public SettingsRestoreOutcome RestoreResult { get; set; } = SettingsRestoreOutcome.Restored;
    public bool DaemonAccepts { get; set; } = true;
    public bool Changes { get; set; } = true;
    public bool WouldChange(Settings settings) => Changes;

    public Task<SettingsApplyOutcome> ApplySettingsAsync(Settings settings)
    {
        ApplyCalls++;
        Applied = settings;
        return Task.FromResult(DaemonAccepts ? ApplyResult : SettingsApplyOutcome.Disconnected);
    }
    public Task<SettingsRestoreOutcome> RestoreDefaultAsync()
    {
        RestoreCalls++;
        return Task.FromResult(RestoreResult);
    }
}
