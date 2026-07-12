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

Platforms:
- Windows - working
- MacOs - Under investigation. code complete.
- Linux - Under investigation

📖 **[User Manual](docs/USERMANUAL.md)** — start here.

Also in [`docs/`](docs/): [Overview](docs/OVERVIEW.md) · [Architecture](docs/ARCHITECTURE.md) · [Diagnostics](docs/DIAGNOSTICS.md)

## Build & run

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
dotnet build OpenTabletArtist.slnx   # builds the app AND the bundled OTD daemon
dotnet run --project OpenTabletArtist
```
