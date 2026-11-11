<#
.SYNOPSIS
    Writes the three YAML files winget wants, filled in for one release.

.DESCRIPTION
    They are generated rather than kept in the repo as finished files because two
    of the values cannot be known until the release exists: the installer's URL
    and its SHA256. A manifest committed with a placeholder hash is one that fails
    `winget validate`, and a manifest nobody can validate is one nobody checks.

    Brought over from the PowerShell DiscWright for 2.0, when the winget package
    DiscWright.DiscWright moved to this app. The C# installer carries the
    PowerShell app's old AppId, so to winget 2.0.0 is the next version of the
    same package and `winget upgrade` moves people across. On the PowerShell
    side the script now refuses to run.

    Writing the files publishes nothing. Submitting them is a separate act, done
    by hand and only when the owner says so:

      1. After the release is published, take the installer's SHA256 from the
         release's SHA256SUMS.txt.
      2. .\packaging\winget\New-WingetManifest.ps1 -Version 2.0.0 -Sha256 <hash>
      3. winget validate --manifest .\build\winget\2.0.0
         winget install  --manifest .\build\winget\2.0.0   # really installs it
      4. Fork microsoft/winget-pkgs, copy the folder to
         manifests\d\DiscWright\DiscWright\<version>\ and open a PR.

    One submission at a time, never wired to publishing a release: an update to
    an existing package merges without a human, so a bad one installs itself on
    other people's machines.

.PARAMETER Version
    The released version, without the v.

.PARAMETER Sha256
    The SHA256 of DiscWright-<version>-setup.exe as attached to the release.

.PARAMETER OutDir
    Where the three files go. Defaults to build\winget\<version>.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Sha256,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $OutDir) {
    $OutDir = Join-Path $repo "build\winget\$Version"
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# DiscWright.DiscWright rather than a person's name, the way Git.Git, 7zip.7zip
# and calibre.calibre are filed: the publisher IS the project. The identifier
# cannot be renamed once winget-pkgs has accepted it, only deprecated and
# resubmitted, so it is the one field worth being sure about.
$id = 'DiscWright.DiscWright'

# ProductCode is how winget recognises an existing install for upgrade and
# uninstall, and Inno Setup registers its uninstall entry as "<AppId>_is1". Read
# from the installer script rather than typed here, so the two cannot drift: a
# ProductCode that disagrees with the AppId makes winget lose track of every
# copy it installed.
$iss = Get-Content -Raw -LiteralPath (Join-Path $repo 'packaging\DiscWright.iss')
$appId = [regex]::Match($iss, '(?m)^AppId=\{(\{[0-9A-Fa-f-]+\})\s*$').Groups[1].Value
if (-not $appId) { throw "No AppId found in packaging\DiscWright.iss." }

# The release workflow attaches the installer under exactly this name.
$url = "https://github.com/lazardjokovic/discwright/releases/download/v$Version/DiscWright-$Version-setup.exe"

# winget wants the hash upper-case and unbracketed.
$sha = $Sha256.Trim().ToUpper() -replace '[^0-9A-F]', ''
if ($sha.Length -ne 64) { throw "Sha256 does not look like a SHA256: '$Sha256'" }

# Schema 1.28.0 in all three manifests and in all three $schema lines with it.
# A moderator asked for it on microsoft/winget-pkgs#433767. The number appears
# six times in this file and a half done bump is the easy mistake, so the tests
# hold all six to each other.

# ---------------------------------------------------------------- version --
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.28.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.28.0
"@ | Set-Content -LiteralPath (Join-Path $OutDir "$id.yaml") -Encoding UTF8

# -------------------------------------------------------------- installer --
#
# Scope: user matches PrivilegesRequired=lowest in the .iss, which installs under
# %LOCALAPPDATA%\Programs with no UAC prompt. Architecture: x64 matches
# ArchitecturesAllowed=x64compatible: the app is published win-x64 and
# self-contained, so there is nothing to run on anything else.
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.28.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerLocale: en-US
MinimumOSVersion: 10.0.0.0
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '${appId}_is1'
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.28.0
"@ | Set-Content -LiteralPath (Join-Path $OutDir "$id.installer.yaml") -Encoding UTF8

# ----------------------------------------------------------------- locale --
#
# ShortDescription is the line `winget search` shows, so it says what the tool
# does rather than what it is built with. In quotes, and it has to be: an
# unquoted YAML value holding a colon and a space is a mapping rather than a
# string, which is how every manifest the PowerShell app wrote after 0.9.0
# failed to parse for six weeks without anybody noticing.
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.28.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: DiscWright
PublisherUrl: https://discwright.com
PublisherSupportUrl: https://github.com/lazardjokovic/discwright/issues
PackageName: DiscWright
PackageUrl: https://discwright.com
License: MIT
LicenseUrl: https://github.com/lazardjokovic/discwright/blob/main/LICENSE
Copyright: Copyright (c) 2026 lazardjokovic
ShortDescription: "Turn a GOG offline installer into a real game disc: the game's own icon and name in This PC, a menu when it goes in, sets of discs for big games, and every disc read back after burning."
Description: |-
  DiscWright turns a GOG offline installer, or any folder of game files, into a
  disc that behaves like one from a box: the drive carries the game's own icon
  and name in This PC, and a menu opens when it goes in, with Install, Manual,
  Extras and, once the game is installed, Play.

  A game too big for one blank becomes a numbered set. Put any disc of it in
  and the menu copies the discs back, asks for the next one, puts the game back
  together and starts its installer. GOG patches are read against the
  installer, so one it already covers stays off the disc.

  It burns the disc too, below the drive's top speed, and reads every file back
  afterwards to compare it with what was built. Every disc carries a checksum
  list that sha256sum can check years later without DiscWright, and a cover and
  a disc face you already have go onto it exactly as they are.
Moniker: discwright
Tags:
- gog
- iso
- disc
- burn
- games
- autorun
- retro
- backup
ReleaseNotesUrl: https://github.com/lazardjokovic/discwright/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.28.0
"@ | Set-Content -LiteralPath (Join-Path $OutDir "$id.locale.en-US.yaml") -Encoding UTF8

Write-Host "wrote three manifests to $OutDir"
Get-ChildItem $OutDir | Select-Object -ExpandProperty Name
