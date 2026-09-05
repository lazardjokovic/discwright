<#
  New-WingetManifest.ps1

  Writes the three YAML files winget wants, filled in for one release.

  They are generated rather than kept in the repo as finished files because two
  of the values cannot be known until the release exists: the installer's URL and
  its SHA256. Committing a manifest with a placeholder hash would mean committing
  one that fails `winget validate`, and a manifest nobody can validate is one
  nobody checks.

  Usage, after the release is published:

      .\New-WingetManifest.ps1 -Version 0.5.1 -Sha256 <hash of the setup .exe>

  Then, to check it before it goes anywhere near Microsoft's repo:

      winget validate --manifest .\build\winget\0.5.1
      winget install --manifest .\build\winget\0.5.1   # really installs it

  Submitting is a separate, manual act: fork microsoft/winget-pkgs, copy the
  folder to manifests\l\LazarDjokovic\DiscWright\<version>\ and open a PR. That
  is deliberately not automated here - it publishes to somebody else's repo.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,

    # SHA256 of the DiscWright-<version>-setup.exe that is actually attached to
    # the release. packaging/Build-Release.ps1 -Installer prints it, and so does
    # the release workflow.
    [Parameter(Mandatory)][string]$Sha256,

    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

if (-not $OutDir) {
    $OutDir = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')) "build\winget\$Version"
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$id  = 'LazarDjokovic.DiscWright'
$url = "https://github.com/lazardjokovic/discwright/releases/download/v$Version/DiscWright-$Version-setup.exe"

# winget wants the hash upper-case and unbracketed.
$sha = $Sha256.Trim().ToUpper() -replace '[^0-9A-F]',''
if ($sha.Length -ne 64) { throw "Sha256 does not look like a SHA256: '$Sha256'" }

# ---------------------------------------------------------------- version --
@"
# Created for DiscWright $Version - see packaging/winget/New-WingetManifest.ps1
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@ | Set-Content -LiteralPath (Join-Path $OutDir "$id.yaml") -Encoding UTF8

# -------------------------------------------------------------- installer --
#
# InstallerType is inno, not zip. A zip would have to declare
# NestedInstallerType: portable, and that refuses every extension except .exe -
# .cmd, .vbs, .ps1 and .bat are all rejected by `winget validate` itself. The
# app is scripts, so there is no .exe to point at, and an installer is the only
# route onto winget that does not mean rewriting the app as a binary.
#
# Scope: user matches PrivilegesRequired=lowest in the .iss - it installs under
# %LOCALAPPDATA%\Programs with no UAC prompt.
#
# ProductCode is how winget recognises an existing install for upgrade and
# uninstall. Inno Setup registers its uninstall entry as "<AppId>_is1", so this
# has to stay in step with AppId in packaging/DiscWright.iss.
@"
# Created for DiscWright $Version - see packaging/winget/New-WingetManifest.ps1
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
ProductCode: '{20F71A81-9D66-445E-BDC3-394B940F190E}_is1'
Installers:
- Architecture: neutral
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.6.0
"@ | Set-Content -LiteralPath (Join-Path $OutDir "$id.installer.yaml") -Encoding UTF8

# ----------------------------------------------------------------- locale --
#
# ShortDescription is the line the store front and `winget search` show, so it
# says what the tool does rather than what it is built with.
@"
# Created for DiscWright $Version - see packaging/winget/New-WingetManifest.ps1
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Lazar Djokovic
PublisherUrl: https://github.com/lazardjokovic
PublisherSupportUrl: https://github.com/lazardjokovic/discwright/issues
PackageName: DiscWright
PackageUrl: https://discwright.com
License: MIT
LicenseUrl: https://github.com/lazardjokovic/discwright/blob/main/LICENSE
Copyright: Copyright (c) 2026 lazardjokovic
ShortDescription: Turn a GOG offline installer into a real game disc, with the game's own icon and title in This PC and a menu when you double-click it.
Description: |-
  DiscWright turns a folder of GOG offline installers into a burnable ISO that
  behaves like a game disc from a box: the drive carries the game's own icon and
  label in This PC, and double-clicking it opens an autorun menu with Play,
  Install, Manual and Extras.

  It builds the ISO with the disc imaging support already in Windows, so there
  is nothing bundled and nothing to trust. More than one game fits on a disc,
  add-ons live under the game they belong to, and a set too big for one disc can
  be split across several.

  Burning is out of scope on purpose - DiscWright writes the ISO and leaves
  burning to the tools that already do it well.
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
ManifestVersion: 1.6.0
"@ | Set-Content -LiteralPath (Join-Path $OutDir "$id.locale.en-US.yaml") -Encoding UTF8

Write-Host "wrote three manifests to $OutDir"
Get-ChildItem $OutDir | Select-Object -ExpandProperty Name
