# Architecture

## System Diagram

```
┌─────────────────────┐                    ┌─────────────────────┐
│  Avalonia App (.NET 10)│     Named Pipe     │   OTD Daemon        │
│  OtdWindowsHelper   │◄───────────────────►│ (OpenTabletDriver   │
│                     │    StreamJsonRpc    │  .Daemon.exe)       │
└─────────────────────┘                    └─────────────────────┘
         ▲                                          │
         │                                          │
    Desktop UI                                   USB/HID
   (user sees)                              (tablet hardware)
```

**Previous architecture:** The project originally used a Svelte 5 web frontend + .NET bridge process. This was replaced with a WPF app, then converted to Avalonia UI for cross-platform potential. The app connects directly to the OTD daemon, eliminating the bridge. The bridge and frontend have been removed.

## Components

### Avalonia App (`wpf/`)

**Role:** Single-process desktop application. Renders all UI, manages state, and communicates directly with the OTD daemon via named pipe.

**Technology:** .NET 10 Avalonia UI with CommunityToolkit.Mvvm (MVVM pattern).

**Key directories:**
- `Services/` — process / I/O / daemon seams: `AppSession.cs` (the shared session — see *Internal structure* below), `DaemonClient.cs` (named pipe + StreamJsonRpc), `DaemonLifecycleService.cs` (locate / launch / stop the daemon exe), `SettingsFileStore.cs` (settings (de)serialization), `VMultiDetector.cs` / `VMultiInstaller.cs` (HID + Setup API scanning and driver install), `WindowsInkPluginService.cs` (Windows Ink plugin install + version checks)
- `ViewModels/` — `MainViewModel.cs` (the shell: navigation + composition only) plus one VM per page: `DashboardViewModel`, `TabletSettingsViewModel`, `PresetsViewModel`, `CustomTabletConfigsViewModel`, `UtilitiesViewModel`, `DiagnosticsViewModel`, `AboutViewModel`
- `Views/` — Avalonia AXAML pages (Dashboard, Paired Tablets, Saved Settings, Custom Tablet Configs, Utilities, Diagnostics, About) and the per-tablet `TabletSettingsDialog`
- `Domain/` — pure, UI-free logic, unit-tested directly: `AreaMappingCalculator`, `PresetNaming`, `TabletConfigNaming`, `ExecutablePath`, `WinInkUpdateState`, `DiagnosticsMath`, `ProfileItem`
- `Concurrency/` — async coordination: `LatestOnlyGate` (coalesce overlapping data loads), `CoalescingSingleFlight` (single-flight reconnect with latest-wins rerun)
- `Controls/` — custom controls: `TabletVisualizer` (area + live pen dot), `IconBox` (card-header icon)
- `Themes/` — resource dictionaries for colors and styles (light mode, glassmorphism) and the shared `ControlTheme`s
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
- `HidSharp` — HID device enumeration for vmulti detection (transitively via OTD)
- `OpenTabletDriver.Desktop`, `OpenTabletDriver`, `OpenTabletDriver.Plugin` — project refs from submodule

**Note on `Avalonia.Diagnostics`:** Temporarily disabled because no 12.x version is published (latest is 11.3.14). Mixing major versions causes runtime issues since Diagnostics uses Avalonia internals. Re-enable when a 12.x release lands.

#### Internal structure

The shell `MainViewModel` owns navigation and composes one view model per page; it holds no feature state of its own. Shared daemon/session state lives in a single **`AppSession`** (`Services/AppSession.cs`) that owns the `DaemonClient`, the daemon lifecycle, the loaded `Settings`, and the device data. Page VMs depend on `AppSession` through narrow **role interfaces** rather than the whole object:

| Interface | Responsibility |
|---|---|
| `IConnectionState` | Connection + daemon-ownership state; the Start / Stop / Restart / LaunchOtdUx commands and their busy/progress state |
| `ISettingsCoordinator` | Current `Settings` and the apply-and-persist path |
| `IDeviceData` | Tablet/device data produced by the data load, plus the `DataLoaded` event |
| `IDaemonDebugSession` | The live debug-report subscription used by Diagnostics |

`AppSession` mutates its observable state only on the UI thread — the daemon's Connected/Disconnected callbacks marshal via the dispatcher, and `Dispatcher.UIThread.VerifyAccess()` guards the data-load and settings-mutation entry points so an off-thread caller fails loudly instead of corrupting bindings.

**Navigation is typed.** `MainViewModel.CurrentPage` is the page VM instance itself (an `ObservableObject`), and the content host resolves it to a view through App-level `DataTemplate`s keyed by VM type (`App.axaml`). There is no page-name string, no view-lookup converter, and no per-view `DataContext` re-point — each view inherits its VM as `DataContext` from the template. The sidebar highlight binds to converter-free `IsXxx` getters derived from `CurrentPage`.

This shape is the result of an incremental "strangler" refactor (issue #41): shared `AppSession` + role interfaces, then a page-by-page VM split, then typed navigation — each step keeping the build green.

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

**Trade-off:** More types and a little forwarding (the shell still pushes a few loaded values into pages that don't yet self-subscribe to `IDeviceData`). The clarity and testability are worth it.

## Technical Challenges

### Solved

**Named pipe message framing.** The OTD daemon uses the default `JsonRpc` constructor, which uses `NewLineDelimited` framing. Must use `new JsonRpc(stream)` directly.

**Newtonsoft.Json vs System.Text.Json.** StreamJsonRpc uses Newtonsoft.Json internally. Must use `JToken` not `JsonElement`.

**JValue to string in commands.** `CommandParameter` with JToken indexer (`{Binding [Name]}`) passes `JValue` objects, but `RelayCommand<string>` expects `string`. Fixed by binding to `[Name].Value` which unwraps to the primitive.

**Avalonia 12 + JObject indexer text bindings.** In Avalonia 12, `{Binding [Name]}` and `{Binding [Name].Value}` against a `JObject` no longer render to `TextBlock.Text` (the cell is blank). The fix is to switch the underlying collection to a typed record (`PresetInfo`) and bind to plain properties (`{Binding Name}`). This pattern was applied to the Saved Settings (Presets) list in PresetsView.

**Svelte 5 navigation bug (historical).** Svelte 5's `$state` reactivity failed to update `{#if}` template blocks when values returned to previously-rendered states. This affected both custom hash routing and SvelteKit's built-in router. Root cause was in Svelte 5's compiled template diffing. Resolved by switching to WPF, later converted to Avalonia.

**VMulti detection (dual method).** VMulti is detected via both HidSharp (HID enumeration — sees only active devices) and the Windows Setup API (`SetupDi*` — sees all devices including disabled ones). This distinguishes "not installed" from "installed but disabled."

**VMulti install/uninstall.** The app can download the VMulti driver package from GitHub, extract it, and run the official `install_hiddriver.bat` / `remove_hiddriver.bat` scripts with admin elevation (UAC prompt).

**TabletDriverCleanup integration.** The app can download and run [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) (the official OTD-team driver cleanup tool) via a Dashboard card. Uses the same pattern as VMulti install — downloads ZIP from GitHub, extracts to temp, launches the exe as admin. Unlike VMulti install, the terminal window is left visible (no `CreateNoWindow`) so the user can read the cleanup results directly, matching the usage described in the SevenPens documentation.

**Display enumeration.** System displays are enumerated using Win32 `EnumDisplayMonitors()` + `EnumDisplaySettings()` — the same APIs OTD uses internally. Tablet settings dialog shows displays as radio buttons with a "Set to display" action.

**Aspect ratio lock.** When mapping a tablet to a display, the tablet area height is automatically adjusted to match the display's aspect ratio (`LockAspectRatio = true`), ensuring proportional 1:1 mapping with no distortion.

**Settings write-back.** The tablet settings dialog can push changes back to the daemon via `SetSettings()`. Currently supports changing the output mode (Fix to WinInk) and display mapping.

**Daemon identity verification.** Because the daemon pipe name (`OpenTabletDriver.Daemon`) is global, the app can connect to a separately-installed OTD instance the user already had running instead of the one it builds from the submodule. After connecting, `DaemonClient.GetServerProcessId()` calls the Win32 `GetNamedPipeServerProcessId` on the client pipe handle to identify the process on the other end; `AppSession.UpdateDaemonSource()` compares that process's exe path to the project's build output (`DaemonLifecycleService.ExpectedExePath()`). The result drives a three-state ownership indicator on the dashboard daemon card: `IsAppOwnedDaemon` (paths match → green "App-owned daemon"), `IsForeignDaemon` (paths differ or our build is missing → amber warning), or — when connected but the server process path can't be read (e.g. elevation) — neither flag is set and the card shows a grey "Daemon source unknown". The check is conservative: it never guesses ownership when it can't positively read the path, so it won't show a false positive in either direction.

### Remaining

**Interactive area mapper.** The Diagnostics page already draws a live tablet area + pen dot (`Controls/TabletVisualizer`). The *interactive* area mapper — drag/resize/rotate the active area on an Avalonia `Canvas` with coordinate transforms — is still to be built.

**Dark mode.** Light mode colors are implemented. Dark mode requires a second `ResourceDictionary` with runtime switching.

**Glassmorphism polish.** Current glass panels use semi-transparent backgrounds with box shadows. True acrylic blur effects could be added for deeper visual fidelity.

**OTD as submodule.** OpenTabletDriver is included as a git submodule at `external/OpenTabletDriver`, pinned to v0.6.7. The Avalonia app references `OpenTabletDriver.Desktop`, `OpenTabletDriver`, and `OpenTabletDriver.Plugin` as project references, giving type-safe access to Settings, Profile, BindingSettings, etc. The daemon is also built from the submodule and auto-started by the app.

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
  └── HidSharp (transitively via OTD)

OTD Daemon (built from submodule, .NET 8)
  ├── OpenTabletDriver.Desktop
  └── (many more — not our concern)
```

## Solution Layout

```
OTDWindowsHelper.slnx
  ├── wpf/OtdWindowsHelper.csproj                                (this app)
  ├── tests/OtdWindowsHelper.Tests/OtdWindowsHelper.Tests.csproj (xUnit tests)
  └── external/OpenTabletDriver/OpenTabletDriver.Daemon/...      (built daemon)
```

The submodule's `OpenTabletDriver.Daemon.exe` is what our app auto-launches when there isn't an OTD daemon already running.

> **Build the solution, not just the app project.** A common failure mode ("Disconnected" / "No tablet detected") is building only `wpf/OtdWindowsHelper.csproj`, which leaves the daemon exe missing. Build `OTDWindowsHelper.slnx` so the daemon is produced too.

## Testing & CI

Logic that doesn't need a UI is unit-tested with **xUnit** in `tests/OtdWindowsHelper.Tests`. Two things make that practical:

- **Pure logic in `Domain/`** (area-mapping math, preset/config naming, version comparison, diagnostics math) is tested with no scaffolding.
- **Seams behind interfaces** — `ISettingsFileStore`, `IDaemonLifecycleService`, the `AppSession` role interfaces, `IDaemonDebugSession`, plus `Concurrency/` primitives — are exercised with small hand-written fakes, so page VMs and session behavior (e.g. the daemon Stop/Start auto-reconnect gate) are covered without a real daemon.

GitHub Actions (`.github/workflows/build.yml`) checks out the submodule recursively, sets up the .NET 8 + .NET 10 SDKs, and runs `dotnet build OTDWindowsHelper.slnx` + `dotnet test` on `windows-latest` for every push and PR.

`Directory.Build.targets` at the repo root neutralizes Avalonia's build-time telemetry target (`AvaloniaStats`), which writes under `%LocalAppData%` and fails in sandboxed/CI/restricted profiles — this had repeatedly blocked reviewers and agents from running the suite.
