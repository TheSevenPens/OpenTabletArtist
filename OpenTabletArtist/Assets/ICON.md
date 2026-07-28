# App icon

The vector sources are the source of truth; the raster assets are generated from them.

| File | Role |
|------|------|
| `appicon.svg` | Full-colour mark — **the app icon** source. |
| `appicon-bw.svg` | Single-colour (B&W) silhouette mark. |
| `appicon.png` | 256×256 raster, rendered from `appicon.svg`. Used as `MainWindow.Icon`. |
| `appicon.ico` | Multi-size (16/24/32/48/64/128/256), rendered from `appicon.svg`. Used as the exe `ApplicationIcon` (taskbar / Explorer). |

The **Home page** (About column) draws the `appicon-bw.svg` geometry inline as a theme-tinted
vector (`AboutView.axaml`), so the mono mark reads correctly on both light and dark skins — no
raster needed there. If you change `appicon-bw.svg`, re-copy its path figures into that `Viewbox`.

## Regenerating the rasters

There are no SVG CLI tools in this environment, so the rasters are rendered with a
throwaway [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) + SkiaSharp console app
(`dotnet run` a tiny program that loads the SVG's `SKPicture` and draws it into an
`SKBitmap` at each size). Steps:

1. `appicon.svg` → `appicon.png` at 256.
2. `appicon.svg` → `appicon.ico` at 16,24,32,48,64,128,256 (PNG-compressed entries,
   Vista+ ICO container).
3. `appicon-bw.svg` → `appicon-home.png` at 512.

(Any SVG→PNG renderer — Inkscape, rsvg-convert, ImageMagick — works equally; the ICO
just packs the per-size PNGs into a standard icon container.)
