# Drives the real window, through UI Automation, against the real testing kit,
# all the way to discs on a disk.
#
# Everything up to now has been tested against folders a test invented. This is
# the first time the window, the recognisers, the planner, the builder and the
# staging steps run together on folders somebody really downloaded.
#
# It needs a desktop. Say so before running it.

[CmdletBinding()]
# -Exe points this at a build to drive. The default is the Debug build, which
# on a machine with Smart App Control enforcing is an exe beside loose DLLs and
# is refused outright, so a single-file publish is what actually runs here.
param(
    [string] $Exe,
    [string] $Game = (Join-Path $(if ($env:DISCWRIGHT_KIT) { $env:DISCWRIGHT_KIT } else { Join-Path $env:USERPROFILE 'DiscWright-Kit' }) '1 SOURCES - add these\A - GOG download (Hollow Knight)'),
    [string] $Into = "$env:USERPROFILE\DiscWright-Lab\driven-by-the-window",
    [string] $Shots = "$env:USERPROFILE\DiscWright-Lab"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]::Descendants

if (-not $Exe) { $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe') }
$exe = $Exe
if (-not (Test-Path $exe)) { throw "No app at $exe" }
if (-not (Test-Path $Game)) { throw "No game folder at $Game" }

if (Test-Path $Into) { Remove-Item $Into -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Into | Out-Null

function Find-ById($root, $id, $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $id)
    foreach ($try in 1..($seconds * 4)) {
        $found = $root.FindFirst($Scope, $condition)
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    throw "Never found '$id'."
}

function Read-Text($element) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        return $pattern.Current.Value
    }
    return $element.Current.Name
}

function Set-Text($element, $text) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Invoke-It($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Save-Shot($element, $name) {
    # Brought to the front first. A photograph is of whatever pixels are at
    # those coordinates, and the app is started while something else has the
    # focus, so without this the picture is of whatever is covering it.
    try { $element.SetFocus(); Start-Sleep -Milliseconds 500 } catch { }

    $r = $element.Current.BoundingRectangle
    if ($r.Width -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $path = Join-Path $Shots $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "  photographed: $path"
}

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use ---"
Write-Host ""

# Quoted, or a path with spaces in it arrives as four arguments, none of
# which is a folder, and the program opens with nothing on it.
$quoted = "`"$Game`""
$app = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru

try {
    $window = $null
    foreach ($try in 1..60) {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
        if ($app.HasExited) { throw "The app exited with code $($app.ExitCode)." }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = $UIA::FromHandle($app.MainWindowHandle)
            if ($null -ne $window) { break }
        }
    }
    if ($null -eq $window) { throw "The window never appeared." }

    # The game came in on the command line, so Windows' own folder picker is
    # out of the loop. Driving it was never the thing being tested, and it is
    # the one part of this that would not hold still: every control in it is a
    # Pane with no patterns, so it can only be typed at, and the typing raced
    # the navigation.
    Write-Host "Opened with: $(Split-Path $Game -Leaf)"

    # ------------------------------------- what else was in that folder
    # When the folder held more than the game, the window asks before adding
    # it. Take everything it offers, which is the whole point of the question.
    $asked = $null
    foreach ($try in 1..12) {
        # A dialog of ours is a CHILD of the window that owns it, the same as
        # Windows' own folder picker. Looking for it under the desktop root
        # instead found nothing, which looked for a while like a dialog that
        # was never shown.
        $asked = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::AutomationIdProperty, 'WhatElseIsInThere')))
        if ($null -ne $asked) { break }
        Start-Sleep -Milliseconds 250
    }

    if ($null -eq $asked) {
        Write-Host ""
        Write-Host "  nothing asked. What is on screen just now:"
        foreach ($top in $UIA::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($top.Current.Name -or $top.Current.AutomationId) {
                Write-Host ("    id='{0}' name='{1}' class={2}" -f `
                    $top.Current.AutomationId, $top.Current.Name, $top.Current.ClassName)
            }
        }
        Write-Host ""
    }

    if ($null -ne $asked) {
        Write-Host ""
        Write-Host "  it asked about what else was in there:"
        $headline = Find-ById $asked 'Headline'
        Write-Host "    $($headline.Current.Name)"

        foreach ($box in $asked.FindAll($Scope,
            (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)))) {
            Write-Host "    [x] $($box.Current.AutomationId)"
        }

        Save-Shot $asked 'driven-what-else.png'
        Invoke-It (Find-ById $asked 'TakeThem')
        Start-Sleep -Milliseconds 900
        Write-Host ""
    }

    # ------------------------------------------- what the window now says
    $name = Read-Text (Find-ById $window 'Label')
    $says = Read-Text (Find-ById $window 'WhatItWillMake')
    Write-Host "  it named the disc: '$name'"
    Write-Host "  and says it will make: '$says'"

    $games = Find-ById $window 'Games'
    $rows = $games.FindAll($Scope,
        (New-Object System.Windows.Automation.PropertyCondition(
            $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    Write-Host "  the list holds $($rows.Count) game(s)"

    # ------------------------------------------------------- where it goes
    Set-Text (Find-ById $window 'OutputFolder') $Into
    Start-Sleep -Milliseconds 400

    $button = Find-ById $window 'MakeTheDisc'
    Write-Host "  the button is now enabled: $($button.Current.IsEnabled)"
    if (-not $button.Current.IsEnabled) { throw "The button never came alive." }

    Save-Shot $window 'driven-before.png'

    # ------------------------------------------------------------ make it
    Write-Host ""
    Write-Host "Pressing it."
    Invoke-It $button

    # Catch it mid-build, which is the only moment the working line exists.
    $caught = $false
    foreach ($try in 1..200) {
        Start-Sleep -Milliseconds 100
        $doing = $window.FindFirst($Scope,
            (New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, 'Doing')))
        if ($null -ne $doing -and -not $caught) {
            $line = Read-Text $doing
            if ($line) {
                Write-Host "  caught it working: '$line'"
                Save-Shot $window 'driven-working.png'
                $caught = $true
            }
        }
        # The finished dialog is a window of its own too.
        $done = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
        if ($null -ne $done) {
            Write-Host ""
            Write-Host "It finished and said:"
            foreach ($text in $done.FindAll($Scope,
                (New-Object System.Windows.Automation.PropertyCondition(
                    $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) {
                $line = $text.Current.Name
                if ($line -and $line.Trim()) { Write-Host "  $line" }
            }

            # No, do not open Explorer: this is unattended. A message box
            # button is a Pane with no patterns, same as the folder dialog, so
            # it is answered with the keyboard.
            [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
            Start-Sleep -Milliseconds 200
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            Start-Sleep -Milliseconds 500

            break
        }
    }

    Start-Sleep -Milliseconds 600
    Save-Shot $window 'driven-after.png'
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600 }
    if (-not $app.HasExited) { $app.Kill() }
    Write-Host ""
    Write-Host "--- the desktop is free ---"
    [Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)
}

# ----------------------------------------------- what actually landed
Write-Host ""
Write-Host "What is on the disc:"
if (Test-Path $Into) {
    Get-ChildItem $Into -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($Into.Length + 1)
        "{0,10:N0}  {1}" -f $_.Length, $rel
    } | Sort-Object { $_ -replace '^\s*[\d,]+\s+' } | Select-Object -First 40
}
else {
    Write-Host "  nothing: $Into does not exist"
}
