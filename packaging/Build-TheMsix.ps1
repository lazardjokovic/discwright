<#
.SYNOPSIS
    Lays out DiscWright as an MSIX package.

.DESCRIPTION
    Produces a loose package folder: the app, its logos and an AppxManifest.
    That folder can be registered as-is for testing, with

        Add-AppxPackage -Register .\build\msix\AppxManifest.xml

    which needs Developer Mode on but no certificate and no Store submission.
    Turning it into an .msix file needs MakeAppx from the Windows SDK, which
    is a separate step and only matters when there is something to submit.

    Why the payload is still a single file, even though MSIX exists partly to
    make loose DLLs respectable: a package registered for development is not
    signed by anybody. Smart App Control judges the DLLs inside it exactly as
    it judges the ones beside an exe, so a loose layout here would be refused
    for reasons that have nothing to do with what is being tested. The Store
    build can go back to an ordinary layout, because Microsoft signs that.

.PARAMETER Identity
    The package identity name. Partner Center assigned it when the name
    DiscWright was reserved, on 2026-11-12, and a submission that differs from
    it by a character is rejected.

.PARAMETER Publisher
    The publisher, as Partner Center assigned it to the account. Not a name
    anybody chose: the Store signs the package with a certificate whose
    subject is exactly this.

.PARAMETER PublisherDisplayName
    The publisher name the account shows, which the Store checks against the
    package.
#>
[CmdletBinding()]
param(
    [string] $Identity = 'LazarDjokovic.DiscWright',
    [string] $Publisher = 'CN=E9DAEFBA-A2F5-4E0B-8F70-D637310F2D3F',
    [string] $PublisherDisplayName = 'Lazar Djokovic',
    [string] $Version,
    [switch] $SkipPublish,
    [switch] $Pack
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$into = Join-Path $repo 'build\msix'
$assets = Join-Path $into 'Assets'

if (-not $Version) {
    $props = Join-Path $repo 'Directory.Build.props'
    $found = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $found) { throw "No <Version> in $props." }
    $Version = $found.Matches[0].Groups[1].Value
}

# A package version is always four parts and its last part must be zero.
$parts = $Version.Split('.')
if ($parts.Count -lt 3) { throw "The version '$Version' is not x.y.z." }
$packageVersion = "{0}.{1}.{2}.0" -f $parts[0], $parts[1], $parts[2]

Write-Host "DiscWright $Version  ->  package $packageVersion" -ForegroundColor Cyan

if (-not $SkipPublish) {
    if (Test-Path $into) { Remove-Item $into -Recurse -Force }
    New-Item -ItemType Directory -Path $into -Force | Out-Null

    Write-Host "publishing..." -ForegroundColor DarkGray
    & dotnet publish (Join-Path $repo 'src\DiscWright.App\DiscWright.App.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version `
        -o $into --nologo -v q

    if ($LASTEXITCODE -ne 0) { throw 'The publish failed.' }
}

$exe = Join-Path $into 'DiscWright.App.exe'
if (-not (Test-Path $exe)) { throw "No exe at $exe" }

# The symbols are not part of a package. They would ship a map of the app to
# anybody who unzipped it, for no benefit to the person running it.
Get-ChildItem $into -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$loose = Get-ChildItem $into -Filter *.dll -ErrorAction SilentlyContinue
if ($loose) {
    throw ("There are {0} loose DLL(s) in the package. An unsigned development registration gets no more trust than a loose folder, so Smart App Control will refuse them: {1}" -f
        $loose.Count, ($loose.Name -join ', '))
}

# ---------------------------------------------------------------- the logos
#
# Rendered from the vector icon, packaging\msix\discwright.svg, at every size
# Windows and the Store ask for, by packaging\msix\Render-TheLogos.py, and
# committed. They used to be drawn here from the .ico, which has seven fixed
# frames, so every other size was a frame scaled by GDI+.
$drawn = Join-Path $PSScriptRoot 'msix\Assets'
if (-not (Test-Path (Join-Path $drawn 'StoreLogo.scale-100.png'))) {
    throw "No logos in $drawn. Run packaging\msix\Render-TheLogos.py."
}

New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item (Join-Path $drawn '*.png') $assets
Write-Host ("  logos: {0}" -f (Get-ChildItem $assets -Filter *.png).Count) -ForegroundColor DarkGray

# ------------------------------------------------------------- the manifest
#
# EntryPoint="Windows.FullTrustApplication" is what makes this a packaged
# desktop app rather than a UWP one. It runs at medium integrity and NOT in an
# AppContainer, which is the whole reason IMAPI2 has a chance of working: the
# capability-gated device access that would stop it is an AppContainer
# mechanism, and this is outside it.
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
    xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
    xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
    xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
    IgnorableNamespaces="uap rescap">

  <Identity
      Name="$Identity"
      Publisher="$Publisher"
      Version="$packageVersion"
      ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>DiscWright</DisplayName>
    <PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application
        Id="DiscWright"
        Executable="DiscWright.App.exe"
        EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
          DisplayName="DiscWright"
          Description="Turns a game's offline installer into a disc that works like one from a box"
          BackgroundColor="transparent"
          Square150x150Logo="Assets\Square150x150Logo.png"
          Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

$manifestPath = Join-Path $into 'AppxManifest.xml'
[IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding $false))

# ------------------------------------------------------------ resources.pri
#
# The index Windows reads to pick a logo for a scale and a size. Without it a
# logo named StoreLogo.scale-200.png is a file nothing asks for, and Windows
# falls back to drawing a blank. MakePri comes with the Windows SDK, as
# MakeAppx does, and is found the same way.
function Find-SdkTool([string] $name) {
    # Only folders that start with a digit are versions: see -Pack below.
    Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin', 'C:\Program Files\Windows Kits\10\bin' `
        -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d' } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
}

$pri = Find-SdkTool 'makepri.exe'
if (-not $pri) {
    throw 'MakePri is not installed. It comes with the Windows SDK: winget install --id Microsoft.WindowsSDK.10.0.26100'
}

# The configuration is kept out of the package folder, or it would be packed.
$priWork = Join-Path $repo 'build\msix-pri'
New-Item -ItemType Directory -Path $priWork -Force | Out-Null
$priConfig = Join-Path $priWork 'priconfig.xml'

& $pri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MakePri createconfig failed with $LASTEXITCODE." }

# One index for one package. The configuration MakePri writes splits it into
# a file per scale, which is how a bundle carries a package per scale, and
# this is a single package with every scale in it: left in, the first build
# put resources.scale-125.pri and four more beside resources.pri.
[xml] $config = Get-Content $priConfig -Raw
foreach ($split in @($config.resources.packaging)) {
    if ($split) { [void] $config.resources.RemoveChild($split) }
}
$config.Save($priConfig)

& $pri new /pr $into /cf $priConfig /mn $manifestPath /of (Join-Path $into 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MakePri new failed with $LASTEXITCODE." }
Write-Host "  resources.pri" -ForegroundColor DarkGray

Write-Host ""
Write-Host "$into" -ForegroundColor Green
Get-ChildItem $into | ForEach-Object {
    if ($_.PSIsContainer) { "  {0}\" -f $_.Name }
    else { "  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
}
if ($Pack) {
    # MakeAppx lives under a VERSION folder, beside which sit x86, x64 and
    # arm64 folders of the same depth. Sorting the lot by name and taking the
    # top is how this was got wrong once: 'x86' sorts above '10.0.26100.0', so
    # the search looked in x86\x64 and concluded the SDK was not installed at
    # all. Only folders that start with a digit are versions.
    $maker = Find-SdkTool 'makeappx.exe'

    if (-not $maker) {
        throw 'MakeAppx is not installed. It comes with the Windows SDK: winget install --id Microsoft.WindowsSDK.10.0.26100'
    }

    $msix = Join-Path (Join-Path $repo 'build') "DiscWright-$Version.msix"
    Remove-Item $msix -Force -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "packing with $maker" -ForegroundColor DarkGray

    # /o overwrites, /p is what comes out, /d is the folder going in.
    & $maker pack /o /p $msix /d $into | ForEach-Object {
        if ($_ -match 'error|warning|Package creation succeeded') { Write-Host "  $_" }
    }

    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with $LASTEXITCODE." }
    if (-not (Test-Path $msix)) { throw "MakeAppx said it worked and there is no $msix." }

    Write-Host ""
    Write-Host ("{0}  ({1:N1} MB)" -f $msix, ((Get-Item $msix).Length / 1MB)) -ForegroundColor Green
    Write-Host "Unsigned on purpose. The Store signs what it accepts, and a local install needs a"
    Write-Host "certificate of your own trusted on this machine."
}
else {
    Write-Host ""
    Write-Host "To register it for testing (needs Developer Mode on):"
    Write-Host "  Add-AppxPackage -Register '$manifestPath'"
    Write-Host ""
    Write-Host "Or pass -Pack to make an .msix out of it."
}
