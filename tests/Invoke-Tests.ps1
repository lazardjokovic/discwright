<#
.SYNOPSIS
    Runs DiscWright's tests the way they should be run locally: the logic tests
    and the window tests together.

.DESCRIPTION
    CI runs only the logic tests, because a hosted runner has a 1024x768 desktop
    with nobody at it and the window needs more room than that. Locally there is
    a real desktop, and the window is most of what this application IS - a
    WinForms front end over a build pipeline. Testing only the pipeline locally
    would leave the half that people actually touch unexercised.

    So this runs both by default. The window suite launches the app, moves the
    pointer and takes the foreground for about a minute.

    LEAVE THE MACHINE ALONE while the window tests run. A stray click lands in
    the middle of a sequence and everything after it fails for a reason that has
    nothing to do with the code. If a failure makes no sense, run it again
    untouched before believing it.

.PARAMETER SkipUI
    Run only the logic tests - for a quick check, or over a remote session where
    there is no usable desktop.

.PARAMETER UIOnly
    Run only the window tests.

.EXAMPLE
    .\tests\Invoke-Tests.ps1

.EXAMPLE
    .\tests\Invoke-Tests.ps1 -SkipUI
#>
# Write-Host is the right call here and nowhere else: this is a runner whose
# printed, coloured summary is the whole product. Nothing downstream consumes it.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'A test runner whose coloured console output is its entire output. Nothing consumes it as data.')]
[CmdletBinding()]
param(
    [switch]$SkipUI,
    [switch]$UIOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not (Get-Module -ListAvailable Pester | Where-Object { $_.Version.Major -ge 5 })) {
    throw 'Pester 5 or later is needed. Install-Module Pester -Force -SkipPublisherCheck -Scope CurrentUser'
}
Import-Module Pester -MinimumVersion 5.0 -Force

$unit = $null
$ui   = $null

if (-not $UIOnly) {
    Write-Host "`n=== logic tests ===" -ForegroundColor Cyan
    $cfg = New-PesterConfiguration
    $cfg.Run.Path          = (Join-Path $root 'tests')
    # tests/ui is run separately below - it needs a desktop and takes a minute,
    # so it does not belong in the same pass as the fast ones.
    $cfg.Run.ExcludePath   = @((Join-Path $root 'tests\ui'))
    $cfg.Filter.ExcludeTag = @('UI')
    $cfg.Output.Verbosity  = 'Normal'
    $cfg.Run.PassThru      = $true
    $unit = Invoke-Pester -Configuration $cfg
}

if (-not $SkipUI) {
    Import-Module (Join-Path $root 'tests\ui\UiDriver.psm1') -Force
    if (-not (Test-UiAvailable)) {
        Write-Host "`n=== window tests: no usable desktop, skipped ===" -ForegroundColor Yellow
    } else {
        Write-Host "`n=== window tests ===" -ForegroundColor Cyan
        Write-Host "    Hands off the mouse and keyboard for about a minute." -ForegroundColor Yellow
        $cfg = New-PesterConfiguration
        $cfg.Run.Path         = (Join-Path $root 'tests\ui')
        $cfg.Output.Verbosity = 'Normal'
        $cfg.Run.PassThru     = $true
        $ui = Invoke-Pester -Configuration $cfg
    }
}

Write-Host "`n=== summary ===" -ForegroundColor Cyan
$failed = 0
foreach ($pair in @(@('logic', $unit), @('window', $ui))) {
    $name = $pair[0]; $res = $pair[1]
    if (-not $res) { Write-Host ("  {0,-8} not run" -f $name); continue }
    $failed += $res.FailedCount
    $colour = if ($res.FailedCount) { 'Red' } else { 'Green' }
    Write-Host ("  {0,-8} {1} passed, {2} failed, {3} skipped" -f
        $name, $res.PassedCount, $res.FailedCount, $res.SkippedCount) -ForegroundColor $colour
    foreach ($f in $res.Failed) { Write-Host "     FAILED: $($f.ExpandedPath)" -ForegroundColor Red }
}

if ($failed) { exit 1 }
Write-Host '  all good' -ForegroundColor Green
