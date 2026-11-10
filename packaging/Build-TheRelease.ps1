<#
.SYNOPSIS
    Builds everything a GitHub release carries, after checking the tag.

.DESCRIPTION
    Run by .github\workflows\release.yml when a v* tag is pushed, and by hand
    to see what a release would hold. Three files come out in build\:

        DiscWright-<version>-setup.exe     the installer
        DiscWright-<version>-portable.exe  the same app as one file, installed nowhere
        SHA256SUMS.txt                     both of them, in the format sha256sum reads

    Before anything is built it refuses three things, because each one is a
    release nobody can take back cleanly once it is public:

      * a tag that is not vX.Y.Z. A pre-release tag such as v2.0.0-beta.1 is
        read by the app's own update check as 2.0.0, so whoever ran it would be
        told they were up to date when the real 2.0.0 came out.
      * a tag that disagrees with <Version> in Directory.Build.props, which is
        what the app says it is. A release called 2.0.1 holding an app that
        says 2.0.0 tells everybody who runs it that 2.0.1 is newer.
      * no release notes, packaging\release-notes\<version>.md. A release with
        a list of pull request titles from the private repository it was made
        in, which is what GitHub would write instead, links to nothing.

.PARAMETER Tag
    The tag being released, v and all.

.PARAMETER CheckOnly
    Checks the tag and the notes, and builds nothing.

.PARAMETER WithoutNotes
    For a dry run of the workflow, which has a version but no notes yet.

.PARAMETER NotesFolder
    Where the notes are. Only the tests change it.

.EXAMPLE
    .\packaging\Build-TheRelease.ps1 -Tag v2.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Tag,
    [switch] $CheckOnly,
    [switch] $WithoutNotes,
    [string] $NotesFolder
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $NotesFolder) { $NotesFolder = Join-Path $PSScriptRoot 'release-notes' }

function Refuse([string] $why) {
    Write-Host "REFUSED: $why"
    exit 1
}

if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') {
    Refuse "The tag '$Tag' is not vX.Y.Z. The app's update check reads anything after a dash as nothing, so a pre-release would hide the real release from whoever ran it."
}

$version = $Matches[1]

$props = Join-Path $repo 'Directory.Build.props'
$found = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $found) { Refuse "There is no <Version> in $props." }

$says = $found.Matches[0].Groups[1].Value
if ($says -ne $version) {
    Refuse "The tag says $version and Directory.Build.props says $says, which is what the app will call itself. Change one so they agree."
}

$notes = Join-Path $NotesFolder "$version.md"
if (-not $WithoutNotes) {
    if (-not (Test-Path $notes)) {
        Refuse "There are no release notes at $notes."
    }

    if (-not (Get-Content $notes -Raw).Trim()) {
        Refuse "The release notes at $notes are empty."
    }
}

Write-Host "DiscWright $version, the tag and the app agree."

if ($CheckOnly) { exit 0 }

& (Join-Path $PSScriptRoot 'Build-TheInstaller.ps1') -Version $version
if (-not $?) { exit 1 }

$out = Join-Path $repo 'build'
$setup = Join-Path $out "DiscWright-$version-setup.exe"
$portable = Join-Path $out "DiscWright-$version-portable.exe"

# The very file the installer wrapped, so the two downloads cannot be two
# different builds of the same version.
Copy-Item (Join-Path $out 'payload\DiscWright.App.exe') $portable -Force

$sums = foreach ($file in $setup, $portable) {
    '{0}  {1}' -f (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
}

# LF and no BOM, because sha256sum -c reads a BOM as part of the first hash.
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))

foreach ($file in $setup, $portable, (Join-Path $out 'SHA256SUMS.txt')) {
    Write-Host ("{0}  ({1:N1} MB)" -f $file, ((Get-Item $file).Length / 1MB))
}
