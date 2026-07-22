# OpenTabletArtist

An alternative experience for  for [OpenTabletDriver](https://opentabletdriver.net/) 

Features:
- Modern and cute user experience
- Configure which display input from your tablet goes to
- Define your active area:
    - auto force-proportions (no distorted movement or strokes)
    - resize active area
- pen dynamics: pressure curve, pressure smoothing position smoothing
- hotkeys
    - display toggle
    - switch presets
- Simplified setup

Platforms:
- Windows - working
- MacOs - Under investigation. code complete.
- Linux - Under investigation 

## Install & use

Download the latest Windows build and run it — no .NET install needed. See the
**[install guide](docs/INSTALL.md)** for the full walkthrough, or the **[User Manual](docs/USERMANUAL.md)**
for the interface in depth.

Also in [`docs/`](docs/): [Overview](docs/OVERVIEW.md) · [Architecture](docs/ARCHITECTURE.md) · [Diagnostics](docs/DIAGNOSTICS.md)

## Build from source

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
dotnet build OpenTabletArtist.slnx   # builds the app AND the bundled OTD daemon
dotnet run --project OpenTabletArtist
```

Full build prerequisites and options are in **[BUILDING.md](docs/BUILDING.md)**.
