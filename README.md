# OpenTabletArtist

A companion app for [OpenTabletDriver](https://opentabletdriver.net/) — configure your tablet's display mapping, pen dynamics, and express keys from a friendly UI, save named profiles, and switch between them with global hotkeys or automatically per application. Packaged for Windows today; the codebase is cross-platform and the macOS port is code-complete (runs from source — packaged builds are next).

📖 **[User Manual](docs/USERMANUAL.md)** — start here.

Also in [`docs/`](docs/): [Overview](docs/OVERVIEW.md) · [Architecture](docs/ARCHITECTURE.md) · [Diagnostics](docs/DIAGNOSTICS.md)

## Build & run

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
dotnet build OpenTabletArtist.slnx   # builds the app AND the bundled OTD daemon
dotnet run --project OpenTabletArtist
```
