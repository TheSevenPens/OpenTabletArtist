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

    /// <summary>Settings and profile from one object, as the real factory now guarantees.</summary>
    public OpenTabletArtist.ViewModels.TabletDetailViewModel? CreateTabletDetail(
        string tabletName, Func<Task> onForget, Action? openConfigsPage = null)
    {
        var settings = new Settings
        {
            Profiles = new OpenTabletDriver.Desktop.Profiles.ProfileCollection
            {
                new Profile { Tablet = tabletName },
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
    public string DaemonSourcePath => "";
    public string DaemonVersion => "";
    public bool HasDaemonVersion => false;
    public bool ShowSaveStatus => false;
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

    /// <summary>What the caller said it had taken, or none if it has said nothing (#910).</summary>
    public SettingsStamp Accepted { get; private set; } = SettingsStamp.None;

    /// <summary>What this fake is publishing, so a test can make an acceptance stale.</summary>
    public SettingsStamp CurrentStamp { get; set; } = new(1, 1);

    /// <summary>
    /// Settings and stamp as one publication, and a hook to make a reload land mid-read (#910).
    /// </summary>
    /// <remarks>
    /// The hook is the point. A fake that simply returned today's two values could not tell a caller
    /// that reads them together from one that reads them twice — which is the whole difference this is
    /// here to observe. <see cref="WhileReadingPublication"/> runs between the two reads a split caller
    /// would make, so a test can advance this fake exactly where the race lives.
    /// </remarks>
    public Action? WhileReadingPublication { get; set; }

    public PreparedSettings? CurrentPublication
    {
        get
        {
            var settings = CurrentSettings;
            WhileReadingPublication?.Invoke();
            return settings is null ? null : new PreparedSettings(settings, CurrentStamp);
        }
    }

    /// <summary>
    /// Refuses a stale acceptance the way the real one does. A fake that accepted anything would let a
    /// view model name a snapshot nobody is showing and still look right (#910).
    /// </summary>
    public bool AcceptCurrentSettings(SettingsStamp accepted)
    {
        // Exact equality, as the real one does since #910: a stamp naming a version this session has
        // not reached is superseded by nothing, and a fake that took it would hide that.
        if (accepted.IsNone || accepted != CurrentStamp) return false;

        Accepted = accepted;
        return true;
    }

    /// <summary>The conflict a test's overwrite was authorised with, or null if none was (#906).</summary>
    public SettingsConflict? OverwroteWith { get; private set; }

    /// <summary>What an overwrite answers. Defaults to success, which is the case worth defaulting to.</summary>
    public SettingsApplyOutcome OverwriteResult { get; set; } = SettingsApplyOutcome.Saved;

    public Task<SettingsApplyOutcome> OverwriteSettingsAsync(Settings settings, SettingsConflict conflict)
    {
        // Recorded rather than silently accepted: a fake that took an overwrite the same way it takes an
        // apply would let a view model authorise one it never had, and look right doing it.
        OverwroteWith = conflict;
        SaveCalls++;
        Applied = SavedAndApplied = settings;
        HasEphemeralOverride = false;
        return Task.FromResult(OverwriteResult);
    }

    /// <summary>The hold a test's resubmission presented, or null if nothing has been resubmitted (#906).</summary>
    public SettingsHold? ResubmittedUnder { get; private set; }

    /// <summary>How many submissions arrived carrying a hold, to tell a resubmission from an apply.</summary>
    public int ResubmitCalls { get; private set; }

    public Task<SettingsApplyOutcome> ResubmitSettingsAsync(Settings settings, SettingsHold held)
    {
        // A resubmission is recorded as itself. Answering it exactly like an ordinary apply would hide
        // the difference these tests exist to observe — which hold, if any, the editor presented.
        ResubmittedUnder = held;
        ResubmitCalls++;
        return ApplyAndSaveSettingsAsync(settings);
    }

    /// <summary>Whether the daemon takes the change. False models no transport — the apply paths then
    /// report failure and commit nothing (#766).</summary>
    public bool DaemonAccepts { get; set; } = true;

    public Task<SettingsApplyOutcome> ApplyLiveOnlyAsync(Settings settings)
    {
        LiveOnlyCalls++;
        if (!DaemonAccepts) return Task.FromResult(SettingsApplyOutcome.Disconnected);
        Applied = settings;
        HasEphemeralOverride = false;
        return Task.FromResult(SettingsApplyOutcome.Live);
    }

    public Task<SettingsApplyOutcome> ApplyEphemeralAsync(Settings settings)
    {
        EphemeralCalls++;
        if (ThrowOnEphemeral != null) throw ThrowOnEphemeral;
        if (!DaemonAccepts) return Task.FromResult(SettingsApplyOutcome.Disconnected);
        Applied = settings;
        HasEphemeralOverride = true;
        return Task.FromResult(SettingsApplyOutcome.Live);
    }

    public Task<SettingsApplyOutcome> ClearEphemeralOverrideAsync()
    {
        ClearCalls++;
        if (ThrowOnClear != null) throw ThrowOnClear;
        if (!DaemonAccepts) return Task.FromResult(SettingsApplyOutcome.Disconnected);
        HasEphemeralOverride = false;
        return Task.FromResult(SettingsApplyOutcome.Live);
    }

    public Task<SettingsRestoreOutcome> RestoreDefaultAsync()
    {
        RestoreCalls++;
        return Task.FromResult(RestoreResult);
    }
}
