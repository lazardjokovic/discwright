# Drives the whole thing through the real window: open with a game, build it,
# then press Burn and answer everything it asks.
#
# -Packaged launches the MSIX-registered app instead of the build on disk,
# which is the only way to test the Store route. A packaged app started by its
# exe path runs with no package identity at all and proves nothing, silently.
#
# -Drive picks the writer rather than leaving it to "whichever one is ready".
# With a disc in both bays that choice is a coin toss, and burning the wrong
# one erases something nobody agreed to lose.

[CmdletBinding()]
param(
    [string] $Drive = 'E:',
    [switch] $Packaged,
    [string] $Game,
    [string] $Into = "$env:USERPROFILE\DiscWright-Lab\burn-from-window",
    [string] $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe')
)

# The kit is found rather than written down, so this works on this PC, on the
# test VM where the user is called something else, and on anybody else's
# machine. Set DISCWRIGHT_KIT to put it somewhere other than the default.
$TheKit = $(if ($env:DISCWRIGHT_KIT) { $env:DISCWRIGHT_KIT } else { Join-Path $env:USERPROFILE 'DiscWright-Kit' })

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes, System.Drawing

$UIA = [System.Windows.Automation.AutomationElement]
$Deep = [System.Windows.Automation.TreeScope]::Descendants
$Kids = [System.Windows.Automation.TreeScope]::Children

$Text = [System.Windows.Automation.ControlType]::Text
$Button = [System.Windows.Automation.ControlType]::Button
$Row = [System.Windows.Automation.ControlType]::ListItem
$AWindow = [System.Windows.Automation.ControlType]::Window

if (-not $Game) { $Game = (Join-Path $TheKit '1 SOURCES - add these\A - GOG download (Hollow Knight)') }

function Want($property, $value) {
    New-Object System.Windows.Automation.PropertyCondition($property, $value)
}

function ById($root, $id, $sec = 10) {
    foreach ($try in 1..($sec * 4)) {
        $found = $root.FindFirst($Deep, (Want $UIA::AutomationIdProperty $id))
        if ($found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Press($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function SetText($e, $t) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }

function ChooseInList($combo, $contains) {
    $open = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $open.Expand()
    Start-Sleep -Milliseconds 400
    try {
        foreach ($row in $combo.FindAll($Deep, (Want $UIA::ControlTypeProperty $Row))) {
            if ($row.Current.Name -like "*$contains*") {
                $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Milliseconds 300
                return $row.Current.Name
            }
        }
    } finally { $open.Collapse() }
    return $null
}

if (Test-Path $Into) { Remove-Item $Into -Recurse -Force }

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use ---"

# ------------------------------------------------------------------ starting
if ($Packaged) {
    $package = Get-AppxPackage -Name 'DiscWright*' | Select-Object -First 1
    if (-not $package) { throw 'No DiscWright package is registered.' }

    $aumid = "$($package.PackageFamilyName)!DiscWright"
    Write-Host "launching the packaged app: $aumid"

    Get-Process DiscWright.App -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
    Start-Sleep -Milliseconds 500

    # Through the shell, which hands back no process object, so it is found
    # afterwards by name.
    Start-Process "shell:appsFolder\$aumid" -ArgumentList "`"$Game`""

    $app = $null
    foreach ($t in 1..160) {
        Start-Sleep -Milliseconds 250
        $app = Get-Process DiscWright.App -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($app -and $app.MainWindowHandle -ne 0) { break }
    }
    if (-not $app -or $app.MainWindowHandle -eq 0) { throw 'The packaged app never showed a window.' }
} else {
    Write-Host "launching $Exe"
    $app = Start-Process -FilePath $Exe -ArgumentList "`"$Game`"" -PassThru
    foreach ($t in 1..60) { Start-Sleep -Milliseconds 250; $app.Refresh(); if ($app.MainWindowHandle -ne 0) { break } }
}

try {
    $w = $UIA::FromHandle($app.MainWindowHandle)

    $asked = ById $w 'WhatElseIsInThere' 5
    if ($asked) { Press (ById $asked 'TakeThem' 3); Start-Sleep -Milliseconds 800 }

    # Nothing below is worth doing if the game never arrived. Burning an empty
    # disc would erase a real one to prove nothing, and a packaged app may not
    # be handed its command line the way a plain exe is.
    $games = ById $w 'Games' 5
    $rows = @($games.FindAll($Deep, (Want $UIA::ControlTypeProperty $Row)))
    Write-Host "the list holds $($rows.Count) game(s)"
    if ($rows.Count -lt 1) {
        throw 'The game never loaded, so there is nothing to burn. Not touching the disc.'
    }

    SetText (ById $w 'OutputFolder' 5) $Into
    Start-Sleep -Milliseconds 500

    # ------------------------------------------------------------- the drive
    $picker = ById $w 'WhichDrive' 5
    if (-not $picker) { throw 'There is no drive picker in this build.' }
    $chose = ChooseInList $picker $Drive
    if (-not $chose) { throw "There is no $Drive in the drive picker." }
    Write-Host "drive chosen: $chose"

    Write-Host "Building..."
    Press (ById $w 'MakeTheDisc' 3)

    foreach ($t in 1..900) {
        Start-Sleep -Milliseconds 200
        $done = $w.FindFirst($Kids, (Want $UIA::ControlTypeProperty $AWindow))
        if ($done) {
            [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
            Start-Sleep -Milliseconds 150
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            break
        }
    }
    Start-Sleep -Milliseconds 900

    Write-Host "Pressing Burn."
    Press (ById $w 'BurnIt' 5)

    # ------------------------------------------------------- what it asks for
    #
    # Every dialog is named rather than guessed at.
    #
    # The first version of this treated ANY child window as the tray prompt,
    # beeped, pressed Enter at it and carried on. On E: the sequence happened
    # to line up. On D:, which never needs a tray push, the window after the
    # erase was something else, Enter cancelled the burn, and the harness
    # reported a tray push that never happened while the disc came out erased
    # and empty. A test that quietly answers the wrong question is worse than
    # one that fails.
    $seen = @{}
    $met = @{}
    $patience = @{}
    $answered = 0

    foreach ($t in 1..4800) {
        Start-Sleep -Milliseconds 250

        $doing = ById $w 'Doing' 1
        $working = $doing -and $doing.Current.Name
        if ($working) {
            $line = $doing.Current.Name -replace ', about .* left$', ''
            if (-not $seen.ContainsKey($line)) { $seen[$line] = $true; Write-Host "    $line" }
        }

        $box = $w.FindFirst($Kids, (Want $UIA::ControlTypeProperty $AWindow))
        if (-not $box) {
            # Finished once something has been answered and the work has
            # stopped. Before anything is answered there is nothing to wait on
            # but the progress line.
            if ($answered -gt 0 -and -not $working) { break }
            continue
        }

        $what = $box.Current.AutomationId
        $head = $box.FindFirst($Deep, (Want $UIA::AutomationIdProperty 'Headline'))
        $headline = if ($head) { $head.Current.Name } else { '' }

        if (-not $met.ContainsKey("$what|$headline")) {
            $met["$what|$headline"] = $true
            Write-Host ""
            Write-Host "  dialog '$what': $headline"
            foreach ($line in $box.FindAll($Deep, (Want $UIA::ControlTypeProperty $Text))) {
                if ($line.Current.Name.Trim() -and $line.Current.Name -ne $headline) {
                    Write-Host "      $($line.Current.Name)"
                }
            }
            $buttons = @($box.FindAll($Deep, (Want $UIA::ControlTypeProperty $Button)))
            Write-Host ("      buttons: " + (($buttons | ForEach-Object { "[$($_.Current.AutomationId)] $($_.Current.Name)" }) -join '  '))
        }

        # A tray push is only a tray push when it says so.
        if ($headline -match 'tray|push it|put it back|close it') {
            Write-Host "  *** CLOSE THE TRAY ***"
            [Console]::Beep(880, 250); Start-Sleep -Milliseconds 120; [Console]::Beep(660, 450)
            foreach ($wait in 1..240) {
                Start-Sleep -Milliseconds 500
                try { if (Test-Path "${Drive}\") { break } } catch { }
            }
            Start-Sleep -Seconds 2
        }

        # Erasing is the one answer that is deliberately not the likely one.
        $pressed = $null
        if ($what -eq 'WorthAsking' -and $headline -match 'Erase') {
            $other = $box.FindFirst($Deep, (Want $UIA::AutomationIdProperty 'TheOtherWay'))
            if ($other) { Press $other; $pressed = 'TheOtherWay (erase it)' }
        }

        if (-not $pressed) {
            foreach ($id in 'TheLikelyOne', 'TakeThatOne', 'Done', 'TakeThem') {
                $button = $box.FindFirst($Deep, (Want $UIA::AutomationIdProperty $id))
                if ($button) { Press $button; $pressed = $id; break }
            }
        }

        if (-not $pressed) {
            # A window exists before its content does. The result box at the
            # end was found with no automation id and no buttons at all, and
            # giving up on the first look reported a dead end over a dialog
            # that was simply half a second from being ready. So it is given a
            # few seconds, and only then called unknown.
            $waiting = "$what|$headline"
            if (-not $patience.ContainsKey($waiting)) { $patience[$waiting] = 0 }
            $patience[$waiting]++

            if ($patience[$waiting] -lt 20) { continue }

            Write-Host "      NOTHING TO PRESS after 5 seconds. Stopping rather than guessing."
            break
        }

        Write-Host "      pressed: $pressed"
        $answered++
        Start-Sleep -Milliseconds 900
    }

    Write-Host ""
    Write-Host "the disc now says:"
    Start-Sleep -Seconds 3
    $v = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$Drive'" -ErrorAction SilentlyContinue
    Write-Host ("  label '{0}', {1:N1} MB" -f $v.VolumeName, ($v.Size / 1MB))
    Get-ChildItem "${Drive}\" -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { "    $($_.Name)" }
}
finally {
    if ($app -and -not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600 }
    if ($app -and -not $app.HasExited) { $app.Kill() }
    Write-Host ""
    Write-Host "--- the desktop is free ---"
    [Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)
}
