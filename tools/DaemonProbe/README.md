# DaemonProbe

A tiny headless smoke test for the OpenTabletDriver **daemon connection** — no GUI required.

It mirrors [`DaemonClient`](../../OpenTabletArtist/Services/DaemonClient.cs)'s exact transport
(`NamedPipeClientStream("OpenTabletDriver.Daemon")` → `new JsonRpc(pipe)` → `InvokeAsync<T>(method)`) and
round-trips `GetTablets` + `GetSettings` against whatever daemon is listening on that pipe. Its job is to
prove the .NET named-pipe transport reaches the daemon on the current OS **before** wiring up the app —
it's the fastest way to verify the foundation on a new platform (this was written to validate macOS for
[#140](https://github.com/TheSevenPens/OpenTabletArtist/issues/140), where .NET maps the "named pipe"
onto a Unix-domain socket transparently).

## Run

```sh
# against whatever daemon is already running (OTD.app, or one you built from the submodule)
dotnet run --project tools/DaemonProbe

# custom pipe name / connect timeout (ms)
dotnet run --project tools/DaemonProbe -- OpenTabletDriver.Daemon 8000
```

Expected output when a daemon is up with a tablet connected:

```
[probe] connecting to pipe 'OpenTabletDriver.Daemon' (timeout 5000 ms) ...
[probe] pipe connected. Attaching JsonRpc...
[probe] GetTablets OK -> 1 tablet(s)
          - Wacom Movink 13 (DTH-135)
[probe] GetSettings OK -> settings object returned
          top-level keys: Revision, Profiles, LockUsableAreaDisplay, LockUsableAreaTablet, Tools
          Profiles: 2
[probe] ROUND-TRIP SUCCEEDED.
```

Exit codes: `0` round-trip OK · `2` couldn't connect · `3` connected but an RPC call failed.

## Notes

- **The daemon binds the pipe only after startup/tablet-detection finishes** (a few seconds). If the probe
  can't connect right after launching a daemon, just retry — `DaemonClient`'s real connect-loop already
  retries for this reason.
- Standalone dev tool: **not part of the solution** (`OpenTabletArtist.slnx`) and never shipped, so it
  doesn't affect app builds or CI.
- To point it at a daemon you built from the submodule, launch that daemon first (see the
  [#140 feasibility doc](../../docs/design/140-macos-feasibility.md) spike log for the macOS specifics).
