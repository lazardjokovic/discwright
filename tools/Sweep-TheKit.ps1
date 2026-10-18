# Puts every folder in the testing kit through the real window, one at a time,
# and writes down what happened to each.
#
# This is the regression run the rewrite has not had. Everything so far was
# either a unit test against folders a test invented, or one folder driven by
# hand. This is all twenty, through the window, to discs on a disk.
#
# It takes the desktop for several minutes: twenty windows open and close, and
# each takes the focus for a moment. Other apps can stay open; typing cannot.

[CmdletBinding()]
param(
    [string] $Kit = (Join-Path $(if ($env:DISCWRIGHT_KIT) { $env:DISCWRIGHT_KIT } else { Join-Path $env:USERPROFILE 'DiscWright-Kit' }) '1 SOURCES - add these'),
    [string] $Into = 'C:\Users\lazar\DiscWright-Lab\kit-sweep',
    [string] $Report = 'C:\Users\lazar\DiscWright-Lab\kit-sweep.md',

    # The two sparse ones are 4.3 GB and 6.2 GB. What the window SAYS about
    # them is the interesting part, and copying them would take the run from
    # minutes to most of an hour, so they are asked and not built.
    [int] $BuildUnderMB = 100,

    # Set on purpose, every time, and never left to the window.
    #
    # DiscWright now starts from the size of the disc that is actually in a
    # drive, which is right for a person and wrong for a sweep: with a CD in
    # the tray the 4.3 GB folder was refused, and with the drive empty it
    # builds, so two runs stopped being comparable. This is a regression run,
    # and a regression run has to ask the same question every time.
    #
    # DVD5 because that is what every sweep before this one used.
    [string] $Size = 'DVD5'
)

$ErrorActionPreference = 'Stop'
# Forms is here for SendKeys, which drives Windows' own folder dialog. That
# dialog hands out every control as a Pane with no patterns on it, so the
# keyboard is the only way in.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

$UIA = [System.Windows.Automation.AutomationElement]
$Kids = [System.Windows.Automation.TreeScope]::Children
$Deep = [System.Windows.Automation.TreeScope]::Descendants

$exe = 'C:\Users\lazar\Projects\discwright-csharp\src\DiscWright.App\bin\Debug\net10.0-windows\DiscWright.App.exe'
if (-not (Test-Path $exe)) { throw "No app at $exe" }

if (Test-Path $Into) { Remove-Item $Into -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Into | Out-Null

# Picks the size of disc, and proves it took.
#
# Typed rather than clicked: an open dropdown is a popup in a window of its
# own, so its rows are under neither the box nor the window, and two goes at
# finding them there came back with nothing.
function Set-TheSize($window, $wanted) {
    $box = ById $window 'DiscSize' 5
    if ($null -eq $box) { throw "No disc size box in the window." }

    $box.SetFocus()
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($wanted.Substring(0, 1))
    Start-Sleep -Milliseconds 500

    # Read back, because a selection that did not happen must not look like one.
    $got = ''
    try {
        foreach ($one in $box.GetCurrentPattern(
            [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()) {
            $got = $one.Current.Name
        }
    } catch { }

    if ($got -notlike "$wanted*") {
        throw "Asked for $wanted and the window says '$got', so this sweep would not be comparable."
    }

    return $got
}

function ById($root, $id, $seconds = 10) {
    $want = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $id)
    foreach ($try in 1..($seconds * 4)) {
        $found = $root.FindFirst($Deep, $want)
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function TextOf($element) {
    if ($null -eq $element) { return '' }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        return $pattern.Current.Value
    }
    return $element.Current.Name
}

function SetText($element, $text) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Press($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function ChildWindow($window, $id, $seconds) {
    $want = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $id)
    foreach ($try in 1..($seconds * 4)) {
        # A dialog of ours is a CHILD of the window that owns it, the same as
        # Windows' own folder picker, and not a child of the desktop root.
        $found = $window.FindFirst($Kids, $want)
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function AnyChildWindow($window) {
    $want = New-Object System.Windows.Automation.PropertyCondition(
        $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)

    return $window.FindFirst($Kids, $want)
}

$folders = Get-ChildItem $Kit -Directory | Sort-Object Name
$rows = @()

[Console]::Beep(660, 150)
Write-Host "--- the desktop is in use: $($folders.Count) folders, do not type ---"
Write-Host ""

foreach ($folder in $folders) {
    $mb = [math]::Round(((Get-ChildItem $folder.FullName -Recurse -File -Force |
        Measure-Object -Property Length -Sum).Sum) / 1MB, 1)

    $row = [ordered]@{
        Folder   = $folder.Name
        MB       = $mb
        Named    = ''
        Says     = ''
        Games    = 0
        AlsoHad  = ''
        Built    = ''
        OnTheDisc = 0
        AddOns   = 0
        Note     = ''
    }

    Write-Host ("{0,-50} {1,8} MB" -f $folder.Name, $mb)

    $where = Join-Path $Into $folder.Name.Substring(0, 1)
    $app = Start-Process -FilePath $exe -ArgumentList "`"$($folder.FullName)`"" -PassThru

    try {
        $window = $null
        foreach ($try in 1..60) {
            Start-Sleep -Milliseconds 250
            $app.Refresh()
            if ($app.HasExited) { break }
            if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
                $window = $UIA::FromHandle($app.MainWindowHandle)
                if ($null -ne $window) { break }
            }
        }

        if ($null -eq $window) {
            $row.Note = 'the window never appeared'
            $rows += [PSCustomObject]$row
            continue
        }

        # Every question it asks on the way in, until it stops asking. A
        # folder of games becomes nine games, and each of those can have its
        # own files left behind, so there is no fixed number of these.
        $asked = @()

        foreach ($round in 1..30) {
            $settle = ChildWindow $window 'WorthAsking' 2
            if ($null -ne $settle) {
                $asked += (TextOf (ById $settle 'Headline' 3))
                # The likely answer, which is the one on the button.
                Press (ById $settle 'TheLikelyOne' 3)
                Start-Sleep -Milliseconds 700
                continue
            }

            $else = ChildWindow $window 'WhatElseIsInThere' 2
            if ($null -ne $else) {
                $headline = ById $else 'Headline' 3
                $asked += (TextOf $headline)
                Press (ById $else 'TakeThem' 3)
                Start-Sleep -Milliseconds 700
                continue
            }

            break
        }

        if ($asked.Count -gt 0) {
            $row.AlsoHad = ($asked | ForEach-Object {
                $_ -replace '^There (is|are) ', '' -replace ' in that folder$', ''
            }) -join '; '
        }

        # Before anything is read off the window, because the size decides
        # what it says it will make and whether it refuses at all.
        Set-TheSize $window $Size | Out-Null
        Start-Sleep -Milliseconds 400

        # Where it goes, before asking what it objects to. The window
        # now says "There is nowhere to put it" while no folder has
        # been chosen, which is correct and which made this sweep
        # read every one of the twenty as refused and build nothing.
        SetText (ById $window 'OutputFolder' 5) $where
        Start-Sleep -Milliseconds 400

        # Anything it refused outright shows up here instead.
        $refusal = TextOf (ById $window 'WhyNot' 3)

        $row.Named = TextOf (ById $window 'Label' 5)
        $row.Says = TextOf (ById $window 'WhatItWillMake' 3)

        $list = ById $window 'Games' 3
        if ($null -ne $list) {
            $row.Games = $list.FindAll($Deep, (New-Object System.Windows.Automation.PropertyCondition(
                $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))).Count
        }

        if ($refusal) {
            $row.Note = $refusal
            $row.Built = 'refused'
            $rows += [PSCustomObject]$row
            Write-Host "    refused: $refusal"
            continue
        }

        if ($mb -gt $BuildUnderMB) {
            $row.Built = 'asked only'
            $row.Note = "too big to build in a sweep"
            $rows += [PSCustomObject]$row
            Write-Host "    says '$($row.Says)', not built here"
            continue
        }

        $button = ById $window 'MakeTheDisc' 5
        if (-not $button.Current.IsEnabled) {
            $row.Built = 'button dead'
            $row.Note = 'nothing to press it for'
            $rows += [PSCustomObject]$row
            continue
        }

        Press $button

        # It is over when it puts a dialog up.
        $done = $null
        foreach ($try in 1..600) {
            Start-Sleep -Milliseconds 200
            $done = AnyChildWindow $window
            if ($null -ne $done) { break }
        }

        if ($null -eq $done) {
            $row.Built = 'never finished'
            $rows += [PSCustomObject]$row
            continue
        }

        # No, do not open Explorer twenty times.
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        Start-Sleep -Milliseconds 150
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Start-Sleep -Milliseconds 400

        $row.Built = 'yes'
    }
    finally {
        if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 400 }
        if (-not $app.HasExited) { $app.Kill() }
    }

    if (Test-Path $where) {
        $landed = Get-ChildItem $where -Recurse -File
        $row.OnTheDisc = $landed.Count
        $row.AddOns = ($landed | Where-Object { $_.FullName -match '\\Add-ons\\' }).Count

        # One per game, in the menu the disc actually carries, because a WPF
        # list only builds the rows it can show and nine games on it answer seven.
        $menu = $landed | Where-Object { $_.Name -eq 'menu.hta' } | Select-Object -First 1
        if ($null -ne $menu) {
            $row.Games = ([regex]::Matches((Get-Content $menu.FullName -Raw), ", folder: ")).Count
        }
        Write-Host "    $($row.Says), $($row.OnTheDisc) files on the disc"
    }

    $rows += [PSCustomObject]$row
}

Write-Host ""
Write-Host "--- the desktop is free ---"
[Console]::Beep(880, 150); Start-Sleep -Milliseconds 80; [Console]::Beep(1175, 220)

$lines = @(
    "# The kit, through the window",
    "",
    "Every folder in ``1 SOURCES - add these``, opened in the real DiscWright and built, driven through UI Automation.",
    "",
    "Run $(Get-Date -Format 'yyyy-MM-dd HH:mm'), against $((git -C 'C:\Users\lazar\Projects\discwright-csharp' rev-parse --short HEAD)), every folder aimed at $Size",
    "",
    "| Folder | Size | Named it | Says it will make | Games | Also had | Built | Files | Add-ons | Note |",
    "|---|---|---|---|---|---|---|---|---|---|"
)

foreach ($r in $rows) {
    $lines += "| $($r.Folder) | $($r.MB) MB | $($r.Named) | $($r.Says) | $($r.Games) | $($r.AlsoHad) | $($r.Built) | $($r.OnTheDisc) | $($r.AddOns) | $($r.Note) |"
}

$lines | Set-Content -Path $Report -Encoding utf8
Write-Host ""
Write-Host "Written to $Report"
$rows | Format-Table Folder, MB, Says, Games, AlsoHad, Built, OnTheDisc -AutoSize
