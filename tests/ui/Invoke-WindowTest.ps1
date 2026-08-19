<#
.SYNOPSIS
    Drives the real DiscWright window with real mouse clicks and reads the real
    controls back.

.DESCRIPTION
    The Pester suite loads DiscWright's functions and calls them directly, which
    proves the logic and nothing about the wiring. A button connected to the
    wrong function, a rule that greys the wrong control, a handler that undoes a
    function's return convention - none of that is visible from a unit test. This
    script clicks the actual buttons.

    It found the bug it was written to look for on its first complete run: the
    Remove handler wrapped Remove-GameEntry in @(), rebuilding the array wrapper
    that the function's comma-return exists to prevent, so removing one of five
    entries left one row holding all four survivors. Every unit test passed,
    because they called the function the correct way and the call site was wrong.

    NOT run by CI. It needs a desktop session, moves the mouse, and takes over
    the foreground for about half a minute.

    Leave the machine alone while it runs. It drives the real pointer and the
    real foreground window, so a stray click lands in the middle of the sequence
    and the failures that follow are the interruption, not the app. If a run
    fails in a way that makes no sense, run it again untouched before believing
    it.

    WHAT IT CANNOT REACH
    WinForms publishes its controls through the legacy accessibility bridge, so
    every one of them arrives as a pattern-less Pane: names, enabled state and
    screen rectangles are exposed, but there is no InvokePattern to call. Clicks
    are therefore synthesized at the rectangle, which the app cannot tell from a
    person's. Two things stay out of reach:

      - ListView rows. The control appears as an empty pane, so the entries are
        asserted through the status label instead, which is generated from the
        same list.
      - The shell's "Browse For Folder" tree, which exposes no descendants at
        all. The way round it is the app's own behaviour: New-FolderDialog seeds
        SelectedPath, so a dialog opened from a box that already holds a path
        opens with that path selected and needs only OK. Typing into a text box
        works, so the output folder box is the way in.

.PARAMETER ProjectFolder
    An output folder holding a discproject.json to load. Build one first with a
    game and a few add-ons - a GOG game folder plus its patch_*.exe files does
    nicely.

.EXAMPLE
    .\Invoke-WindowTest.ps1 -ProjectFolder 'D:\Discs\Hollow Knight'
#>
# Two rules are suppressed for this file only, rather than switched off in
# PSScriptAnalyzerSettings.psd1 where they would stop applying to the app.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'A console harness whose printed output is the result. Nothing downstream consumes it.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'UI Automation elements are destroyed by the app between the enumeration and the read of a property, which throws. Skipping the element that vanished is the correct response and there is nothing to log.')]
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectFolder,
    [string]$AppPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'DiscWright.ps1'),
    [string]$ShotPath
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DwMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public static void ClickAt(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(60);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(40);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
}
"@

# Deliberately not $A / $T. PowerShell variable names are case-insensitive, so a
# loop written "foreach ($t in ...)" silently destroys a TreeScope held in $T.
$UiEl    = [System.Windows.Automation.AutomationElement]
$UiScope = [System.Windows.Automation.TreeScope]
$UiAny   = [System.Windows.Automation.Condition]::TrueCondition

$script:Checks = 0
$script:Fails  = @()
function Check([string]$what, [bool]$ok, [string]$detail = '') {
    $script:Checks++
    if ($ok) { Write-Host "  PASS  $what" }
    else { $script:Fails += $what; Write-Host "  FAIL  $what$(if($detail){"  ($detail)"})" }
}

function Get-Win([string]$titleLike, [int]$timeoutSec = 40) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        foreach ($k in $UiEl::RootElement.FindAll($UiScope::Children, $UiAny)) {
            try { if ($k.Current.Name -like $titleLike) { return $k } } catch {}
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Find-El($root, [string]$nameLike, [int]$timeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $all = $root.FindAll($UiScope::Descendants, $UiAny)
        for ($i = 0; $i -lt $all.Count; $i++) {
            try { if ($all.Item($i).Current.Name -like $nameLike) { return $all.Item($i) } } catch {}
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Set-WindowFocus($win) {
    [void][DwMouse]::SetForegroundWindow((New-Object IntPtr($win.Current.NativeWindowHandle)))
    Start-Sleep -Milliseconds 300
}

function Invoke-Click($el) {
    $r = $el.Current.BoundingRectangle
    if ($r.Width -le 0) { throw "'$($el.Current.Name)' has no clickable area" }
    [DwMouse]::ClickAt([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 400
}

function Test-Enabled($win, [string]$nameLike) {
    $el = Find-El $win $nameLike 5
    if (-not $el) { return $null }
    return [bool]$el.Current.IsEnabled
}

# The text boxes carry no name. Each is identified by sitting directly after its
# numbered label in the window's child order.
function Get-BoxAfter($win, [string]$labelLike) {
    $kids = $win.FindAll($UiScope::Children, $UiAny)
    for ($i = 0; $i -lt $kids.Count; $i++) {
        try { if ($kids.Item($i).Current.Name -like $labelLike) { return $kids.Item($i + 1) } } catch {}
    }
    return $null
}

function Get-Status($win) {
    $all = $win.FindAll($UiScope::Descendants, $UiAny)
    for ($i = 0; $i -lt $all.Count; $i++) {
        try { $n = $all.Item($i).Current.Name; if ($n -match 'Disc: ') { return $n } } catch {}
    }
    return ''
}

function Save-Shot($win, [string]$path) {
    if (-not $path) { return }
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "  (screenshot: $path)"
}

if (-not (Test-Path (Join-Path $ProjectFolder 'discproject.json'))) {
    throw "no discproject.json in $ProjectFolder - build a disc there first"
}
if (-not (Test-Path $AppPath)) { throw "DiscWright.ps1 not found at $AppPath" }

# -WindowStyle Hidden, or the launcher's console sits on top of the form and eats
# the clicks.
$proc = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $AppPath)

try {
    $win = Get-Win 'DiscWright*'
    if (-not $win) { throw 'the window never appeared' }
    Set-WindowFocus $win; Start-Sleep -Milliseconds 600; Set-WindowFocus $win

    Write-Host "`n=== 1. nothing unusable is left clickable, with an empty list ==="
    Check 'Add game... is enabled'      ((Test-Enabled $win 'Add game*') -eq $true)
    Check 'Add-on... is greyed'         ((Test-Enabled $win 'Add-on*') -eq $false)
    Check 'Change... is greyed'         ((Test-Enabled $win 'Change*') -eq $false)
    Check 'Remove is greyed'            ((Test-Enabled $win 'Remove') -eq $false)
    Check 'Show disc folder is greyed'  ((Test-Enabled $win 'Show disc folder') -eq $false)

    Write-Host "`n=== 2. loading a project through the real folder dialog ==="
    $box = Get-BoxAfter $win '6)  Output folder*'
    if (-not $box) { throw 'the output folder box was not found' }
    Invoke-Click $box
    [System.Windows.Forms.SendKeys]::SendWait('^a' + ($ProjectFolder -replace '([+^%~(){}\[\]])', '{$1}'))
    Start-Sleep -Milliseconds 400
    Invoke-Click (Find-El $win 'Open existing disc*' 5)
    Start-Sleep -Seconds 2
    $dlg = Find-El $win 'Browse For Folder' 10
    Check 'the folder dialog opened' ($null -ne $dlg)
    if ($dlg) { Invoke-Click (Find-El $dlg 'OK' 5); Start-Sleep -Seconds 4 }

    $status = Get-Status $win
    Write-Host "  status label: '$status'"
    Check 'the project loaded and the entries are counted' ($status -match '^\d+ game') "got '$status'"
    Check 'games and add-ons are counted apart' ($status -match 'add-on|^\d+ games? \(') "got '$status'"

    Write-Host "`n=== 3. the greying rule once entries exist ==="
    Check 'Add-on... is now enabled'                  ((Test-Enabled $win 'Add-on*') -eq $true)
    Check 'Change... still greyed, nothing selected'  ((Test-Enabled $win 'Change*') -eq $false)
    Check 'Remove still greyed, nothing selected'     ((Test-Enabled $win 'Remove') -eq $false)
    Check 'Show disc folder is now enabled'           ((Test-Enabled $win 'Show disc folder') -eq $true)

    Write-Host "`n=== 4. selecting a row wakes Change and Remove ==="
    $lv = Get-BoxAfter $win '1)  Installers*'
    if (-not $lv) { throw 'the installer list was not found' }
    $r = $lv.Current.BoundingRectangle
    [DwMouse]::ClickAt([int]($r.X + 60), [int]($r.Y + 30))   # first row, under the header
    Start-Sleep -Milliseconds 700
    Check 'Change... enabled after selecting a row' ((Test-Enabled $win 'Change*') -eq $true)
    Check 'Remove enabled after selecting a row'    ((Test-Enabled $win 'Remove') -eq $true)

    Write-Host "`n=== 5. Remove takes out one entry, not several ==="
    $before = 0
    if ($status -match '^(\d+) game') { $before = [int]$Matches[1] }
    if ($status -match '\+ (\d+) add-on') { $before += [int]$Matches[1] }
    Invoke-Click (Find-El $win 'Remove' 5)
    Start-Sleep -Seconds 2
    $after = Get-Status $win
    Write-Host "  status label: '$after'"
    $left = 0
    if ($after -match '^(\d+) game') { $left = [int]$Matches[1] }
    if ($after -match '\+ (\d+) add-on') { $left += [int]$Matches[1] }
    # This is the check that caught the @() wrapper: the handler used to collapse
    # every survivor into a single entry, so one Remove took the count to one.
    Check "one Remove leaves $($before - 1) entries" ($left -eq ($before - 1)) "$before -> $left"

    Save-Shot $win $ShotPath
}
finally {
    Start-Sleep -Milliseconds 300
    try { $proc.Kill() } catch {}
}

Write-Host "`n=== $($script:Checks - $script:Fails.Count)/$($script:Checks) checks passed ==="
if ($script:Fails.Count) {
    $script:Fails | ForEach-Object { Write-Host "   failed: $_" }
    exit 1
}
