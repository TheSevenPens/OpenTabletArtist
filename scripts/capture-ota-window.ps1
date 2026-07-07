<#
.SYNOPSIS
  Capture the OpenTabletArtist window to a PNG (clean, window-only, no masking).

.DESCRIPTION
  Grabs the current OTA window's pixels via GDI CopyFromScreen and saves a PNG. Handy for
  documentation / issue screenshots. The app must be running and its window visible and frontmost;
  maximize it first for full-page shots. Windows-only.

.PARAMETER Name
  Base filename (no extension). Defaults to a timestamp.

.PARAMETER Dir
  Output folder (created if missing). Defaults to %USERPROFILE%\Pictures\OpenTabletArtist.

.EXAMPLE
  # Capture the current page as Home.png
  .\capture-ota-window.ps1 -Name Home

.EXAMPLE
  # Capture every page: click each nav item in the app, then run one of these per page
  .\capture-ota-window.ps1 -Name 01-Home -Dir "$env:USERPROFILE\Pictures\OTA-Pages"
#>
param(
    [string]$Name = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [string]$Dir  = (Join-Path $env:USERPROFILE 'Pictures\OpenTabletArtist')
)

Add-Type -AssemblyName System.Drawing

$proc = Get-Process OpenTabletArtist -ErrorAction SilentlyContinue |
        Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (-not $proc) { throw 'OpenTabletArtist is not running (no window found).' }

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OtaCap {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$r = New-Object OtaCap+RECT
[void][OtaCap]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top

New-Item -ItemType Directory -Force -Path $Dir | Out-Null
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))

$out = Join-Path $Dir "$Name.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Write-Output "Saved $out ($w x $h)"
