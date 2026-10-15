# Opens a disc through the real window and reads back what the window now says.

param(
    [string] $Disc = (Join-Path $(if ($env:DISCWRIGHT_KIT) { $env:DISCWRIGHT_KIT } else { Join-Path $env:USERPROFILE 'DiscWright-Kit' }) '2 DISCS - open these\10 everything at once')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes, System.Drawing

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

function TextOf($e) {
    if ($null -eq $e) { return '' }
    $p = $null
    if ($e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p)) { return $p.Current.Value }
    return $e.Current.Name
}

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use ---"

$app = Start-Process -FilePath $exe -PassThru
try {
    foreach ($t in 1..60) { Start-Sleep -Milliseconds 250; $app.Refresh(); if ($app.MainWindowHandle -ne 0) { break } }
    $w = $UIA::FromHandle($app.MainWindowHandle)

    Write-Host "Pressing Open."
    (ById $w 'OpenADisc' 5).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1500

    # Windows' own file dialog, typed into exactly as the folder one is.
    [System.Windows.Forms.SendKeys]::SendWait(((Join-Path $Disc 'discproject.json') -replace '([+^%~(){}\[\]])', '{$1}'))
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 2000

    # Anything it says about what it found.
    $said = $w.FindFirst($Kids, (New-Object System.Windows.Automation.PropertyCondition(
        $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))

    if ($said) {
        Write-Host "  it said something about the opening; taking the default"
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Start-Sleep -Milliseconds 800
    }

    Write-Host ""
    Write-Host "the window now says:"
    Write-Host "  name          : $(TextOf (ById $w 'Label' 5))"
    Write-Host "  where it goes : $(TextOf (ById $w 'OutputFolder' 3))"
    Write-Host "  disc size     : $((ById $w 'DiscSize' 3).Current.Name)"
    Write-Host "  will make     : $(TextOf (ById $w 'WhatItWillMake' 3))"

    $list = ById $w 'Games' 3
    $rows = $list.FindAll($Deep, (New-Object System.Windows.Automation.PropertyCondition(
        $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))

    Write-Host "  games on it   :"
    foreach ($row in $rows) { Write-Host "      $($row.Current.Name)" }

    try { $w.SetFocus(); Start-Sleep -Milliseconds 500 } catch { }
    $r = $w.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save('C:\Users\lazar\DiscWright-Lab\opened-a-disc.png', [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host ""
    Write-Host "  photographed: C:\Users\lazar\DiscWright-Lab\opened-a-disc.png"
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500 }
    if (-not $app.HasExited) { $app.Kill() }
    Write-Host ""
    Write-Host "--- the desktop is free ---"
    [Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)
}
