# OpenTabletArtist

A Windows companion app for [OpenTabletDriver](https://opentabletdriver.net/) — configure your tablet's display mapping, pen dynamics, express keys, and more from a friendly UI.

📖 **[User Manual](docs/USERMANUAL.md)** — start here.

Also in [`docs/`](docs/): [Overview](docs/OVERVIEW.md) · [Architecture](docs/ARCHITECTURE.md) · [Diagnostics](docs/DIAGNOSTICS.md)

## Build & run

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
dotnet build OpenTabletArtist.slnx   # builds the app AND the bundled OTD daemon
dotnet run --project OpenTabletArtist
```
