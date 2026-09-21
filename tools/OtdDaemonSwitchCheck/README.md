# OTD connection check

Run `dotnet run --project tools/OtdDaemonSwitchCheck -- --read-only` to inspect an already-running daemon.

The tool reports connection identity, profile count, and whether live settings match the saved file. It does not apply, save, start, stop, or switch daemons. Its historical project name is retained to avoid breaking build references.

Dynamic daemon switching is no longer supported. Reconnect behavior is tested in OtdInterop.Tests using new settings sessions and a fixed startup target.

See [the settings workspace design](../../docs/design/otd-interop-settings-workspace.md) for the supported apply/save/reload model.
