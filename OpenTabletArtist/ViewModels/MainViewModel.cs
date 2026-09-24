using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OtdInterop;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Application shell: owns navigation and the composed page view models, plus the shared
/// <see cref="AppSession"/> (daemon connection + settings + data) and the
/// <see cref="IDialogService"/>. It holds no feature state of its own and no longer pushes data
/// into pages — each page VM self-subscribes to the session's role interfaces (#41 follow-up).
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // Used for preset files. The library's own writer, which the coordinator uses for the daemon's
    // active settings file, is internal and not handed out -- but this store takes a path and would
    // write any path given to it, so that is caller discipline rather than a rule (#807).
    private readonly IPresetStore _presetStore = new PresetStore(AppLogBridge.Instance);
    private readonly AppSession _session;
    private readonly DaemonStatusViewModel _daemonStatus;
    private readonly TabletAutoMapper _autoMapper;
    private readonly WindowsInkAutoSetup _winInkAutoSetup;
    private readonly IDialogService _dialogs;
    private readonly DriverConflictMonitor _conflicts;
    private readonly HealthService _health;
    private readonly ProfileSwitchService _profileSwitch;
    // One shared hotkey window holds every global hotkey; the managers each register their own ids on it.
    private readonly GlobalHotkeyService _globalHotkeys;
    private readonly ProfileHotkeyManager _profileHotkeys;
    private readonly MonitorCycleService _monitorCycle;
    private readonly MonitorCycleHotkeys _monitorHotkeys;

    // One cached settings VM per tablet (heavy: holds subscriptions). Reconciled on each data load.
    private readonly Dictionary<string, TabletDetailViewModel> _tabletDetails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Daemon connection state + Start/Stop/Restart commands — surfaced for the tray menu (#72).</summary>
    public IConnectionState Connection => _session;

    /// <summary>Active-profile override state (#320) — the shell binds this for the "Profile override" cue.</summary>
    public ProfileSwitchService ProfileSwitch => _profileSwitch;

    /// <summary>Monitor-cycle switch events (#89) — the shell subscribes to show a toast on cycle.</summary>
    public MonitorCycleService MonitorCycle => _monitorCycle;

    /// <summary>
    /// Settles settings work already in flight and closes the session, for an exit that can wait (#828).
    /// </summary>
    /// <remarks>
    /// Called from the tray's Quit, which is already asynchronous and already bounds its shutdown steps.
    /// The window's <c>Closed</c> handler still runs <see cref="Dispose"/>, which cannot wait — that is
    /// the path an exit takes when nobody asked for one, and it remains the fallback rather than the
    /// intended route.
    /// </remarks>
    public Task<bool> CloseSessionAsync(TimeSpan settleWithin) => _session.CloseAsync(settleWithin);

    /// <summary>
    /// Decides what "stop the daemon" means while it is still connected, for an exit that stops it after
    /// closing (#828).
    /// </summary>
    /// <remarks>
    /// Returns null when the user declines the confirmation, which is asked here while there is still a
    /// window to ask in.
    /// </remarks>
    public Task<Func<Task>?> PrepareDaemonStopAsync() => _session.PrepareDaemonStopAsync();

    // Surfaced for the tray's tablet actions (#186/#187): the dynamics-reveal line and the
    // Open Tablet Settings / Switch Display items read device data, persist via the settings
    // coordinator, and open the per-tablet dialog through the dialog service.
    public IDeviceData DeviceData => _session;
    public ISettingsCoordinator SettingsCoordinator => _session;
    public IDialogService Dialogs => _dialogs;

    // About folded into Home's right column (#combine) — Home owns its own AboutViewModel now, so there's
    // no standalone About page or nav item.
    public DriverCleanupViewModel DriverCleanup { get; }
    public CustomTabletConfigsViewModel Configs { get; }
    public PresetsViewModel Presets { get; }
    public HotkeysViewModel Hotkeys { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public DashboardViewModel Dashboard { get; }
    public TestViewModel Test { get; }
    public LogViewModel Log { get; }
    public PluginsViewModel Plugins { get; }
    public DaemonViewModel Daemon { get; }
    public WindowsInkViewModel WindowsInk { get; }
    public VMultiViewModel VMulti { get; }
    /// <summary>The ADVANCED page: OpenTabletDriver's own surfaces as tabs — Daemon / Console / Configs /
    /// Diagnostics / Plugins. Windows Ink moved onto Plugins (it is a plugin, #winink-to-plugins); VMulti
    /// and Driver Cleanup moved to SETTINGS → Drivers (#vmulti-to-drivers, #drivers-tab).</summary>
    public AdvancedViewModel Advanced { get; }
    /// <summary>The SETTINGS page: OTA's own preferences as tabs — Presets / Hotkeys / Theme / System /
    /// Drivers / Dev — split out of ADVANCED into their own page.</summary>
    public SettingsViewModel Settings { get; }
    public StartupViewModel Startup { get; } = new();
    /// <summary>The DEVELOPER content, hosted as the SETTINGS → Dev tab. Assigned in the constructor (not a field
    /// initializer) so its break-config commands can reach the session's settings coordinator + device data.</summary>
    public DeveloperViewModel Developer { get; }
    public ThemeViewModel Theme { get; } = new();
    /// <summary>The SETTINGS → Shortcut tab (create a Start-menu shortcut; Windows-only).</summary>
    public ShortcutViewModel Shortcut { get; } = new();
    public DesktopEntryViewModel DesktopEntry { get; } = new();

    /// <summary>Tablets list + supported-tablets link, now rendered as a section of Home (the standalone
    /// Tablets page was merged in). Populated by <see cref="RebuildTablets"/> on each data load.</summary>
    public TabletsOverviewViewModel TabletsOverview { get; }

    /// <summary>The single TABLET page (#542): one page in the page menu, hosting a switcher dropdown over
    /// the selected tablet's detail view. Replaces the old per-tablet nav children. Its tablet list +
    /// default selection are refreshed by <see cref="RebuildTablets"/> on each data load.</summary>
    public TabletPageViewModel TabletPage { get; }

    /// <summary>The PEN page (#pen-split): the pen settings (basics · pressure) split out of the
    /// tablet page into their own top-level section. Same switcher/selection as the tablet page and shares
    /// the per-tablet detail cache, so both edit the same tablet VM. Refreshed by <see cref="RebuildTablets"/>.</summary>
    public PenPageViewModel PenPage { get; }

    // The active page is the VM instance itself (typed navigation, #15). The content host
    // resolves it to a view via DataTemplates keyed by VM type, so there's no page-name string,
    // no view-lookup converter, and no per-view DataContext re-point.
    [ObservableProperty] private ObservableObject? _currentPage;

    /// <summary>Pages that manage their own scrolling get the outer content ScrollViewer <c>Disabled</c>,
    /// which bounds them to the viewport so their inner scroll engages and their fixed chrome stops
    /// scrolling away: the tablet detail page (fixed header + per-tab scroll, #507), and the ADVANCED
    /// <c>Console</c> tab (fixed toolbar + an internally-scrolling log list, so it never shows dual
    /// scrollbars). Every other page is plain content and uses the outer scroll (<c>Auto</c>).</summary>
    public Avalonia.Controls.Primitives.ScrollBarVisibility ContentScrollBarVisibility =>
        CurrentPage is TabletPageViewModel or TabletDetailViewModel
        || (CurrentPage is AdvancedViewModel adv && adv.SelectedTab == Domain.AdvancedTab.Console)
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

    /// <summary>The whole page menu as one ordered, data-driven list (Zune Phase 0.2): HOME ·
    /// TABLET · PEN · SCRIBBLE · SETTINGS · ADVANCED. Each entry has a label, its target page, a selection
    /// flag the page menu highlights (synced in <see cref="OnCurrentPageChanged"/>), and a visibility flag.
    /// Modelling every entry identically — not just the middle ones — lets the shell swap the page menu
    /// for a horizontal wordmark bar without special-casing any node.</summary>
    public ObservableCollection<NavLeafViewModel> NavSections { get; } = new();

    public MainViewModel()
    {
        var daemonLifecycle = new DaemonLifecycleService();
        _session = new AppSession(
            OtdSession.Create(AppLogBridge.Instance, daemonLifecycle),
            daemonLifecycle);
        var dialogs = new DialogService(_session);
        _dialogs = dialogs;
        _session.ResolveUnsavedChanges = ResolveUnsavedSettingsAsync;

        // Stopping or restarting a daemon this app didn't start asks first (#613, option 2). Wired here
        // rather than injected because DialogService is built FROM the session, so it cannot be a
        // constructor argument to it. Naming the path matters more than naming the act: "an external
        // daemon" is abstract, the exe it is running is the thing the user recognises.
        _session.ConfirmForeignDaemonAction = verb =>
        {
            var known = !string.IsNullOrEmpty(_session.DaemonSourcePath);

            // Two different situations, and claiming the first when we're in the second asserts something
            // OTA cannot know. The daemon is single-instance, so an unreadable path is not ambiguity about
            // which one — it is a process OTA can't see into (another user, or elevated).
            var subject = known
                ? $"OpenTabletArtist didn't build the OpenTabletDriver that's running:\n\n{_session.DaemonSourcePath}"
                : "OpenTabletArtist can't read where the running OpenTabletDriver lives.";

            // Restart is the sharper one: it doesn't just stop that daemon, it starts one in its place.
            // Which one is not a guess. AppSession pins the executable it first connected to, and restart
            // relaunches exactly that; only when the path could not be read does it fall back to OTA's own
            // copy. Saying "whichever OpenTabletArtist finds" was true of the old four-tier ladder and is
            // now wrong in the case the artist is most likely to be in (#936).
            var consequence = verb == "restart"
                ? known
                    ? "Restarting stops it and starts the same one again."
                    : "Restarting stops it, then starts the OpenTabletDriver this app ships — "
                      + "which is not the one running now."
                : "Stopping it affects anything else using it.";

            // Off Windows there's no pipe-to-process lookup, so Stop can only stop them all.
            var breadth = !OperatingSystem.IsWindows() && verb == "stop"
                ? "\n\nThis stops every OpenTabletDriver daemon running, not just one."
                : "";

            return dialogs.ShowConfirmAsync(
                verb == "restart" ? "Restart the OTD daemon?" : "Stop this daemon?",
                $"{subject}\n\n{consequence}{breadth}");
        };
        TabletsOverview = new TabletsOverviewViewModel();

        // Conflicting-driver detection (#245), shared by the Driver cleanup page and the Home alert.
        _conflicts = new DriverConflictMonitor(_session.Daemon, _session);

        // Health-check catalog (#317): shared source of the "Needs attention" issues for Home + pages.
        // Takes the conflict monitor too so a conflicting driver surfaces as a health issue.
        _health = new HealthService(_session, _session, _conflicts);

        // First-detection auto-mapping (#362): map a brand-new tablet to the primary display so it
        // doesn't span every monitor out of the box. Only ever acts once per tablet (persisted).
        _autoMapper = new TabletAutoMapper(_session);

        // Windows Ink auto-setup: install the plugin if missing and switch tablets to Windows Ink
        // (once VMulti is functional), so the user never has to configure Windows Ink themselves.
        _winInkAutoSetup = new WindowsInkAutoSetup(_session);

        // Global hotkeys (#320, #89): one shared hotkey window; the profile-switch manager and the
        // monitor-cycle manager each register their own chords on it and filter presses by their ids.
        _globalHotkeys = new GlobalHotkeyService();
        _profileSwitch = new ProfileSwitchService(_session, _presetStore, () => _session.PresetDirectory);
        _profileSwitch.BeforeReplaceAsync = restoring => restoring
            ? ConfirmRevertAsync() : ResolveUnsavedSettingsAsync();
        _profileHotkeys = new ProfileHotkeyManager(_globalHotkeys, _profileSwitch);
        _monitorCycle = new MonitorCycleService(_session, _session);
        _monitorHotkeys = new MonitorCycleHotkeys(_globalHotkeys, _monitorCycle);
        // Page VMs depend on the session through its role interfaces and on IDialogService,
        // and self-subscribe to the session's data load / connection state.
        DriverCleanup = new DriverCleanupViewModel(dialogs, _conflicts);
        Configs = new CustomTabletConfigsViewModel(dialogs,
            new ConfigurationsDirectoryProvider(() => _session.ConfigurationDirectory));
        Presets = new PresetsViewModel(_presetStore, _session, _session, dialogs, _profileHotkeys, _profileSwitch);
        Hotkeys = new HotkeysViewModel(_profileHotkeys, _monitorHotkeys, dialogs, _session);
        Diagnostics = new DiagnosticsViewModel(_session.Daemon, _session, _session);
        WindowsInk = new WindowsInkViewModel(_session, dialogs, _health);
        VMulti = new VMultiViewModel(dialogs, _health);
        // Shared daemon status/control surface for the Home problem card + the Daemon tab.
        _daemonStatus = new DaemonStatusViewModel(_session, () => OpenAdvancedTab(AdvancedTab.Daemon));
        Dashboard = new DashboardViewModel(_session, _daemonStatus, dialogs, NavigateToTabletByName, _health, TabletsOverview,
            () => OpenSettingsTab(SettingsTab.Drivers),  // Driver Cleanup has its own pivot (#drivers-tab)
            () => OpenAdvancedTab(AdvancedTab.Plugins),  // Windows Ink → Plugins pivot (#winink-to-plugins)
            () => OpenSettingsTab(SettingsTab.Drivers),  // VMulti → SETTINGS → DRIVERS (#vmulti-to-drivers)
            () => OpenAdvancedTab(AdvancedTab.CustomTabletConfigs),
            NavigateToPenByName);                        // pen-behaviour "Fix" → PEN page (#pen-split)
        Test = new TestViewModel(_session.Daemon, _session);
        Log = new LogViewModel(_session.Daemon, _session);
        // Windows Ink moved to PLUGINS from the pivot now called VMULTI, so it is handed to that page —
        // and only
        // on Windows, since PLUGINS (unlike DRIVERS) is not filtered off other platforms.
        Plugins = new PluginsViewModel(_session, _session,
            OperatingSystem.IsWindows() ? WindowsInk : null);
        Daemon = new DaemonViewModel(_daemonStatus, _health);
        // Developer page: its "introduce a real config error" commands act on the live tablet settings.
        Developer = new DeveloperViewModel(_session, _session);

        // The ADVANCED page groups OpenTabletDriver's own surfaces behind one page-menu entry, with
        // its own tab menu, like the tablet page. It shares the sub-view models built above.
        Advanced = new AdvancedViewModel(Daemon, Configs, Diagnostics, Log, Plugins);
        // The Console tab manages its own scroll, so the outer scroll toggles as the ADVANCED tab changes
        // (not just when the top-level page changes) — re-evaluate ContentScrollBarVisibility on tab switch.
        Advanced.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AdvancedViewModel.SelectedTab))
                OnPropertyChanged(nameof(ContentScrollBarVisibility));
        };
        // The SETTINGS page holds OTA's own preferences as tabs, sharing the same VM instances,
        // under its own page-menu entry in front of ADVANCED. Presets (#571) and Developer
        // (#572) are folded in as tabs.
        Settings = new SettingsViewModel(Startup, Hotkeys, Theme, Shortcut, DesktopEntry, DriverCleanup,
            VMulti, Presets, Developer);

        // The single TABLET page (#542): a switcher dropdown over the selected tablet's headerless detail
        // view. It resolves detail VMs through the shell (which owns the per-tablet cache + daemon plumbing).
        // Its switcher is linked to the app-wide active tablet (IDeviceData.ActiveTabletName), so the TABLET,
        // PEN, and SCRIBBLE dropdowns all share one selection: picking a tablet here sets the active tablet,
        // and the default follows it. SyncPagesToActiveTablet pushes external changes back (see below).
        TabletPage = new TabletPageViewModel(ResolveTabletDetail,
            getLastUsed: () => _session.ActiveTabletName,
            setLastUsed: name => _session.SetActiveTablet(name));
        // The PEN page shares the same resolver (so it edits the same per-tablet detail VM as the tablet
        // page) and the same active-tablet selection.
        PenPage = new PenPageViewModel(ResolveTabletDetail,
            getLastUsed: () => _session.ActiveTabletName,
            setLastUsed: name => _session.SetActiveTablet(name));

        // The whole top-level nav as one ordered list (Zune Phase 0.2). Presets live in
        // SETTINGS tabs (#571). Selection is synced in OnCurrentPageChanged. TABLET's page carries a
        // placeholder when no tablet is known; SETTINGS + ADVANCED each open a tabbed page.
        NavSections.Add(new NavLeafViewModel("HOME", Dashboard));
        NavSections.Add(new NavLeafViewModel("TABLET", TabletPage));
        NavSections.Add(new NavLeafViewModel("PEN", PenPage));
        NavSections.Add(new NavLeafViewModel("SCRIBBLE", Test));
        NavSections.Add(new NavLeafViewModel("SETTINGS", Settings));
        NavSections.Add(new NavLeafViewModel("ADVANCED", Advanced));

        // Keep the TABLET + PEN switchers pointed at the app-wide active tablet, so all three tablet
        // switchers (incl. SCRIBBLE, which drives it directly) stay linked when it changes from anywhere
        // (another switcher, the tray, or auto-selection on connect).
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IDeviceData.ActiveTabletName))
                SyncPagesToActiveTablet();
        };

        // Build the per-tablet nav children now and on every data load (tablets connect/pair/forget).
        _session.DataLoaded += RebuildTablets;
        // Then reconcile any open tablet page with the freshly-loaded settings so an external edit
        // (e.g. via the OTD UX) is picked up. Subscribed after RebuildTablets so it runs on survivors.
        _session.DataLoaded += ReconcileOpenTabletDetails;
        ReportPendingInputTo(_session);

        // A different OpenTabletDriver answering means every open editor is looking at another machine's
        // settings. Editors are cached by tablet name, so one survives a replacement that happens to
        // expose the same name -- and a change it was holding belonged to the daemon that has gone
        // (#905).
        _session.SettingsReplaced += ResetEditorInput;
        _session.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PageInputEnabled));
        RebuildTablets();

        CurrentPage = Dashboard;

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        // Auto-start the daemon if needed and begin connecting (session owns connect + polling).
        await _session.StartAndConnectAsync();
    }

    [RelayCommand]
    private void Navigate(object page) => CurrentPage = page as ObservableObject;

    /// <summary>Open the ADVANCED tabbed page on a specific tab — the deep-link target for health-issue
    /// "Fix" buttons (Windows Ink, VMulti, Driver Cleanup) and the Home daemon card's "Open daemon page".</summary>
    private void OpenAdvancedTab(AdvancedTab tab)
    {
        Advanced.SelectedTab = tab;
        Navigate(Advanced);
    }

    /// <summary>Open the SETTINGS tabbed page on a specific tab (the command-palette deep-link target).</summary>
    private void OpenSettingsTab(SettingsTab tab)
    {
        Settings.SelectedTab = tab;
        Navigate(Settings);
    }

    /// <summary>The ordered set of pages the "screenshot all pages" developer aid visits (#437): Home,
    /// every visible root nav leaf, and each ADVANCED sub-tab. Each entry is a filename slug + the action
    /// that navigates to it; the caller renders after each and restores the original page.</summary>
    public IReadOnlyList<(string Slug, Action Navigate)> ScreenshotTargets()
    {
        var list = new List<(string, Action)> { ("home", () => CurrentPage = Dashboard) };
        // Each tablet, shown by selecting it on the single TABLET page.
        foreach (var choice in TabletPage.Tablets)
        {
            var name = choice.Name;
            list.Add(($"tablet-{Slugify(name)}", () => NavigateToTabletByName(name)));
        }
        // The simple leaf sections (Scribble) — Home, Tablet, Settings, and Advanced are captured
        // separately above/below (Home explicitly, each tablet via the switcher, Settings/Advanced by tab).
        foreach (var leaf in NavSections.Where(l => l.IsVisible
                     && !ReferenceEquals(l.Page, Dashboard) && !ReferenceEquals(l.Page, TabletPage)
                     && !ReferenceEquals(l.Page, Settings) && !ReferenceEquals(l.Page, Advanced)))
        {
            var page = leaf.Page;
            list.Add((Slugify(leaf.Label), () => CurrentPage = page));
        }
        // Capture the visible tabs only.
        foreach (var tab in Advanced.Tabs.Where(t => t.IsVisible))
        {
            var t = tab.Tab;
            list.Add(($"advanced-{Slugify(t.ToString())}", () => OpenAdvancedTab(t)));
        }
        foreach (var tab in Settings.Tabs.Where(t => t.IsVisible))
        {
            var t = tab.Tab;
            list.Add(($"settings-{Slugify(t.ToString())}", () => OpenSettingsTab(t)));
        }
        return list;
    }

    private static string Slugify(string s) => s.ToLowerInvariant().Replace(' ', '-');

    /// <summary>Resolve (lazily creating + caching) a tablet's detail VM by name for the TABLET page host
    /// (#542). Returns null if no such profile exists. The VM is headerless — the page owns the header.</summary>
    private TabletDetailViewModel? ResolveTabletDetail(string name)
    {
        if (!_tabletDetails.TryGetValue(name, out var vm))
        {
            vm = _dialogs.CreateTabletDetail(name, () => ForgetTabletByNameAsync(name),
                () => OpenAdvancedTab(AdvancedTab.CustomTabletConfigs));
            if (vm == null) return null;
            _tabletDetails[name] = vm;
        }
        vm.ShowHeader = false;
        return vm;
    }

    /// <summary>Open the TABLET page on a specific tablet (the Home cards, a health-issue "Fix" deep-link),
    /// optionally deep-linking to one of its tabs.</summary>
    private void NavigateToTabletByName(string name, TabletDetailTab? tab = null)
    {
        TabletPage.Select(name, tab);
        CurrentPage = TabletPage;
    }

    /// <summary>Open the PEN page on a specific tablet, optionally deep-linking to a pen pivot (the
    /// pen-behaviour health "Fix"). The pen settings live on their own page now (#pen-split).</summary>
    private void NavigateToPenByName(string name, TabletDetailTab? tab = null)
    {
        PenPage.Select(name, tab);
        CurrentPage = PenPage;
    }

    private async Task ForgetTabletByNameAsync(string name)
    {
        // A connected tablet can't truly be removed: the daemon regenerates a default profile for any
        // detected device (ProfileCollection.GetProfile → Generate), so forgetting it clears its saved
        // settings and it comes back at defaults — it stays in the list. A remembered (disconnected)
        // tablet is removed outright. Word the confirm to match either case (#575).
        bool connected = _session.DetectedTablets.Any(d => d.Name == name);
        var (title, message) = connected
            ? ("Reset this tablet?",
               $"\"{name}\" is connected, so this clears its saved settings and returns it to defaults — " +
               "it stays in your list. This can't be undone.")
            : ("Forget this tablet?",
               $"\"{name}\" and its saved settings will be removed from your list. This can't be undone.");
        if (!await _dialogs.ShowConfirmAsync(title, message))
            return;

        var settings = _session.CurrentSettings;
        var profile = settings?.Profiles.FirstOrDefault(p => p.Tablet == name);
        if (settings != null && profile != null)
        {
            settings.Profiles.Remove(profile);
            // Reload rebuilds the list + prunes the VM; the TABLET page reselects a survivor (or shows the
            // no-tablet placeholder) in RebuildTablets.
            await _session.ApplySettingsAsync(settings);
        }
    }

    /// <summary>Reconcile the TABLET page's switcher list + cached page VMs with the session's tablets.</summary>
    private void RebuildTablets()
    {
        var ordered = _session.Profiles
            .OrderByDescending(p => p.IsDetected)
            .ThenByDescending(p => p.LastSeen ?? DateTime.MinValue)
            .ThenBy(p => p.Tablet, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = new HashSet<string>(ordered.Select(p => p.Tablet), StringComparer.OrdinalIgnoreCase);

        // Drop cached detail VMs for tablets that are gone; the TABLET page reselects a survivor below.
        foreach (var key in _tabletDetails.Keys.Where(k => !names.Contains(k)).ToList())
        {
            _tabletDetails[key].Dispose();
            _tabletDetails.Remove(key);
        }

        // Rebuild the richer overview rows (Home cards, same order) — status, last-seen, navigable (#307).
        TabletsOverview.Tablets = ordered
            .Select(p =>
            {
                var tablet = p.Tablet;
                return new TabletOverviewItemViewModel(tablet, p.IsDetected, p.StatusText,
                    p.StatusDetail,
                    () => NavigateToTabletByName(tablet),
                    () => ForgetTabletByNameAsync(tablet));
            })
            .ToList();
        TabletsOverview.HasTablets = ordered.Count > 0;

        // Refresh the TABLET + PEN pages' switcher + default selection (first detected / last-used, #542).
        // Runs after the prune so a reselection resolves a fresh (surviving) detail VM.
        var choices = ordered.Select(p => (p.Tablet, p.IsDetected)).ToList();
        TabletPage.SetTablets(choices);
        PenPage.SetTablets(choices);
        // Both switchers now reflect the shared active tablet (e.g. after the daemon auto-selected one) —
        // but only where they have nothing valid selected. This runs on every ~15s poll, so it must fill
        // gaps, not overrule the user (#tablet-selection-sticks).
        SyncPagesToActiveTablet(force: false);
    }

    /// <summary>Point the TABLET + PEN switchers at the app-wide active tablet, keeping all three tablet
    /// switchers (incl. SCRIBBLE) linked. A no-op when they already match; suppresses re-persist so it
    /// doesn't loop back into <see cref="AppSession.SetActiveTablet"/>.
    ///
    /// <para><paramref name="force"/> distinguishes the two callers. When the active tablet genuinely
    /// CHANGED (the tray, another switcher, auto-select on connect) the pages must follow, so it forces.
    /// The periodic rebuild passes false: there it is only filling in a selection that is missing or
    /// stale, and must not overwrite one the user made. Forcing there was the bug behind "selecting a
    /// remembered tablet doesn't stick" — ActiveTabletName only ever names a CONNECTED tablet
    /// (SetActiveTablet ignores anything else, deliberately, so single-target flows can't point at a
    /// disconnected one), so re-asserting it every ~15s poll dragged the switcher off any tablet that
    /// was merely remembered, seconds after the user picked it.</para></summary>
    private void SyncPagesToActiveTablet(bool force = true)
    {
        if (_session.ActiveTabletName is not { } name) return;
        TabletPage.SyncSelection(name, force);
        PenPage.SyncSelection(name, force);
    }

    /// <summary>Drop debounced input before replacing the editable document.</summary>
    private void ResetEditorInput()
    {
        if (HasPendingInput)
            _session.DiscardedChangeNotice = "Pending local input was discarded when the settings workspace was replaced.";
        foreach (var editor in _tabletDetails.Values) editor.ResetPendingEdits();
    }

    private void ReconcileOpenTabletDetails() =>
        EditorReconciliation.Forward(_session.CurrentSettings, _tabletDetails);

    public AppSession SettingsSession => _session;
    public bool PageInputEnabled => !_session.SettingsBusy &&
        (CurrentPage is not (TabletPageViewModel or PenPageViewModel or TabletDetailViewModel)
            || _session.CanEditSettings);

    private bool HasPendingInput => _tabletDetails.Values.Any(e => e.HasPendingEdits);

    /// <summary>
    /// Lets the session ask whether anything is half-edited before it adopts an outside change (#920).
    /// </summary>
    /// <remarks>
    /// Wired from here because this is where the cached editors are. The session cannot see them and the
    /// library is told nothing about them at all; the question "may local work be replaced" is the
    /// shell's to answer.
    /// </remarks>
    private void ReportPendingInputTo(AppSession session) =>
        session.HasPendingEditorInput = () => HasPendingInput;

    [RelayCommand]
    private async Task SaveSettings() => await SaveNowAsync();

    private async Task<bool> SaveNowAsync()
    {
        if (_session.SettingsBusy) return false;
        _session.SettingsBusy = true;
        try
        {
            foreach (var editor in _tabletDetails.Values.ToArray())
                await editor.FlushPendingEditsAsync();
            return await _session.SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't finish pending edits before saving.", ex);
            _session.SaveState = SettingsSaveState.ApplyFailed;
            return false;
        }
        finally { _session.SettingsBusy = false; }
    }

    [RelayCommand]
    private async Task RevertSettings() => await _profileSwitch.RestoreDefaultAsync();

    private async Task<bool> ConfirmRevertAsync()
    {
        if (_session.SettingsBusy || _prompting) return false;
        _prompting = true;
        try
        {
            if (!await _dialogs.ShowConfirmAsync("Revert to saved settings?",
                "This replaces the OTD daemon's live settings with its saved file and discards unsaved edits. Nothing is saved."))
                return false;
            ResetEditorInput();
            return true;
        }
        finally { _prompting = false; }
    }

    [RelayCommand]
    private async Task ReloadSettings()
    {
        if (_session.SettingsBusy) return;
        if ((HasPendingInput || _session.SettingsPaused) && !await _dialogs.ShowConfirmAsync(
            "Reload current OTD daemon settings?",
            // "your applied changes survive this" was a promise this cannot keep: another editor, the
            // daemon's own recovery, or a write nobody waited for may have replaced them already. Reload
            // shows what is there; it cannot vouch for how it got there.
            "Pending local edits will be discarded. Reload reads the OTD daemon's current live "
            + "settings. It does not restore the saved file or save anything. Applied changes are "
            + "retained only if they are still present in the OTD daemon."))
            return;
        _session.SettingsBusy = true;
        try
        {
            ResetEditorInput();
            await _session.ReloadSettingsAsync();
        }
        finally { _session.SettingsBusy = false; }
    }

    private bool _prompting;

    public async Task<bool> ResolveUnsavedSettingsAsync()
    {
        if (_prompting || _session.SettingsBusy) return false;
        if (!_session.HasUnsavedChanges && !HasPendingInput) return true;
        Helpers.UnsavedSettingsChoice choice;
        _prompting = true;
        try { choice = await Helpers.Dialogs.ShowUnsavedSettingsAsync(); }
        finally { _prompting = false; }
        if (choice == Helpers.UnsavedSettingsChoice.Cancel) return false;
        if (choice == Helpers.UnsavedSettingsChoice.Save) return await SaveNowAsync();
        ResetEditorInput();
        return true;
    }

    // Throttle so rapid focus flicker doesn't spam the daemon; the reload itself is coalesced anyway.
    private long _lastActivationReloadTick;

    /// <summary>The window regained focus — re-pull settings so an external edit made while we were in
    /// the background (e.g. the user changed the mapping in the OTD UX and alt-tabbed back) is reflected
    /// promptly instead of waiting for the ~30s fallback poll. The reload flows through the session's
    /// DataLoaded, which runs <see cref="ReconcileOpenTabletDetails"/>.</summary>
    public void OnWindowActivated()
    {
        if (!_session.IsConnected) return;
        var now = Environment.TickCount64;
        if (now - _lastActivationReloadTick < 750) return;
        _lastActivationReloadTick = now;
        _ = _session.ReloadAsync();
    }

    partial void OnCurrentPageChanged(ObservableObject? oldValue, ObservableObject? newValue)
    {
        // Stop the debug stream when leaving the ADVANCED tabbed page (which hosts Diagnostics).
        // The tabbed-page view also stops it on tab-switch; this covers navigating away entirely.
        if (ReferenceEquals(oldValue, Advanced) && !ReferenceEquals(newValue, Advanced))
            _ = Diagnostics.StopDebuggingAsync();

        // Start/stop the Test page's driver-input source so the daemon debug stream is only on
        // while Test is visible (same lifecycle treatment as Diagnostics).
        if (ReferenceEquals(newValue, Test) && !ReferenceEquals(oldValue, Test))
            _ = Test.ActivateAsync();
        else if (ReferenceEquals(oldValue, Test) && !ReferenceEquals(newValue, Test))
            _ = Test.DeactivateAsync();

        // Rescan hotkeys when the settings page opens so newly saved presets are available.
        if (ReferenceEquals(newValue, Settings))
        {
            _ = Hotkeys.RefreshAsync();
        }

        // Refresh the page-menu highlight — every page highlights via its own IsSelected.
        foreach (var section in NavSections)
            section.IsSelected = ReferenceEquals(CurrentPage, section.Page);
        OnPropertyChanged(nameof(ContentScrollBarVisibility));
        OnPropertyChanged(nameof(ShowTabletSwitcher));
        OnPropertyChanged(nameof(PageInputEnabled));
    }

    /// <summary>Whether the shell's top bar shows the tablet switcher (#switcher-in-shell): on the three
    /// pages that scope what they show to one tablet — TABLET, PEN and SCRIBBLE. PenPageViewModel derives
    /// from TabletPageViewModel, so the one type test covers the first two.
    /// <para>
    /// Scribble used to carry its OWN switcher instead, in the page's control row, and only when two or
    /// more tablets were connected — so the app taught two different answers to "is there a switcher, and
    /// where?" (#697). One rule now: it is in the top bar, on all three, always. The binding stays on
    /// TabletPage because every one of them writes through AppSession.SetActiveTablet, and Scribble
    /// follows ActiveTabletName like the others.
    /// </para></summary>
    public bool ShowTabletSwitcher => CurrentPage is TabletPageViewModel or TestViewModel;

    public void Dispose()
    {
        _session.DataLoaded -= RebuildTablets;
        _session.DataLoaded -= ReconcileOpenTabletDetails;
        _session.SettingsReplaced -= ResetEditorInput;
        _autoMapper.Dispose();    // unsubscribes DataLoaded (first-detection auto-mapping)
        _winInkAutoSetup.Dispose(); // unsubscribes DataLoaded (Windows Ink auto-setup)
        Daemon.Dispose();         // stops the connection card's uptime timer + unsubscribes
        _daemonStatus.Dispose();  // unsubscribes from session PropertyChanged
        foreach (var vm in _tabletDetails.Values) vm.Dispose(); // unsubscribe per-tablet detection
        Diagnostics.Dispose();    // stops debugging + unsubscribes connection sync
        Dashboard.Dispose();      // cancels VMulti install/uninstall token + unsubscribes
        Presets.Dispose();        // unsubscribes DataLoaded
        Hotkeys.Dispose();        // unsubscribes DataLoaded
        Test.Dispose();           // stops the daemon debug stream if running
        Log.Dispose();        // unsubscribes the daemon log stream + connection sync
        Plugins.Dispose();        // unsubscribes DataLoaded
        WindowsInk.Dispose();     // unsubscribes DataLoaded + connection sync
        VMulti.Dispose();         // cancels the VMulti install/uninstall token
        DriverCleanup.Dispose();
        _conflicts.Dispose();
        _health.Dispose();        // unsubscribes from DataLoaded + connection changes
        _profileHotkeys.Dispose(); // drops its registrations + event hook (shared service, not disposed here)
        _monitorHotkeys.Dispose(); // drops its registration + event hook
        _globalHotkeys.Dispose();  // destroys the shared message-only hotkey window

        // Last, after everything that unsubscribes from it. It used to sit above DriverCleanup, the
        // conflict monitor and the health service, all of which detach handlers from a session that had
        // already gone. Detaching from a disposed object is harmless in itself, which is why this
        // survived -- but the ordering said those three did not depend on the session, and they do.
        _session.Dispose();       // cancels the connect/poll loops, disposes the daemon client + load gate
    }
}

/// <summary>A flat leaf node in the page navigation bar (#477): a label, the page it opens, a selection
/// flag the page menu highlights (synced by the shell on navigation), and a visibility flag (feature-gated
/// entries hide themselves). Clicking it runs the shell's Navigate command with <see cref="Page"/>.</summary>
public partial class NavLeafViewModel : ObservableObject
{
    public NavLeafViewModel(string label, ObservableObject page, bool isVisible = true)
    {
        Label = label;
        Page = page;
        _isVisible = isVisible;
    }

    public string Label { get; }
    public ObservableObject Page { get; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible;
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

/// <summary>
/// View-model record for a settings snapshot file shown in the Saved Settings list.
/// Plain-property record so Avalonia bindings can resolve Name/LastModified directly
/// (JObject indexer bindings stopped rendering for TextBlock.Text in Avalonia 12).
/// </summary>
public record PresetInfo(string Name, string Path, string Content, string LastModified);
