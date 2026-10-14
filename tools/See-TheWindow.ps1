# Opens the window, asks UI Automation for every control the markup promises,
# photographs it, and closes it.
#
# The markup test proves the ids are WRITTEN. Only this proves they ARRIVE,
# which is the single reason WPF was chosen over another go at WinForms.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$exe = 'C:\Users\lazar\Projects\discwright-csharp\src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe'
$shot = 'C:\Users\lazar\DiscWright-Lab\the-window.png'

if (-not (Test-Path $exe)) { throw "No app at $exe" }
New-Item -ItemType Directory -Force -Path (Split-Path $shot) | Out-Null

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use for a few seconds ---"

$app = Start-Process -FilePath $exe -PassThru

try {
    $window = $null
    foreach ($try in 1..60) {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
        if ($app.HasExited) {
            throw "The app exited on its own with code $($app.ExitCode). Smart App Control is the first suspect."
        }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
            if ($null -ne $window) { break }
        }
    }

    if ($null -eq $window) { throw "The window never appeared." }

    Write-Host ""
    Write-Host "UI Automation sees: $($window.Current.Name) [$($window.Current.AutomationId)]"
    Write-Host ""

    # Every id the markup promises, asked for the way a window test would.
    $wanted = @(
        'AddAGame', 'TakeItOff', 'Games', 'NothingYet',
        'Label', 'DiscSize', 'OutputFolder', 'ChooseWhereItGoes',
        'MoreOptions', 'Menu', 'Checksums', 'ArtworkOnDisc', 'LegacyFileSystems', 'CutFiles',
        'WhatItWillMake', 'WhyNot', 'Worry', 'MakeTheDisc'
    )

    $missing = @()
    foreach ($id in $wanted) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)

        $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)

        if ($null -eq $found) {
            # A collapsed Expander hides its contents from the tree, which is
            # correct: they are not on screen to be clicked either.
            $missing += $id
            Write-Host ("  {0,-20} NOT FOUND" -f $id)
        }
        else {
            Write-Host ("  {0,-20} {1}" -f $id, $found.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''))
        }
    }

    # Open More options and ask again for whatever was hidden behind it.
    if ($missing.Count -gt 0) {
        $more = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MoreOptions')))

        if ($null -ne $more) {
            Write-Host ""
            Write-Host "Opening More options and asking again:"
            $more.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 600

            $stillMissing = @()
            foreach ($id in $missing) {
                $found = $window.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))

                if ($null -eq $found) {
                    $stillMissing += $id
                    Write-Host ("  {0,-20} STILL NOT FOUND" -f $id)
                }
                else {
                    Write-Host ("  {0,-20} {1}" -f $id, $found.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''))
                }
            }
            $missing = $stillMissing
        }
    }

    # Type a name into it the way a test would, and read back what the window
    # says it will make.
    $name = $window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Label')))

    if ($null -ne $name) {
        $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('A TYPED NAME')
        Start-Sleep -Milliseconds 300
        Write-Host ""
        Write-Host "Set the name through UI Automation, and it reads back: '$($name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)'"
    }

    $button = $window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MakeTheDisc')))

    if ($null -ne $button) {
        Write-Host "The button with nothing chosen is enabled: $($button.Current.IsEnabled)"
    }

    # The photograph.
    $r = $window.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    Write-Host ""
    Write-Host "Photographed to $shot"

    if ($missing.Count -gt 0) {
        throw ("UI Automation could not reach: " + ($missing -join ', '))
    }

    Write-Host "Every one of the $($wanted.Count) ids was reachable."
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 400 }
    if (-not $app.HasExited) { $app.Kill() }
    Write-Host "--- the desktop is free ---"
    [Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)
}
