# Architecture

## System Diagram

```
┌─────────────────────┐         ┌──────────────────┐         ┌─────────────────────┐
│   Svelte 5 + Vite   │  HTTP   │   .NET 8 Bridge  │  Named  │   OTD Daemon        │
│   Frontend           │◄──────►│   (Minimal API)  │◄──Pipe─►│ (OpenTabletDriver   │
│   localhost:5173     │  + WS   │   localhost:5000  │  JSON   │  .Daemon.exe)       │
└─────────────────────┘         └──────────────────┘  -RPC   └─────────────────────┘
         ▲                                                              │
         │                                                              │
     Browser                                                     USB/HID
    (user sees)                                               (tablet hardware)
```

## Components

### Frontend (`frontend/`)

**Role:** The user-facing interface. Renders all UI, handles user interaction, manages client-side state.

**Technology:** Svelte 5 with Vite for development tooling.

**Key directories:**
- `src/lib/theme/` — CSS custom property system for dark/light modes and glassmorphism
- `src/lib/stores/` — Reactive state (Svelte 5 runes) for theme, connection, tablets, settings
- `src/lib/services/` — REST and WebSocket clients that communicate with the bridge
- `src/lib/components/` — Reusable UI components (layout shell, glass panels, area mapper)
- `src/lib/pages/` — Route-level page components (Dashboard, AreaMapping, etc.)
- `src/lib/types/` — TypeScript interfaces mirroring OTD data models

**Dependencies:** Svelte 5, Vite 8, TypeScript. No other runtime dependencies — the UI is built entirely from scratch using CSS custom properties and native SVG.

### Bridge (`bridge/`)

**Role:** A thin translation layer. Connects to the OTD daemon's named pipe, then re-exposes the daemon's capabilities as HTTP REST endpoints and a WebSocket for real-time events. The frontend never touches the named pipe directly.

**Technology:** .NET 8 minimal API with StreamJsonRpc.

**Key files:**
- `Program.cs` — HTTP server setup, endpoint routing, CORS, WebSocket handler
- `Services/DaemonClient.cs` — Named pipe client with automatic reconnection

**Dependencies:** StreamJsonRpc (v2.22.23, same version as the daemon).

**Why .NET?** The OTD daemon speaks StreamJsonRpc, which is a .NET library. While JSON-RPC is a language-agnostic protocol, using the same library ensures perfect wire compatibility without reverse-engineering the message framing (header-delimited JSON-RPC).

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

## Key Design Decisions

### 1. Web frontend instead of native UI

**Decision:** Use Svelte + Vite rather than WinUI3, Avalonia, or another native framework.

**Rationale:** The primary goal is fast iteration on visual design. Vite's hot module replacement gives sub-second feedback on CSS and component changes. The web platform's CSS capabilities (backdrop-filter, custom properties, transitions, SVG) provide the richest toolkit for the glassmorphism aesthetic. Native frameworks offer better system integration but slower build/reload cycles.

**Trade-off:** Requires a bridge process. Cannot directly call .NET APIs from the browser.

### 2. Separate bridge process instead of referencing OTD projects

**Decision:** The bridge defines its own slim DTO types and uses StreamJsonRpc as a generic JSON-RPC client. It does not reference `OpenTabletDriver.Desktop` or any other OTD project.

**Rationale:** Referencing `OpenTabletDriver.Desktop` would pull in transitive dependencies: `OpenTabletDriver.Native` (platform interop DLLs), `OpenTabletDriver.Configurations` (hundreds of tablet JSON files), the plugin system, Octokit, WaylandNET, and more. The bridge needs none of this — it only needs to call RPC methods and forward JSON. Keeping it decoupled makes the bridge tiny, fast to build, and free of native dependency issues.

**Trade-off:** DTO types must be kept in sync manually. If the daemon's data shapes change, the bridge DTOs need updating.

### 3. JSON passthrough in the bridge

**Decision:** The bridge uses `JsonElement` (opaque JSON) for most RPC return values rather than strongly-typed C# models.

**Rationale:** The frontend (TypeScript) is the real consumer of the data shapes. Deserializing into C# models only to re-serialize into JSON for HTTP is unnecessary work. Passing JSON through keeps the bridge minimal and avoids double-maintenance of type definitions.

### 4. Hash-based routing

**Decision:** Use `location.hash` for client-side routing (`#/`, `#/area`, `#/bindings`, etc.) with a simple `$state` variable, rather than a routing library.

**Rationale:** This is a single-page prototype with six pages. A routing library adds dependency weight and API surface for no benefit. Hash routing requires zero server configuration and works with any static file server.

### 5. CSS custom properties for theming

**Decision:** All colors, spacing, blur values, and glassmorphism parameters are CSS custom properties scoped to `[data-theme="dark"]` and `[data-theme="light"]` selectors.

**Rationale:** This allows instant theme switching with zero JavaScript re-rendering — only CSS values change. Components don't need to know which theme is active; they reference variables like `var(--glass-bg)` and the correct value resolves automatically.

## Technical Challenges

### Solved

**Svelte 5 runes in module-level stores.** `$effect()` cannot be called outside of a component rendering context. Module-level `.svelte.ts` files that use `$state` are fine, but `$effect` at the top level throws `effect_orphan`. Solved by using imperative side effects (direct DOM/localStorage calls) in store mutation methods instead of reactive effects.

**Named pipe client compatibility.** The OTD daemon uses `HeaderDelimitedMessageHandler` for its JSON-RPC framing (not the default `NewLineDelimited`). The bridge must use the same handler to be wire-compatible.

**Cross-platform named pipes.** .NET's `NamedPipeClientStream` works on Windows, macOS, and Linux. The pipe name `"OpenTabletDriver.Daemon"` is consistent across platforms. On Unix systems, the pipe maps to a Unix domain socket.

### Remaining

**Backdrop-filter performance.** Heavy `backdrop-filter: blur()` on multiple stacked glass panels can be GPU-intensive. Need to profile and potentially reduce blur layers or use static blurred backgrounds for deeply nested panels.

**SVG area mapper interaction.** The area mapping visualization currently renders static rectangles. Drag-to-move and handle-to-resize require pointer event handling with SVG coordinate transforms (screen space to viewBox space). This is non-trivial, especially with rotation.

**Daemon event subscription.** StreamJsonRpc supports event forwarding, but the bridge currently uses a simplified approach. Full bidirectional event wiring (where the bridge subscribes to daemon events via the RPC proxy's C# events) needs testing with the actual daemon.

**Settings write-back.** The UI can display settings but does not yet write changes back to the daemon. The `SetSettings` endpoint exists but the frontend forms are read-only placeholders.

## Dependency Graph

```
Frontend (Svelte 5)
  └── Vite 8 (dev tooling)
  └── TypeScript
  └── No runtime dependencies (pure CSS + SVG)

Bridge (.NET 8)
  └── StreamJsonRpc 2.22.23
  └── ASP.NET Core (built into SDK)

OTD Daemon (external, not modified)
  └── StreamJsonRpc 2.22.23
  └── OpenTabletDriver.Desktop
  └── OpenTabletDriver.Plugin
  └── OpenTabletDriver.Native
  └── (many more — not our concern)
```
