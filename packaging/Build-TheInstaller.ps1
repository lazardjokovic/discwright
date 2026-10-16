<#
.SYNOPSIS
    Builds the DiscWright 2.0 installer.

.DESCRIPTION
    Publishes the app as one self-contained file, then wraps it.

    The single file is not a preference. Smart App Control refuses a freshly
    built unsigned DLL by name, so an app published as an exe plus eight DLLs
    starts and then dies with 0xE0434352. Inside a bundle the assemblies load
    from memory and there is nothing for Code Integrity to check. The long
    version is at the top of DiscWright.iss.

.PARAMETER Version
    Overrides the version. The default is read from Directory.Build.props, so
    the installer cannot disagree with the app it carries.

.PARAMETER SkipPublish
    Wraps whatever is already in build\payload. For iterating on the script
    itself without waiting for a publish.

.EXAMPLE
    .\packaging\Build-TheInstaller.ps1
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $repo 'build\payload'
$out = Join-Path $repo 'build'

# The version comes from the app unless told otherwise, because an installer
# that names a different version from the app inside it is worse than one that
# names none: Add/Remove Programs and the window would disagree and only one of
# them can be checked at a glance.
if (-not $Version) {
    $props = Join-Path $repo 'Directory.Build.props'
    $found = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $found) { throw "No <Version> in $props, so there is nothing to name this." }
    $Version = $found.Matches[0].Groups[1].Value
}

Write-Host "DiscWright $Version" -ForegroundColor Cyan

# ISCC is wherever it was installed. winget's package puts it under the user's
# own Programs folder, not Program Files, so looking in only one place finds
# nothing on a machine that installed it the other way.
$builder = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $builder) {
    throw "Inno Setup is not installed. winget install --id JRSoftware.InnoSetup"
}

if (-not $SkipPublish) {
    Write-Host "publishing one file..." -ForegroundColor DarkGray
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }

    & dotnet publish (Join-Path $repo 'src\DiscWright.App\DiscWright.App.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version `
        -o $payload --nologo -v q

    if ($LASTEXITCODE -ne 0) { throw "The publish failed, so there is nothing to wrap." }

}

# Checked whatever the payload came from, and NOT inside the publish above.
#
# A guard that only runs when the payload was just built correctly is a guard
# against nothing: the case it exists for is -SkipPublish reusing a payload
# from an ordinary publish, which is an exe plus eight DLLs that Smart App
# Control refuses one by one. Found by planting exactly that and watching this
# script wrap it without complaint.
$exe = Join-Path $payload 'DiscWright.App.exe'
if (-not (Test-Path $exe)) {
    throw "No exe at $exe. Run without -SkipPublish."
}

$loose = Get-ChildItem $payload -Filter *.dll -ErrorAction SilentlyContinue
if ($loose) {
    throw ("There are {0} loose DLL(s) in the payload, so the bundle did not bundle and Smart App Control will refuse them one by one: {1}" -f
        $loose.Count, ($loose.Name -join ', '))
}

Write-Host ("  one file, {0:N1} MB" -f ((Get-Item $exe).Length / 1MB)) -ForegroundColor DarkGray

if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out -Force | Out-Null }

Write-Host "wrapping it..." -ForegroundColor DarkGray
& $builder "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'DiscWright.iss') | ForEach-Object {
    if ($_ -match 'error|warning' -or $_ -match 'Successful') { Write-Host "  $_" }
}

if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with $LASTEXITCODE." }

$setup = Join-Path $out "DiscWright-$Version-setup.exe"
if (-not (Test-Path $setup)) { throw "Inno Setup said it worked but there is no $setup." }

Write-Host ""
Write-Host ("{0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
Write-Host "Per-user, so it installs with no UAC prompt. It sits beside v1 and does not touch it."
