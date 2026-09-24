#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the downloadable Windows version and wraps it in one BlackjackSetup.exe.

.DESCRIPTION
    The published app cannot be a single file. It is roughly 700 files because
    it carries its own .NET runtime and its own Windows App SDK, which is what
    lets it run on a machine with nothing installed, and MAUI on Windows does
    not support PublishSingleFile - the Windows App SDK needs its files loose
    on disk.

    So the app stays a folder and the INSTALLER becomes the single file. That
    is the thing to hand people: one exe, which puts the folder somewhere
    sensible, makes a Start menu shortcut, and registers an uninstaller.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repo      = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repo 'BlackjackApp.Maui\BlackjackApp.Maui.csproj'
$framework = 'net10.0-windows10.0.19041.0'

Write-Host 'Publishing the app...' -ForegroundColor Cyan
dotnet publish $project -f $framework -c $Configuration -p:Distributable=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$publishDir = Join-Path $repo "BlackjackApp.Maui\bin\$Configuration\$framework\win-x64\publish"
if (-not (Test-Path (Join-Path $publishDir 'BlackjackApp.Maui.exe'))) {
    throw "Published, but found no BlackjackApp.Maui.exe under $publishDir."
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ''
    Write-Warning @"
Inno Setup 6 is not installed, so the single-file installer cannot be built.
Install it and run this again:

    winget install JRSoftware.InnoSetup

The published app itself is finished and usable right now - zip this folder
and that is a working download, just not a one-file one:

    $publishDir
"@
    return
}

$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host 'Building the installer...' -ForegroundColor Cyan
& $iscc "/DPublishDir=$publishDir" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'BlackjackSetup.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$setup = Join-Path $dist 'BlackjackSetup.exe'
$mb    = [math]::Round((Get-Item $setup).Length / 1MB, 1)

Write-Host ''
Write-Host "Installer ready: $setup  ($mb MB)" -ForegroundColor Green
Write-Host 'That one file is what you give people.'
