# Opens the window, asks UI Automation for every control the markup promises,
# photographs it, and closes it.
#
# The markup test proves the ids are WRITTEN. Only this proves they ARRIVE,
# which is the single reason WPF was chosen over another go at WinForms.

# Which build to open. The Debug build runs here now. Until 2026-10-16 Smart
# App Control blocked those freshly built assemblies outright and the app died
# on startup with 0x800711C7 before a window existed, which is why this can be
# pointed at a published Release copy instead. That is still worth doing: it is
# what somebody downloading a release actually gets.
#
# param comes FIRST. Put after anything else it is not a parse error, it is a
# call to a command called param, which the parser is perfectly happy with and
# which fails the moment the script is actually run.
param(
    [string] $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$exe = $Exe
$shot = "$env:USERPROFILE\DiscWright-Lab\the-window.png"

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
    #
    # The refusal and the worry are NOT in here. They are collapsed while there
    # is nothing to say, and a collapsed thing is not in the automation tree at
    # all, which is right: an empty line is not something to read out. That they
    # come back when there IS something to say is checked further down, which is
    # the half of it worth checking.
    $wanted = @(
        # TakeItOff is gone: removing is the cross on each row now, which only
        # exists with rows on the list and only shows under the mouse.
        'AddAGame', 'Games', 'NothingYet',
        'Label', 'DiscSize', 'OutputFolder', 'ChooseWhereItGoes',
        'MoreOptions', 'Menu', 'Checksums', 'ArtworkOnDisc', 'LegacyFileSystems', 'CutFiles',
        'WhatItWillMake', 'WhatTheButtonsMean', 'StartANewDisc', 'OpenADisc', 'MakeTheDisc'
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

    # ------------------------------------------------- the lines that hide

    function Find-ById($root, $id) {
        return $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
    }

    # Nothing is wrong yet, so neither line should be anywhere at all.
    foreach ($id in @('WhyNot', 'Worry')) {
        if ($null -ne (Find-ById $window $id)) {
            $missing += ($id + ' was in the tree with nothing to say')
        }
    }

    Write-Host ""
    Write-Host "With nothing wrong, the refusal and the worry are not in the tree, which is right."

    # A name AutoRun cannot carry. That is a worry and not a refusal: the disc
    # gets built, and what This PC shows will not be what was typed.
    if ($null -ne $name) {
        $cyrillic = -join @(0x0414, 0x0430, 0x043D, 0x043D, 0x044B, 0x0435 | ForEach-Object { [char]$_ })
        $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($cyrillic)
        Start-Sleep -Milliseconds 800

        $worry = Find-ById $window 'Worry'

        if ($null -ne $worry) {
            Write-Host "With a name AutoRun cannot carry, it says: $($worry.Current.Name)"
        }
        else {
            $missing += 'Worry never came back when there was something to say'
        }
    }

    if ($missing.Count -gt 0) {
        throw ("UI Automation could not reach: " + ($missing -join ', '))
    }

    Write-Host "Every one of the $($wanted.Count) ids was reachable, and the worry came and went."
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 400 }
    if (-not $app.HasExited) { $app.Kill() }
    Write-Host "--- the desktop is free ---"
    [Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)
}
