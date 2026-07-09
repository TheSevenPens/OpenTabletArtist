<#
.SYNOPSIS
  Predictable build for OpenTabletArtist: builds the whole solution and clears the
  stale-daemon / file-lock situations the docs warn about.

.DESCRIPTION
  Wraps `dotnet build OpenTabletArtist.slnx` — the *solution*, so the OTD daemon exe is
  produced too. Building only the app project leaves the daemon missing, which surfaces later
  as "Not connected" / "No tablet detected" (see docs/USERMANUAL.md, docs/ARCHITECTURE.md).

  Before building, it clears the common blockers so the result is repeatable:
   * Stops any running OpenTabletArtist, OpenTabletDriver.Daemon, and OpenTabletDriver.UX.Wpf
     processes. While running they hold the OTD DLLs / app exe and the build fails with
     "the file is locked by ...". The daemon auto-starts again the next time you launch the
     app. Use -NoStop to skip this (the build then fails fast if a lock is present).
   * Ensures the OpenTabletDriver submodule is checked out (external/OpenTabletDriver),
     initializing it if missing.

  After a successful build it verifies the daemon exe exists and prints its path.

.PARAMETER Configuration
  Debug (default) or Release.

.PARAMETER Test
  Also run the xUnit test project after a successful build (mirrors CI: --no-build).

.PARAMETER NoStop
  Don't stop running OTA/daemon processes. If any are running the build may fail with a file
  lock; the script warns and lets dotnet report it rather than terminating the processes.

.PARAMETER Clean
  Run `dotnet clean` on the solution before building.

.EXAMPLE
  .\scripts\build.ps1
  # Stop stale processes, build the solution in Debug, confirm the daemon exe.

.EXAMPLE
  .\scripts\build.ps1 -Configuration Release -Test
  # Release build followed by the test suite.

.EXAMPLE
  .\scripts\build.ps1 -NoStop
  # Leave running processes alone (fails fast if they hold a lock).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Test,
    [switch]$NoStop,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo      = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution  = Join-Path $repo 'OpenTabletArtist.slnx'
$daemonDir = Join-Path $repo 'external/OpenTabletDriver/OpenTabletDriver.Daemon'
$daemonExe = Join-Path $daemonDir "bin/$Configuration/net8.0/OpenTabletDriver.Daemon.exe"
$testProj  = Join-Path $repo 'tests/OpenTabletArtist.Tests/OpenTabletArtist.Tests.csproj'

# Processes that hold build outputs open (OTD DLLs / the app exe).
$lockingProcesses = @('OpenTabletArtist', 'OpenTabletDriver.Daemon', 'OpenTabletDriver.UX.Wpf')

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# --- 1. Sanity: solution present, submodule checked out ---
if (-not (Test-Path $solution)) { throw "Solution not found at $solution" }

$daemonProj = Join-Path $daemonDir 'OpenTabletDriver.Daemon.csproj'
if (-not (Test-Path $daemonProj)) {
    Write-Step 'OpenTabletDriver submodule missing — running git submodule update --init --recursive'
    & git -C $repo submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize the OpenTabletDriver submodule.' }
}

# --- 2. Release file locks by stopping stale processes ---
$running = @(Get-Process -Name $lockingProcesses -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $list = ($running | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ', '
    if ($NoStop) {
        Write-Warning "Running and may lock build outputs: $list. Re-run without -NoStop to stop them."
    }
    else {
        foreach ($p in $running) {
            Write-Step "Stopping $($p.ProcessName) (PID $($p.Id)) to release locked build outputs"
            try { $p | Stop-Process -Force -ErrorAction Stop }
            catch { Write-Warning "Could not stop $($p.ProcessName) (PID $($p.Id)): $_" }
        }
        Start-Sleep -Milliseconds 800   # let Windows release the file handles
    }
}

# --- 3. Optional clean ---
if ($Clean) {
    Write-Step "dotnet clean ($Configuration)"
    & dotnet clean $solution -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "clean failed (exit $LASTEXITCODE)." }
}

# --- 4. Build the solution (app + daemon + tests) ---
Write-Step "dotnet build OpenTabletArtist.slnx ($Configuration)"
& dotnet build $solution -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

# --- 5. Confirm the daemon exe was produced ---
if (Test-Path $daemonExe) {
    Write-Host "Daemon exe: $daemonExe" -ForegroundColor Green
}
else {
    Write-Warning "Build succeeded but the daemon exe was not found at $daemonExe -- the app may sit at 'Not connected'."
}

# --- 6. Optional tests ---
if ($Test) {
    Write-Step 'dotnet test (--no-build)'
    & dotnet test $testProj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
}

Write-Host "`nBuild succeeded ($Configuration)." -ForegroundColor Green
