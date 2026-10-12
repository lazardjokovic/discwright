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

# Worth knowing before writing any script that calls Invoke-Pester around work
# of its own: Pester 5.9.1 leaves a variable called $p in the caller's scope,
# set to the path of its own Pester.ps1. A script holding a file path in $p
# across the call - to restore a file it had deliberately broken, say - then
# writes over Pester's file instead of its own. Measured, after it happened:
# the module needed that one file restored. Anything that has to survive an
# Invoke-Pester call wants a name of its own.

# Pester 5, deliberately, and not merely "5 or later".
#
# Under Pester 6.1.0 every file in this suite hangs in BeforeAll and launches a
# DiscWright window while doing it - including a file that only calls
# Parser::ParseFile on DiscWright.ps1 and dot-sources nothing at all. The same
# code runs in well under a second in a plain script. Measured on this suite:
# 6.1.0 never finishes, 5.9.1 finishes in 18 seconds with everything passing.
#
# Picking the highest 5.x rather than one exact build, so this does not need
# editing for every patch release. If only 6.x is installed, say what to do
# rather than importing it and hanging.
$pester5 = Get-Module -ListAvailable Pester |
    Where-Object { $_.Version.Major -eq 5 } |
    Sort-Object Version -Descending |
    Select-Object -First 1

if (-not $pester5) {
    throw @'
Pester 5.x is needed and was not found.

    Install-Module Pester -RequiredVersion 5.9.1 -Force -SkipPublisherCheck -Scope CurrentUser

Pester 6 can stay installed alongside it; this runner asks for 5.x by version.
'@
}
# Is Pester still Pester?
#
# Pester runs every block by dot-sourcing one small file of its own, Pester.ps1,
# which sits beside the module and is four lines long: it takes a scriptblock
# and runs it. Nothing in a test run works without it.
#
# It gets overwritten. The hazard is written up thirty lines above this one -
# Invoke-Pester leaves a variable called $p behind, holding the path of that
# very file - and on 12 October the suite was found with DiscWright.ps1 sitting
# in it, dated 4 October. What that looks like from outside is the worst
# failure this runner can have:
#
#   Discovery found 1388 tests in 11.93s.
#   Running tests.
#
# and then nothing, for ever, with the application's own window open on the
# desktop. Pester dot-sources what it believes is its four-line helper, gets an
# application instead, and blocks on ShowDialog with nobody to close it.
# Discovery still works, which is the cruel part: the run looks like it has
# started. Forty minutes went into finding it by hand.
#
# So it is checked, in the two ways that tell a four-line file from a program:
# how big it is, and whether the code above the signature block still mentions
# the scriptblock it is supposed to run.
function Test-PesterHelper([string]$moduleBase) {
    $helper = Join-Path $moduleBase 'Pester.ps1'
    if (-not (Test-Path -LiteralPath $helper)) {
        return "$helper is missing. Pester cannot run a single test without it."
    }
    $size = (Get-Item -LiteralPath $helper).Length
    # 16 KB of it is the Authenticode signature. A program is hundreds of KB.
    if ($size -gt 64KB) {
        return ("$helper is {0:N0} bytes. The real one is a four-line file of about 17 KB, " +
                'so something has been written over it.') -f $size
    }
    $text = Get-Content -LiteralPath $helper -Raw
    $code = $text -split '# SIG # Begin signature block', 2
    if ($code[0] -notmatch '\$ScriptBlock') {
        return "$helper no longer runs the scriptblock it is handed. Something has been written over it."
    }
    return ''
}

$why = Test-PesterHelper $pester5.ModuleBase
if ($why) {
    throw @"
Pester $($pester5.Version) is damaged, so no test can run.

    $why

Put it back:

    Install-Module Pester -RequiredVersion $($pester5.Version) -Force -SkipPublisherCheck -Scope CurrentUser

or, to replace only that one file:

    Save-Module Pester -RequiredVersion $($pester5.Version) -Path `$env:TEMP
    Copy-Item "`$env:TEMP\Pester\$($pester5.Version)\Pester.ps1" '$($pester5.ModuleBase)' -Force
"@
}

Import-Module Pester -RequiredVersion $pester5.Version -Force
Write-Host "Pester $((Get-Module Pester).Version)" -ForegroundColor DarkGray

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
    # A failed BeforeAll is not a failed test, and Pester counts it nowhere
    # near FailedCount. The whole block it belongs to simply does not run,
    # so the count goes DOWN and the summary used to say 'all good' while a
    # Describe had not executed at all. That happened: nine tests sat out a
    # run and the runner called it clean, which is the one thing a runner
    # must never do.
    $broken = @($res.Failed).Count
    $blocks = @()
    foreach ($c in @($res.Containers)) {
        foreach ($b in @($c.Blocks)) {
            if ($b.ErrorRecord -and @($b.ErrorRecord).Count) { $blocks += $b.Path -join ' > ' }
        }
    }
    $failed += $res.FailedCount + @($blocks).Count
    $colour = if ($res.FailedCount -or @($blocks).Count) { 'Red' } else { 'Green' }
    Write-Host ("  {0,-8} {1} passed, {2} failed, {3} skipped" -f
        $name, $res.PassedCount, $res.FailedCount, $res.SkippedCount) -ForegroundColor $colour
    foreach ($f in $res.Failed) { Write-Host "     FAILED: $($f.ExpandedPath)" -ForegroundColor Red }
    foreach ($b in $blocks) {
        Write-Host "     DID NOT RUN: $b (its setup failed)" -ForegroundColor Red
    }
}

if ($failed) { exit 1 }
Write-Host '  all good' -ForegroundColor Green
