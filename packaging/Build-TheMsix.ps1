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
    The package identity name. The real one comes from Partner Center once the
    app name is reserved, and must match exactly or the submission is rejected.

.PARAMETER Publisher
    The certificate subject. For a development registration this is any valid
    X.500 name. For the Store it is assigned and cannot be chosen.

.PARAMETER PublisherDisplayName
    Must match the Publisher display name on the Partner Center account, or
    submission fails validation.
#>
[CmdletBinding()]
param(
    [string] $Identity = 'DiscWright.DiscWright',
    [string] $Publisher = 'CN=Lazar Djokovic',
    [string] $PublisherDisplayName = 'Lazar Djokovic',
    [string] $Version,
    [switch] $SkipPublish
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
# Drawn from the app's own .ico so the package cannot drift from the window.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $assets -Force | Out-Null

$ico = Join-Path $repo 'src\DiscWright.App\DiscWright.ico'
if (-not (Test-Path $ico)) { throw "No icon at $ico" }

$sizes = @{
    'Square44x44Logo.png'   = 44
    'Square150x150Logo.png' = 150
    'StoreLogo.png'         = 50
}

foreach ($name in $sizes.Keys) {
    $side = $sizes[$name]

    # Asked for at the size wanted, so Windows picks the closest frame in the
    # .ico rather than scaling the first one it finds.
    $icon = New-Object Drawing.Icon $ico, $side, $side
    $bmp = New-Object Drawing.Bitmap $side, $side
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawIcon($icon, (New-Object Drawing.Rectangle 0, 0, $side, $side))
    $bmp.Save((Join-Path $assets $name), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $icon.Dispose()
}

Write-Host "  logos: $($sizes.Keys -join ', ')" -ForegroundColor DarkGray

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
          DisplayName="DiscWright (packaged)"
          Description="Turn a folder of installers into a burnable game disc"
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

Write-Host ""
Write-Host "$into" -ForegroundColor Green
Get-ChildItem $into | ForEach-Object {
    if ($_.PSIsContainer) { "  {0}\" -f $_.Name }
    else { "  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
}
Write-Host ""
Write-Host "To register it for testing (needs Developer Mode on):"
Write-Host "  Add-AppxPackage -Register '$manifestPath'"
