<#
.SYNOPSIS
  Predictable build for OpenTabletArtist: builds the whole solution and clears the
  stale-daemon / file-lock situations the docs warn about.

.DESCRIPTION
  Wraps `dotnet build OpenTabletArtist.slnx` and then builds the OTD daemon separately.

  The daemon is deliberately NOT in the solution (#786): OTA is moving to shipping
  OpenTabletDriver's own released binary rather than one we build, and a build that quietly
  produces a daemon makes "which daemon am I running?" harder to answer. Ordinary builds and CI
  no longer produce one — so a plain `dotnet build OpenTabletArtist.slnx` leaves the app with no
  daemon to launch, which surfaces as "Not connected" / "No tablet detected".

  This script still builds it, because a development setup wants a runnable app. Use
  -SkipDaemon when you are driving an installed OTD instead.

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
    [switch]$Clean,
    # The daemon is not in the solution (#786); this builds it separately. Skip it when you are
    # driving an OpenTabletDriver you already have installed.
    [switch]$SkipDaemon
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

# Everything below runs from the repository root.
#
# Not cosmetic: `dotnet test` chooses its runner from global.json, and global.json is resolved from
# the CURRENT DIRECTORY rather than from the project path. This script passes absolute paths, so
# before this it ran happily from anywhere -- and from anywhere but the checkout it found no
# Microsoft.Testing.Platform runner, ran no tests, and exited 0. `build.ps1 -Test` reported success
# having tested nothing (#942). A deliberately failing test showed it plainly: inside the checkout
# `failed: 1` and exit 2; outside, no output and exit 0.
#
# The whole body is inside this rather than just the test call, so a dotnet command added later
# cannot bring the problem back. Pop-Location is in a finally because a throw here would otherwise
# leave the caller's shell in a directory it did not choose.
Push-Location $repo
try {
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

    # --- 5. Build the daemon (not part of the solution any more -- see the notes above) ---
    if ($SkipDaemon) {
        Write-Host "Skipping the daemon build (-SkipDaemon). The app will need an OTD you already have." -ForegroundColor Yellow
    }
    else {
        Write-Step "dotnet build OpenTabletDriver.Daemon ($Configuration)"
        & dotnet build $daemonProj -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Daemon build failed (exit $LASTEXITCODE)." }

        if (Test-Path $daemonExe) {
            Write-Host "Daemon exe: $daemonExe" -ForegroundColor Green
        }
        else {
            Write-Warning "The daemon build succeeded but the exe was not found at $daemonExe -- the app may sit at 'Not connected'."
        }
    }

    # --- 6. Optional tests ---
    if ($Test) {
        Write-Step 'dotnet test (--no-build)'
        & dotnet test $testProj -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
    }

    Write-Host "`nBuild succeeded ($Configuration)." -ForegroundColor Green
}
finally { Pop-Location }
