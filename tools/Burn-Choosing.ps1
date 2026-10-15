# Builds a small disc in the real window, presses Burn, and answers whatever it
# asks, writing down every question so the asking itself can be checked.
#
# $Pick is the drive letter to choose when it offers a choice.

param(
    [Parameter(Mandatory)] [string] $Pick,
    [string] $Game = (Join-Path $(if ($env:DISCWRIGHT_KIT) { $env:DISCWRIGHT_KIT } else { Join-Path $env:USERPROFILE 'DiscWright-Kit' }) '1 SOURCES - add these\A - GOG download (Hollow Knight)'),
    [string] $Into = 'C:\Users\lazar\DiscWright-Lab\burn-choosing'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes

$UIA = [System.Windows.Automation.AutomationElement]
$Deep = [System.Windows.Automation.TreeScope]::Descendants
$Kids = [System.Windows.Automation.TreeScope]::Children

$exe = 'C:\Users\lazar\Projects\discwright-csharp\src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe'

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

if (Test-Path $Into) { Remove-Item $Into -Recurse -Force }

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use ---"

$app = Start-Process -FilePath $exe -ArgumentList "`"$Game`"" -PassThru
try {
    foreach ($t in 1..60) { Start-Sleep -Milliseconds 250; $app.Refresh(); if ($app.MainWindowHandle -ne 0) { break } }
    $w = $UIA::FromHandle($app.MainWindowHandle)

    $asked = ById $w 'WhatElseIsInThere' 5
    if ($asked) { Press (ById $asked 'TakeThem' 3); Start-Sleep -Milliseconds 800 }

    SetText (ById $w 'OutputFolder' 5) $Into
    Start-Sleep -Milliseconds 400

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

    $seen = @{}
    $answered = $false

    foreach ($t in 1..3600) {
        Start-Sleep -Milliseconds 250

        # A choice between discs or drives.
        $chooser = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::AutomationIdProperty, 'PickOne')))

        if ($chooser -and -not $answered) {
            Write-Host ""
            Write-Host "  it offered a choice: $((ById $chooser 'Headline' 3).Current.Name)"
            Write-Host "    $((ById $chooser 'Because' 3).Current.Name)"

            $list = ById $chooser 'Choices' 3
            $rows = $list.FindAll($Deep, (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))

            $at = -1
            for ($i = 0; $i -lt $rows.Count; $i++) {
                Write-Host "      [$i] $($rows[$i].Current.Name)"
                if ($rows[$i].Current.Name -like "$Pick*") { $at = $i }
            }

            if ($at -lt 0) { Write-Host "    $Pick is not on offer; taking the first"; $at = 0 }

            $rows[$at].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 300
            Write-Host "    chose [$at]"
            Press (ById $chooser 'TakeThatOne' 3)
            $answered = $true
            Start-Sleep -Milliseconds 700
            continue
        }

        # A yes or no question.
        $settle = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::AutomationIdProperty, 'WorthAsking')))

        if ($settle) {
            Write-Host ""
            Write-Host "  it asked: $((ById $settle 'Headline' 3).Current.Name)"
            Press (ById $settle 'TheOtherWay' 3)
            Write-Host "    answered with the other way"
            Start-Sleep -Milliseconds 700
            continue
        }

        $doing = ById $w 'Doing' 1
        if ($doing -and $doing.Current.Name) {
            $line = $doing.Current.Name -replace ', about .* left$', '' -replace ', \d+ of about .*$', ''
            if (-not $seen.ContainsKey($line)) { $seen[$line] = $true; Write-Host "    $line" }
        }

        # Anything else is the message box at the end.
        $box = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))

        if ($box -and $answered) {
            Start-Sleep -Milliseconds 500
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            Start-Sleep -Milliseconds 500
            $still = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
            if (-not $still) { break }
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
