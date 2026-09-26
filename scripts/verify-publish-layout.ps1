<#
.SYNOPSIS
  Publishes the app the way a release does and asserts the output root holds one file (#962).

.DESCRIPTION
  The release bundle is meant to unpack to a single visible file, so somebody who downloads the zip can
  tell what to run (#586). `release.yml` asserts that -- but `release.yml` only runs on a `v*` tag, so
  the assertion catches nothing until a release is already being cut.

  That is not hypothetical. OtdInterop landed in #807 with `GenerateDocumentationFile` set, deliberately:
  its ownership and session-lifetime contracts live in doc comments. A self-contained single-file publish
  copies a referenced project's documentation file into the output root, so from that merge onwards the
  root held `OtdInterop.xml` beside the exe. Nothing noticed for weeks. It surfaced when v0.77.0 was
  tagged -- after the notes were written, the version bumped and the tag pushed -- and the release had to
  be abandoned, fixed, and the tag re-pointed.

  This script is the same assertion, runnable without publishing a release. `build.yml` calls it on the
  Windows lane so the failure lands on a pull request instead.

  DELIBERATELY NARROW. It checks the root and nothing else. The release job additionally checks the
  bundled daemon, the two plugins, the VMulti archive and its digest, and the Windows Ink version match
  -- all of which need downloads this script does not do. The root is where the regression actually
  happened, and a narrow check is one that stays honest.

  NOT THE RELEASE GATE. `release.yml` keeps its own copy of the root check, and that one is still
  authoritative. Two copies can drift, which is a real cost; the reason for paying it is that unifying
  them means editing `release.yml`, and nothing validates a change to that file except cutting a tag --
  which publishes a real release to do it. If the two are ever unified, validate it with a throwaway
  tag and delete the release it creates.

.PARAMETER Root
  Check an already-published tree instead of publishing one. The folder holding OpenTabletArtist.exe.

.PARAMETER OutputPath
  Where to publish. Defaults to a new temporary directory, removed afterwards.

.PARAMETER BaseOutputPath
  Redirect intermediate build output. Useful locally: a running OpenTabletArtist locks bin\Debug.

.EXAMPLE
  .\verify-publish-layout.ps1

.EXAMPLE
  .\verify-publish-layout.ps1 -Root publish\OpenTabletArtist
#>
[CmdletBinding()]
param(
    [string]$Root,
    [string]$OutputPath,
    [string]$BaseOutputPath
)

$ErrorActionPreference = 'Stop'

# Reported as an Actions annotation when there is one, and as plain text when run by hand.
function Write-Failure($msg) {
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::error::$msg" } else { Write-Host $msg -ForegroundColor Red }
}

$repo = Split-Path -Parent $PSScriptRoot
$temporary = $null

try {
    if (-not $Root) {
        if (-not $OutputPath) {
            $OutputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ota-layout-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
            $temporary = $OutputPath
        }

        # These flags must match the publish in release.yml. A check that publishes differently from the
        # release is checking a bundle nobody ships.
        $publishArgs = @(
            'publish', (Join-Path $repo 'OpenTabletArtist/OpenTabletArtist.csproj'),
            '-c', 'Release',
            '-r', 'win-x64',
            '--self-contained',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true',
            '-o', $OutputPath
        )
        if ($BaseOutputPath) { $publishArgs += "-p:BaseOutputPath=$BaseOutputPath" }

        Write-Host "==> Publishing to $OutputPath"
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

        $Root = $OutputPath
    }

    if (-not (Test-Path $Root)) { throw "No such directory: $Root" }

    # release.yml strips debug symbols before checking, because the native SkiaSharp and HarfBuzz .pdb
    # files ship inside their NuGet packages and -p:DebugType=none does not touch them. Discount them
    # here rather than deleting: this may be pointed at a tree somebody else owns.
    $loose = Get-ChildItem $Root -File |
        Where-Object { $_.Extension -ne '.pdb' -and $_.Name -ne 'OpenTabletArtist.exe' }

    # Asserted, not assumed. Without this the check passes against an empty directory, a publish that
    # silently produced nothing, or a -Root typo -- and "the root holds only the exe" would be true of a
    # root holding no exe at all.
    $exe = Join-Path $Root 'OpenTabletArtist.exe'
    if (-not (Test-Path $exe)) {
        Write-Failure "No OpenTabletArtist.exe in $Root, so there is no single-file root to check."
        exit 1
    }

    if ($loose) {
        Write-Failure ("The publish root should hold only OpenTabletArtist.exe, but also has: " +
                       ($loose.Name -join ', '))
        Write-Host ""
        Write-Host "A referenced project's XML documentation is the usual cause: a self-contained"
        Write-Host "single-file publish copies it to the root. Set"
        Write-Host "PublishReferencesDocumentationFiles=false on the app, rather than turning the"
        Write-Host "documentation off in the library that wants it."
        exit 1
    }

    Write-Host "Publish root verified: OpenTabletArtist.exe and nothing else." -ForegroundColor Green

    # Explicit, because an Actions pwsh step exits with whatever $LASTEXITCODE holds at the end. Falling
    # off the end of a successful run leaves it set by whatever ran last -- or unset -- and the step's
    # result then depends on something this script did not decide.
    exit 0
}
finally {
    if ($temporary -and (Test-Path $temporary)) {
        Remove-Item $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
}
