# Suggestions for the OpenTabletDriver team

## Purpose & context

OpenTabletArtist (OTA) is a Windows companion **UI** built on top of OpenTabletDriver (OTD). It talks
to the OTD daemon over the named pipe / StreamJsonRpc, and bundles the daemon from the OTD source. As
we've built out OTA's UI, we've run into a handful of places where the daemon's API doesn't quite
expose what a rich UI wants — so we work around it (parsing logs, hardcoding lists, hand-building JSON).

None of these are blockers — we've shipped around all of them. But each one is a spot where a small
daemon-side addition would let a UI do the *right* thing instead of a fragile workaround, and would
help **any** OTD front-end, not just ours.

This document is written to **start a conversation**, not to file a pile of issues. For each item we
describe:

1. **What we're building** — the concrete OTA feature and why it exists.
2. **What makes it hard today** — the exact limitation, so it's clear this is grounded, not hand-wavy.
3. **What would make it simpler** — the smallest change we can think of that unblocks it cleanly.

If a direction sounds good, we're happy to help shape the API and contribute the implementation.

---

## 1. Knowing which plugins are actually loaded

**What we're building.** The per-tablet **Filters** tab lists the filters on a profile, and we want to
show whether each one is *actually running*. We also do a small cleanup: profiles created by an older
build of our app carried a filter under an old type name (a rename left `OtdWindowsHelper.Dynamics.
DynamicsFilter` sitting next to the current `OpenTabletArtist.Dynamics.DynamicsFilter`), and we want to
recognize and remove the dead one.

**What makes it hard today.** The daemon has a `PluginManager` with the loaded plugin `TypeInfo`s, but
there's no RPC to read it. From the client we can't tell an **active** filter (the daemon has a plugin
for its type) from an **inert** one (a leftover store the daemon silently ignores because no plugin
matches the type). So we can't honestly badge "this filter isn't doing anything," and our cleanup has
to hardcode which type names are "ours but stale."

**What would make it simpler.** An RPC that returns the **loaded plugin types** — e.g.
`IEnumerable<string> GetLoadedPluginTypes()` (or richer `PluginMetadata`). With that, a UI can mark any
`PluginSettingStore` whose `Path` isn't loaded as inert, and offer to clean it up generically.

---

## 2. Discovering binding & filter types + their settings

**What we're building.** OTA now has an **editable ExpressKeys** tab: pick a binding type (None /
Keyboard / Mouse button / Mouse scroll), and for Keyboard, Ctrl/Shift/Alt + a key. To build those
editors we need the list of available binding types, each one's settings (`[Property]` names), and the
**valid values** for each setting — the key list (`KeyBinding.ValidKeys`), the `MouseButton` enum, the
`ScrollDirection` values, etc.

**What makes it hard today.** None of that is queryable over the pipe. Because our app doesn't (and
shouldn't) reference the plugin assemblies, we **hardcode** the type-name strings and curate our own
copies of the valid-key list, mouse buttons, and scroll directions. These are duplicated from OTD and
will silently drift out of sync when OTD changes a key name, adds a binding, or a third-party plugin
adds a new binding type we can't discover at all.

**What would make it simpler.** A metadata RPC describing the installed **bindings and filters**: for
each, the type name, friendly name (`[PluginName]`), and its `[Property]` settings with any validated
value set (`[PropertyValidated]` / enum). A UI could then render a correct editor for *any* binding —
including third-party ones — with zero hardcoding, and never fall out of sync.

---

## 3. Structured conflicting-driver detection

**What we're building.** OTA has a **Driver Cleanup** page (and a Home alert) that tells users when a
manufacturer tablet driver (Wacom, Huion, XP-Pen, …) is present and can interfere with OTD — with the
driver name, its impact, and the offending processes — then links to the cleanup tool.

**What makes it hard today.** The daemon *has* this information (`DriverInfo.GetDriverInfos()` on
startup) but only emits it as **log warnings** ("'Wacom Tablet' driver is detected. It will block
detection… Processes found: […]"). So our UI has to **parse those log strings** — coupled to the exact
wording and to being connected when they're logged (we fall back to scraping `GetCurrentLog()`). It's
fragile, and it can't distinguish severity cleanly.

Relatedly, the XP-Pen detector matched **our own process**: its `Pentablet` heuristic matches
"O**penTablet**Artist", and the self-exclusion is hardcoded to the regex `OpenTabletDriver`, which
doesn't cover a fork — so the daemon flagged OpenTabletArtist as an XP-Pen driver. We filter it out
client-side, but it's a sharp edge for any renamed build.

**What would make it simpler.** An RPC returning the structured detections — e.g. name +
`DriverStatus` flags + process names — so a UI can present them directly with no string parsing.
Separately, widening the self-exclusion to match `OpenTablet` (or making it configurable) would stop
forks from being flagged as conflicts.

---

## 4. Constructing / validating settings stores without the plugin assembly

**What we're building.** Whenever OTA writes a binding, filter, or output-mode change, it has to build
a `PluginSettingStore` for a plugin type it doesn't reference (Key Binding, Multi-Key, Mouse, the
Dynamics/Hover/Calibration filters, Windows Ink modes).

**What makes it hard today.** We hand-build the store by `JsonConvert.DeserializeObject<PluginSetting
Store>("{}")`, then set `Path` and the `Settings` entries by string. It works, but we're guessing at
property names and valid values with no validation until the daemon applies them — a typo just silently
does nothing.

**What would make it simpler.** A way to **construct/validate** a store from a type name + property
values via RPC (returning the built store, or validation errors). Combined with the metadata in #2,
a UI could offer any plugin's settings correctly without ever touching plugin binaries.

---

## 5. Faster / observable daemon startup

**What we're building.** OTA auto-starts the daemon on launch and shows connection progress. We want
"connecting" to feel quick.

**What makes it hard today.** We timed it: to an already-running daemon, connect is ~1.4s; from a cold
start it's ~6s — and **~5s of that is the daemon's own initialization** (loading configs, plugins, and
the first tablet detection) before its pipe accepts connections. There's no signal for "the daemon is
up but still initializing" vs "ready," so a client can only wait.

**What would make it simpler.** Any of: a **"ready" state/event** (or begin listening on the pipe
before full init and report progress), or publishing the daemon **ReadyToRun/AOT** to cut JIT cold
start. Even just documenting the startup phases would let front-ends show honest progress instead of a
long silent "Connecting…".

---

## 6. Granular settings writes

**What we're building.** Small, frequent edits — change one profile's display mapping, toggle a filter,
set an express key.

**What makes it hard today.** Every change is a full `GetSettings` → mutate → `SetSettings` round-trip
of the entire settings object. That's a lot of data for a one-field change and invites races when
several UI surfaces touch settings close together (we coalesce loads to manage it).

**What would make it simpler.** A patch-style API — set a single profile, or a single binding/filter —
so a UI can make targeted edits without shipping (and risking clobbering) the whole settings blob.

---

## What already works really well

Credit where it's due — these are the parts that made OTA *possible* and pleasant to build, and we'd
hate to see them change:

- **Event forwarding over StreamJsonRpc.** The daemon pushing `DeviceReport`, `TabletsChanged`, and
  `Message` as client notifications is fantastic. OTA's live pen dot, the express-key press highlight,
  live tablet detection, and the Log page all ride on it, and it's the cleanest part of the whole
  integration.
- **`GetCurrentLog()` + the `Message` stream together** let a client seed *and* follow the log without
  races — a nice pattern we reused for driver detection.
- **The typed `Settings` model over the pipe** (profiles, `AbsoluteModeSettings`, `PluginSettingStore`)
  is expressive enough that we can do real editing, not just display.
- **`GetApplicationInfo` / `GetTablets` / `GetDiagnosticInfo`** give us the paths, tablet specs, and
  diagnostics we need without shelling out or guessing.
- **The daemon being a plain separate process we can launch and talk to** keeps the whole architecture
  simple and debuggable.

Thank you — OTA exists because OTD is this approachable to build on.
