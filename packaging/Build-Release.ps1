<#
  Build-Release.ps1

  Assembles the files a user actually needs into a folder, then zips it.

  This exists because "Code -> Download ZIP" is not a release artifact. That zip
  carries tests/, docs/research/ (throwaway harnesses kept as evidence, several
  megabytes of them) and a versioned root folder, and its URL changes shape
  between a tag and a branch. winget needs one stable URL whose SHA256 never
  moves, so the release gets a zip built on purpose instead.

  Output lands in build/ , which .gitignore covers.
#>

[CmdletBinding()]
param(
    # Where to put the staging folder and the zip. Left empty here on purpose:
    # under Windows PowerShell 5.1 $PSScriptRoot is not populated yet while
    # parameter defaults are being bound, so a default built from it lands as
    # an empty string. Resolved in the body instead, where it is set.
    [string]$OutDir,

    # Also compile the Inno Setup installer. Off by default because ISCC.exe is
    # not on most machines and the zip is the artifact that matters most; CI
    # passes this, where Inno Setup ships with the runner image.
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $OutDir) { $OutDir = Join-Path $repo 'build' }

# The version is read out of the app rather than passed in. A number typed on
# the command line is a number that can disagree with the one the app reports,
# and CI already fails a tag whose $APP_VERSION does not match it - so the app
# is the single place this is written down.
$appPs1 = Join-Path $repo 'DiscWright.ps1'
$m = [regex]::Match((Get-Content $appPs1 -Raw), '\$APP_VERSION\s*=\s*''([^'']+)''')
if (-not $m.Success) { throw "No `$APP_VERSION assignment found in $appPs1" }
$version = $m.Groups[1].Value

# What ships. Deliberately not "everything that is not a test": extras/ and
# tools/ are standalone helper scripts that the README never mentions and the
# app never loads, and shipping them invites questions nothing answers.
$payload = @(
    'DiscWright.ps1'
    'DiscWright.ico'
    'DiscWright.vbs'
    'Run DiscWright.cmd'
    'README.md'
    'LICENSE'
)

$stage = Join-Path $OutDir "DiscWright-$version"
$zip   = Join-Path $OutDir "DiscWright-$version.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip)   { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

foreach ($f in $payload) {
    $src = Join-Path $repo $f
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing payload file: $f" }
    Copy-Item -LiteralPath $src -Destination (Join-Path $stage $f) -Force
}

# Compress-Archive is in PS 5.1, so this needs nothing installed. The wildcard
# on the source is what keeps the zip flat-ish: the staging folder's own name
# becomes the single root folder inside, which is what people expect to extract.
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$sha = (Get-FileHash -Path $zip -Algorithm SHA256).Hash

$setup    = $null
$setupSha = $null

if ($Installer) {
    # Inno Setup does not put ISCC.exe on PATH, so look where it installs before
    # giving up on it.
    $iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty Source
    if (-not $iscc) {
        $iscc = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }
    if (-not $iscc) {
        throw ("Inno Setup's ISCC.exe was not found. Install Inno Setup 6, or " +
               "drop -Installer to build the zip alone.")
    }

    $iss = Join-Path $PSScriptRoot 'DiscWright.iss'
    # The version is passed in rather than read again inside the .iss, so both
    # artifacts in one run can only ever carry the same number.
    & $iscc "/DAppVersion=$version" "/O$OutDir" $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed with exit code $LASTEXITCODE" }

    $setup = Join-Path $OutDir "DiscWright-$version-setup.exe"
    if (-not (Test-Path $setup)) { throw "ISCC.exe reported success but $setup is missing" }
    $setupSha = (Get-FileHash -Path $setup -Algorithm SHA256).Hash
}

Write-Host "version   $version"
Write-Host "staged    $stage"
Write-Host "zip       $zip"
Write-Host "sha256    $sha"
if ($setup) {
    Write-Host "setup     $setup"
    Write-Host "setupsha  $setupSha"
}

# Emitted as an object too, so CI can consume it without parsing the text above.
[pscustomobject]@{
    Version   = $version
    Stage     = $stage
    Zip       = $zip
    Sha256    = $sha
    Setup     = $setup
    SetupSha  = $setupSha
}
