# Architecture

## System Diagram

```
┌─────────────────────┐                    ┌─────────────────────┐
│  Avalonia App (.NET 10)│     Named Pipe     │   OTD Daemon        │
│  OpenTabletArtist   │◄───────────────────►│ (OpenTabletDriver   │
│                     │    StreamJsonRpc    │  .Daemon.exe)       │
└─────────────────────┘                    └─────────────────────┘
         ▲                                          │
         │                                          │
    Desktop UI                                   USB/HID
   (user sees)                              (tablet hardware)
```

**Previous architecture:** The project originally used a Svelte 5 web frontend + .NET bridge process. This was replaced with a WPF app, then converted to Avalonia UI for cross-platform potential. The app connects directly to the OTD daemon, eliminating the bridge. The bridge and frontend have been removed.

## Components

### Avalonia App (`OpenTabletArtist/`)

**Role:** Single-process desktop application. Renders all UI, manages state, and communicates directly with the OTD daemon via named pipe.

**Technology:** .NET 10 Avalonia UI with CommunityToolkit.Mvvm (MVVM pattern).

**UX & navigation terminology:** the canonical vocabulary for the app's navigation and page structure —
*page navigation bar / node*, *page*, *tabbed page / subpage navigation / tab*, *title / complex header* —
is defined in [docs/design/ux-terminology.md](design/ux-terminology.md). Use those terms in code and comments.

**Key directories:**
- `Services/` — process / I/O / daemon seams: `AppSession.cs` (the shared session — see *Internal structure* below), `DaemonClient.cs` (named pipe + StreamJsonRpc), `DaemonLifecycleService.cs` (locate / launch / stop the daemon exe), `SettingsFileStore.cs` (settings (de)serialization), `DialogService.cs` (the `IDialogService` seam — all app dialogs), `ConfigurationsDirectoryProvider.cs` (locates the OTD configs folder), `DaemonPenInputSource.cs` (Test tab — daemon `DeviceReport` stream → pen samples), `VMultiDetector.cs` / `VMultiInstaller.cs` (HID + Setup API scanning and driver install), `WindowsInkPluginService.cs` (Windows Ink plugin install + version checks)
- `ViewModels/` — `MainViewModel.cs` (the shell: navigation + composition only) plus one VM per page: `DashboardViewModel`, `TabletSettingsViewModel`, `PresetsViewModel`, `CustomTabletConfigsViewModel`, `UtilitiesViewModel`, `DiagnosticsViewModel`, `PluginsViewModel`, `TestViewModel`, `SettingsViewModel`, `AboutViewModel`
- `Views/` — Avalonia AXAML pages (Home, Tablets, Saved Settings, Custom Tablet Compatibility, Driver Cleanup, Diagnostics, Log, Plugins, Scribble, Daemon, Theme, About) and the per-tablet `TabletDetailView` — shown **in-app** for the Tablets nav (#tablet-ux-overhaul), and also hosted by the tray's thin `TabletSettingsDialog` for the focused Pen Dynamics editor
- `Domain/` — pure, UI-free logic, unit-tested directly: `AreaMappingCalculator`, `DisplayMappingApplier` (apply/lookup a profile's display mapping, shared by the in-app page and the tray), `AuxKeyBinding` (express-key bindings), `ConflictingDriverParser` (parse the daemon's conflicting-driver warnings), `TabletAreaInfo`, `DynamicsStatus` (describe what the dynamics filter is doing to the pen), `PresetNaming`, `TabletConfigNaming`, `ExecutablePath`, `WinInkUpdateState`, `DiagnosticsMath`, `ProfileItem`, `PenSample` + `DeviceReportSample` (Test tab — normalized pen reading + `DeviceReport` parser)
- `Concurrency/` — async coordination: `LatestOnlyGate` (coalesce overlapping data loads), `CoalescingSingleFlight` (single-flight reconnect with latest-wins rerun)
- `Controls/` — custom controls: `PenTestCanvas` (Scribble — SkiaSharp paint surface), `PressureCurveChart` (Dynamics curve editor), `ScreenMappingDiagram` (unified to-scale monitor picker + tablet active-area + live pen dot + L-connector), `TabletStatusBanner` (detected/connected banner), `SakuraPetals` (falling-petal overlay)
- `Themes/` — resource dictionaries for colors and styles (light + dark theme variants, glassmorphism) and the shared `ControlTheme`s
- `Converters/` — Avalonia value converters (bool/string helpers; the old `PageToView` / `StringEquals` nav converters were removed when navigation moved to typed DataTemplates)
- `Helpers/` — dialog helpers (MessageBox / InputBox replacements via `Dialogs.ShowConfirmAsync`, etc.)

**Dependencies (Avalonia 12.0.1):**
- `Avalonia` 12.0.1 — Cross-platform UI framework
- `Avalonia.Desktop` 12.0.1 — Desktop platform support
- `Avalonia.Themes.Fluent` 12.0.1 — Base Fluent theme
- `Avalonia.Fonts.Inter` 12.0.1 — Bundled Inter font for cross-machine consistency
- `StreamJsonRpc` 2.22.23 — JSON-RPC client matching OTD daemon version
- `Newtonsoft.Json` 13.0.3 — JSON handling (required by StreamJsonRpc)
- `CommunityToolkit.Mvvm` 8.4.0 — MVVM infrastructure (`[ObservableProperty]`, `[RelayCommand]`)
- `SkiaSharp` 3.119.3-preview.1.1 — raster paint surface for the Test tab (pinned to the version Avalonia 12 already pulls in via `Avalonia.Skia`; the project also sets `AllowUnsafeBlocks` for the bitmap copy)
- `HidSharp` — HID device enumeration for vmulti detection (transitively via OTD)
- `OpenTabletDriver.Desktop`, `OpenTabletDriver`, `OpenTabletDriver.Plugin` — project refs from submodule

**Note on `Avalonia.Diagnostics`:** Temporarily disabled because no 12.x version is published (latest is 11.3.14). Mixing major versions causes runtime issues since Diagnostics uses Avalonia internals. Re-enable when a 12.x release lands.

#### Internal structure

The shell `MainViewModel` owns navigation and composes one view model per page; it holds no feature state of its own. Shared daemon/session state lives in a single **`AppSession`** (`Services/AppSession.cs`) that owns the `DaemonClient`, the daemon lifecycle, the loaded `Settings`, and the device data.

A page VM takes only the slice of the session it actually needs, via a narrow **role interface** — not the whole object. Some pages (`About`, `Utilities`, `CustomTabletConfigs`) don't depend on the session at all. `DashboardViewModel` is the deliberate exception: it surfaces connection + device + settings together, so it takes the concrete `AppSession` rather than a single role interface. The role interfaces are:

| Interface | Responsibility |
|---|---|
| `IConnectionState` | Connection + daemon-ownership state; the Start / Stop / Restart / LaunchOtdUx commands and their busy/progress state |
| `ISettingsCoordinator` | Current `Settings` and the apply-and-persist path |
| `IDeviceData` | Tablet/device data produced by the data load, plus the `DataLoaded` event — including the full connected-tablet set (`DetectedTablets`, one Dashboard card each) and the user-selectable **active tablet** (`ActiveTabletName`/`SetActiveTablet`) that the single-target flows (tray actions, Test, Diagnostics) act on (#190) |
| `IDaemonDebugSession` | The live debug-report subscription used by Diagnostics |

`AppSession` mutates its observable state only on the UI thread — the daemon's Connected/Disconnected callbacks marshal via the dispatcher, and `Dispatcher.UIThread.VerifyAccess()` guards the data-load and settings-mutation entry points so an off-thread caller fails loudly instead of corrupting bindings.

**Pages pull, the shell doesn't push.** Page VMs that show live data self-subscribe to the session — `IDeviceData.DataLoaded` (Paired Tablets and Saved Settings refresh themselves; the Dashboard refreshes its Windows Ink card) and `IConnectionState` (Diagnostics self-syncs its connected state). The shell pushes nothing into them; it only composes and disposes.

**Dialogs are abstracted** behind `IDialogService` (`Services/DialogService.cs`, #37). Every dialog flow — the per-tablet settings dialog, message/confirm/input, and the read-only config viewer — goes through it, so no page VM constructs a `Window` or calls a static dialog helper, and the flows are fakeable in tests. Likewise the Custom Tablet Configs folder comes from `IConfigurationsDirectoryProvider`, so that page is testable against a temp directory.

**Navigation is typed.** `MainViewModel.CurrentPage` is the page VM instance itself (an `ObservableObject`), and the content host resolves it to a view through App-level `DataTemplate`s keyed by VM type (`App.axaml`). There is no page-name string, no view-lookup converter, and no per-view `DataContext` re-point — each view inherits its VM as `DataContext` from the template. The sidebar highlight binds to converter-free `IsXxx` getters derived from `CurrentPage`.

This shape is the result of an incremental "strangler" refactor (issue #41): shared `AppSession` + role interfaces, then a page-by-page VM split, then typed navigation — each step keeping the build green.

**System tray + background mode (#72).** `AppTray` (`OpenTabletArtist/AppTray.cs`) owns an Avalonia `TrayIcon` that reflects daemon status and offers Show / Start-or-Stop / Restart / Quit, driven off the `IConnectionState` role interface (the same commands and `ShowStartButton`/`DaemonStatusText` the dashboard uses). Closing the main window hides it to the tray instead of exiting (`MainWindow.AllowCloseForQuit()` is the one path that really shuts down, taken only by the tray's Quit). This keeps the daemon controls reachable while the window is closed and is the prerequisite for the investigated per-application-settings feature.

**Single instance (#191).** `Services/SingleInstance` (held by `Program`) gates startup on a named `Mutex`: the first instance becomes primary and listens on a named `EventWaitHandle`; a second launch detects the mutex, signals that event — waking the primary to `MainWindow.BringToFront()` (the same surface path the tray click uses) — and exits before any Avalonia/tray init, so there's never a duplicate window or tray icon. Windows-only (named-event signalling isn't portable); a no-op elsewhere.

The tray also surfaces a few tablet actions for the detected tablet, so it takes `IDeviceData` + `ISettingsCoordinator` + `IDialogService` alongside `IConnectionState` and refreshes on `IDeviceData.DataLoaded` (signature-gated so the 3s poll doesn't churn the menu): a read-only **dynamics-reveal line** (#186) computed by `Domain/DynamicsStatus` from the profile's `PressureCurveProfile` settings, **Open Tablet Settings** (opens the per-tablet dialog via `IDialogService`, showing the window first since the dialog is owned by it), and a **Switch Display** submenu (#187) that maps the detected tablet to a chosen monitor via `Domain/DisplayMappingApplier` and persists through `ISettingsCoordinator` — the same mapping the Screen-Mapping tab's *Apply mapping* uses (`DisplayMappingApplier` is shared by both, with `CurrentlyMapped` driving the dialog's mapped-display match and the tray's check-mark).

#### Test tab (live pen verification)

The Test page is a paint canvas for confirming pressure / tilt / twist actually work — modeled on the narrow scope of the [WebTabletTesterBasic](https://github.com/TheSevenPens/WebTabletTesterBasic) app (canvas + mode + readouts, not a drawing app).

- **Rendering** is raster accumulation, not retained geometry: each pen sample stamps onto a persistent `SKBitmap` (`Controls/PenTestCanvas`, SkiaSharp) which is blitted into an Avalonia `WriteableBitmap`. A retained scene of stroke objects would grow unbounded as the drawing fills; a bitmap stays O(1). We own the `SKBitmap` and hand Avalonia a pixel buffer (no render-lease interop).
- **Input source is a toggle, but position always comes from the OS pointer** so the stroke renders under the pen in both modes. The toggle selects where the *dynamics* (pressure/tilt/twist) come from:
  - **App** — the pointer's own `PointerPointProperties` (Windows Ink).
  - **Driver** — the OTD daemon's `DeviceReport` stream (`Services/DaemonPenInputSource` → `Domain.DeviceReportSample` → normalized `Domain.PenSample`), pushed to the canvas via `SetDriverDynamics`. This is the driver's raw signal, before the Windows Ink output stage.
- **Readouts** reflect the selected source: Canvas X/Y (where the stroke lands, always the pointer) vs Raw X/Y (the source's pre-normalization coords — tablet units in Driver mode), plus pressure/tilt/azimuth/altitude/twist (azimuth & altitude reuse `DiagnosticsMath`).
- **Lifecycle**: the shell starts/stops the daemon debug stream on Test page enter/leave (same treatment as Diagnostics). Delete/Backspace clear the canvas.

#### Daemon communication

How the app actually talks to the daemon, end to end.

**Transport.** A Windows **named pipe** (`"OpenTabletDriver.Daemon"`) carrying **JSON-RPC** via StreamJsonRpc. The client connects with `NamedPipeClientStream` (`Asynchronous | WriteThrough | CurrentUserOnly`) and wraps it in `new JsonRpc(stream)` — the bare constructor on purpose, because the daemon uses StreamJsonRpc's default **newline-delimited framing** (specifying a header-delimited handler would break it). StreamJsonRpc serializes with **Newtonsoft.Json**, which is why request/response payloads come back as `JToken`/`JArray`, not `System.Text.Json` types.

**Client API (`Services/DaemonClient.cs`).** A thin typed wrapper over the RPC methods, using OTD's own model types (from the submodule) so writes round-trip exactly like OTD's UX:
- `GetSettings()` / `SetSettings(Settings)`, `GetApplicationInfo()`, `GetTablets()` (returns `JArray` — complex runtime data we parse selectively), `CheckForUpdates()`
- plugins: `DownloadPlugin` / `UninstallPlugin` / `LoadPlugins`
- `SetTabletDebug(bool)` — toggles the live pen stream. It's a single global daemon flag but has several consumers (Diagnostics, the Test tab's Driver mode, the Dynamics tab's live-pressure dot), so `DaemonClient` **reference-counts** it: the RPC fires only on a 0↔1 transition (a failed enable rolls the count back; a disconnect resets it), so one consumer turning it off can't starve another.
- **Server → client push:** the client registers a *local* RPC method via `AddLocalRpcMethod("DeviceReport", …)`; the daemon invokes it to push pen reports, which `DaemonClient` re-raises as its `DeviceReport` event (consumed by Diagnostics and the Test tab's Driver mode). `IsConnected` is derived from the live `JsonRpc` instance.

**Connect / reconnect lifecycle (`Services/AppSession.cs`).** `AppSession` owns the client and drives connection:
1. `StartAndConnectAsync()` launches the daemon exe (via `DaemonLifecycleService`) if none is running, then connects.
2. `ConnectAsync()` is **fire-and-forget**: it triggers a connect loop through `Concurrency/CoalescingSingleFlight` so only one connect runs at a time and a request arriving mid-connect is honored once (latest-wins) — closing the dropped-reconnect race.
3. On success the client raises **`Connected`** → `AppSession` runs the data load (settings + tablets + app-info, coalesced by `Concurrency/LatestOnlyGate` so overlapping loads don't clobber each other) and starts a periodic poll (~3s) so newly-plugged tablets are detected.
4. On an unexpected drop the `JsonRpc.Disconnected` handler nulls the dead RPC, raises **`Disconnected`**, and — **only if `AutoReconnect` is set** — kicks off another connect. A user-initiated **Stop** clears `AutoReconnect` first, so "stopped" stays stopped; Start/Restart/Connect set it back.

**Threading.** Reports and connect/disconnect callbacks arrive off the UI thread; `AppSession` marshals to the dispatcher before mutating observable state (and `Dispatcher.UIThread.VerifyAccess()` guards the load/settings entry points), so binders and page VMs never have to marshal.

**Identity.** Because the pipe name is global, the app may connect to a separately-installed OTD instead of its own build — see *Daemon identity verification* under Technical Challenges for how that's detected (`GetNamedPipeServerProcessId`).

### OTD Daemon (external)

**Role:** The actual tablet driver. Manages USB/HID communication with tablet hardware, applies input processing (smoothing, area mapping, bindings), and exposes a configuration API over a named pipe.

**Interface:** `IDriverDaemon` — a JSON-RPC service on named pipe `"OpenTabletDriver.Daemon"`.

**Key operations:**
| Method | Purpose |
|---|---|
| `GetTablets()` | List connected tablets with specs |
| `GetSettings()` | Retrieve full configuration (profiles, bindings, filters) |
| `SetSettings(settings)` | Apply updated configuration |
| `GetApplicationInfo()` | Version, paths, directories |
| `DetectTablets()` | Trigger re-detection of hardware |

**Key events:** `TabletsChanged`, `DeviceReport`, `Message`, `Resynchronize`

This component is not part of our codebase. It is the standard OTD daemon, running unmodified.

### vmulti (Windows Virtual HID Driver)

**What it is:** A virtual HID miniport driver (originally by Djpnewton) that creates virtual input devices at the Windows kernel level. It injects HID reports directly into the input stack as if they came from real hardware.

**Why OTD needs it on Windows:** OTD's core input on Windows uses `SendInput()` for basic cursor positioning. However, `SendInput` cannot transmit pressure or tilt data. For artists who need pressure sensitivity and tilt in drawing applications, vmulti is **required**. It works alongside the Windows Ink plugin to create a virtual digitizer device that reports pressure and tilt through the Windows Ink API.

**The full artist setup on Windows requires:**
1. vmulti driver installed (enables the virtual HID device)
2. Windows Ink plugin installed within OTD
3. Output mode set to "Windows Ink Absolute Mode" (not the default "Absolute Mode")
4. Drawing application configured to use Windows Ink input

**Detecting vmulti:** The vmulti driver registers as a HID device with Vendor ID `0x00FF` (255) and Product ID `0xBACC` (47820). Detection is done by enumerating HID devices matching these IDs. The vmulti binary is distributed at [X9VoiD/vmulti-bin](https://github.com/X9VoiD/vmulti-bin/).

**Relevance to our prototype:** Since our target audience is creatives (not gamers), pressure and tilt are essential. Our UX should guide users through the vmulti + Windows Ink setup and surface clear status about whether these components are properly configured. See the [SevenPens OTD Windows install guide](https://docs.sevenpens.com/drawtab/guides/drivers/opentabletdriver/otd-windows-install) for the full artist workflow.

## Key Design Decisions

### 1. Avalonia UI instead of web frontend

**Decision:** Use Avalonia UI (.NET 10) rather than Svelte/React/web tech.

**Rationale:** The original Svelte 5 frontend had a persistent navigation bug where client-side routing broke when navigating back to previously-visited pages. This was traced to a Svelte 5 rendering issue. The app was first rebuilt as WPF (Windows-only), then converted to Avalonia UI for cross-platform potential. Avalonia provides native navigation via simple property binding (`CurrentPage` → `ContentControl`, resolved by typed `DataTemplate`s), direct named pipe access (no bridge needed), and eliminates an entire process from the architecture.

**Trade-off:** Avalonia is cross-platform capable but Windows-specific features (P/Invoke for display enumeration, vmulti detection) currently limit portability. The glassmorphism design language translates well to Avalonia's styling system.

### 2. Direct daemon connection instead of bridge

**Decision:** The Avalonia app connects directly to the OTD daemon via named pipe, replacing the bridge + HTTP architecture.

**Rationale:** Since Avalonia is .NET, it can use StreamJsonRpc directly — the same library the daemon uses. No HTTP translation layer needed. This eliminates a process, reduces latency, and simplifies deployment to a single .exe.

### 3. MVVM with CommunityToolkit.Mvvm

**Decision:** Use the MVVM pattern with source-generated properties and commands.

**Rationale:** `[ObservableProperty]` and `[RelayCommand]` attributes generate all the `INotifyPropertyChanged` boilerplate. Navigation is typed: `CurrentPage` is the active page's view model, and a `ContentControl` resolves it to a view via `DataTemplate`s keyed by VM type — no routing framework, no string keys, no view-lookup converter.

### 4. Typed OTD models where possible, JToken for everything else

**Decision:** Reference the OTD library projects (`OpenTabletDriver.Desktop`, etc.) from the submodule and use the typed models (`Settings`, `Profile`, `BindingSettings`, `PluginSettingStore`) directly. Use `Newtonsoft.Json.Linq.JToken` only for data we don't yet have a typed model for, or local view-model records (e.g. `PresetInfo`).

**Rationale:** Strong typing eliminates a class of binding bugs and refactoring risks. Typed models also let us call OTD's own serializer (`Settings.Serialize()`) for write-back, ensuring round-tripping is identical to OTD's own UX.

### 5. Avalonia 12 with reflection bindings (compiled bindings opt-out)

**Decision:** On the Avalonia 12 upgrade, opt out of the new compiled-bindings-by-default behaviour via `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>` in the csproj.

**Rationale:** Compiled bindings require an explicit `x:DataType` directive on every view and inner DataTemplate. Adding those across the codebase is a separate refactor with no behavioural payoff for this prototype. Reflection bindings preserve the existing 11.x behaviour. A future cleanup could opt back in per-view to gain compile-time type checking and a small perf win.

> Because bindings are reflection-based, a wrong binding name renders blank rather than failing the build — so view changes get a manual render pass before merge.

### 6. Shared session behind role interfaces (issue #41)

**Decision:** Put all shared daemon/session state in one `AppSession`, and have each page VM depend on a narrow role interface (`IConnectionState`, `ISettingsCoordinator`, `IDeviceData`, `IDaemonDebugSession`) instead of on `MainViewModel` or the whole session.

**Rationale:** The shell had grown into a god-object holding connection state, settings, device data, and every page's logic. Splitting it lets each page state its real dependency surface, makes the page VMs unit-testable with small fakes, and keeps UI-thread affinity in one place. Pure logic was pulled out to `Domain/` (calculators, naming, version math) and async coordination to `Concurrency/`, both covered directly by tests.

**Trade-off:** More types and a little wiring at the composition root (the shell hands each page the role interfaces / services it needs). The clarity and testability are worth it — and with the page VMs now self-subscribing to `IDeviceData` / `IConnectionState` and going through `IDialogService`, the shell holds no feature state and pushes nothing into the pages.

## Technical Challenges

### Solved

**Named pipe message framing.** The OTD daemon uses the default `JsonRpc` constructor, which uses `NewLineDelimited` framing. Must use `new JsonRpc(stream)` directly.

**Newtonsoft.Json vs System.Text.Json.** StreamJsonRpc uses Newtonsoft.Json internally. Must use `JToken` not `JsonElement`.

**JValue to string in commands.** `CommandParameter` with JToken indexer (`{Binding [Name]}`) passes `JValue` objects, but `RelayCommand<string>` expects `string`. Fixed by binding to `[Name].Value` which unwraps to the primitive.

**Avalonia 12 + JObject indexer text bindings.** In Avalonia 12, `{Binding [Name]}` and `{Binding [Name].Value}` against a `JObject` no longer render to `TextBlock.Text` (the cell is blank). The fix is to switch the underlying collection to a typed record (`PresetInfo`) and bind to plain properties (`{Binding Name}`). This pattern was applied to the Saved Settings (Presets) list in PresetsView.

**Svelte 5 navigation bug (historical).** Svelte 5's `$state` reactivity failed to update `{#if}` template blocks when values returned to previously-rendered states. This affected both custom hash routing and SvelteKit's built-in router. Root cause was in Svelte 5's compiled template diffing. Resolved by switching to WPF, later converted to Avalonia.

**VMulti detection (dual method).** VMulti is detected via both HidSharp (HID enumeration — sees only active devices) and the Windows Setup API (`SetupDi*` — sees all devices including disabled ones). This distinguishes "not installed" from "installed but disabled."

**VMulti driver source & hardware id (#113).** The driver package is the **only** release of [X9VoiD/vmulti-bin](https://github.com/X9VoiD/vmulti-bin) — `v1.0`, the OTD-recommended package — pinned in `VMultiInstaller.DownloadUrl`. X9VoiD repackages djpnewton's vmulti driver, so the device id its `vmulti.inf` registers is **`djpnewton\vmulti`** — that's exactly the hardware id `VMultiDetector` matches, so detection is correct and we are not on a different/wrong driver. The root device created at install time is `pentablet\hid`; the function driver then enumerates the `djpnewton\vmulti` HID devices. The zip also bundles the tools we drive: `devcon.exe`, `DIFxCmd.exe`, `DIFxAPI.dll`, plus `vmulti.sys/.cat`, `hidkmdf.sys`, `WdfCoInstaller01009.dll`, `WinTab32.dll`. We're pinned to v1.0 (the only release); bundling the payload with our own release instead of downloading at runtime is tracked by #136.

**VMulti install/uninstall (in-app, #110/#111/#112).** The app downloads the package and extracts it, then runs the driver operations **in-app** — a generated no-`@pause` script launched once, elevated, with a hidden window (single UAC prompt, no flashing console), instead of the package's self-elevating `install_hiddriver.bat` / `remove_hiddriver.bat`. Install runs `devcon install vmulti.inf pentablet\hid` (no `/r`, so we never auto-reboot); uninstall runs `DIFxCmd /u` + `devcon remove pentablet\hid` and also removes the leftover driverless `djpnewton\vmulti` nodes (Device Manager Code 28) the stock removal left behind. The card re-detects the real state afterward and offers a restart.

**TabletDriverCleanup integration.** The app can download and run [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) (the official OTD-team driver cleanup tool) from the Driver Cleanup page, which also surfaces the daemon's conflicting-driver detections as cards (and a Home alert). Uses the same pattern as VMulti install — downloads ZIP from GitHub, extracts to temp, launches the exe as admin. Unlike VMulti install, the terminal window is left visible (no `CreateNoWindow`) so the user can read the cleanup results directly, matching the usage described in the SevenPens documentation.

**Display enumeration.** System displays are enumerated using Win32 `EnumDisplayMonitors()` + `EnumDisplaySettings()` — the same APIs OTD uses internally. The Screen Mapping tab draws them to scale in the `ScreenMappingDiagram` (click a monitor, then *Apply mapping*).

**Aspect ratio lock.** When mapping a tablet to a display, the tablet area height is automatically adjusted to match the display's aspect ratio (`LockAspectRatio = true`), ensuring proportional 1:1 mapping with no distortion.

**Settings write-back.** The per-tablet settings page pushes changes back to the daemon via `SetSettings()` — output mode (Fix to WinInk), display mapping, calibration, pen dynamics, hover limit, and express-key bindings.

**External-change reconciliation.** Because the daemon pipe is global, another client (notably the OTD UX) can change the same app-owned daemon's settings underneath us — and the daemon pushes *no* event for it: `SetSettings` only fires `Resynchronize` on its failure/recovery path, never on a successful apply. So an open tablet page would otherwise keep showing the mapping it last remembered. The fix is pull-based: the shell re-pulls settings on **window activation** (`MainWindow.Activated` → `MainViewModel.OnWindowActivated`, throttled), plus the existing `TabletsChanged` push and the ~30s fallback poll. Each load runs `MainViewModel.ReconcileOpenTabletDetails`, which hands every cached `TabletDetailViewModel` the fresh settings + its profile. The VM compares a content **fingerprint** (`Domain/ProfileFingerprint`, a SHA-256 over the serialized profile) of the reload against its live `_profile`; equal ⇒ no-op (this also absorbs our own applies, which mutate `_profile` before pushing, so no false positive). On a genuine divergence it **adopts the reload silently** — unless the user has an unsaved edit (a picked-but-unapplied display mapping), where it stashes the reload and raises a non-destructive **"changed outside OTA — Reload"** header banner instead of discarding the in-progress change. `AdoptProfile` is the shared re-point path used by both the manual header Refresh and the external reload.

**Windows shell pen/touch feedback suppression.** Windows draws its own pen/touch feedback over the app — the ripple/contact rings on every tap, the press-and-hold ring — and runs the press-and-hold → right-click gesture. Two independent levers, both per-window (no OS-wide change), in `Services/ShellPenFeedback`: (1) `SetWindowFeedbackSetting` disables the *visual* feedback (tap rings etc.) — applied **app-wide** on every window (`MainWindow` + all dialogs, via the `Views/AppWindow` base for programmatic dialogs and `ShellPenFeedback.DisableOnOpen` for the XAML ones); it's visual-only, so pen **right-click still works** (e.g. right-click a tablet node → Forget). (2) The press-and-hold **gesture** is disabled only on the **calibration overlay** (where a deliberate still hold is our capture gesture, #457) by answering `WM_TABLET_QUERYSYSTEMGESTURESTATUS` with `TABLET_DISABLE_PRESSANDHOLD` (via Avalonia's `Win32Properties` WndProc hook); the cursor Windows still re-asserts at the ~1 s dwell is re-hidden each frame with `SetCursor(NULL)` while holding (residual sub-frame flicker tracked in #479). Full background write-up: the *devnotes* "disabling shell pen/touch feedback" note.

**Daemon identity verification.** Because the daemon pipe name (`OpenTabletDriver.Daemon`) is global, the app can connect to a separately-installed OTD instance the user already had running instead of the one it builds from the submodule. After connecting, `DaemonClient.GetServerProcessId()` calls the Win32 `GetNamedPipeServerProcessId` on the client pipe handle to identify the process on the other end; `AppSession.UpdateDaemonSource()` compares that process's exe path to the project's build output (`DaemonLifecycleService.ExpectedExePath()`). The result drives a three-state ownership indicator on the dashboard daemon card: `IsAppOwnedDaemon` (paths match → green "App-owned daemon"), `IsForeignDaemon` (paths differ or our build is missing → amber warning), or — when connected but the server process path can't be read (e.g. elevation) — neither flag is set and the card shows a grey "Daemon source unknown". The check is conservative: it never guesses ownership when it can't positively read the path, so it won't show a false positive in either direction. the user already had running instead of the one it builds from the submodule. After connecting, `DaemonClient.GetServerProcessId()` calls the Win32 `GetNamedPipeServerProcessId` on the client pipe handle to identify the process on the other end; `AppSession.UpdateDaemonSource()` compares that process's exe path to the project's build output (`DaemonLifecycleService.ExpectedExePath()`). The result drives a three-state ownership indicator on the dashboard daemon card: `IsAppOwnedDaemon` (paths match → green "App-owned daemon"), `IsForeignDaemon` (paths differ or our build is missing → amber warning), or — when connected but the server process path can't be read (e.g. elevation) — neither flag is set and the card shows a grey "Daemon source unknown". The check is conservative: it never guesses ownership when it can't positively read the path, so it won't show a false positive in either direction.

### Remaining

**Interactive area mapper.** The Diagnostics page already draws a live tablet area + pen dot (`Controls/TabletVisualizer`). The *interactive* area mapper — drag/resize/rotate the active area on an Avalonia `Canvas` with coordinate transforms — is still to be built.

**Pointer calibration (#127, #195, #196).** An interactive calibration for pen displays with a selectable capture mode. The pure math is in `Domain/` (all unit-tested, source-shared into the plugin via `CalibrationMath.cs`):
- **Corners** (default) → a **perspective homography** (`CalibrationMath.SolveHomography` / the `Homography` struct), which corrects keystone/parallax as well as offset/scale/rotation — an upgrade from the original least-squares affine (still read for legacy stores, #195).
- **Fine grid** (3×3 / 5×5) → a **per-node bilinear offset field** (`CalibrationSolver.SolveGrid` / the `CalibrationGrid` struct), correcting localized distortion a single global transform can't (#196).

`CalibrationSolver` turns measured taps + `AbsolutePositionMapper.MapFromDesktop` into the chosen model. The correction is applied by a second plugin filter (`OpenTabletArtist.Dynamics.CalibrationFilter`, `PreTransform`, before the dynamics filter), which branches on a stored `Model` field (homography / grid / legacy affine). It's persisted via `Services/CalibrationProfile` (mirrors `PressureCurveProfile`; tags the model + payload, keeps a mapping fingerprint for staleness). The UI is a full-display overlay (`Views/CalibrationOverlayWindow` + `ViewModels/CalibrationViewModel`) launched from the Screen-Mapping tab in Absolute mode, with the mode picked from a selector next to **Calibrate…**; capture uses the daemon debug stream and bypasses any existing calibration so taps are recorded uncorrected. See `docs/design/127-pointer-calibration.md`.

**Theming.** Light, Dark, and a custom **Sakura** (`Anime`) variant are implemented via `ResourceDictionary.ThemeDictionaries` in `Themes/Colors.axaml` (theme-overridable keys — accent/glass/backdrop — live **only** in the per-variant dictionaries, since a top-level key would shadow them). `Services/ThemeService` persists the user's choice (`AppSettings`) and applies it through `Application.RequestedThemeVariant` — "System" follows the OS; Sakura defaults on. The selector lives on the **Theme** page (`SettingsViewModel` / `SettingsView`), which also hosts the Sakura falling-petals toggle (`AnimationSettings` + `SakuraPetals`) and the frosted-glass card-opacity slider (`AcrylicSettings`, which drives the global `GlassBgBrush`). Custom-drawn controls still use fixed colors and could be re-tuned per variant.

**Settings card convention (#389).** New settings pages should use `Controls/SettingCard` (+ `ToggleSetting`, `LabeledField`, etc.) instead of hand-rolled `GlassPanel` cards. Existing pages migrate opportunistically when otherwise touched; Theme and Startup are the reference implementations.

**Glassmorphism polish.** Current glass panels use semi-transparent backgrounds with box shadows. True acrylic blur effects could be added for deeper visual fidelity.

**OTD as submodule.** OpenTabletDriver is included as a git submodule at `external/OpenTabletDriver`, pinned to v0.6.7. The Avalonia app references `OpenTabletDriver.Desktop`, `OpenTabletDriver`, and `OpenTabletDriver.Plugin` as project references, giving type-safe access to Settings, Profile, BindingSettings, etc. The daemon is also built from the submodule and auto-started by the app.

**Pen-dynamics plugin.** Pressure remapping + smoothing is implemented as our own OTD filter plugin (`plugins/OpenTabletArtist.Dynamics`, net8 to match the daemon) rather than taking a dependency on a third-party plugin (e.g. Slimy Scylla). Because it runs inside the daemon's pipeline, it applies to every app, not just one. Key seams:

- **Shared math** — `Domain/PressureCurve.cs` (the `Extended` curve: input/output min-max remap, softness exponent, Clamp-vs-Cut dead zone), `Domain/PenSmoothing.cs` (EMA + the `amount → factor` perceptual mapping borrowed from Slimy Scylla, `amount^(0.02/amount)`), and `Domain/PenDynamicsProcessor.cs` (the stateful per-stroke pipeline: curve + pressure/position smoothing, with the curve/smooth ordering and reset logic) are the single source of truth, **source-linked** (`<Compile Include … Link>`) into the plugin so the daemon-side filter and the app-side editor compute identically, and unit-tested once in the app's test project.
- **The filter** — `PressureCurveFilter` (type name kept stable for back-compat; display name *OpenTabletArtist - Pen Dynamics*) implements `IPositionedPipelineElement<IDeviceReport>` at `PreTransform`, reads `MaxPressure` via `[TabletReference]` injection, and delegates to a `PenDynamicsProcessor`. Smoothing applies **only while the pen is down** (pressure > 0) and state resets on lift / pen-out — the OTD analogue of Slimy Scylla's "apply while drawing" default, which steadies stroke starts/ends without depending on proximity reports. Hover (`Pressure == 0`) is left untouched.
- **Auto-install** — the app bundles the built DLL and copies it into the daemon's plugin directory on connect (`Services/PressurePluginInstaller.cs` + `Domain/PressurePluginPaths.cs`); a fresh copy triggers `LoadPlugins`, an update restarts the daemon (it can't hot-replace an already-loaded assembly). Only for the app-owned daemon.
- **Per-profile config** — `Services/PressureCurveProfile.cs` reads/writes the filter's `PluginSettingStore` (by type name, since the app doesn't reference the plugin assembly) in a profile's `Filters`, as a `PenDynamicsSettings` (curve + smoothing). The Pressure tab's editor (`Controls/PressureCurveChart.cs`, adapted from PenDynamicsLab) drives it, debouncing edits into a single `ApplyAndSaveSettings`.

The same plugin assembly also carries the **calibration filter** (above) and a **hover-limit filter** (`HoverFilter`, #188, modeled on Kuuube's Hover Distance Limiter): at `PreTransform` it drops a report when `IProximityReport.HoverDistance` exceeds a configured max, so a pen lifted past that height stops moving the cursor (drawing is untouched — hover is ~0 in contact). Persisted per-profile by `Services/HoverProfile.cs` and edited from the dialog's **Hover** tab. Each filter's type name is guarded by a unit test that resolves it against the built plugin assembly.

## Dependency Graph

```
Avalonia App (.NET 10)
  ├── Avalonia 12.0.1
  ├── Avalonia.Desktop 12.0.1
  ├── Avalonia.Themes.Fluent 12.0.1
  ├── Avalonia.Fonts.Inter 12.0.1
  ├── OpenTabletDriver.Desktop (project ref from submodule)
  ├── OpenTabletDriver (project ref from submodule)
  ├── OpenTabletDriver.Plugin (project ref from submodule)
  ├── StreamJsonRpc 2.22.23
  ├── Newtonsoft.Json 13.0.3
  ├── CommunityToolkit.Mvvm 8.4.0
  ├── SkiaSharp 3.119.3-preview.1.1 (Test-tab paint surface)
  └── HidSharp (transitively via OTD)

OTD Daemon (built from submodule, .NET 8)
  ├── OpenTabletDriver.Desktop
  └── (many more — not our concern)
```

## Solution Layout

```
OpenTabletArtist.slnx
  ├── OpenTabletArtist/OpenTabletArtist.csproj                   (this app)
  ├── plugins/OpenTabletArtist.Dynamics/...                      (our OTD filter plugin, net8)
  ├── tests/OpenTabletArtist.Tests/OpenTabletArtist.Tests.csproj (xUnit tests)
  └── external/OpenTabletDriver/OpenTabletDriver.Daemon/...      (built daemon)
```

The submodule's `OpenTabletDriver.Daemon.exe` is what our app auto-launches when there isn't an OTD daemon already running.

> **Build the solution, not just the app project.** A common failure mode ("Disconnected" / "No tablet detected") is building only `OpenTabletArtist/OpenTabletArtist.csproj`, which leaves the daemon exe missing. Build `OpenTabletArtist.slnx` so the daemon is produced too.

## Testing & CI

Logic that doesn't need a UI is unit-tested with **xUnit** in `tests/OpenTabletArtist.Tests`. Two things make that practical:

- **Pure logic in `Domain/`** (area-mapping math, preset/config naming, version comparison, diagnostics math) is tested with no scaffolding.
- **Seams behind interfaces** — `ISettingsFileStore`, `IDaemonLifecycleService`, the `AppSession` role interfaces, `IDaemonDebugSession`, plus `Concurrency/` primitives — are exercised with small hand-written fakes, so page VMs and session behavior (e.g. the daemon Stop/Start auto-reconnect gate) are covered without a real daemon.

GitHub Actions (`.github/workflows/build.yml`) checks out the submodule recursively, sets up the .NET 8 + .NET 10 SDKs, and runs `dotnet build OpenTabletArtist.slnx` + `dotnet test` on `windows-latest` for every push and PR.

**Releases.** `.github/workflows/release.yml` publishes a downloadable Windows build when a `v*` tag is pushed (or via manual dispatch). It runs the tests, then `dotnet publish`es the app **self-contained for `win-x64`** (no .NET runtime needed on the user's machine) and the OTD daemon into a `Daemon/` subfolder next to the app, zips the result, and attaches it to a GitHub Release with generated notes. The bundled `Daemon/` path is what `Domain/DaemonExePaths` checks first, so the daemon auto-starts from the release layout exactly as in dev. To cut a release: `git tag v0.1.0 && git push origin v0.1.0`.

`Directory.Build.targets` at the repo root neutralizes Avalonia's build-time telemetry target (`AvaloniaStats`), which writes under `%LocalAppData%` and fails in sandboxed/CI/restricted profiles — this had repeatedly blocked reviewers and agents from running the suite.
