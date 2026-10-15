# Drives the whole thing through the real window: open with a game, build it,
# then press Burn and answer everything it asks.
#
# It ejects the disc at the end, and this drive cannot shut its own tray, so it
# beeps and waits for a push.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes, System.Drawing

$UIA = [System.Windows.Automation.AutomationElement]
$Deep = [System.Windows.Automation.TreeScope]::Descendants
$Kids = [System.Windows.Automation.TreeScope]::Children

$exe = 'C:\Users\lazar\Projects\discwright-csharp\src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe'
$game = 'C:\Users\lazar\DiscWright-Lab\manual-testing\1 SOURCES - add these\A - GOG download (Hollow Knight)'
$into = 'C:\Users\lazar\DiscWright-Lab\burn-from-window'

function ById($root, $id, $sec = 10) {
    $want = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $id)
    foreach ($try in 1..($sec * 4)) {
        $found = $root.FindFirst($Deep, $want)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Press($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function SetText($e, $t) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }

if (Test-Path $into) { Remove-Item $into -Recurse -Force }

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use ---"

$app = Start-Process -FilePath $exe -ArgumentList "`"$game`"" -PassThru
try {
    foreach ($t in 1..60) { Start-Sleep -Milliseconds 250; $app.Refresh(); if ($app.MainWindowHandle -ne 0) { break } }
    $w = $UIA::FromHandle($app.MainWindowHandle)

    $asked = ById $w 'WhatElseIsInThere' 5
    if ($asked) { Press (ById $asked 'TakeThem' 3); Start-Sleep -Milliseconds 800 }

    SetText (ById $w 'OutputFolder' 5) $into
    Start-Sleep -Milliseconds 500

    Write-Host "Building..."
    Press (ById $w 'MakeTheDisc' 3)

    foreach ($t in 1..900) {
        Start-Sleep -Milliseconds 200
        $done = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
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

    # It asks about erasing, because that CD-RW has something on it.
    $erase = $null
    foreach ($t in 1..40) {
        Start-Sleep -Milliseconds 250
        $erase = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::AutomationIdProperty, 'WorthAsking')))
        if ($erase) { break }
    }

    if ($erase) {
        Write-Host "  it asked: $((ById $erase 'Headline' 3).Current.Name)"
        # "Erase it" is the other way, not the likely one.
        Press (ById $erase 'TheOtherWay' 3)
        Write-Host "  answered: erase it"
    }

    # Watch the line along the bottom while it works.
    $seen = @{}
    $told = $false
    foreach ($t in 1..2400) {
        Start-Sleep -Milliseconds 250

        $doing = ById $w 'Doing' 1
        if ($doing -and $doing.Current.Name) {
            $line = $doing.Current.Name -replace ', about .* left$', ''
            if (-not $seen.ContainsKey($line)) { $seen[$line] = $true; Write-Host "    $line" }
        }

        # The tray prompt, which needs hands.
        $prompt = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))

        if ($prompt -and -not $told) {
            Write-Host ""
            Write-Host "  *** CLOSE THE TRAY ***"
            [Console]::Beep(880, 250); Start-Sleep -Milliseconds 120; [Console]::Beep(660, 450)
            $told = $true

            # Wait for the disc to come back, then press OK for them.
            foreach ($wait in 1..240) {
                Start-Sleep -Milliseconds 500
                try { if (Test-Path 'E:\') { break } } catch { }
            }
            Start-Sleep -Seconds 2
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            Write-Host "  tray closed, checking the disc..."
            continue
        }

        if ($told -and $prompt) {
            # The result box at the end.
            Write-Host ""
            Write-Host "  it finished and said:"
            foreach ($text in $prompt.FindAll($Deep, (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) {
                if ($text.Current.Name -and $text.Current.Name.Trim()) { Write-Host "    $($text.Current.Name)" }
            }
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            break
        }
    }
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600 }
    if (-not $app.HasExited) { $app.Kill() }
    Write-Host ""
    Write-Host "--- the desktop is free ---"
    [Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)
}
