<#
.SYNOPSIS
  Captures every OpenTabletArtist page as a crisp 1920x1080 PNG for the user documentation.

.DESCRIPTION
  Lays the running app's window out at a fixed 1280x720 DIP, selects each page and tab by clicking it,
  captures the window's client area as real screen pixels, and resamples to 1920x1080. See README.md for why
  it works this way and what to check before running it.

  The app must already be running, showing the tablet you want in the shots, with nothing else open.

.PARAMETER OutDir
  Where the PNGs go, one per page, named by slug (home, pen-pressure, settings-presets, ...).

.PARAMETER DocsRepo
  Optional. Path to the OpenTabletArtistDocs checkout. Each capture is also copied over the asset it
  replaces there, per docs-image-map.json.

.PARAMETER Only
  Slugs to capture; default is all of them.

.PARAMETER Python
  Python with Pillow installed. Defaults to the real binary behind a mise shim when mise is present,
  otherwise `python`.

.EXAMPLE
  .\capture-all.ps1 -OutDir $env:TEMP\ota-shots -DocsRepo C:\Users\me\Documents\GitHub\OpenTabletArtistDocs
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $env:TEMP 'ota-shots'),
    [string]$DocsRepo,
    [string[]]$Only = @(),
    [string]$Python,
    [string]$WindowTitle = 'OpenTabletArtist'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

# A mise shim runs three processes per call and leaves orphans when killed; use the real binary.
if (-not $Python) {
    $Python = if (Get-Command mise -ErrorAction SilentlyContinue) { (& mise which python).Trim() } else { 'python' }
}
& $Python -I -c "import PIL" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Python at '$Python' has no Pillow. Install it with: $Python -m pip install pillow" }

$procs = @(Get-Process OpenTabletArtist -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) { throw 'OpenTabletArtist is not running. Start the released build first (see README.md).' }
if ($procs.Count -gt 1) { throw "$($procs.Count) OpenTabletArtist processes are running; close the extras so the shots come from one." }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$shot = Join-Path $here 'window_shot.py'

# -- navigation ----------------------------------------------------------------------------------
# UI Automation's Select() ticks a radio button but does not run the app's navigation, so click it with
# the real mouse at the middle of its bounding rectangle. Coordinates from this PowerShell host are
# consistent between UI Automation and the cursor, which is all this needs; window_shot.py does the
# pixel-exact work in its own, properly DPI-aware, process.
Add-Type @"
using System;using System.Runtime.InteropServices;
public class OtaNav{ [DllImport("user32.dll")]public static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e); }
"@
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes

function Click-Radio([string]$name) {
    $prefix = $name.EndsWith('*'); if ($prefix) { $name = $name.TrimEnd('*') }
    $h = (Get-Process OpenTabletArtist).MainWindowHandle
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    $ct = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
    $found = $null; $names = @()
    foreach ($c in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ct)) {
        $n = $c.Current.Name; $names += $n
        if (-not $found) { if ($prefix) { if ($n -like "$name*") { $found = $c } } elseif ($n -eq $name) { $found = $c } }
    }
    # Loud on purpose: a renamed tab must fail the run, not leave the previous page captured under a new name.
    if (-not $found) { throw "No radio button named '$name'. Present: $($names -join ' | ')" }
    $r = $found.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 120
    [OtaNav]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [OtaNav]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

# slug, top-level page (name, or 'Prefix*'), tab on that page (or $null)
$targets = @(
    @('home',               'home',     $null),
    @('health',             'Health*',  $null),
    @('tablet-about',       'tablet',   'about'),
    @('tablet-mapping',     'tablet',   'mapping'),
    @('tablet-calibration', 'tablet',   'calibration'),
    @('tablet-buttons',     'tablet',   'buttons'),
    @('tablet-wheels',      'tablet',   'wheels'),
    @('pen-basics',         'pen',      'basics'),
    @('pen-pressure',       'pen',      'pressure'),
    @('pen-tilt',           'pen',      'tilt'),
    @('pen-buttons',        'pen',      'buttons'),
    @('scribble',           'scribble', $null),
    @('settings-presets',   'settings', 'presets'),
    @('settings-hotkeys',   'settings', 'hotkeys'),
    @('settings-theme',     'settings', 'theme'),
    @('settings-system',    'settings', 'system'),
    @('settings-drivers',   'settings', 'drivers'),
    @('advanced-daemon',    'advanced', 'daemon'),
    @('advanced-console',   'advanced', 'console'),
    @('advanced-configs',   'advanced', 'configs'),
    @('advanced-filters',   'advanced', 'filters'),
    @('advanced-plugins',   'advanced', 'plugins')
)

# Lay the window out first: resizing reflows every page.
& $Python -I $shot size --title $WindowTitle
if ($LASTEXITCODE -ne 0) { throw 'Could not size the window.' }
Start-Sleep -Seconds 1

$map = if ($DocsRepo) { Get-Content (Join-Path $here 'docs-image-map.json') -Raw | ConvertFrom-Json } else { $null }
$done = 0
foreach ($t in $targets) {
    $slug = $t[0]
    if ($Only.Count -gt 0 -and $Only -notcontains $slug) { continue }
    Click-Radio $t[1]; Start-Sleep -Milliseconds 900
    if ($t[2]) { Click-Radio $t[2]; Start-Sleep -Milliseconds 1200 }
    $png = Join-Path $OutDir "$slug.png"
    & $Python -I $shot shot --title $WindowTitle --out $png
    if ($LASTEXITCODE -ne 0) { throw "Capture of '$slug' failed." }
    if ($DocsRepo) {
        $asset = $map.$slug
        if (-not $asset) { throw "docs-image-map.json has no entry for '$slug'." }
        Copy-Item $png (Join-Path $DocsRepo ".gitbook\assets\$asset") -Force
    }
    $done++
}
Write-Host "Captured $done page(s) into $OutDir" -ForegroundColor Green
