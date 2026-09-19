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

    public OpenTabletArtist.ViewModels.TabletDetailViewModel CreateTabletDetail(Profile profile, Func<Task> onForget, Action? openConfigsPage = null)
        => new(profile, null);

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(ConfirmResult);

    public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
        => Task.FromResult(InputResult);

    public Task<HotkeyChord?> ShowHotkeyCaptureAsync(HotkeyChord? initial = null) => Task.FromResult(HotkeyResult);

    /// <summary>Result returned by <see cref="ShowProcessPickerAsync"/> (default null = cancelled).</summary>
    public OpenTabletArtist.Domain.AppIdentity? ProcessPickerResult { get; set; }
    public Task<OpenTabletArtist.Domain.AppIdentity?> ShowProcessPickerAsync() => Task.FromResult(ProcessPickerResult);

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
    public string SettingsFilePath => "";
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

    public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";
    public bool IsDaemonRunning => _isConnected;
    public bool IsAppOwnedDaemon => Ownership == DaemonOwnership.Owned;
    public bool IsForeignDaemon => Ownership == DaemonOwnership.External;
    public string DaemonSourcePath => "";
    public string DaemonVersion => "";
    public bool HasDaemonVersion => false;
    public bool ShowSaveStatus => false;
    public bool SaveFailed => false;
    public string SaveStatusText => "";
    public bool ShowAppOwnedDaemon => false;
    public bool ShowForeignDaemonWarning => false;
    public bool HasBundledDaemon => false;
    public bool CanSwitchToBundledDaemon => false;
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

    /// <summary>The last settings handed to ANY apply path.</summary>
    public Settings? Applied { get; private set; }

    /// <summary>The last settings handed to <see cref="ApplyAndSaveSettingsAsync"/> specifically — the
    /// only path that persists, so tests that care about persistence use this rather than
    /// <see cref="Applied"/>.</summary>
    public Settings? SavedAndApplied { get; private set; }

    public int SaveCalls { get; private set; }
    public int LiveOnlyCalls { get; private set; }
    public int EphemeralCalls { get; private set; }
    public int ClearCalls { get; private set; }
    public int RestoreCalls { get; private set; }

    /// <summary>What <see cref="ApplyAndSaveSettingsAsync"/> reports.</summary>
    public SettingsApplyOutcome ApplyResult { get; set; } = SettingsApplyOutcome.Saved;

    /// <summary>What <see cref="RestoreDefaultAsync"/> reports. Set a failure to exercise the
    /// "override is still active" path (#734).</summary>
    public SettingsRestoreOutcome RestoreResult { get; set; } = SettingsRestoreOutcome.Restored;

    /// <summary>When set, the matching call throws — a reachable daemon that refused the change.</summary>
    public Exception? ThrowOnEphemeral { get; set; }
    public Exception? ThrowOnClear { get; set; }

    public bool HasEphemeralOverride { get; private set; }

    public Task<SettingsApplyOutcome> ApplyAndSaveSettingsAsync(Settings settings)
    {
        SaveCalls++;
        Applied = SavedAndApplied = settings;
        HasEphemeralOverride = false;
        return Task.FromResult(ApplyResult);
    }

    /// <summary>Whether the daemon takes the change. False models no transport — the apply paths then
    /// report failure and commit nothing (#766).</summary>
    public bool DaemonAccepts { get; set; } = true;

    public Task<bool> ApplyLiveOnlyAsync(Settings settings)
    {
        LiveOnlyCalls++;
        if (!DaemonAccepts) return Task.FromResult(false);
        Applied = settings;
        HasEphemeralOverride = false;
        return Task.FromResult(true);
    }

    public Task<bool> ApplyEphemeralAsync(Settings settings)
    {
        EphemeralCalls++;
        if (ThrowOnEphemeral != null) throw ThrowOnEphemeral;
        if (!DaemonAccepts) return Task.FromResult(false);
        Applied = settings;
        HasEphemeralOverride = true;
        return Task.FromResult(true);
    }

    public Task<bool> ClearEphemeralOverrideAsync()
    {
        ClearCalls++;
        if (ThrowOnClear != null) throw ThrowOnClear;
        if (!DaemonAccepts) return Task.FromResult(false);
        HasEphemeralOverride = false;
        return Task.FromResult(true);
    }

    public Task<SettingsRestoreOutcome> RestoreDefaultAsync()
    {
        RestoreCalls++;
        return Task.FromResult(RestoreResult);
    }
}
