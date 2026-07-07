using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for a single tablet's settings — the tabbed editor (Screen Mapping, Pen Switches,
/// ExpressKeys, Dynamics, Hover, Filters, JSON). Hosted either as an in-app page (the Tablets nav)
/// or in the focused Pen Dynamics dialog the tray opens. Delegate-based (apply / refresh / detection
/// probe / live pen input / calibrate) so the host wires it to the session.
/// </summary>
public partial class TabletDetailViewModel : ObservableObject, IDisposable
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string WinInkRelativeModePath = "VoiDPlugins.OutputMode.WinInkRelativeMode";

    private Profile _profile;
    private Settings? _settings;
    private readonly Func<Settings, Task>? _applyAction;
    // Opens the modal binding editor for a card's current binding + label; returns the chosen binding
    // (or Unbound on Clear), or null on Cancel. Provided by the host that has the owner window.
    private readonly Func<AuxBinding, string, Task<AuxBinding?>>? _editBinding;
    // Detection source — when provided, the banner + tablet-dependent actions live-update on each
    // data load (tablet plug/unplug, #177). Unsubscribed in Dispose so a cached page VM doesn't leak.
    private readonly IDeviceData? _deviceData;
    // Removes this tablet's saved profile (wired by the page host). Null in the tray dialog.
    private readonly Func<Task>? _forgetAction;
    // Opens the calibration overlay for the chosen options; the host supplies the owner window.
    // Null when calibration isn't available (the focused Pen Dynamics dialog hides Screen Mapping).
    private readonly Func<CalibrationOptions, Task>? _onCalibrate;
    // Returns the freshly-reloaded settings together with this tablet's profile from within them, so
    // the VM can keep _settings and _profile coherent (the profile is a reference inside the settings).
    private readonly Func<Task<(Settings? Settings, Profile? Profile)>>? _refreshAction;
    // Probes whether this tablet is currently detected/connected (re-checked on open and on Refresh).
    private readonly Func<bool>? _isDetectedProbe;
    private readonly (float Width, float Height)? _tabletDigitizer;

    // External-change reconciliation: the same app-owned daemon can be edited by another client
    // (notably the OTD UX changing a mapping). The daemon pushes no "settings changed" event on a
    // successful apply, so the shell re-pulls (on window activation, TabletsChanged, and the poll) and
    // calls ReconcileExternalChange. When a reload diverges from what this editor holds we adopt it
    // silently — unless the user has an unsaved edit, where we stash it and surface a banner instead
    // of discarding their in-progress change.
    private Settings? _pendingExternalSettings;
    private Profile? _pendingExternalProfile;

    /// <summary>Non-empty when the daemon's settings changed outside OTA while the user had an unsaved
    /// edit here; drives a header banner with a Reload action. Empty otherwise.</summary>
    [ObservableProperty] private string _externalChangeText = "";
    public bool HasExternalChange => !string.IsNullOrEmpty(ExternalChangeText);
    partial void OnExternalChangeTextChanged(string value) => OnPropertyChanged(nameof(HasExternalChange));

    [ObservableProperty] private IReadOnlyList<DisplayInfo> _displays = [];
    [ObservableProperty] private int? _selectedDisplayNumber;

    /// <summary>A display is selected, so "Apply" can map to it.</summary>
    public bool CanApplyDisplay => SelectedDisplayNumber != null && _applyAction != null;

    /// <summary>How the stored display mapping relates to the connected monitors, so the tab can flag an
    /// off-screen or custom mapping instead of silently rendering it. Recomputed via
    /// <see cref="RefreshTabletArea"/> on every mapping/display change.</summary>
    public DisplayMappingValidity MappingValidity => DisplayMappingApplier.ClassifyMapping(_profile, Displays);
    public bool ShowMappingOffScreen => MappingValidity == DisplayMappingValidity.OffScreen;
    public bool ShowMappingCustom => MappingValidity == DisplayMappingValidity.Custom;
    public string MappingValidityText => MappingValidity switch
    {
        DisplayMappingValidity.OffScreen =>
            "This tablet's mapped area extends beyond your displays — part of the tablet maps to off-screen " +
            "space, so the pen reaches dead zones there. Pick a display below and Apply mapping to fix it.",
        DisplayMappingValidity.Custom =>
            "This tablet isn't mapped to a single whole display (a custom or multi-display area). Pick a " +
            "display below and Apply mapping for a standard, undistorted 1:1 setup.",
        _ => "",
    };

    /// <summary>The selected display differs from the one currently applied, so the change still needs
    /// "Apply mapping" — drives the pending hint so it's obvious the selection isn't live yet (#179
    /// follow-up). Suppressed during the initial load so an as-opened profile doesn't read as pending
    /// before the user changes anything.</summary>
    [ObservableProperty] private bool _mappingChangePending;
    private bool _suppressMappingPending;

    private void RecomputeMappingPending() =>
        MappingChangePending = _applyAction != null
            && SelectedDisplayNumber != null
            && SelectedDisplayNumber != CurrentlyMappedNumber();

    partial void OnSelectedDisplayNumberChanged(int? value)
    {
        OnPropertyChanged(nameof(CanApplyDisplay));
        ApplyDisplayCommand.NotifyCanExecuteChanged();
        if (!_suppressMappingPending) RecomputeMappingPending();
    }

    partial void OnIsWinInkAbsoluteChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAbsoluteMode)); // keep the Absolute/Relative toggle in sync
        OnPropertyChanged(nameof(CanCalibrate));   // calibration needs an Absolute mode (#127)
        OnPropertyChanged(nameof(CanRunCalibration));
        OnPropertyChanged(nameof(ShowConnectToCalibrateHint));
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkAbsoluteModePath);
    }

    // --- Pointer calibration entry point (#127) ---

    /// <summary>Calibration corrects an Absolute mapping, so the calibration UI is only shown in an
    /// Absolute mode. (Whether it can actually run also needs a live tablet — see
    /// <see cref="CanRunCalibration"/>.)</summary>
    public bool CanCalibrate => IsWinInkAbsolute;

    /// <summary>Calibration captures live pen taps, so it additionally needs the tablet connected
    /// (#177) and a host that can open the overlay. Gates the Calibrate button so it can't be clicked
    /// into the "not detected" dead-end, and flips live as the tablet is (un)plugged.</summary>
    public bool CanRunCalibration => CanCalibrate && IsTabletDetected && _onCalibrate != null;

    /// <summary>In an Absolute mode but the tablet isn't connected, so calibration is shown but
    /// disabled — prompt the user to connect it (#177).</summary>
    public bool ShowConnectToCalibrateHint => CanCalibrate && !IsTabletDetected;

    /// <summary>Which display the calibration overlay opens on — the tablet's currently-mapped display —
    /// so the user knows which screen to watch. Recomputed whenever the mapping/displays change (via
    /// <see cref="RefreshTabletArea"/>).</summary>
    public string CalibrationDisplayText
    {
        get
        {
            var mapped = DisplayMappingApplier.CurrentlyMapped(_profile, Displays);
            return mapped != null
                ? $"Calibration opens on Display {mapped.Number} ({mapped.Name}) — where your tablet is mapped."
                : "Calibration opens on the display your tablet is mapped to.";
        }
    }

    /// <summary>Calibration capture presets: the corner method (→ homography, #195) or a finer grid
    /// (→ bilinear offsets, #196). Each backs a calibration card whose START button begins that mode.</summary>
    public IReadOnlyList<CalibrationModeChoice> CalibrationModeChoices { get; } = new List<CalibrationModeChoice>
    {
        new("4 point", CalibrationMode.Corners, 0, 0),
        new("9 point", CalibrationMode.Grid, 3, 3),
        new("25 point", CalibrationMode.Grid, 5, 5),
    };

    // Point count of the calibration currently applied to this profile (0 = none), so each card can show
    // whether it's the one in use.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent4Point))]
    [NotifyPropertyChangedFor(nameof(IsCurrent9Point))]
    [NotifyPropertyChangedFor(nameof(IsCurrent25Point))]
    private int _currentCalibrationPoints;

    public bool IsCurrent4Point => CurrentCalibrationPoints == 4;
    public bool IsCurrent9Point => CurrentCalibrationPoints == 9;
    public bool IsCurrent25Point => CurrentCalibrationPoints == 25;

    /// <summary>Start calibrating in the chosen mode — each calibration card's START button passes its
    /// <see cref="CalibrationModeChoice"/>. Reloads afterward so the status + stale hint stay coherent (#147).</summary>
    [RelayCommand]
    private async Task StartCalibration(CalibrationModeChoice? choice)
    {
        if (_onCalibrate == null || choice == null) return;
        await _onCalibrate(choice.ToOptions());
        await Refresh();
    }

    /// <summary>True when a calibration exists but was captured against a different area mapping than
    /// the current one — it may no longer be accurate, so suggest recalibrating (#147).</summary>
    [ObservableProperty] private bool _calibrationStale;

    /// <summary>An enabled calibration is active on this profile (drives the status indicator).</summary>
    [ObservableProperty] private bool _isCalibrated;
    /// <summary>Human-readable calibration state incl. the mode used ("Not calibrated" / "Calibrated — …").</summary>
    [ObservableProperty] private string _calibrationStatusText = "Not calibrated";

    public bool CanClearCalibration => IsCalibrated && _applyAction != null;
    partial void OnIsCalibratedChanged(bool value) => OnPropertyChanged(nameof(CanClearCalibration));

    private void RefreshCalibrationStatus()
    {
        var cal = CalibrationProfile.ReadProfile(_profile);
        CalibrationStale = CalibrationProfile.IsStale(cal, CurrentMappingFingerprint());
        IsCalibrated = cal is { Enabled: true };
        CalibrationStatusText = !IsCalibrated
            ? "Not calibrated"
            : cal!.Model switch
            {
                CalibrationProfile.CalibrationModel.Homography => "Calibrated — 4 point (perspective)",
                CalibrationProfile.CalibrationModel.Grid => $"Calibrated — {(cal.Grid?.Cols ?? 0) * (cal.Grid?.Rows ?? 0)} point",
                _ => "Calibrated — 4 point (legacy)",
            };
        CurrentCalibrationPoints = !IsCalibrated ? 0 : cal!.Model switch
        {
            CalibrationProfile.CalibrationModel.Homography => 4,
            CalibrationProfile.CalibrationModel.Grid => (cal.Grid?.Cols ?? 0) * (cal.Grid?.Rows ?? 0),
            _ => 4,
        };
    }

    /// <summary>Remove the calibration filter, returning the pointer to its uncorrected default.</summary>
    [RelayCommand]
    private async Task ClearCalibration()
        => await ApplySettingsChange(p => CalibrationProfile.Clear(_settings, p.Tablet ?? ""));

    /// <summary>Fingerprint of the profile's current Absolute mapping (input + output area + mapped
    /// display), matching how <see cref="CalibrationProfile.Fingerprint"/> was written at calibration.</summary>
    private string? CurrentMappingFingerprint()
    {
        var abs = _profile.AbsoluteModeSettings;
        if (abs?.Tablet is not { } t || abs.Display is not { } d || CurrentlyMappedNumber() is not { } num)
            return null;
        var input = new MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation);
        var output = new MappingArea(d.X, d.Y, d.Width, d.Height);
        return CalibrationProfile.Fingerprint(input, output, num);
    }

    partial void OnIsWinInkRelativeChanged(bool value)
    {
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkRelativeModePath);
    }

    /// <summary>The Output Mode toggle: on = Windows Ink Absolute, off = Windows Ink Relative.
    /// Setting it switches the mode (also fixing a non-Windows-Ink mode to Windows Ink).</summary>
    public bool IsAbsoluteMode
    {
        get => IsWinInkAbsolute;
        set => _ = SetOutputMode(value ? WinInkAbsoluteModePath : WinInkRelativeModePath);
    }

    private async Task SetOutputMode(string path)
    {
        await ApplySettingsChange(p =>
        {
            p.OutputMode ??= new PluginSettingStore(path, true);
            p.OutputMode.Path = path;
        });
        if (path.Contains("WinInk", StringComparison.OrdinalIgnoreCase))
            WinInkAutoOptOut.Clear(_profile.Tablet);
        else
            WinInkAutoOptOut.OptOut(_profile.Tablet);
    }

    public TabletDetailViewModel(Profile profile, Settings? settings,
        Func<Settings, Task>? applyAction = null,
        Func<Task<(Settings? Settings, Profile? Profile)>>? refreshAction = null,
        (float Width, float Height)? tabletDigitizer = null,
        IDaemonDebugSession? penInput = null,
        Func<bool>? isDetected = null,
        bool dynamicsOnly = false,
        IDeviceData? deviceData = null,
        Func<Task>? forgetAction = null,
        Func<CalibrationOptions, Task>? onCalibrate = null,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding = null)
    {
        _profile = profile;
        _settings = settings;
        _applyAction = applyAction;
        _editBinding = editBinding;
        _refreshAction = refreshAction;
        _isDetectedProbe = isDetected;
        _tabletDigitizer = tabletDigitizer;
        _deviceData = deviceData;
        _forgetAction = forgetAction;
        _onCalibrate = onCalibrate;
        DynamicsOnly = dynamicsOnly;

        if (penInput != null)
        {
            _penInput = new DaemonPenInputSource(penInput, AcceptDeviceReportForThisTablet);
            _penInput.Sample += OnPenSample;
            _penInput.AuxButtons += OnAuxButtons;
            _penInput.WheelButtons += OnWheelButtons;
            _penInput.WheelPositions += OnWheelPositions;
            _penInput.WheelDeltas += OnWheelDeltas;
        }

        // Live-refresh the detection banner + tablet-dependent actions as tablets connect/disconnect
        // while this view is open (#177, via the session's DataLoaded after a TabletsChanged push #170).
        if (_deviceData != null)
            _deviceData.DataLoaded += RefreshDetectionStatus;

        // Show/hide the developer-only Filters/JSON tabs live as their Developer-tab toggles change.
        DeveloperSettings.Instance.PropertyChanged += OnDeveloperSettingsChanged;

        TabletName = profile.Tablet ?? "Unknown Tablet";
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        Displays = DisplayEnumerator.Enumerate();
        LoadAuxEnabledState();
        LoadWheelEnabledState();
        RefreshFromProfile();
        RefreshDetectionStatus();
        // Highlight the display the tablet is currently mapped to (else the primary). Suppress the
        // pending flag for this initial, programmatic selection so it doesn't open "pending".
        _suppressMappingPending = true;
        SelectedDisplayNumber = DefaultSelectedDisplay();
        _suppressMappingPending = false;
    }

    /// <summary>Show the Filters tab — a developer-only view of the profile's raw filters, hidden unless
    /// enabled on Advanced → Developer (users never need it).</summary>
    public bool ShowFiltersTab => DeveloperSettings.Instance.ShowFiltersTab;
    /// <summary>Show the JSON tab — the raw settings JSON, hidden unless enabled on Advanced → Developer.</summary>
    public bool ShowJsonTab => DeveloperSettings.Instance.ShowJsonTab;

    private void OnDeveloperSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeveloperSettings.ShowFiltersTab)) OnPropertyChanged(nameof(ShowFiltersTab));
        else if (e.PropertyName == nameof(DeveloperSettings.ShowJsonTab)) OnPropertyChanged(nameof(ShowJsonTab));
    }

    // Parameterless constructor for design-time
    public TabletDetailViewModel()
    {
        _profile = new Profile();
        TabletName = "Design Tablet";
        Displays = [];
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private static string GetBindingName(PluginSettingStore? store) =>
        PenSwitchBinding.GetBindingLabel(store, GetPluginFriendlyName);

    /// <summary>Set when an in-page Refresh finds this tablet's profile gone (unplugged/removed since
    /// it was opened); surfaced as a header warning. Cleared on a successful refresh.
    /// (#124 / Cursor review on #125)</summary>
    [ObservableProperty] private string? _refreshWarning;

    /// <summary>When true, this is the focused Pen Dynamics editor: the tab bar is hidden and only the
    /// Dynamics content shows (#133). The Dynamics tab is preselected by the view.</summary>
    [ObservableProperty] private bool _dynamicsOnly;

    // --- Tablet detected/connected banner (#132) ---

    /// <summary>True when this tablet is the currently-connected one (green check vs. amber warning).</summary>
    [ObservableProperty] private bool _isTabletDetected;
    /// <summary>Banner text describing the detection state.</summary>
    [ObservableProperty] private string _detectionText = "";

    partial void OnIsTabletDetectedChanged(bool value)
    {
        // Tablet-dependent actions follow the live detection state (#177).
        OnPropertyChanged(nameof(CanRunCalibration));
        OnPropertyChanged(nameof(ShowConnectToCalibrateHint));
    }

    /// <summary>Re-evaluate the detection banner + tablet-dependent actions from the current session
    /// state. Called on open, on manual Refresh, and live whenever the daemon reports a tablet
    /// add/remove while the page stays open (#177, driven by the #170 TabletsChanged signal).</summary>
    public void RefreshDetectionStatus()
    {
        IsTabletDetected = _isDetectedProbe?.Invoke() ?? false;
        DetectionText = IsTabletDetected
            ? "Detected"
            : "Not currently detected — showing this tablet's saved settings.";
        // Detection changing is exactly when the digitizer specs (dis)appear, so recompute the active
        // area too. This self-heals "Active-area details aren't available" on reconnect — no restart.
        RefreshTabletArea();
        RefreshAbout(); // the ABOUT facts come from the same specs, so recover them on reconnect too
    }

    /// <summary>Rebuild the ABOUT tab's fact list from the tablet's live specs (empty when not detected).</summary>
    private void RefreshAbout()
    {
        var info = _deviceData != null ? TabletAboutInfo.From(_deviceData.Tablets, TabletName) : null;
        TabletFacts = info != null ? BuildFacts(info) : System.Array.Empty<TabletFact>();
    }

    /// <summary>The tablet's spec read-out for the ABOUT tab. Each row is included only when known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabletFacts))]
    private IReadOnlyList<TabletFact> _tabletFacts = System.Array.Empty<TabletFact>();

    public bool HasTabletFacts => TabletFacts.Count > 0;

    private static IReadOnlyList<TabletFact> BuildFacts(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (!string.IsNullOrEmpty(a.Name)) facts.Add(new("Name", a.Name));
        if (a.WidthMm > 0 && a.HeightMm > 0)
        {
            facts.Add(new("Active area",
                $"{a.WidthMm:0.#} × {a.HeightMm:0.#} mm  ({a.WidthMm / 25.4:0.0} × {a.HeightMm / 25.4:0.0} in)"));
            double diag = System.Math.Sqrt(a.WidthMm * a.WidthMm + a.HeightMm * a.HeightMm);
            facts.Add(new("Active area diagonal", $"{diag:0.#} mm  ({diag / 25.4:0.0} in)"));
            facts.Add(new("Active area aspect ratio", TabletAboutInfo.FormatAspectRatio(a.WidthMm, a.HeightMm)));
        }
        if (a.LpMm is > 0 && a.Lpi is > 0)
            facts.Add(new("Digitizer resolution", $"{a.LpMm:N0} LPmm ({a.Lpi:N0} LPI)"));
        if (a.MaxPressure is > 0) facts.Add(new("Pressure levels", $"{a.MaxPressure:N0}"));
        if (a.PenButtons is { } pb) facts.Add(new("Pen buttons", pb.ToString()));
        if (a.ExpressKeys is > 0) facts.Add(new("Buttons", a.ExpressKeys!.Value.ToString()));
        if (a.MouseButtons is > 0) facts.Add(new("Mouse buttons", a.MouseButtons!.Value.ToString()));
        if (a.WheelCount > 0) facts.Add(new("Touch ring / wheel", a.WheelCount == 1 ? "Yes" : a.WheelCount.ToString()));
        if (a.StripCount > 0) facts.Add(new("Touch strips", a.StripCount.ToString()));
        if (a.HasTouch) facts.Add(new("Touch input", "Supported"));
        if (a.VendorId is { } vid && a.ProductId is { } pid)
            facts.Add(new("USB ID (VID:PID)", $"{vid:X4}:{pid:X4}"));
        return facts;
    }

    /// <summary>Open OpenTabletDriver's supported-tablets page (the ABOUT tab's Resources link).</summary>
    [RelayCommand]
    private void OpenSupportedTabletsPage()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("https://opentabletdriver.net/Tablets") { UseShellExecute = true });
        }
        catch { /* best-effort; nothing to recover if no browser is available */ }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_refreshAction == null) return;
        var (settings, profile) = await _refreshAction();
        if (profile == null)
        {
            // The tablet/profile is gone (unplugged or removed since it was opened). Keep showing the
            // last-known data rather than blanking it, but warn it may be stale.
            RefreshWarning = "This tablet is no longer detected — showing the last known settings.";
            RefreshDetectionStatus();
            return;
        }

        if (settings == null) { RefreshDetectionStatus(); return; }
        AdoptProfile(settings, profile);
    }

    /// <summary>Point the editor at a freshly-loaded settings/profile pair (from a manual Refresh or an
    /// external-change reload) and rebuild every bound view from it. Both are reassigned together so
    /// later edits push the same settings object the profile lives in — otherwise persists would mutate
    /// stale settings (#124).</summary>
    private void AdoptProfile(Settings settings, Profile profile)
    {
        _settings = settings;
        _profile = profile;
        // The profile must be a live reference inside the settings we now persist through; if a future
        // source returns a detached profile, edits would silently write elsewhere.
        Debug.Assert(settings.Profiles.Contains(profile),
            "Adopted profile must be a reference inside the adopted settings (#124).");
        RefreshWarning = null;
        ClearExternalChange();
        RefreshFromProfile();
        // Move the display picker to the now-current mapping (suppress the pending flag for this
        // programmatic selection so it doesn't read as an unapplied change).
        _suppressMappingPending = true;
        SelectedDisplayNumber = DefaultSelectedDisplay();
        _suppressMappingPending = false;
        MappingChangePending = false; // the selection now matches the adopted mapping (any prior pick is discarded)
        RefreshDetectionStatus();
    }

    /// <summary>Reconcile this editor with a fresh settings load from the session. Called by the shell
    /// on every data load (the window-activation pull, TabletsChanged, and the fallback poll). It's a
    /// no-op when the reload matches what we already show — including our own applies, since those
    /// mutate <c>_profile</c> before pushing, so its live fingerprint already equals the reloaded one.
    /// When it genuinely diverged (an external editor changed the daemon), adopt it silently, or — if
    /// the user has an unsaved edit — raise a non-destructive banner instead of discarding it.</summary>
    public void ReconcileExternalChange(Settings? freshSettings, Profile? freshProfile)
    {
        if (freshSettings == null || freshProfile == null) return; // tablet gone — detection banner owns that
        var freshFp = ProfileFingerprint.Compute(freshProfile);
        var ownFp = ProfileFingerprint.Compute(_profile);
        if (freshFp.Length == 0 || ownFp.Length == 0) return; // can't compare → don't risk a false positive
        if (freshFp == ownFp) { ClearExternalChange(); return; } // in sync (covers our own applies)

        if (HasUnsavedEdit)
        {
            _pendingExternalSettings = freshSettings;
            _pendingExternalProfile = freshProfile;
            ExternalChangeText =
                "These settings were changed outside OpenTabletArtist (for example in the OpenTabletDriver " +
                "UX). Reload to use the current values — your unsaved change here will be discarded.";
        }
        else
        {
            AdoptProfile(freshSettings, freshProfile);
        }
    }

    /// <summary>Adopt the externally-changed settings the banner is holding (the banner's Reload action).</summary>
    [RelayCommand]
    private void ReloadExternalChange()
    {
        if (_pendingExternalSettings != null && _pendingExternalProfile != null)
            AdoptProfile(_pendingExternalSettings, _pendingExternalProfile);
    }

    private void ClearExternalChange()
    {
        _pendingExternalSettings = null;
        _pendingExternalProfile = null;
        if (ExternalChangeText.Length != 0) ExternalChangeText = "";
    }

    /// <summary>The user has a change here that isn't yet persisted, so an external reload must not
    /// silently overwrite it. Currently just a picked-but-unapplied display mapping — every other edit
    /// auto-applies immediately; extend this if more deferred edits are added.</summary>
    private bool HasUnsavedEdit => MappingChangePending;

    private async Task ApplySettingsChange(Action<Profile> modify)
    {
        if (_applyAction == null || _settings == null) return;
        modify(_profile);
        await _applyAction(_settings);
        RefreshFromProfile();
    }

    /// <summary>Show the Forget button only on the in-app page (host wires the action), not in the
    /// tray's focused Pen Dynamics dialog.</summary>
    public bool ShowForget => !DynamicsOnly && _forgetAction != null;

    /// <summary>Remove this tablet's saved profile (the host then navigates away).</summary>
    [RelayCommand]
    private async Task Forget()
    {
        if (_forgetAction != null) await _forgetAction();
    }

    private void RefreshFromProfile()
    {
        // Output mode
        OutputModePath = _profile.OutputMode?.Path ?? "Not set";
        OutputModeShort = OutputModePath.Split('.').LastOrDefault() ?? OutputModePath;

        _skipOutputModeChange = true;
        IsWinInkAbsolute = OutputModePath.Equals(WinInkAbsoluteModePath, StringComparison.OrdinalIgnoreCase);
        IsWinInkRelative = OutputModePath.Equals(WinInkRelativeModePath, StringComparison.OrdinalIgnoreCase);
        _skipOutputModeChange = false;

        var isWinInk = IsWinInkAbsolute || IsWinInkRelative;
        CanFixOutputMode = !isWinInk && _applyAction != null;

        // Bindings — pen switches (tip, eraser, barrel buttons)
        var bindings = _profile.BindingSettings;
        RefreshPenSwitchRows();

        // ExpressKeys — editable single-key bindings. While mapping is suspended we show the stashed
        // bindings (greyed) so the user still sees what will come back.
        var auxStores = EffectiveAuxStores();
        AuxButtonCount = auxStores.Count.ToString();
        var canEditAux = _applyAction != null && AuxButtonsEnabled;

        var newAuxButtons = new List<ButtonBinding>();
        for (int i = 0; i < auxStores.Count; i++)
        {
            var store = auxStores[i];
            var binding = AuxKeyBinding.ReadBinding(store); // null = a binding this editor can't model
            newAuxButtons.Add(new ButtonBinding(
                index: i + 1,
                binding: binding ?? AuxBinding.Unbound,
                isOtherBinding: binding == null,
                otherLabel: binding == null ? GetBindingName(store) : "",
                canEdit: canEditAux,
                applyBinding: ApplyAuxBindingAsync,
                editBinding: _editBinding));
        }
        AuxButtons = newAuxButtons;
        NoAuxButtons = newAuxButtons.Count == 0;
        ShowAuxControls = newAuxButtons.Count > 0 && _applyAction != null;

        // Wheel bindings — rotation (CW/CCW) + wheel buttons + thresholds per wheel.
        RefreshWheels();

        // Filters + raw JSON view (also refreshed after a dynamics toggle/edit persists, so the
        // Filters tab reflects the DynamicsFilter's enabled state without a manual Refresh).
        UpdateFiltersDisplay();

        // Active-area diagram (full + effective area + mapped display).
        RefreshTabletArea();

        // Pen dynamics — curve + smoothing (load without triggering a persist)
        var pc = PressureCurveProfile.Read(_settings, _profile.Tablet ?? "");
        var dynamics = pc?.Dynamics ?? PenDynamicsSettings.Default;
        _skipCurvePersist = true;
        Curve = dynamics.Curve;
        PressureSmoothing = dynamics.PressureSmoothing;
        PositionSmoothing = dynamics.PositionSmoothing;
        SmoothAfterCurve = dynamics.SmoothAfterCurve;
        PressureCurveEnabled = pc?.Enabled ?? false;
        _skipCurvePersist = false;
        CanEditPressure = _applyAction != null;

        // Hover limit (#188) — load without triggering a persist.
        var hover = HoverProfile.Read(_settings, _profile.Tablet ?? "");
        _skipHoverPersist = true;
        MaxHoverDistance = hover?.MaxHoverDistance ?? DefaultMaxHoverDistance;
        HoverLimitEnabled = hover?.Enabled ?? false;
        NearProximityOnly = hover?.NearProximityOnly ?? false;
        _skipHoverPersist = false;
        CanEditHover = _applyAction != null;

        RefreshCalibrationStatus();
    }

    /// <summary>Recomputes the Filters-tab list and the raw-JSON view from the current
    /// <see cref="_profile"/>. Called on a full refresh and again after a dynamics edit persists,
    /// so the Filters tab tracks the DynamicsFilter's enabled state without a manual Refresh.</summary>
    private void UpdateFiltersDisplay()
    {
        Filters.Clear();
        foreach (var f in _profile.Filters)
        {
            var path = f?.Path ?? "Unknown";
            var typeName = path.Split('.').LastOrDefault() ?? "Unknown";
            Filters.Add(new FilterCardViewModel(
                title: FriendlyFilterName(typeName),
                fullPath: path,
                enabled: f?.Enable ?? true,
                origin: ProfileFilterMaintenance.Classify(path)));
        }
        HasFilters = Filters.Count > 0;

        RawJson = JsonConvert.SerializeObject(_profile, Formatting.Indented);
    }

    /// <summary>Maps our plugin filter class names to the friendly labels they carry as
    /// <c>[PluginName]</c> in the plugin; anything else (third-party filters) keeps its type name.</summary>
    private static string FriendlyFilterName(string typeName) => typeName switch
    {
        "DynamicsFilter" => "Pen Dynamics",
        "HoverFilter" => "Hover Limit",
        "CalibrationFilter" => "Calibration",
        _ => typeName,
    };

    [RelayCommand]
    private async Task FixOutputMode()
    {
        await SetOutputMode(WinInkAbsoluteModePath);
    }

    [RelayCommand(CanExecute = nameof(CanApplyDisplay))]
    private async Task ApplyDisplay()
    {
        var display = Displays.FirstOrDefault(d => d.Number == SelectedDisplayNumber);
        if (_applyAction == null || _settings == null || display == null) return;

        // Same mapping the tray's "Switch display" uses — aspect-locked, full-monitor (#187).
        await ApplySettingsChange(p => DisplayMappingApplier.ApplyToProfile(p, _tabletDigitizer, display, Displays));
        MappingChangePending = false; // the selection is now the applied mapping
    }

    /// <summary>Re-read the connected monitors from Windows (manual Refresh or a live display change).</summary>
    [RelayCommand]
    private void RefreshDisplays()
    {
        var keep = SelectedDisplayNumber;
        Displays = DisplayEnumerator.Enumerate();
        SelectedDisplayNumber =
            (keep != null && Displays.Any(d => d.Number == keep)) ? keep
            : DefaultSelectedDisplay();
        RefreshTabletArea(); // displays changed → recompute the mapped-display side of the diagram
    }

    [RelayCommand]
    private void OpenDisplaySettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    /// <summary>The display the profile is currently mapped to (full-monitor match), or null.</summary>
    private int? CurrentlyMappedNumber() => DisplayMappingApplier.CurrentlyMapped(_profile, Displays)?.Number;

    /// <summary>The display to pre-select in the picker. A clean mapping selects its monitor; a custom /
    /// off-screen mapping selects nothing (so the diagram doesn't fake a clean pick — the warning guides
    /// the user to choose); no mapping falls back to the primary as a sensible starting point.</summary>
    private int? DefaultSelectedDisplay() => DisplayMappingApplier.ClassifyMapping(_profile, Displays) switch
    {
        DisplayMappingValidity.Clean => CurrentlyMappedNumber(),
        DisplayMappingValidity.Custom or DisplayMappingValidity.OffScreen => null,
        _ => Displays.FirstOrDefault(d => d.IsPrimary)?.Number ?? Displays.FirstOrDefault()?.Number,
    };

    private void RefreshPenSwitchRows()
    {
        var bindings = _profile.BindingSettings;
        var canEdit = _applyAction != null;
        var rows = new List<PenSwitchRowViewModel>
        {
            new(PenSwitchKind.Tip, 1, bindings.TipButton, canEdit, ApplyPenSwitchBindingAsync),
            new(PenSwitchKind.Eraser, 1, bindings.EraserButton, canEdit, ApplyPenSwitchBindingAsync),
        };
        for (int i = 0; i < bindings.PenButtons.Count; i++)
            rows.Add(new(PenSwitchKind.PenButton, i + 1, bindings.PenButtons[i], canEdit, ApplyPenSwitchBindingAsync));
        if (rows.Count > 0) rows[0].IsFirst = true; // suppresses the leading divider in the merged card

        // Expose each switch by slot so the visual pen diagram can bind tip/eraser/buttons (#pen-switch-diagram).
        PenTipRow = rows[0];
        PenEraserRow = rows[1];
        var buttons = rows.Skip(2).ToList();
        PenButton1Row = buttons.ElementAtOrDefault(0);
        PenButton2Row = buttons.ElementAtOrDefault(1);
        PenButton3Row = buttons.ElementAtOrDefault(2);
    }

    private async Task ApplyPenSwitchBindingAsync(PenSwitchKind kind, int penButtonIndex, PluginSettingStore store)
    {
        await ApplySettingsChange(p =>
        {
            switch (kind)
            {
                case PenSwitchKind.Tip:
                    p.BindingSettings.TipButton = store;
                    break;
                case PenSwitchKind.Eraser:
                    p.BindingSettings.EraserButton = store;
                    break;
                case PenSwitchKind.PenButton:
                    if (penButtonIndex >= 1 && penButtonIndex <= p.BindingSettings.PenButtons.Count)
                        p.BindingSettings.PenButtons[penButtonIndex - 1] = store;
                    break;
            }
        });
    }

    // ── ExpressKeys (auxiliary buttons): key bindings, enable-all toggle, clear-all ──────────────
    private string AuxEnabledKey => $"AuxEnabled:{_profile.Tablet}";
    private string AuxBackupKey => $"AuxBackup:{_profile.Tablet}";

    /// <summary>Master switch (per tablet, persisted): when off, the buttons do nothing — their
    /// bindings are stashed and empty ones are written to the driver, restored when toggled on.</summary>
    [ObservableProperty] private bool _auxButtonsEnabled = true;
    /// <summary>Show the enable-all toggle + clear-all button (only when the tablet has aux buttons
    /// and this host can edit).</summary>
    [ObservableProperty] private bool _showAuxControls;
    private bool _suppressAuxEnabledApply;

    private void LoadAuxEnabledState()
    {
        _suppressAuxEnabledApply = true;
        AuxButtonsEnabled = AppSettings.Get(AuxEnabledKey) != "false"; // default enabled
        _suppressAuxEnabledApply = false;
    }

    /// <summary>The aux stores to display: the live profile when enabled, else the stash (so a
    /// suspended set is still visible) falling back to the profile.</summary>
    private PluginSettingStoreCollection EffectiveAuxStores()
    {
        if (!AuxButtonsEnabled)
        {
            var backup = AppSettings.Get(AuxBackupKey);
            if (!string.IsNullOrEmpty(backup))
            {
                try
                {
                    var restored = JsonConvert.DeserializeObject<PluginSettingStoreCollection>(backup);
                    if (restored != null) return restored;
                }
                catch { /* corrupt stash — fall back to the live (empty) profile */ }
            }
        }
        return _profile.BindingSettings.AuxButtons;
    }

    private async Task ApplyAuxBindingAsync(int buttonIndex, AuxBinding binding)
    {
        if (!AuxButtonsEnabled) return; // editing is locked while suspended
        var store = AuxKeyBinding.MakeBinding(binding); // null = unbound
        await ApplySettingsChange(p =>
        {
            var aux = p.BindingSettings.AuxButtons;
            if (buttonIndex >= 1 && buttonIndex <= aux.Count)
                aux[buttonIndex - 1] = store!;
        });
    }

    // ── Wheel tab: rotation bindings (CW/CCW), wheel buttons, thresholds, live flash ──
    // Reuses the ExpressKeys ButtonBinding editor — each rotation/button is a single OTD
    // PluginSettingStore, exactly what AuxKeyBinding.Read/MakeBinding handle.
    private string WheelEnabledKey => $"WheelEnabled:{_profile.Tablet}";
    private string WheelBackupKey => $"WheelBackup:{_profile.Tablet}";

    [ObservableProperty] private List<WheelEditor> _wheels = new();
    public bool HasWheels => Wheels.Count > 0;
    public bool NoWheels => Wheels.Count == 0;
    public bool ShowWheelControls => Wheels.Count > 0 && _applyAction != null;

    /// <summary>Master enable for all wheel bindings (stash/restore, like ExpressKeys).</summary>
    [ObservableProperty] private bool _wheelEnabled = true;
    private bool _suppressWheelEnabledApply;

    partial void OnWheelsChanged(List<WheelEditor> value)
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(NoWheels));
        OnPropertyChanged(nameof(ShowWheelControls));
    }

    private void LoadWheelEnabledState()
    {
        _suppressWheelEnabledApply = true;
        WheelEnabled = AppSettings.Get(WheelEnabledKey) != "false";
        _suppressWheelEnabledApply = false;
    }

    /// <summary>Live wheel bindings, or the stashed set while wheels are suspended (so the greyed
    /// editor still shows what will come back).</summary>
    private List<WheelBindingSettings> EffectiveWheelBindings()
    {
        if (!WheelEnabled)
        {
            var backup = AppSettings.Get(WheelBackupKey);
            if (!string.IsNullOrEmpty(backup))
            {
                try
                {
                    var restored = JsonConvert.DeserializeObject<List<WheelBindingSettings>>(backup);
                    if (restored != null) return restored;
                }
                catch { /* corrupt stash — fall through to live */ }
            }
        }
        return _profile.BindingSettings.WheelBindings;
    }

    private void RefreshWheels()
    {
        var wheels = EffectiveWheelBindings();
        var canEdit = _applyAction != null && WheelEnabled;
        var list = new List<WheelEditor>();
        bool multi = wheels.Count > 1;
        for (int w = 0; w < wheels.Count; w++)
        {
            var ws = wheels[w];
            int wi = w;
            // Physical rotation runs opposite to OTD's reported direction on tested rings (the Wacom
            // Intuos Pro ring's position increments counter-clockwise), so our "Clockwise" maps to
            // OTD's CounterClockwiseRotation and vice versa. WheelEditor's live flash is inverted to
            // match, and PersistThresholdsAsync swaps the threshold fields the same way.
            var cw = MakeWheelRow(wi, "Clockwise", ws.CounterClockwiseRotation, canEdit,
                (_, b) => ApplyWheelRotationAsync(wi, false, b));
            var ccw = MakeWheelRow(wi, "Counter-clockwise", ws.ClockwiseRotation, canEdit,
                (_, b) => ApplyWheelRotationAsync(wi, true, b));
            var buttons = new List<ButtonBinding>();
            for (int b = 0; b < ws.WheelButtons.Count; b++)
            {
                int bi = b;
                var label = ws.WheelButtons.Count > 1 ? $"Button {b + 1}" : "Wheel button";
                buttons.Add(MakeWheelRow(wi, label, ws.WheelButtons[b], canEdit,
                    (_, bind) => ApplyWheelButtonAsync(wi, bi, bind)));
            }
            list.Add(new WheelEditor(
                wheelIndex: wi,
                title: multi ? $"Wheel {w + 1}" : "Wheel",
                showTitle: multi,
                clockwise: cw, counterClockwise: ccw, buttons: buttons,
                clockwiseThreshold: ws.CounterClockwiseActivationThreshold,       // physical CW ↔ OTD CCW
                counterClockwiseThreshold: ws.ClockwiseActivationThreshold,
                stepSizeDegrees: ws.StepSize,
                applyThreshold: canEdit ? ApplyWheelThresholdAsync : null));
        }
        Wheels = list;
    }

    private ButtonBinding MakeWheelRow(int wheelIndex, string label, PluginSettingStore? store,
        bool canEdit, Func<int, AuxBinding, Task> apply)
    {
        var binding = AuxKeyBinding.ReadBinding(store); // null = a binding this editor can't model
        return new ButtonBinding(
            index: wheelIndex,
            binding: binding ?? AuxBinding.Unbound,
            isOtherBinding: binding == null,
            otherLabel: binding == null ? GetBindingName(store) : "",
            canEdit: canEdit,
            applyBinding: apply,
            label: label,
            editBinding: _editBinding);
    }

    private Task ApplyWheelRotationAsync(int wheelIndex, bool clockwise, AuxBinding binding)
    {
        if (!WheelEnabled) return Task.CompletedTask;
        var store = AuxKeyBinding.MakeBinding(binding); // null = unbound
        return ApplySettingsChange(p =>
        {
            var wheels = p.BindingSettings.WheelBindings;
            if (wheelIndex < 0 || wheelIndex >= wheels.Count) return;
            if (clockwise) wheels[wheelIndex].ClockwiseRotation = store;
            else wheels[wheelIndex].CounterClockwiseRotation = store;
        });
    }

    private Task ApplyWheelButtonAsync(int wheelIndex, int buttonIndex, AuxBinding binding)
    {
        if (!WheelEnabled) return Task.CompletedTask;
        var store = AuxKeyBinding.MakeBinding(binding); // null = unbound
        return ApplySettingsChange(p =>
        {
            var wheels = p.BindingSettings.WheelBindings;
            if (wheelIndex < 0 || wheelIndex >= wheels.Count) return;
            var buttons = wheels[wheelIndex].WheelButtons;
            if (buttonIndex >= 0 && buttonIndex < buttons.Count) buttons[buttonIndex] = store!;
        });
    }

    // Thresholds change rapidly while a slider is dragged — debounce into one apply (and one rebuild)
    // so the slider isn't yanked out from under the drag.
    private readonly Dictionary<(int Wheel, bool Clockwise), double> _pendingThresholds = new();
    private CancellationTokenSource? _wheelThresholdCts;

    private Task ApplyWheelThresholdAsync(int wheelIndex, bool clockwise, double degrees)
    {
        _pendingThresholds[(wheelIndex, clockwise)] = degrees;
        _wheelThresholdCts?.Cancel();
        var cts = _wheelThresholdCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);
        return Task.CompletedTask;

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(350, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistThresholdsAsync);
        }
    }

    private async Task PersistThresholdsAsync()
    {
        if (_pendingThresholds.Count == 0 || !WheelEnabled) return;
        var pending = new Dictionary<(int Wheel, bool Clockwise), double>(_pendingThresholds);
        _pendingThresholds.Clear();
        await ApplySettingsChange(p =>
        {
            var wheels = p.BindingSettings.WheelBindings;
            foreach (var (key, deg) in pending)
            {
                if (key.Wheel < 0 || key.Wheel >= wheels.Count) continue;
                // physical clockwise ↔ OTD CounterClockwise (see RefreshWheels)
                if (key.Clockwise) wheels[key.Wheel].CounterClockwiseActivationThreshold = (float)deg;
                else wheels[key.Wheel].ClockwiseActivationThreshold = (float)deg;
            }
        });
    }

    partial void OnWheelEnabledChanged(bool value)
    {
        if (_suppressWheelEnabledApply) return;
        _ = SetWheelEnabledAsync(value);
    }

    private async Task SetWheelEnabledAsync(bool enabled)
    {
        if (_applyAction == null) return;
        AppSettings.Set(WheelEnabledKey, enabled ? "true" : "false");
        if (!enabled)
        {
            // Suspend: stash the current bindings, then clear them so the wheel does nothing.
            AppSettings.Set(WheelBackupKey, JsonConvert.SerializeObject(_profile.BindingSettings.WheelBindings));
            await ApplySettingsChange(ClearWheels);
        }
        else
        {
            // Resume: restore the stashed rotation/button bindings (if any), then drop the stash.
            var backup = AppSettings.Get(WheelBackupKey);
            await ApplySettingsChange(p =>
            {
                if (string.IsNullOrEmpty(backup)) return;
                try
                {
                    var restored = JsonConvert.DeserializeObject<List<WheelBindingSettings>>(backup);
                    if (restored == null) return;
                    var wheels = p.BindingSettings.WheelBindings;
                    for (int i = 0; i < wheels.Count && i < restored.Count; i++)
                    {
                        wheels[i].ClockwiseRotation = restored[i].ClockwiseRotation;
                        wheels[i].CounterClockwiseRotation = restored[i].CounterClockwiseRotation;
                        var live = wheels[i].WheelButtons;
                        var saved = restored[i].WheelButtons;
                        for (int b = 0; b < live.Count && b < saved.Count; b++) live[b] = saved[b];
                    }
                }
                catch { /* corrupt stash — leave the (cleared) profile as-is */ }
            });
            AppSettings.Set(WheelBackupKey, "");
        }
    }

    /// <summary>Remove every wheel binding (rotations + buttons). Also drops any suspended stash.</summary>
    [RelayCommand]
    private async Task ClearWheelBindings()
    {
        if (_applyAction == null) return;
        AppSettings.Set(WheelBackupKey, "");
        await ApplySettingsChange(ClearWheels);
    }

    private static void ClearWheels(Profile p)
    {
        foreach (var w in p.BindingSettings.WheelBindings)
        {
            w.ClockwiseRotation = null;
            w.CounterClockwiseRotation = null;
            for (int b = 0; b < w.WheelButtons.Count; b++) w.WheelButtons[b] = null!;
        }
    }

    // ── Live wheel feedback: flash the matching rotation row / light a pressed wheel button ──
    private void OnWheelButtons(bool[][] states)
    {
        for (int w = 0; w < Wheels.Count; w++)
        {
            var buttons = Wheels[w].Buttons;
            var wheelStates = w < states.Length ? states[w] : Array.Empty<bool>();
            for (int b = 0; b < buttons.Count; b++)
                buttons[b].IsPressed = b < wheelStates.Length && wheelStates[b];
        }
    }

    private void OnWheelPositions(uint?[] positions)
    {
        for (int w = 0; w < Wheels.Count && w < positions.Length; w++)
            Wheels[w].OnAbsolutePosition(positions[w]);
    }

    private void OnWheelDeltas(int[] deltas)
    {
        for (int w = 0; w < Wheels.Count && w < deltas.Length; w++)
            Wheels[w].OnRelativeDelta(deltas[w]);
    }

    partial void OnAuxButtonsEnabledChanged(bool value)
    {
        if (_suppressAuxEnabledApply) return;
        _ = SetAuxButtonsEnabledAsync(value);
    }

    private async Task SetAuxButtonsEnabledAsync(bool enabled)
    {
        if (_applyAction == null) return;
        AppSettings.Set(AuxEnabledKey, enabled ? "true" : "false");
        if (!enabled)
        {
            // Suspend: stash the current bindings, then write empty ones so the buttons do nothing.
            AppSettings.Set(AuxBackupKey, JsonConvert.SerializeObject(_profile.BindingSettings.AuxButtons));
            await ApplySettingsChange(ClearAux);
        }
        else
        {
            // Resume: restore the stash (if any), then drop it.
            var backup = AppSettings.Get(AuxBackupKey);
            await ApplySettingsChange(p =>
            {
                if (string.IsNullOrEmpty(backup)) return;
                try
                {
                    var restored = JsonConvert.DeserializeObject<PluginSettingStoreCollection>(backup);
                    if (restored != null)
                    {
                        var aux = p.BindingSettings.AuxButtons;
                        for (int i = 0; i < aux.Count && i < restored.Count; i++) aux[i] = restored[i];
                    }
                }
                catch { /* corrupt stash — leave the (empty) profile as-is */ }
            });
            AppSettings.Set(AuxBackupKey, "");
        }
    }

    /// <summary>Remove every express-key binding. Also drops any suspended stash, so a later
    /// enable doesn't bring cleared bindings back.</summary>
    [RelayCommand]
    private async Task ClearAuxButtons()
    {
        if (_applyAction == null) return;
        AppSettings.Set(AuxBackupKey, "");
        await ApplySettingsChange(ClearAux);
    }

    private static void ClearAux(Profile p)
    {
        var aux = p.BindingSettings.AuxButtons;
        for (int i = 0; i < aux.Count; i++) aux[i] = null!;
    }

    public string TabletName { get; }

    /// <summary>Brand line for the header — the first word of the name (e.g. "Wacom"), shown small above
    /// the model. Empty when the name is a single word, so the header shows the model alone.</summary>
    public string TabletBrand
    {
        get { var i = TabletName.IndexOf(' '); return i > 0 ? TabletName[..i] : ""; }
    }

    /// <summary>Model line for the header — everything after the first word (e.g. "PTK-670"), or the whole
    /// name when it's a single word.</summary>
    public string TabletModel
    {
        get { var i = TabletName.IndexOf(' '); return i > 0 ? TabletName[(i + 1)..].TrimStart() : TabletName; }
    }

    public bool HasTabletBrand => TabletBrand.Length > 0;

    [ObservableProperty] private string _outputModeShort = "";
    [ObservableProperty] private string _outputModePath = "";
    [ObservableProperty] private bool _canFixOutputMode;
    [ObservableProperty] private bool _isWinInkAbsolute;
    [ObservableProperty] private bool _isWinInkRelative;
    private bool _skipOutputModeChange;
    public bool HasAreaMapping { get; }
    // Per-slot views for the visual pen diagram (button slots are null when the pen lacks that button).
    [ObservableProperty] private PenSwitchRowViewModel? _penTipRow;
    [ObservableProperty] private PenSwitchRowViewModel? _penEraserRow;
    [ObservableProperty] private PenSwitchRowViewModel? _penButton1Row;
    [ObservableProperty] private PenSwitchRowViewModel? _penButton2Row;
    [ObservableProperty] private PenSwitchRowViewModel? _penButton3Row;

    [ObservableProperty] private string _auxButtonCount = "0";
    [ObservableProperty] private bool _noAuxButtons;
    [ObservableProperty] private List<ButtonBinding> _auxButtons = [];
    /// <summary>One card per filter on the profile (Filters tab). Rebuilt by <see cref="UpdateFiltersDisplay"/>.</summary>
    public ObservableCollection<FilterCardViewModel> Filters { get; } = new();

    /// <summary>False when the profile has no filters — the Filters tab shows its empty state.</summary>
    [ObservableProperty] private bool _hasFilters;
    [ObservableProperty] private string _rawJson = "";

    // ── Pressure curve tab ──────────────────────────────────────

    [ObservableProperty] private PressureCurveSettings _curve = PressureCurveSettings.Default;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DynamicsControlsOpacity))]
    private bool _pressureCurveEnabled;
    [ObservableProperty] private bool _canEditPressure;

    /// <summary>Dim the curve/smoothing controls when Pen Dynamics is off so they read as inactive.
    /// <c>IsEnabled</c> alone barely greys them, and the custom-drawn curve chart ignores
    /// <c>IsEnabled</c> entirely, so without this it would still look fully interactive.</summary>
    public double DynamicsControlsOpacity => PressureCurveEnabled ? 1.0 : 0.4;

    [ObservableProperty] private double _pressureSmoothing;
    [ObservableProperty] private double _positionSmoothing;
    [ObservableProperty] private bool _smoothAfterCurve = true;

    public string PressureSmoothingText => PressureSmoothing.ToString("0.00");
    public string PositionSmoothingText => PositionSmoothing.ToString("0.00");

    private bool _skipCurvePersist;
    private CancellationTokenSource? _persistCts;

    partial void OnPressureSmoothingChanged(double value)
    {
        OnPropertyChanged(nameof(PressureSmoothingText));
        SchedulePersist();
    }

    partial void OnPositionSmoothingChanged(double value)
    {
        OnPropertyChanged(nameof(PositionSmoothingText));
        SchedulePersist();
    }

    partial void OnSmoothAfterCurveChanged(bool value) => SchedulePersist();

    /// <summary>Softness slider value, projected onto the <see cref="Curve"/> struct.</summary>
    public double Softness
    {
        get => Curve.Softness;
        set { if (Curve.Softness != value) Curve = Curve with { Softness = value }; }
    }

    /// <summary>Cut (dead-zone) vs Clamp (floor) below the input minimum.</summary>
    public bool CutBelowMinimum
    {
        get => Curve.MinApproach == PressureMinApproach.Cut;
        set
        {
            var want = value ? PressureMinApproach.Cut : PressureMinApproach.Clamp;
            if (Curve.MinApproach != want) Curve = Curve with { MinApproach = want };
        }
    }

    public string SoftnessText => Curve.Softness.ToString("0.00");

    // Read-only display of the node values (#131). Editing is via dragging the chart nodes; these
    // just show where the pink (min) / cyan (max) nodes currently sit (input → output).
    public string InputMinimumText => Curve.InputMinimum.ToString("0.00");
    public string OutputMinimumText => Curve.Minimum.ToString("0.00");
    public string InputMaximumText => Curve.InputMaximum.ToString("0.00");
    public string OutputMaximumText => Curve.Maximum.ToString("0.00");

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    partial void OnCurveChanged(PressureCurveSettings value)
    {
        OnPropertyChanged(nameof(Softness));
        OnPropertyChanged(nameof(CutBelowMinimum));
        OnPropertyChanged(nameof(SoftnessText));
        OnPropertyChanged(nameof(InputMinimumText));
        OnPropertyChanged(nameof(InputMaximumText));
        OnPropertyChanged(nameof(OutputMinimumText));
        OnPropertyChanged(nameof(OutputMaximumText));
        SchedulePersist();
    }

    partial void OnPressureCurveEnabledChanged(bool value) => SchedulePersist();

    [RelayCommand]
    private void ResetCurve() => Curve = PressureCurveSettings.Default;

    /// <summary>Reset every dynamics setting — the curve, both smoothing amounts, and the order —
    /// back to their no-op defaults (#185). Each setter schedules the debounced persist, so the
    /// cleared state is written to the daemon. Leaves the On/Off toggle as the user set it.</summary>
    [RelayCommand]
    private void ResetAllDynamics()
    {
        Curve = PressureCurveSettings.Default;
        PressureSmoothing = 0;
        PositionSmoothing = 0;
        SmoothAfterCurve = true;
    }

    /// <summary>Quick-start curve presets (#103).</summary>
    [RelayCommand]
    private void ApplyPreset(string kind) => Curve = kind switch
    {
        "soft" => PressureCurveSettings.Default with { Softness = 0.5 },   // lighter touch (concave)
        "firm" => PressureCurveSettings.Default with { Softness = -0.5 },  // firmer (convex)
        _ => PressureCurveSettings.Default,                               // linear
    };

    // ── Live device-report preview: pen-pressure dot (#102) + aux-button highlight + area map ──
    private readonly DaemonPenInputSource? _penInput;
    [ObservableProperty] private double? _livePressure;
    /// <summary>Live pen position (0..1 over the full tablet area) for the active-area diagram, or
    /// null when no pen is in range. (#250)</summary>
    [ObservableProperty] private double? _livePenX;
    [ObservableProperty] private double? _livePenY;

    /// <summary>With more than one tablet connected, the daemon debug stream interleaves all of them;
    /// only show this profile's tablet (same rule as Diagnostics #190).</summary>
    private bool AcceptDeviceReportForThisTablet(JObject data)
    {
        if (_deviceData is not { DetectedTablets.Count: > 1 }) return true;
        var reportTablet = data["Tablet"]?["Properties"]?["Name"]?.ToString();
        var ourTablet = _profile.Tablet;
        if (reportTablet == null || string.IsNullOrEmpty(ourTablet)) return true;
        return string.Equals(reportTablet, ourTablet, StringComparison.OrdinalIgnoreCase);
    }

    // DeviceReportSample normalizes to 0..1; feed both the pressure dot and the active-area map.
    private void OnPenSample(PenSample s)
    {
        LivePenX = s.X;
        LivePenY = s.Y;
        LivePressure = s.IsDown ? Clamp01(s.Pressure) : null;
    }

    // Light up each aux-button card while its physical button is held (express-key live highlight).
    private void OnAuxButtons(bool[] states)
    {
        for (int i = 0; i < AuxButtons.Count; i++)
            AuxButtons[i].IsPressed = i < states.Length && states[i];
    }

    /// <summary>Enables the daemon's device-report stream (live pressure dot + aux highlight). Driven
    /// by the view: on while the Dynamics or ExpressKeys tab is visible, off otherwise.</summary>
    public void StartLiveInput() => _ = _penInput?.StartAsync();

    /// <summary>Stops the stream and clears any live state so nothing stays lit after we look away.</summary>
    public void StopLiveInput()
    {
        LivePressure = null;
        LivePenX = null;
        LivePenY = null;
        foreach (var b in AuxButtons) b.IsPressed = false;
        foreach (var w in Wheels) w.ClearLiveState();
        _ = _penInput?.StopAsync();
    }

    /// <summary>Active-area diagram geometry (full + effective area + mapped display), or null when
    /// the tablet has no absolute mapping / digitizer specs. (#250/#252)</summary>
    [ObservableProperty] private TabletAreaInfo? _tabletArea;

    private void RefreshTabletArea()
    {
        var t = _profile.AbsoluteModeSettings?.Tablet;
        // Read the digitizer live: its specs can arrive after this (cached) view-model was created — e.g.
        // when the tablet (re)connects — so we don't rely only on the value captured at construction.
        // That snapshot is the fallback (e.g. the tray dialog has no live device data). Previously only
        // the snapshot was used, so a tablet whose specs weren't ready when its page was first opened
        // stayed stuck on "Active-area details aren't available" until an app restart.
        var dig = _deviceData?.GetTabletDigitizer(_profile.Tablet ?? "") ?? _tabletDigitizer;
        if (t != null && dig is { } d && d.Width > 0 && d.Height > 0)
        {
            var mapped = DisplayMappingApplier.CurrentlyMapped(_profile, Displays);
            TabletArea = new TabletAreaInfo(
                FullWidth: d.Width, FullHeight: d.Height,
                EffWidth: t.Width, EffHeight: t.Height, EffCenterX: t.X, EffCenterY: t.Y,
                HasDisplay: mapped != null,
                DisplayNumber: mapped?.Number ?? 0, DisplayName: mapped?.Name ?? "",
                DisplayWidth: mapped?.Width ?? 0, DisplayHeight: mapped?.Height ?? 0);
        }
        else
        {
            TabletArea = null;
        }
        // The mapped display drives where the calibration overlay pops up (Calibration tab hint).
        OnPropertyChanged(nameof(CalibrationDisplayText));
        // The stored mapping's validity drives the Display Mapping tab's warning/note.
        OnPropertyChanged(nameof(MappingValidity));
        OnPropertyChanged(nameof(ShowMappingOffScreen));
        OnPropertyChanged(nameof(ShowMappingCustom));
        OnPropertyChanged(nameof(MappingValidityText));
    }

    // ── Active Area tab: read-out of the full vs. effective (used) area ──────────
    // The diagram binds TabletArea directly; these drive the stat cells beside it. All recompute
    // when TabletArea changes (a new mapping applied, displays changed, or profile reloaded).
    partial void OnTabletAreaChanged(TabletAreaInfo? value)
    {
        OnPropertyChanged(nameof(ActiveAreaFullText));
        OnPropertyChanged(nameof(ActiveAreaUsedText));
        OnPropertyChanged(nameof(ActiveAreaUsagePercentText));
        OnPropertyChanged(nameof(ActiveAreaDimsPercentText));
        OnPropertyChanged(nameof(ActiveAreaDiagonalText));
    }

    /// <summary>Show active-area lengths in inches instead of millimetres (the tab's unit toggle). A
    /// view-only display preference — OTD stores everything in mm, so nothing is converted on disk.</summary>
    [ObservableProperty] private bool _useImperialUnits;

    partial void OnUseImperialUnitsChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveAreaFullText));
        OnPropertyChanged(nameof(ActiveAreaUsedText));
        OnPropertyChanged(nameof(ActiveAreaDiagonalText));
    }

    public string ActiveAreaFullText => TabletArea is { } a ? FormatSize(a.FullWidth, a.FullHeight) : "—";
    public string ActiveAreaUsedText => TabletArea is { } a ? FormatSize(a.EffWidth, a.EffHeight) : "—";

    /// <summary>Corner-to-corner size of the used vs. full area, e.g. "257 mm active · 269 mm full".</summary>
    public string ActiveAreaDiagonalText => TabletArea is { } a
        ? $"{FormatLength(Diagonal(a.EffWidth, a.EffHeight))} active  ·  {FormatLength(Diagonal(a.FullWidth, a.FullHeight))} full"
        : "—";

    private static double Diagonal(double w, double h) => System.Math.Sqrt(w * w + h * h);

    // Length display helpers: OTD's areas are millimetres; inches = mm / 25.4. Metric shows one decimal
    // (e.g. "269 mm"), imperial two (inches are ~25× larger, so a decimal buys real precision, "10.59 in").
    private const double MmPerInch = 25.4;
    private string UnitLabel => UseImperialUnits ? "in" : "mm";
    private string Num(double mm) =>
        (UseImperialUnits ? mm / MmPerInch : mm).ToString(UseImperialUnits ? "0.##" : "0.#");
    private string FormatLength(double mm) => $"{Num(mm)} {UnitLabel}";
    private string FormatSize(double wMm, double hMm) => $"{Num(wMm)} × {Num(hMm)} {UnitLabel}";

    /// <summary>Share of the full digitizer area covered by the effective area (width×height).</summary>
    public string ActiveAreaUsagePercentText => TabletArea is { FullWidth: > 0, FullHeight: > 0 } a
        ? $"{a.EffWidth * a.EffHeight / (a.FullWidth * a.FullHeight) * 100:0}%" : "—";

    /// <summary>Per-axis coverage, e.g. "80% × 62%" — useful once the area no longer fills an edge.</summary>
    public string ActiveAreaDimsPercentText => TabletArea is { FullWidth: > 0, FullHeight: > 0 } a
        ? $"{a.EffWidth / a.FullWidth * 100:0}% × {a.EffHeight / a.FullHeight * 100:0}%" : "—";

    /// <summary>Debounce rapid edits (node drags / slider) into a single daemon apply.</summary>
    private void SchedulePersist()
    {
        if (_skipCurvePersist || _applyAction == null || _settings == null) return;
        _persistCts?.Cancel();
        var cts = _persistCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistCurveAsync);
        }
    }

    private async Task PersistCurveAsync()
    {
        if (_applyAction == null || _settings == null) return;
        var dynamics = new PenDynamicsSettings(Curve, PressureSmoothing, PositionSmoothing, SmoothAfterCurve);
        PressureCurveProfile.Write(_settings, _profile.Tablet ?? "", dynamics, PressureCurveEnabled);
        // The write mutated _profile.Filters (added/enabled/disabled the DynamicsFilter); reflect that
        // in the Filters tab and JSON view immediately rather than waiting for a manual Refresh.
        UpdateFiltersDisplay();
        await _applyAction(_settings);
    }

    // ── Hover limit tab (#188) ──────────────────────────────────

    public const int DefaultMaxHoverDistance = 127; // sensible mid-point when first enabled (range 0-255)
    public int HoverDistanceMax => HoverProfile.MaxDistance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverControlsOpacity))]
    private bool _hoverLimitEnabled;
    [ObservableProperty] private bool _canEditHover;
    [ObservableProperty] private double _maxHoverDistance = DefaultMaxHoverDistance;
    /// <summary>Only track while the pen is in the tablet's near-proximity band (Wacom's default). (#311)</summary>
    [ObservableProperty] private bool _nearProximityOnly;

    public string MaxHoverDistanceText => ((int)MaxHoverDistance).ToString();
    /// <summary>Dim the hover controls when the limit is off so they read as inactive (like Dynamics).</summary>
    public double HoverControlsOpacity => HoverLimitEnabled ? 1.0 : 0.4;

    private bool _skipHoverPersist;
    private CancellationTokenSource? _hoverPersistCts;

    partial void OnHoverLimitEnabledChanged(bool value) => SchedulePersistHover();
    partial void OnNearProximityOnlyChanged(bool value) => SchedulePersistHover();

    partial void OnMaxHoverDistanceChanged(double value)
    {
        OnPropertyChanged(nameof(MaxHoverDistanceText));
        SchedulePersistHover();
    }

    private void SchedulePersistHover()
    {
        if (_skipHoverPersist || _applyAction == null || _settings == null) return;
        _hoverPersistCts?.Cancel();
        var cts = _hoverPersistCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistHoverAsync);
        }
    }

    private async Task PersistHoverAsync()
    {
        if (_applyAction == null || _settings == null) return;
        HoverProfile.Write(_settings, _profile.Tablet ?? "", (int)MaxHoverDistance, HoverLimitEnabled, NearProximityOnly);
        UpdateFiltersDisplay();
        await _applyAction(_settings);
    }

    public void Dispose()
    {
        if (_deviceData != null) _deviceData.DataLoaded -= RefreshDetectionStatus;
        DeveloperSettings.Instance.PropertyChanged -= OnDeveloperSettingsChanged;
        StopLiveInput();
    }
}

/// <summary>One label/value row in the tablet ABOUT tab's spec list.</summary>
public sealed record TabletFact(string Label, string Value);

/// <summary>A single filter on a tablet's profile, shown as a card in the Filters tab.</summary>
public sealed class FilterCardViewModel
{
    public FilterCardViewModel(string title, string fullPath, bool enabled,
        ProfileFilterMaintenance.FilterOrigin origin)
    {
        Title = title;
        FullPath = fullPath;
        Enabled = enabled;
        IsLegacy = origin == ProfileFilterMaintenance.FilterOrigin.Legacy;
    }

    /// <summary>Friendly label (e.g. "Pen Dynamics") or the raw type name for unknown filters.</summary>
    public string Title { get; }
    /// <summary>The filter's full type path (with namespace) — the subtitle. Showing the namespace is
    /// what makes a stale duplicate (old vs current namespace) visibly distinct rather than identical.</summary>
    public string FullPath { get; }
    public bool Enabled { get; }
    public string StatusText => Enabled ? "Enabled" : "Disabled";
    /// <summary>True for a filter left over from an older app/plugin name — inert (the driver has no
    /// plugin for it) and normally cleaned on load, but flagged so a stray one stands out.</summary>
    public bool IsLegacy { get; }
}

public partial class PenSwitchRowViewModel : ObservableObject
{
    private readonly PenSwitchKind _kind;
    private readonly int _penButtonIndex;
    private readonly Func<PenSwitchKind, int, PluginSettingStore, Task> _applyAsync;

    public PenSwitchRowViewModel(
        PenSwitchKind kind,
        int penButtonIndex,
        PluginSettingStore? store,
        bool canEdit,
        Func<PenSwitchKind, int, PluginSettingStore, Task> applyAsync)
    {
        _kind = kind;
        _penButtonIndex = penButtonIndex;
        _applyAsync = applyAsync;
        SectionLabel = kind switch
        {
            PenSwitchKind.Tip => "PEN TIP",
            PenSwitchKind.Eraser => "ERASER",
            PenSwitchKind.PenButton => $"BUTTON {penButtonIndex}",
            _ => "SWITCH"
        };
        RefreshFromStore(store, canEdit);
    }

    public string SectionLabel { get; }

    /// <summary>First row in the merged Pen Switches card — its separating divider is hidden.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private string _bindingLabel = "None";
    [ObservableProperty] private PenSwitchBindingMode _mode;
    [ObservableProperty] private bool _canEdit;

    public bool IsAutoSelected => Mode == PenSwitchBindingMode.Auto;
    public bool IsLegacySelected => Mode == PenSwitchBindingMode.Legacy;
    public bool IsOtherSelected => Mode == PenSwitchBindingMode.Other;
    public bool IsRecommended => Mode == PenSwitchBindingMode.Auto;
    public bool IsNotRecommended => Mode != PenSwitchBindingMode.Auto;

    partial void OnModeChanged(PenSwitchBindingMode value)
    {
        OnPropertyChanged(nameof(IsAutoSelected));
        OnPropertyChanged(nameof(IsLegacySelected));
        OnPropertyChanged(nameof(IsOtherSelected));
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(IsNotRecommended));
        SetAutoCommand.NotifyCanExecuteChanged();
        SetLegacyCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        SetAutoCommand.NotifyCanExecuteChanged();
        SetLegacyCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromStore(PluginSettingStore? store, bool canEdit)
    {
        CanEdit = canEdit;
        Mode = PenSwitchBinding.DetectMode(store, _kind, _penButtonIndex);
        BindingLabel = PenSwitchBinding.GetDisplayLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private bool CanSetAuto => CanEdit && Mode != PenSwitchBindingMode.Auto;
    private bool CanSetLegacy => CanEdit && Mode != PenSwitchBindingMode.Legacy;

    [RelayCommand(CanExecute = nameof(CanSetAuto))]
    private Task SetAuto() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeAdaptiveBinding(_kind, _penButtonIndex));

    [RelayCommand(CanExecute = nameof(CanSetLegacy))]
    private Task SetLegacy() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeLegacyBinding(_kind));
}

public partial class ButtonBinding : ObservableObject
{
    private readonly Func<int, AuxBinding, Task>? _applyBinding;
    private readonly Func<AuxBinding, string, Task<AuxBinding?>>? _editBinding;
    private AuxBinding _applied;

    public ButtonBinding(int index, AuxBinding binding, bool isOtherBinding, string otherLabel,
        bool canEdit, Func<int, AuxBinding, Task>? applyBinding, string? label = null,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding = null)
    {
        Index = index;
        _label = label;
        IsOtherBinding = isOtherBinding;
        OtherLabel = otherLabel;
        CanEdit = canEdit;
        _applyBinding = applyBinding;
        _editBinding = editBinding;
        _applied = binding;
    }

    public int Index { get; }
    private readonly string? _label;
    /// <summary>Row title. Defaults to "Button N"; wheel rows pass a custom label (the direction).</summary>
    public string Label => _label ?? $"Button {Index}";

    /// <summary>Read-only summary shown on the card: the friendly name of a binding this editor can't
    /// model, else "Ctrl + Z" / "Left click" / "Scroll up" / "Unbound".</summary>
    public string Summary => IsOtherBinding && !_applied.IsBound ? OtherLabel : AuxKeyBinding.Describe(_applied);

    /// <summary>False disables the Edit button (read-only host, or button mapping suspended).</summary>
    public bool CanEdit { get; }

    /// <summary>True when this button already holds a binding this editor can't model (Windows Ink, an
    /// adaptive binding, or a multi-key macro) — the summary shows its friendly name until it's replaced.</summary>
    public bool IsOtherBinding { get; }
    public string OtherLabel { get; }

    /// <summary>True while the physical button is held down — highlights the card live.</summary>
    [ObservableProperty] private bool _isPressed;

    /// <summary>Open the modal editor and apply the result — a binding, or <see cref="AuxBinding.Unbound"/>
    /// from Clear. Cancel (null) leaves the binding untouched. Nothing is applied until the dialog
    /// returns, so there's no inline apply-on-change to loop.</summary>
    [RelayCommand]
    private async Task Edit()
    {
        if (_editBinding == null || !CanEdit) return;
        var result = await _editBinding(_applied, Label);
        if (result is not { } binding) return; // cancelled
        if (binding == _applied) return;        // no change
        _applied = binding;
        OnPropertyChanged(nameof(Summary));
        if (_applyBinding != null) await _applyBinding(Index, binding);
    }
}
