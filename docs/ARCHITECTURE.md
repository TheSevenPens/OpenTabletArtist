# Architecture

## System Diagram

```
┌─────────────────────┐                    ┌─────────────────────┐
│   WPF App (.NET 8)  │     Named Pipe     │   OTD Daemon        │
│   TabletDriverUX    │◄───────────────────►│ (OpenTabletDriver   │
│                     │    StreamJsonRpc    │  .Daemon.exe)       │
└─────────────────────┘                    └─────────────────────┘
         ▲                                          │
         │                                          │
    Desktop UI                                   USB/HID
   (user sees)                              (tablet hardware)
```

**Previous architecture:** The project originally used a Svelte 5 web frontend + .NET bridge process. This was replaced with a WPF app that connects directly to the OTD daemon, eliminating the bridge and solving persistent Svelte 5 client-side navigation bugs.

The Svelte frontend code is preserved in `frontend/` for reference.

## Components

### WPF App (`wpf/`)

**Role:** Single-process desktop application. Renders all UI, manages state, and communicates directly with the OTD daemon via named pipe.

**Technology:** .NET 8 WPF with CommunityToolkit.Mvvm (MVVM pattern).

**Key directories:**
- `Services/` — `DaemonClient.cs` (named pipe + StreamJsonRpc), `VMultiDetector.cs` (HID scanning)
- `ViewModels/` — `MainViewModel.cs` (navigation, connection state, data loading)
- `Views/` — XAML pages (Dashboard, TabletSettings, Presets, Console, About)
- `Themes/` — Resource dictionaries for colors and styles (light mode, glassmorphism)
- `Converters/` — WPF value converters

**Dependencies:**
- `StreamJsonRpc` 2.22.23 — JSON-RPC client matching OTD daemon version
- `HidSharp` 2.1.0 — HID device enumeration for vmulti detection
- `Newtonsoft.Json` 13.0.3 — JSON handling (required by StreamJsonRpc)
- `CommunityToolkit.Mvvm` 8.4.0 — MVVM infrastructure (`[ObservableProperty]`, `[RelayCommand]`)

### Bridge (`bridge/`) — Legacy

**Status:** No longer used at runtime. The WPF app connects directly to the daemon. Preserved as reference for the HTTP/WebSocket API pattern.

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

### 1. WPF instead of web frontend

**Decision:** Use WPF (.NET 8) rather than Svelte/React/web tech.

**Rationale:** The original Svelte 5 frontend had a persistent navigation bug where client-side routing broke when navigating back to previously-visited pages. This was traced to a Svelte 5 rendering issue. WPF provides native navigation via simple property binding (`CurrentPage` → `ContentControl` with `DataTrigger`), direct named pipe access (no bridge needed), and eliminates an entire process from the architecture.

**Trade-off:** Windows-only (no cross-platform). No hot reload for XAML (though XAML Hot Reload in Visual Studio helps). Design iteration is slower than CSS but the app actually works reliably.

### 2. Direct daemon connection instead of bridge

**Decision:** The WPF app connects directly to the OTD daemon via named pipe, replacing the bridge + HTTP architecture.

**Rationale:** Since WPF is .NET, it can use StreamJsonRpc directly — the same library the daemon uses. No HTTP translation layer needed. This eliminates a process, reduces latency, and simplifies deployment to a single .exe.

### 3. MVVM with CommunityToolkit.Mvvm

**Decision:** Use the MVVM pattern with source-generated properties and commands.

**Rationale:** `[ObservableProperty]` and `[RelayCommand]` attributes generate all the `INotifyPropertyChanged` boilerplate. Navigation is a simple `string CurrentPage` property that drives a `ContentControl` via `DataTrigger` — no routing framework needed.

### 4. JToken for data passthrough

**Decision:** Use `Newtonsoft.Json.Linq.JToken` for daemon data rather than strongly-typed C# models.

**Rationale:** Same rationale as the bridge — the daemon's data shapes are complex and evolving. JToken avoids maintaining parallel C# model classes. The XAML binds directly to JToken properties via indexer syntax (`{Binding [Name]}`).

## Technical Challenges

### Solved

**Named pipe message framing.** The OTD daemon uses the default `JsonRpc` constructor, which uses `NewLineDelimited` framing. Must use `new JsonRpc(stream)` directly.

**Newtonsoft.Json vs System.Text.Json.** StreamJsonRpc uses Newtonsoft.Json internally. Must use `JToken` not `JsonElement`.

**JValue to string in WPF commands.** WPF `CommandParameter` with JToken indexer (`{Binding [Name]}`) passes `JValue` objects, but `RelayCommand<string>` expects `string`. Fixed by binding to `[Name].Value` which unwraps to the primitive.

**Svelte 5 navigation bug (historical).** Svelte 5's `$state` reactivity failed to update `{#if}` template blocks when values returned to previously-rendered states. This affected both custom hash routing and SvelteKit's built-in router. Root cause was in Svelte 5's compiled template diffing. Resolved by switching to WPF.

### Remaining

**SVG area mapper.** The area mapping visualization needs to be recreated as a WPF Canvas with rectangles and coordinate transforms.

**Settings write-back.** The UI can display settings but does not yet write changes back to the daemon.

**Dark mode.** Light mode colors are implemented. Dark mode requires a second `ResourceDictionary` with runtime switching.

**Glassmorphism polish.** Current glass panels use semi-transparent backgrounds with drop shadows. True acrylic blur effects (via Windows Composition API) could be added for deeper visual fidelity.

## Dependency Graph

```
WPF App (.NET 8)
  └── StreamJsonRpc 2.22.23
  └── HidSharp 2.1.0
  └── Newtonsoft.Json 13.0.3
  └── CommunityToolkit.Mvvm 8.4.0

OTD Daemon (external, not modified)
  └── StreamJsonRpc 2.22.23
  └── OpenTabletDriver.Desktop
  └── (many more — not our concern)
```
