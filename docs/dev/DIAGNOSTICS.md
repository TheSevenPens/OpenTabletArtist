# Diagnostics Tab - OTD Tablet Debugger Reference

This document describes how OpenTabletDriver's tablet debugger works internally, so we can implement equivalent functionality in our Diagnostics tab without breaking daemon communication.

## Architecture Overview

```
Tablet HW → DeviceReader → ReportParser → IDeviceReport
                                              ↓
                                    (if RawClone=true)
                                              ↓
                                    RawReport event
                                              ↓
                              DriverDaemon.PostDebugReport()
                                              ↓
                              DebugReportData (JToken-serialized)
                                              ↓
                              DeviceReport event via StreamJsonRpc
                                              ↓
                              Named pipe → Client process
                                              ↓
                              UI thread marshal → Update display
```

## RPC Methods

### `SetTabletDebug(bool isEnabled)`

The single most important method. Controls whether the daemon emits debug report events.

**Daemon-side implementation** (`DriverDaemon.cs:650-659`):
```csharp
public Task SetTabletDebug(bool enabled)
{
    debugging = enabled;
    foreach (var dev in Driver.InputDevices.SelectMany(d => d.InputDevices))
        dev.RawClone = debugging;
    return Task.CompletedTask;
}
```

- Sets `RawClone = true` on every `InputDevice`, which causes `DeviceReader` to parse the raw bytes a second time and fire `RawReport`
- The daemon subscribes to `RawReport` during `DetectTablets()` and wraps each report in `DebugReportData` before firing the `DeviceReport` event
- **CRITICAL**: Must call `SetTabletDebug(false)` on cleanup/close, otherwise the daemon keeps cloning every report (wasted CPU + memory)

### `DeviceReport` Event

```csharp
event EventHandler<DebugReportData> DeviceReport;
```

Fired for every tablet report when debugging is enabled. This is a **high-frequency event** (typically 100-266 Hz depending on tablet).

### Other Relevant Methods (already in our DaemonClient)

| Method | Returns | Purpose |
|--------|---------|---------|
| `GetTablets()` | `TabletReference[]` | Tablet specs (digitizer dimensions, max pressure, etc.) |
| `GetSettings()` | `Settings` | Current profiles, output mode |
| `GetApplicationInfo()` | `AppInfo` | Paths, version |

## DebugReportData Structure

```csharp
public class DebugReportData
{
    public TabletReference Tablet { get; set; }  // Which tablet sent this
    public string Path { get; set; }              // Report type fullname, e.g. "OpenTabletDriver.Plugin.Tablet.TabletReport"
    public JToken Data { get; set; }              // JSON-serialized report fields
}
```

- `Tablet` contains the tablet's `Properties` (name, specs) and `Identifiers`
- `Path` is the .NET type name — used to deserialize `Data` back to the correct type
- `Data` is a Newtonsoft `JToken` containing the report fields

### Deserializing Reports

OTD's approach uses `AppInfo.PluginManager.PluginTypes` to resolve the type by name, then calls `Data.ToObject(type)`. **We don't have the plugin manager**, so we should deserialize manually from the JToken fields based on what `Path` tells us about the report type.

## Report Types and Their Fields

All reports implement `IDeviceReport` which has `byte[] Raw` (the raw HID bytes).

### Core Reports

| Interface | Fields | Typical Path |
|-----------|--------|-------------|
| `IAbsolutePositionReport` | `Vector2 Position` | (base for tablet reports) |
| `ITabletReport` : `IAbsolutePositionReport` | `uint Pressure`, `bool[] PenButtons` | `OpenTabletDriver.Plugin.Tablet.TabletReport` |
| `ITiltReport` | `Vector2 Tilt` | `OpenTabletDriver.Plugin.Tablet.TiltTabletReport` |
| `IAuxReport` | `bool[] AuxButtons` | `OpenTabletDriver.Plugin.Tablet.AuxReport` |
| `IProximityReport` | `bool NearProximity`, `uint HoverDistance` | via `OutOfRangeReport` |
| `IEraserReport` | `bool Eraser` | — |
| `IToolReport` | `ulong Serial`, `uint RawToolID`, `ToolType Tool` | — |

### Touch/Mouse Reports

| Interface | Fields |
|-----------|--------|
| `ITouchReport` | `TouchPoint[] Touches` (each has `byte TouchID`, `Vector2 Position`) |
| `IMouseReport` | `bool[] MouseButtons`, `Vector2 Scroll` |
| `IAbsoluteWheelReport` | nullable position |
| `IRelativeWheelReport` | nullable delta |
| `IWheelButtonReport` | `bool[] WheelButtons` |

### Special Reports

| Type | Meaning |
|------|---------|
| `OutOfRangeReport` | Pen lifted out of proximity |

### JToken Field Layout (What We Actually Receive)

For a typical `TabletReport`, the `Data` JToken looks like:
```json
{
  "Raw": "AQIDBA==",
  "Position": { "X": 1920.5, "Y": 1080.2 },
  "Pressure": 512,
  "PenButtons": [true, false]
}
```

For a `TiltTabletReport`:
```json
{
  "Raw": "AQIDBA==",
  "Position": { "X": 1920.5, "Y": 1080.2 },
  "Pressure": 512,
  "PenButtons": [true, false],
  "Tilt": { "X": 15.5, "Y": 10.2 }
}
```

For an `AuxReport`:
```json
{
  "Raw": "AQIDBA==",
  "AuxButtons": [false, true, false, false]
}
```

## OTD Debugger UI Layout

```
┌──────────────────────────────────────────────┐
│  TabletVisualizer (canvas, ~200px height)     │
│  ┌──────────────────────────────────────────┐ │
│  │  [digitizer area rectangle]              │ │
│  │       ● ← pen position dot (5x5px)      │ │
│  │       ○ ○ ← touch points (10x10px)      │ │
│  └──────────────────────────────────────────┘ │
├──────────────────────────────────────────────┤
│  Device Name:       Wacom CTL-672            │
│  Report Rate:       266hz                    │
│  Reports Recorded:  0                        │
│  ☐ Enable Data Recording                     │
├──────────────────────────────────────────────┤
│  Raw Tablet Data:   01 02 03 04 05 06 07 08 │
│  Max Position:      [21648, 13500]           │
│  Tablet Report:                              │
│    Position:[15234.0, 8921.0]                │
│    Pressure:2048                             │
│    PenButtons:[True, False]                  │
│    Tilt:[15.5, -3.2]                         │
└──────────────────────────────────────────────┘
```

### Visualizer Details

- Canvas draws a scaled rectangle representing the tablet's digitizer area
- Aspect ratio is maintained: `finalScale = min(yScale, xScale)`
- Drawing is centered in the canvas
- Pen position: filled circle (5x5px) at scaled coordinates
- Touch points: outlined circles (10x10px) at scaled coordinates
- Coordinate scaling: `tabletMm / tabletPx * scale` where `tabletMm = (Width, Height)` and `tabletPx = (MaxX, MaxY)`
- Rendering at 60 FPS via `ScheduledDrawable` / `CompositionScheduler`

### Report Rate Calculation

Uses exponential moving average with 1% smoothing factor:
```csharp
ReportPeriod += (timeDelta.TotalMilliseconds - ReportPeriod) * 0.01f;
reportRate = Math.Round(1000.0 / ReportPeriod) + "hz";
```

## Event Subscription Lifecycle

### Startup
1. Subscribe to `DeviceReport` event on the RPC client
2. Call `SetTabletDebug(true)` via RPC
3. Start receiving reports

### Shutdown (CRITICAL)
1. Call `SetTabletDebug(false)` via RPC
2. Unsubscribe from `DeviceReport` event
3. Clean up any recording streams

**If `SetTabletDebug(false)` is not called**, the daemon continues cloning every report indefinitely. This wastes resources but should not break communication.

### OTD Code Reference
```csharp
// On open:
App.Driver.DeviceReport += HandleReport;
App.Driver.TabletsChanged += HandleTabletsChanged;
await App.Driver.Instance.SetTabletDebug(true);

// On close:
await App.Driver.Instance.SetTabletDebug(false);
```

## Threading Considerations

1. **RPC events arrive on a thread pool thread** — must marshal to UI thread before updating UI
2. **High frequency** — at 266 Hz, that's a report every ~3.75ms. UI updates should be throttled or use a scheduled draw pattern
3. OTD uses `Application.Instance.AsyncInvoke()` (Eto.Forms) for marshaling; we use `Dispatcher.Invoke()` (WPF) or `Dispatcher.UIThread.InvokeAsync()` (Avalonia)
4. The visualizer only redraws at 60 FPS regardless of report rate — reports update state, rendering is decoupled

## How OTD's DaemonRpcClient Receives Events

```csharp
// In DaemonRpcClient.OnConnected():
Instance.DeviceReport += (sender, e) =>
    Application.Instance.AsyncInvoke(() => DeviceReport?.Invoke(sender, e));
```

**Important**: `Instance` is the StreamJsonRpc proxy. StreamJsonRpc automatically deserializes the `DebugReportData` from JSON-RPC notifications.

## Our DaemonClient Gap Analysis

Our `DaemonClient` currently uses raw `_rpc.InvokeAsync<T>()` calls. To receive debug events, we need to:

1. **Subscribe to the `DeviceReport` JSON-RPC notification** — StreamJsonRpc can raise events on proxy interfaces, but our client doesn't use a proxy. We'll need to use `_rpc.AddLocalRpcMethod()` or attach a target object, or switch to using `_rpc.Attach<IDriverDaemon>()`.
2. **Call `SetTabletDebug(true/false)`** — straightforward `_rpc.InvokeAsync("SetTabletDebug", true)`
3. **Marshal to UI thread** — use Avalonia's `Dispatcher.UIThread.InvokeAsync()`

### Option A: Add event handler via JsonRpc notification

```csharp
// StreamJsonRpc can handle server-to-client events as notifications
// The event name becomes the JSON-RPC method name
_rpc.AddLocalRpcMethod("DeviceReport", new Action<DebugReportData>(OnDeviceReport));
```

### Option B: Use a proxy interface (how OTD does it)

```csharp
var proxy = _rpc.Attach<IDriverDaemon>();
proxy.DeviceReport += (s, e) => { ... };
```

This is how OTD does it, but requires referencing `IDriverDaemon` and all its dependencies.

### Option C: Listen for raw JToken notifications

```csharp
// Low-level approach — receive as JToken and parse ourselves
_rpc.AddLocalRpcMethod("DeviceReport", new Action<JToken>(OnDeviceReportRaw));
```

**Recommendation**: Option A or C, since we already use raw RPC calls and don't want to pull in the full `IDriverDaemon` interface as a proxy.

## Report Formatting Reference

### Raw Bytes Display
```csharp
BitConverter.ToString(report.Raw).Replace('-', ' ')
// Output: "01 02 03 04 05 06 07 08"
```

### Formatted Report Display
Type-switch on report interfaces, appending each field:
```
Position:[15234.0, 8921.0]
Pressure:2048
PenButtons:[True, False]
Tilt:[15.5, -3.2]
NearProximity:True
HoverDistance:25
AuxButtons:[False, True, False, False]
```

### Special Cases
- `OutOfRangeReport` → display "Pen is out of Range"
- `ITouchReport` → display each touch point: `Point #1: <X, Y>;`
- Wheel reports → show value or "Idle" if null

## Data Recording Format

OTD writes one line per report to a timestamped text file:
```
Device:TabletName, { 01 02 03 ... }, Delta:16.234ms, Position:[X,Y], Pressure:512, ..., ReportType:TypeName
```

File naming: `tablet-data_{unix_timestamp}.txt`

## Risk Mitigation

Previous attempts at adding diagnostics broke daemon communication. Potential causes:

1. **Not calling `SetTabletDebug(false)` on cleanup** — leaves daemon in debug mode, wasting resources
2. **Subscribing to events incorrectly** — if the JSON-RPC subscription fails or uses wrong method names, it could interfere with the RPC connection
3. **Blocking the RPC dispatch thread** — if event handlers do heavy work synchronously on the RPC thread, it can stall all RPC communication
4. **Deserializing with wrong types** — `DebugReportData.ToObject()` uses `PluginManager` which we don't have; deserializing with wrong types could throw and break the event pipeline
5. **Reconnection issues** — if debug mode is enabled and the connection drops, the reconnection handler might not properly re-subscribe

### Safe Implementation Strategy
1. **Start with just the RPC call** — add `SetTabletDebug` to DaemonClient, verify the app still works
2. **Then add event subscription** — wire up event handling, verify connection stability
3. **Then add UI** — display the data in the Diagnostics view
4. **Test each step** — confirm daemon communication works after each change before moving to the next
