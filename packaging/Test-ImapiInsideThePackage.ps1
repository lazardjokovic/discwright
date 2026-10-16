<#
.SYNOPSIS
    Asks whether IMAPI2 can see the disc writers from inside the MSIX package.

.DESCRIPTION
    The one question that decides whether DiscWright can go to the Microsoft
    Store at all. Burning is the app, and if a packaged DiscWright cannot see a
    recorder then the Store route is dead however good the listing looks.

    The documented position is encouraging but indirect: a packaged desktop app
    declaring EntryPoint="Windows.FullTrustApplication" runs at medium
    integrity and NOT in an AppContainer, and the capability-gated device
    access that would block this is an AppContainer mechanism. That is a reason
    to expect it to work, not evidence that it does.

    So this runs the real COM calls under the real package identity, and it
    REFUSES TO ANSWER unless it can prove it is running inside the package.
    A probe that quietly ran unpackaged would report success for the wrong
    reason, which is the worst outcome available here: a green light that sends
    somebody off to build a Store submission on a false premise.

.PARAMETER PackageFamilyName
    Defaults to whatever DiscWright package is registered.
#>
[CmdletBinding()]
param(
    [string] $PackageFamilyName,
    [string] $AppId = 'DiscWright'
)

$ErrorActionPreference = 'Stop'

if (-not $PackageFamilyName) {
    $package = Get-AppxPackage -Name 'DiscWright*' | Select-Object -First 1
    if (-not $package) {
        throw 'No DiscWright package is registered. Run packaging\Build-TheMsix.ps1 and register it first.'
    }
    $PackageFamilyName = $package.PackageFamilyName
}

$answer = Join-Path $env:TEMP 'discwright-imapi-inside.json'
Remove-Item $answer -Force -ErrorAction SilentlyContinue

# What runs inside the package. It writes its answer to a file, because
# Invoke-CommandInDesktopPackage launches a process and does not hand back its
# output.
$inside = @'
$result = [ordered]@{}

# ---- first, prove where this is running -----------------------------------
#
# GetCurrentPackageFullName returns APPMODEL_ERROR_NO_PACKAGE (15700) for a
# process with no package identity. Without this check a probe that slipped
# outside the package would answer the wrong question and look like good news.
Add-Type -Namespace Probe -Name Identity -MemberDefinition @"
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
public static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);
"@

$length = 0
$code = [Probe.Identity]::GetCurrentPackageFullName([ref] $length, $null)

if ($code -eq 15700) {
    $result.packaged = $false
    $result.package = $null
} else {
    $builder = New-Object Text.StringBuilder $length
    [void] [Probe.Identity]::GetCurrentPackageFullName([ref] $length, $builder)
    $result.packaged = $true
    $result.package = $builder.ToString()
}

$result.integrity = (& whoami /groups) -match 'Mandatory Label' | Select-Object -First 1

# ---- then, and only then, the actual question -----------------------------
$recorders = @()
try {
    $master = New-Object -ComObject IMAPI2.MsftDiscMaster2
    $result.imapiCreated = $true
    $result.isSupportedEnvironment = [bool] $master.IsSupportedEnvironment
    $result.count = $master.Count

    foreach ($id in $master) {
        $one = [ordered]@{ id = $id }
        try {
            $recorder = New-Object -ComObject IMAPI2.MsftDiscRecorder2
            $recorder.InitializeDiscRecorder($id)
            $one.drive = ($recorder.VolumePathNames -join ',')
            $one.vendor = $recorder.VendorId.Trim()
            $one.product = $recorder.ProductId.Trim()
            $one.canWrite = [bool] $recorder.SupportedProfiles

            # Opening the drive for a real operation is the step that device
            # access would actually stop, so it is worth doing rather than
            # trusting that enumeration implies it.
            $format = New-Object -ComObject IMAPI2.MsftDiscFormat2Data
            try {
                $format.Recorder = $recorder
                $one.openedForWriting = $true
                $one.mediaBlank = [bool] $format.MediaHeuristicallyBlank
                $one.mediaType = [int] $format.CurrentPhysicalMediaType
            } catch {
                $one.openedForWriting = $false
                $one.openError = $_.Exception.Message
            }
        } catch {
            $one.error = $_.Exception.Message
        }
        $recorders += $one
    }
} catch {
    $result.imapiCreated = $false
    $result.imapiError = $_.Exception.Message
}

$result.recorders = $recorders
$result | ConvertTo-Json -Depth 5 | Set-Content -Path 'ANSWERPATH' -Encoding utf8
'@

$inside = $inside.Replace('ANSWERPATH', $answer)

$script = Join-Path $env:TEMP 'discwright-imapi-probe.ps1'
Set-Content -Path $script -Value $inside -Encoding utf8

Write-Host "running IMAPI2 inside $PackageFamilyName ..." -ForegroundColor Cyan

Invoke-CommandInDesktopPackage `
    -PackageFamilyName $PackageFamilyName `
    -AppId $AppId `
    -Command 'powershell.exe' `
    -Args "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""

# It is launched, not waited on, so wait for the answer to appear.
$waited = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path $answer) -and $waited.Elapsed.TotalSeconds -lt 90) {
    Start-Sleep -Milliseconds 400
}

if (-not (Test-Path $answer)) {
    throw 'The probe inside the package never wrote an answer. Check Event Viewer under Microsoft-Windows-AppModel-Runtime.'
}

$found = Get-Content $answer -Raw | ConvertFrom-Json

Write-Host ""
if (-not $found.packaged) {
    Write-Host 'INCONCLUSIVE. The probe ran with no package identity, so it answered the wrong question.' -ForegroundColor Red
    Write-Host 'Nothing about this tells us whether a packaged DiscWright can burn.'
    exit 2
}

Write-Host "inside: $($found.package)" -ForegroundColor Green
Write-Host "IMAPI2 object created: $($found.imapiCreated)"
if (-not $found.imapiCreated) {
    Write-Host "  error: $($found.imapiError)" -ForegroundColor Red
    exit 1
}

Write-Host "supported environment: $($found.isSupportedEnvironment)"
Write-Host "recorders seen: $($found.count)"
Write-Host ""

foreach ($r in $found.recorders) {
    if ($r.error) {
        Write-Host ("  {0}  FAILED: {1}" -f $r.id, $r.error) -ForegroundColor Red
        continue
    }
    $verdict = if ($r.openedForWriting) { 'opened for writing' } else { "COULD NOT OPEN: $($r.openError)" }
    $colour = if ($r.openedForWriting) { 'Green' } else { 'Red' }
    Write-Host ("  {0}  {1} {2}" -f $r.drive, $r.vendor, $r.product)
    Write-Host ("      {0}" -f $verdict) -ForegroundColor $colour
    if ($r.openedForWriting) {
        Write-Host ("      blank: {0}, media type: {1}" -f $r.mediaBlank, $r.mediaType)
    }
}

Write-Host ""
$opened = @($found.recorders | Where-Object { $_.openedForWriting }).Count
if ($opened -gt 0) {
    Write-Host "$opened of $($found.count) writer(s) opened for writing from inside the package." -ForegroundColor Green
    Write-Host 'The Store route is not blocked by device access.'
} else {
    Write-Host 'No writer could be opened from inside the package.' -ForegroundColor Red
    Write-Host 'The Store route needs rethinking before anything is built toward it.'
}
