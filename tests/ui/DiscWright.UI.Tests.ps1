<#
    Tests that drive the real window.

        Invoke-Pester tests/ui

    Everything in tests/DiscWright.Tests.ps1 calls DiscWright's functions
    directly. That proves the logic and says nothing about the wiring: a button
    connected to the wrong function, a rule that greys the wrong control, a
    handler that undoes a function's return convention. All of that is invisible
    there and visible here.

    It is not a hypothetical gap. The first complete run of this suite found the
    Remove handler wrapping Remove-GameEntry in @(), which collapsed every
    remaining entry into one row - with 178 unit tests passing, because they
    called the function correctly and the call site was wrong.

    These tests need a desktop. They move the pointer, take the foreground, and
    take about a minute. LEAVE THE MACHINE ALONE while they run: a stray click
    lands in the middle of a sequence and everything after it fails for a reason
    that has nothing to do with the app. If a failure makes no sense, run it
    again untouched before believing it.

    They skip themselves where there is no desktop, so a headless runner reports
    them as skipped rather than failing.
#>

# Same reason as UiDriver.psm1: the empty catches here guard reads of UI
# Automation elements that the app destroys and rebuilds while they are being
# walked. Skipping one that has just gone is the intent, and there is nothing to
# log. Suppressed for this file only, so the rule keeps applying to the app.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'Reads of UI Automation elements the app destroyed mid-walk. Skipping the vanished element is correct and there is nothing to log.')]
param()

BeforeDiscovery {
    Import-Module (Join-Path $PSScriptRoot 'UiDriver.psm1') -Force
    $script:HaveDesktop = Test-UiAvailable
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'UiDriver.psm1') -Force
    Add-Type -AssemblyName System.Drawing

    $script:AppPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'DiscWright.ps1'
    $script:ShotDir = Join-Path $PSScriptRoot 'shots'

    # DiscWright's own functions build the fixture, so the suite provisions
    # itself and needs nothing prepared by hand.
    $errs = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:AppPath, [ref]$null, [ref]$errs)
    if ($errs -and $errs.Count) { throw "DiscWright.ps1 has $($errs.Count) parse errors" }
    foreach ($f in $ast.FindAll({ param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
        . ([scriptblock]::Create($f.Extent.Text))
    }
    $script:PROJECT_FILE = 'discproject.json'

    $script:Sandbox = Join-Path ([IO.Path]::GetTempPath()) ('dwui_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Force -Path $script:Sandbox | Out-Null

    # Two game folders and, beside the first game, two extra installers to add as
    # add-ons. Sparse files: the whole fixture costs nothing and builds instantly.
    function New-Installer([string]$dir, [string]$name, [double]$mb = 2) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $fs = [IO.File]::Create((Join-Path $dir $name)); $fs.SetLength([long]($mb * 1MB)); $fs.Close()
        return (Join-Path $dir $name)
    }
    $script:SrcRoot = Join-Path $script:Sandbox 'src'
    $script:GameA = Join-Path $script:SrcRoot 'aaa_first_game'
    $script:GameB = Join-Path $script:SrcRoot 'bbb_second_game'
    $null = New-Installer $script:GameA 'setup_first_game_1.0.exe' 3
    $script:PatchOne = New-Installer $script:GameA 'patch_first_game_1.0_to_1.1.exe' 1
    $script:PatchTwo = New-Installer $script:GameA 'patch_first_game_1.1_to_1.2.exe' 1
    $null = New-Installer $script:GameB 'setup_second_game_2.0.exe' 4

    $bmp = New-Object System.Drawing.Bitmap(1280,720)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(20,30,45)); $g.Dispose()
    $script:Art = Join-Path $script:Sandbox 'art.png'
    $bmp.Save($script:Art, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

    # A built disc to reopen, so the load path can be tested without needing the
    # folder tree.
    $script:ProjOut = Join-Path $script:Sandbox 'built'
    New-Item -ItemType Directory -Force -Path $script:ProjOut | Out-Null
    $gameEntry = Get-GameInfo $script:GameA
    $addEntry  = Get-AddOnInfo $script:PatchOne
    $addEntry.ParentIndex = 0
    $null = Invoke-Build @{
        Games=@($gameEntry,$addEntry); Label='UI Fixture'; IconPath=$script:Art; IconIsIco=$false
        Menu=$true; BgPath=$script:Art; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); OutDir=$script:ProjOut
    } { param($m) $null = $m }

    # A second project, sized so that one CD-R cannot hold it. Sparse again, so a
    # gigabyte of "game" costs nothing and takes no time. Saved as a project file
    # rather than built: Open existing disc reads the JSON, and writing a real
    # 1 GB ISO to prove a dropdown is wired would be a poor trade.
    $script:BigA = Join-Path $script:SrcRoot 'ccc_big_one'
    $script:BigB = Join-Path $script:SrcRoot 'ddd_big_two'
    $null = New-Installer $script:BigA 'setup_big_one_1.0.exe' 500
    $null = New-Installer $script:BigB 'setup_big_two_1.0.exe' 500
    $script:BigOut = Join-Path $script:Sandbox 'bigproj'
    New-Item -ItemType Directory -Force -Path $script:BigOut | Out-Null
    Save-Project @{
        Games=@((Get-GameInfo $script:BigA),(Get-GameInfo $script:BigB)); Label='Big Set'
        IconPath=$script:Art; IconIsIco=$false
        Menu=$true; BgPath=$script:Art; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); MediaKey=''; OutDir=$script:BigOut
    } $script:BigOut

    $script:App = $null
}

AfterAll {
    if ($script:App) { Stop-DiscWright $script:App }
    if ($script:Sandbox -and (Test-Path $script:Sandbox)) {
        Remove-Item $script:Sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'The window as it opens' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'reports its version in the title bar' {
        # A screenshot of a bug report should say what produced it without asking.
        $script:Win.Current.Name | Should -Match '^DiscWright \d+\.\d+\.\d+'
    }

    It 'fits on the screen it opened on' {
        $r = $script:Win.Current.BoundingRectangle
        $usable = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Height
        $r.Height | Should -BeLessOrEqual $usable
    }

    It 'has no two controls sitting on top of each other' {
        # The form is laid out in absolute pixels, so tightening one row can put
        # a control through its neighbour and nothing complains - a preview box
        # was moved onto its own Browse button exactly that way. Rectangles are
        # what UI Automation reports accurately, so they are what gets checked.
        $kids = $script:Win.FindAll([System.Windows.Automation.TreeScope]::Children,
                                    [System.Windows.Automation.Condition]::TrueCondition)
        $boxes = @()
        for ($i = 0; $i -lt $kids.Count; $i++) {
            try {
                $c = $kids.Item($i); $r = $c.Current.BoundingRectangle
                # A tooltip is a floating window that appears over whatever the
                # pointer is resting on, and covering things is its entire job.
                # It shows up as a child in the automation tree all the same, so
                # if the mouse happens to be over BUILD ISO when this runs it
                # reports a collision with the log box that is not a fault.
                if ($c.Current.ClassName -match 'tooltips_class') { continue }
                if ($r.Width -gt 0 -and $r.Height -gt 0) {
                    $boxes += [pscustomobject]@{ Name = $c.Current.Name; R = $r }
                }
            } catch {}
        }
        $boxes.Count | Should -BeGreaterThan 10 -Because 'the window should have plenty of controls'

        $clashes = @()
        for ($i = 0; $i -lt $boxes.Count; $i++) {
            for ($j = $i + 1; $j -lt $boxes.Count; $j++) {
                $a = $boxes[$i].R; $b = $boxes[$j].R
                # A group box legitimately contains other things; only siblings
                # that merely collide are a fault, so containment is allowed.
                $overlapW = [Math]::Min($a.Right, $b.Right) - [Math]::Max($a.Left, $b.Left)
                $overlapH = [Math]::Min($a.Bottom, $b.Bottom) - [Math]::Max($a.Top, $b.Top)
                if ($overlapW -le 2 -or $overlapH -le 2) { continue }
                $aInB = ($a.Left -ge $b.Left - 1 -and $a.Right -le $b.Right + 1 -and
                         $a.Top -ge $b.Top - 1 -and $a.Bottom -le $b.Bottom + 1)
                $bInA = ($b.Left -ge $a.Left - 1 -and $b.Right -le $a.Right + 1 -and
                         $b.Top -ge $a.Top - 1 -and $b.Bottom -le $a.Bottom + 1)
                if ($aInB -or $bInA) { continue }
                $clashes += "'$($boxes[$i].Name)' and '$($boxes[$j].Name)' overlap by ${overlapW}x${overlapH}px"
            }
        }
        $clashes.Count | Should -Be 0 -Because ($clashes -join '; ')
    }

    It 'leaves <_> greyed until there is something for it to act on' -ForEach @(
        'Add-on*', 'Change*', 'Remove', 'Show disc folder', 'Preview menu', 'New disc'
    ) {
        Test-CtlEnabled $script:Win $_ | Should -BeFalse
    }

    It 'leaves Add game... available, since it is the only way to start' {
        Test-CtlEnabled $script:Win 'Add game*' | Should -BeTrue
    }
}

Describe 'Opening a disc that was already built' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        # Seeded from the box, so the wanted folder is already selected.
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        $script:Status = Get-StatusText $script:Win
        Save-WindowShot $script:Win (Join-Path $script:ShotDir 'opened-project.png')
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'loads every installer the project recorded' {
        Get-EntryCount $script:Win | Should -Be 2
    }

    It 'counts games and add-ons apart rather than calling them all games' {
        # A disc of one game and its patch announced "2 games", which describes a
        # disc that is not the one about to be built.
        $script:Status | Should -Match '1 game \+ 1 add-on'
    }

    It 'works out the media from the total' {
        $script:Status | Should -Match 'Disc: '
    }

    It 'wakes the buttons that need an entry to exist' {
        Test-CtlEnabled $script:Win 'Add-on*'          | Should -BeTrue
        Test-CtlEnabled $script:Win 'Show disc folder' | Should -BeTrue
        Test-CtlEnabled $script:Win 'Preview menu'     | Should -BeTrue
    }

    It 'keeps Change... and Remove greyed while no row is selected' {
        Test-CtlEnabled $script:Win 'Change*' | Should -BeFalse
        Test-CtlEnabled $script:Win 'Remove'  | Should -BeFalse
    }

    It 'wakes Change... and Remove when a row is selected' {
        Select-ListRow -Win $script:Win -Index 0
        Test-CtlEnabled $script:Win 'Change*' | Should -BeTrue
        Test-CtlEnabled $script:Win 'Remove'  | Should -BeTrue
    }
}

Describe 'Starting a new disc' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # Loaded rather than typed in, so every field this has to clear is genuinely
    # populated - list, label, icon, background, output folder - and the test is
    # about the reset rather than about how the form got filled.
    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'wakes New disc once there is something to clear' {
        Test-CtlEnabled $script:Win 'New disc' | Should -BeTrue
    }

    It 'asks before discarding, and says nothing on disk is touched' {
        Invoke-CtlNamed $script:Win 'New disc' | Out-Null
        # Dismissed with No: the confirmation has to be a real gate, not a
        # formality that clears the form whichever button is pressed.
        $script:Asked = Read-MessageBox -Win $script:Win -TitleLike 'New disc' -Button 'No'
        $script:Asked | Should -Match 'Nothing on disk is touched'
        Get-EntryCount $script:Win | Should -BeGreaterThan 0
    }

    It 'clears the installer list when confirmed' {
        Invoke-CtlNamed $script:Win 'New disc' | Out-Null
        Read-MessageBox -Win $script:Win -TitleLike 'New disc' -Button 'Yes' | Out-Null
        Start-Sleep -Seconds 1
        Get-EntryCount $script:Win | Should -Be 0
    }

    It 'clears the disc label and the icon' {
        (Get-BoxAfter $script:Win '2)  Disc label*').Current.Name | Should -BeNullOrEmpty
        (Get-BoxAfter $script:Win '3)  Disc icon*').Current.Name  | Should -BeNullOrEmpty
    }

    It 'keeps the output folder, which is the one field you would retype' {
        (Get-BoxAfter $script:Win '6)  Output folder*').Current.Name | Should -Be $script:ProjOut
    }

    It 'greys itself out again, having nothing left to clear' {
        # The output folder survives on purpose, so it must not count as dirty -
        # otherwise this stays lit and a second click does nothing visible.
        Test-CtlEnabled $script:Win 'New disc' | Should -BeFalse
    }

    It 'greys the buttons that need an entry, and leaves Add game available' {
        Test-CtlEnabled $script:Win 'Add-on*'      | Should -BeFalse
        Test-CtlEnabled $script:Win 'Preview menu' | Should -BeFalse
        Test-CtlEnabled $script:Win 'Add game*'    | Should -BeTrue
    }

    It 'ends up looking like a window that just opened' {
        Save-WindowShot $script:Win (Join-Path $script:ShotDir 'after-new-disc.png')
        # The status line under the list is blank on a fresh window. Leaving the
        # previous disc's "2 games + 1 add-on" sitting under an empty list is
        # exactly the kind of stale text that makes a reset look like a failure.
        Get-StatusText $script:Win | Should -BeNullOrEmpty
    }
}

Describe 'Adding add-ons through the file dialog' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'starts from the two the project already had' {
        Get-EntryCount $script:Win | Should -Be 2
    }

    It 'adds an installer that is not named setup_*' {
        # The whole reason the filter was relaxed: a GOG patch is patch_*.exe and
        # a mod is named whatever its author chose.
        Invoke-CtlNamed $script:Win 'Add-on*' | Out-Null
        Complete-FileDialog -Win $script:Win -TitleLike 'Pick one or more add-on*' -Files @($script:PatchTwo) | Out-Null
        Start-Sleep -Seconds 2
        Get-EntryCount $script:Win | Should -Be 3
        Save-WindowShot $script:Win (Join-Path $script:ShotDir 'added-addon.png')
    }

    It 'files it under the game rather than beside it' {
        (Get-StatusText $script:Win) | Should -Match '1 game \+ 2 add-ons'
    }

    It 'leaves the disc unchanged when the dialog is cancelled' {
        # Before the duplicate test, deliberately. That one replaces the status
        # line with a warning, and the count can only be read while the line is
        # still reporting a count.
        Invoke-CtlNamed $script:Win 'Add-on*' | Out-Null
        Complete-FileDialog -Win $script:Win -TitleLike 'Pick one or more add-on*' -Cancel | Out-Null
        Start-Sleep -Seconds 1
        Get-EntryCount $script:Win | Should -Be 3
    }

    It 'refuses the same installer twice, and says so' {
        Invoke-CtlNamed $script:Win 'Add-on*' | Out-Null
        Complete-FileDialog -Win $script:Win -TitleLike 'Pick one or more add-on*' -Files @($script:PatchTwo) | Out-Null
        Start-Sleep -Seconds 2
        (Get-StatusText $script:Win) | Should -Match 'already on this disc'
    }
}

Describe 'Turning an entry into an add-on through the Change dialog' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        # Two plain games, so one of them can be made an add-on of the other.
        $script:TwoOut = Join-Path $script:Sandbox 'two-games'
        New-Item -ItemType Directory -Force -Path $script:TwoOut | Out-Null
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        # The project holds a game and an add-on; add the second game's installer
        # as another entry so there is a second parent to choose between.
        Invoke-CtlNamed $script:Win 'Add-on*' | Out-Null
        Complete-FileDialog -Win $script:Win -TitleLike 'Pick one or more add-on*' `
            -Files @((Join-Path $script:GameB 'setup_second_game_2.0.exe')) | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'opens on the entry that is selected' {
        Select-ListRow -Win $script:Win -Index 2
        Invoke-CtlNamed $script:Win 'Change*' | Out-Null
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 8
        $dlg | Should -Not -BeNullOrEmpty
        Save-WindowShot $script:Win (Join-Path $script:ShotDir 'change-dialog.png')
    }

    It 'greys the parent list the moment "a game of its own" is chosen' {
        # Tested as behaviour rather than as a starting state, because the
        # starting state depends on what was selected: the dialog opens with the
        # parent list live for an entry that is already an add-on, and dead for
        # one that is not. What must always hold is that choosing "a game of its
        # own" kills it - an add-on of nothing is not something this dialog is
        # allowed to produce.
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        $dlg | Should -Not -BeNullOrEmpty

        # The parent list is found by position, not by name. A combo box reports
        # its SELECTED ITEM as its accessible name - "alpha", not "" - so there
        # is no fixed string to look for. It is the wide control on the row
        # directly under the "an add-on" radio.
        function Get-ParentList($d) {
            $rb = (Find-Ctl $d 'An add-on*' 5).Current.BoundingRectangle
            $all = $d.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                              [System.Windows.Automation.Condition]::TrueCondition)
            for ($i = 0; $i -lt $all.Count; $i++) {
                try {
                    $r = $all.Item($i).Current.BoundingRectangle
                    if ($r.Width -gt 300 -and $r.Height -lt 30 -and
                        $r.Y -gt ($rb.Y + $rb.Height - 6) -and
                        $r.Y -lt ($rb.Y + $rb.Height + 20)) { return $all.Item($i) }
                } catch {}
            }
            return $null
        }
        Set-Alias Get-UnnamedCombo Get-ParentList

        # This entry arrived as an add-on, so the list starts live.
        (Get-UnnamedCombo $dlg).Current.IsEnabled | Should -BeTrue

        Invoke-Ctl -Ctl (Find-Ctl $dlg 'A game of its own' 5) -SettleMs 500
        (Get-UnnamedCombo $dlg).Current.IsEnabled | Should -BeFalse

        Invoke-Ctl -Ctl (Find-Ctl $dlg 'An add-on*' 5) -SettleMs 500
        (Get-UnnamedCombo $dlg).Current.IsEnabled | Should -BeTrue
    }

    It 'renames the entry and closes' {
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        # The name box sits directly after its label in the dialog's child order.
        $kids = $dlg.FindAll([System.Windows.Automation.TreeScope]::Children,
                             [System.Windows.Automation.Condition]::TrueCondition)
        $box = $null
        for ($i = 0; $i -lt $kids.Count; $i++) {
            try { if ($kids.Item($i).Current.Name -like 'Name on the menu*') { $box = $kids.Item($i + 1); break } } catch {}
        }
        $box | Should -Not -BeNullOrEmpty
        Set-CtlText -Ctl $box -Text 'Renamed By Test'
        Invoke-Ctl -Ctl (Find-Ctl $dlg 'OK' 5) -SettleMs 1200
        (Find-Ctl $script:Win 'Entry on the disc' 2) | Should -BeNullOrEmpty -Because 'OK closes it'
    }

    It 'leaves the disc with the same number of entries' {
        # Renaming is not adding or removing.
        Get-EntryCount $script:Win | Should -Be 3
    }
}

Describe 'Removing an entry' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        Invoke-CtlNamed $script:Win 'Add-on*' | Out-Null
        Complete-FileDialog -Win $script:Win -TitleLike 'Pick one or more add-on*' -Files @($script:PatchTwo) | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'takes out one entry, not several' {
        # The regression this suite was written for: the handler wrapped the
        # function's comma-return in @(), so one Remove collapsed every survivor
        # into a single row.
        Get-EntryCount $script:Win | Should -Be 3
        Select-ListRow -Win $script:Win -Index 1
        Invoke-CtlNamed $script:Win 'Remove' | Out-Null
        Start-Sleep -Seconds 1
        Get-EntryCount $script:Win | Should -Be 2
    }

    It 'promotes orphaned add-ons instead of deleting them with their game' {
        # Removing the game leaves its add-ons on the disc as games of their own.
        # Silently discarding an installer the user chose is the worse outcome.
        Select-ListRow -Win $script:Win -Index 0
        Invoke-CtlNamed $script:Win 'Remove' | Out-Null
        Start-Sleep -Seconds 1
        Get-EntryCount $script:Win | Should -Be 1
        (Get-StatusText $script:Win) | Should -Not -Match 'add-on'
    }

    It 'clears the status line when the last entry goes' {
        # Update-MediaLabel returned early on an empty list without touching the
        # label, so the previous disc's summary stayed under an empty list - still
        # green, still naming a size and a disc type that nothing on the form
        # accounted for any more. New disc always cleared it; Remove never did.
        #
        # Removes until the list is empty rather than assuming a count. The tests
        # above it in this block leave a different number behind than a filtered
        # run does, and a test that only passes in sequence is a test that lies
        # the first time somebody runs it on its own.
        (Get-StatusText $script:Win) | Should -Not -BeNullOrEmpty -Because 'entries are still on the disc'
        Clear-AllEntries -Win $script:Win
        Get-EntryCount $script:Win | Should -Be 0
        Get-StatusText $script:Win | Should -BeNullOrEmpty
    }

    It 'keeps a disc label that came out of the project file' {
        # The label is only taken back when DiscWright typed it itself. This one
        # was loaded from a project, so emptying the list must leave it alone.
        (Get-BoxAfter $script:Win '2)  Disc label*').Current.Name | Should -Be 'UI Fixture'
    }
}

Describe 'The label DiscWright typed itself' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # Adding a game to an empty form seeds the disc label from its name. Removing
    # that game used to leave the name behind - and because seeding only fires
    # into an EMPTY box, the next game added never replaced it. Swapping the game
    # out therefore built a disc carrying the previous game's name in This PC.
    #
    # Driven through Open existing disc rather than Add game, because the folder
    # tree is invisible to UI Automation and Add game is now aimed at wherever a
    # game was last picked from - which is nowhere, on a freshly started app.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'starts from the project label, not from a game name' {
        (Get-BoxAfter $script:Win '2)  Disc label*').Current.Name | Should -Be 'UI Fixture'
    }

    It 'survives every entry being removed, because the user owns it' {
        Clear-AllEntries -Win $script:Win
        Get-EntryCount $script:Win | Should -Be 0
        (Get-BoxAfter $script:Win '2)  Disc label*').Current.Name | Should -Be 'UI Fixture'
    }

    It 'and the status line is gone even though the label stayed' {
        Get-StatusText $script:Win | Should -BeNullOrEmpty
    }
}

Describe 'What the build refuses, and whether it says why' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # BUILD ISO stays clickable and checks its requirements when pressed, so
    # every one of these refusals is a dialog a real user will meet. A refusal
    # that does not say which step is missing is a refusal that gets reported as
    # "it does not work".

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'refuses an empty disc, and names step 1' {
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match 'step 1'
        $msg | Should -Match 'GOG'
    }

    It 'renames the build button once there is a disc to overwrite' {
        # BUILD ISO becomes REBUILD ISO, which is the only warning before an
        # existing disc folder is wiped and written again.
        Test-CtlEnabled $script:Win 'BUILD ISO' | Should -Not -BeNullOrEmpty
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        Test-CtlEnabled $script:Win 'REBUILD ISO' | Should -BeTrue
    }

    It 'refuses a disc with no label, and names step 2' {
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '2)  Disc label*') -Text ''
        Send-Keys '{BACKSPACE}'
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match 'step 2'
    }

    It 'explains what the label is for, not just that it is missing' {
        # The wording is the whole value of the dialog: it has to tell somebody
        # who has never seen the app what to type.
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match 'This PC'
    }

    It 'refuses a disc with no output folder, and names step 6' {
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '2)  Disc label*') -Text 'Something'
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text ''
        Send-Keys '{BACKSPACE}'
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match 'step 6'
    }

    It 'leaves the disc untouched after a refusal' {
        # A refused build must not have half-written anything.
        Get-EntryCount $script:Win | Should -Be 2
    }
}

Describe 'Choosing the disc you are going to burn' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:BigOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'opens on the setting that behaves the way it always has' {
        Get-MediaTargetText $script:Win | Should -BeLike 'Fit on one disc*'
    }

    It 'recommends a size until it is told which disc you own' {
        # The old line, unchanged: a size DiscWright picked, not a plan.
        Get-StatusText $script:Win | Should -Match 'Disc: '
    }

    It 'turns the advice line into a plan once a disc is chosen' {
        # Index 1 is CD-R, the first real medium. A gigabyte of games does not
        # fit one, so this is a genuine set and not a relabelled single disc.
        Set-MediaTarget -Win $script:Win -Index 1
        Get-MediaTargetText $script:Win | Should -Be 'CD-R 700 MB'
        $s = Get-StatusText $script:Win
        $s | Should -Match 'discs of CD-R 700 MB'
        $s | Should -Not -Match 'Disc: '
    }

    It 'needs fewer discs when a bigger one is chosen' {
        # Index 2 is DVD5. The same games now fit on one, and a single disc is
        # not called a set.
        Set-MediaTarget -Win $script:Win -Index 2
        Get-MediaTargetText $script:Win | Should -Be 'DVD5 4.7 GB'
        Get-StatusText $script:Win | Should -Match '1 disc, DVD5 4\.7 GB'
    }

    It 'goes back to recommending when the automatic setting is chosen again' {
        Set-MediaTarget -Win $script:Win -Index 0
        Get-MediaTargetText $script:Win | Should -BeLike 'Fit on one disc*'
        Get-StatusText $script:Win | Should -Match 'Disc: '
    }

    It 'is put back to the automatic setting by New disc' {
        Set-MediaTarget -Win $script:Win -Index 3
        Get-MediaTargetText $script:Win | Should -Be 'DVD9 8.5 GB (dual layer)'
        Invoke-CtlNamed $script:Win 'New disc' | Out-Null
        Read-MessageBox -Win $script:Win -TitleLike 'New disc' -Button 'Yes' | Out-Null
        Start-Sleep -Seconds 1
        Get-MediaTargetText $script:Win | Should -BeLike 'Fit on one disc*'
    }
}
