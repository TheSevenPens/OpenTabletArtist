<#
.SYNOPSIS
  Verifies the missing-.NET-runtime recovery on a clean Windows machine (#786, D2).

.DESCRIPTION
  OpenTabletDriver's official Windows release is framework-dependent, so a machine without the .NET 8
  runtime gets a daemon that exits before printing anything. OTA is supposed to notice, explain, and
  offer to install the runtime.

  None of that can be tested in CI: a GitHub runner has the .NET SDK, so it always has the runtime.
  Every defect found on this path so far was found by reading the code, not by running it -- #796 alone
  fixed three faults that are invisible until someone without .NET 8 presses the button. This script is
  what closes that gap.

  It does the parts a machine can do (preconditions, daemon exit code, evidence capture) and walks you
  through the parts it cannot (the UAC prompt, watching the window stay responsive), recording a verdict
  for each. It writes a markdown report you can paste into the issue.

  RUN THIS ON A THROWAWAY VM. Step 4 installs the .NET runtime, which cannot be cleanly undone, and the
  machine stops being a clean-room the moment it succeeds. Snapshot first if you want to re-run.

.PARAMETER BundlePath
  The unpacked release: the folder holding OpenTabletArtist.exe and Daemon\. Download the asset from a
  release, or copy publish\OpenTabletArtist from a build.

.PARAMETER OutputPath
  Where to write the report and captured logs. Defaults to .\runtime-recovery-evidence.

.EXAMPLE
  .\verify-runtime-recovery.ps1 -BundlePath C:\OpenTabletArtist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,
    [string]$OutputPath = (Join-Path (Get-Location) 'runtime-recovery-evidence')
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Good($msg) { Write-Host "    $msg" -ForegroundColor Green }
function Write-Bad($msg)  { Write-Host "    $msg" -ForegroundColor Red }

$results = [System.Collections.Generic.List[object]]::new()
function Record($name, $verdict, $detail) {
    $results.Add([pscustomobject]@{ Check = $name; Verdict = $verdict; Detail = $detail })
    if ($verdict -eq 'PASS') { Write-Good "PASS  $name" } else { Write-Bad "$verdict  $name -- $detail" }
}

New-Item -ItemType Directory -Force $OutputPath | Out-Null

# --- 1. Preconditions ---------------------------------------------------------------------
# A machine that already has .NET 8 cannot test any of this, and would report a cheerful pass for the
# wrong reason. Refuse rather than produce a meaningless report.

Write-Step 'Checking this really is a clean machine'

$daemonExe = Join-Path $BundlePath 'Daemon\OpenTabletDriver.Daemon.exe'
$appExe    = Join-Path $BundlePath 'OpenTabletArtist.exe'
foreach ($p in @($appExe, $daemonExe)) {
    if (-not (Test-Path $p)) { throw "Not found: $p. Point -BundlePath at an unpacked release." }
}

# What was actually verified. A report that doesn't identify the bundle is evidence about nothing --
# the next person cannot tell whether it covers the build they are about to ship (#803).
$bundleFacts = foreach ($p in @($appExe, $daemonExe)) {
    $item = Get-Item $p
    [pscustomobject]@{
        File    = (Resolve-Path $p).Path.Substring($BundlePath.TrimEnd('').Length + 1)
        Version = $item.VersionInfo.FileVersion
        Size    = $item.Length
        Sha256  = (Get-FileHash $p -Algorithm SHA256).Hash
    }
}
foreach ($f in $bundleFacts) { Write-Host "    $($f.File)  v$($f.Version)  $($f.Sha256.Substring(0,16))..." -ForegroundColor DarkGray }

$sharedRoots = @(
    (Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.NETCore.App'),
    $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'shared\Microsoft.NETCore.App' })
) | Where-Object { $_ -and (Test-Path $_) }

$installed = @()
foreach ($r in $sharedRoots) { $installed += (Get-ChildItem $r -Directory | Select-Object -Expand Name) }
$net8 = $installed | Where-Object { $_ -match '^8\.' }

if ($net8) {
    Write-Bad "This machine already has .NET 8 ($($net8 -join ', '))."
    Write-Bad 'The recovery path cannot be exercised here. Use a VM with no .NET 8 runtime.'
    throw 'Preconditions not met: .NET 8 is present.'
}
Record 'No .NET 8 runtime present' 'PASS' "installed runtimes: $(if ($installed) { $installed -join ', ' } else { 'none' })"

# --- 2. The daemon fails the way OTA expects ----------------------------------------------
# OTA's detection keys off the host's exit code. If this machine produces a different one, the offer
# will never appear and everything after this is moot -- so establish it before involving the UI.

Write-Step 'Running the bundled daemon directly to see how it fails'

$proc = Start-Process -FilePath $daemonExe -PassThru -Wait -NoNewWindow `
    -RedirectStandardOutput (Join-Path $OutputPath 'daemon-stdout.txt') `
    -RedirectStandardError  (Join-Path $OutputPath 'daemon-stderr.txt')
$code = $proc.ExitCode
$hex = '0x{0:X8}' -f $code

# 0x80008083 = no host library at all; 0x80008096 = host present, no matching framework.
$expected = @(-2147450749, -2147450730)
if ($expected -contains $code) {
    Record 'Daemon exits with a missing-runtime code' 'PASS' "$code ($hex)"
} else {
    Record 'Daemon exits with a missing-runtime code' 'FAIL' `
        "got $code ($hex); OTA only recognises 0x80008083 / 0x80008096, so the offer will not appear"
}

# --- 3. The guided part -------------------------------------------------------------------
# Each of these corresponds to a defect fixed in #796 that no automated test could have caught.

Write-Step 'Now the parts that need you'
Write-Host '    Launch the app from the bundle and go to the Daemon page.' -ForegroundColor Yellow
Write-Host "    ($appExe)" -ForegroundColor DarkGray

function Ask($name, $question, $why) {
    Write-Host "`n    $question" -ForegroundColor Yellow
    Write-Host "      why: $why" -ForegroundColor DarkGray
    $a = Read-Host '      [y]es / [n]o / [s]kip'
    switch ($a.ToLower()) {
        'y' { Record $name 'PASS' '' }
        's' { Record $name 'SKIP' 'not exercised' }
        default {
            $detail = Read-Host '      what happened instead?'
            Record $name 'FAIL' $detail
        }
    }
}

Ask 'Offer appears' `
    'Does the Daemon page offer to install the .NET 8 runtime?' `
    'the offer is derived from the disk; if it is missing, detection is wrong on this machine'

Ask 'Failure is explained' `
    'Does the error text name the .NET 8 runtime as the cause (not just "exited immediately")?' `
    'the launch-failure path should recognise the host exit code, not guess'

Write-Host "`n    Press Install .NET 8, and CANCEL the UAC prompt when it appears." -ForegroundColor Yellow

Ask 'UAC names Microsoft' `
    'Did the elevation prompt show Microsoft as a verified publisher?' `
    'confirms the downloaded installer is genuinely signed, independent of OTA''s own check'

Ask 'Cancelling is silent' `
    'After cancelling: no error message, and the offer still there?' `
    '#796 -- every launch failure used to be read as consent; cancelling must not look like a fault'

Write-Host "`n    Press Install .NET 8 again, and ACCEPT the prompt. While it installs:" -ForegroundColor Yellow
Write-Host '    drag the window, switch tabs, resize it.' -ForegroundColor Yellow

Ask 'App stays responsive' `
    'Did the window keep responding for the whole install?' `
    '#796 -- the installer was awaited synchronously on the UI thread and froze the app'

Ask 'Outcome is shown' `
    'Did a message confirm the install (and mention a restart, if one is needed)?' `
    '#796 -- this used to be drawn inside the offer, which the install removes, hiding it'

Ask 'Daemon actually starts' `
    'Did the daemon start and the page show connected, without you pressing Start?' `
    '#796 -- refreshing only retried the pipe, and the daemon had already exited; nothing relaunched it'

Ask 'Tablet works' `
    'With a tablet plugged in: is it detected?' `
    'the point of the whole exercise'

# --- 4. Report ----------------------------------------------------------------------------

Write-Step 'Writing the report'

$log = Join-Path $env:LOCALAPPDATA 'OpenTabletArtist'
if (Test-Path $log) {
    Copy-Item $log (Join-Path $OutputPath 'appdata') -Recurse -Force -ErrorAction SilentlyContinue
}

# Skips are not passes (#803). Every manual check can be skipped, so counting only FAIL meant a run
# where the operator skipped everything printed "All checks passed" and exited 0 -- the script certifying
# that nothing had been verified. This whole path exists because unrunnable checks hid real defects;
# a check that was not run must not read as one that succeeded.
$failed  = ($results | Where-Object Verdict -eq 'FAIL').Count
$skipped = ($results | Where-Object Verdict -eq 'SKIP').Count
$passed  = ($results | Where-Object Verdict -eq 'PASS').Count
$report = Join-Path $OutputPath 'report.md'

$lines = @()
$lines += '# Runtime recovery on a clean machine'
$lines += ''
$lines += "- Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
$lines += "- Windows: $((Get-CimInstance Win32_OperatingSystem).Caption) ($([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture))"
$lines += "- Bundle: $BundlePath"
$lines += "- Runtimes present at start: $(if ($installed) { $installed -join ', ' } else { 'none' })"
$lines += ''
$lines += '| File | Version | Size | SHA256 |'
$lines += '|---|---|---|---|'
foreach ($f in $bundleFacts) { $lines += "| ``$($f.File)`` | $($f.Version) | $($f.Size) | ``$($f.Sha256)`` |" }
$lines += "- Daemon exit code without a runtime: $code ($hex)"
$lines += ''
$lines += '| Check | Verdict | Detail |'
$lines += '|---|---|---|'
foreach ($r in $results) { $lines += "| $($r.Check) | $($r.Verdict) | $($r.Detail) |" }
$lines += ''
$lines += $(if ($failed -gt 0) {
    "**$failed check(s) failed**, $passed passed, $skipped skipped. The recovery path is broken."
} elseif ($skipped -gt 0) {
    "**Incomplete: $skipped check(s) were not run**, $passed passed. This run does NOT verify the " +
    'recovery path -- the skipped checks are the ones no machine can perform automatically, which is ' +
    'the entire reason this script is manual.'
} else {
    "**All $passed checks passed.**"
})
$lines | Set-Content -Path $report -Encoding utf8

Write-Host "`nReport: $report" -ForegroundColor Cyan
Write-Host "Evidence: $OutputPath" -ForegroundColor Cyan
if ($failed -gt 0) { Write-Bad "$failed check(s) failed, $skipped skipped."; exit 1 }
# A distinct code so a caller can tell "it is broken" from "you did not check".
if ($skipped -gt 0) { Write-Bad "Incomplete: $skipped check(s) not run. This verifies nothing."; exit 2 }
Write-Good "All $passed checks passed."
