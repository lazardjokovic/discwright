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
    # One game that no single CD could hold, for the disc-set refusals. 800 MB
    # in one file: a set cannot help, because the file itself is larger than the
    # disc, and the refusal has to name it rather than talk about totals.
    $script:SoloBig = Join-Path $script:SrcRoot 'eee_big_solo'
    $null = New-Installer $script:SoloBig 'setup_big_solo_1.0.exe' 800
    $script:SoloOut = Join-Path $script:Sandbox 'soloproj'
    New-Item -ItemType Directory -Force -Path $script:SoloOut | Out-Null
    Save-Project @{
        Games=@((Get-GameInfo $script:SoloBig)); Label='Big Solo'
        IconPath=$script:Art; IconIsIco=$false
        Menu=$true; BgPath=$script:Art; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Install','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); MediaKey='CD'; OutDir=$script:SoloOut
    } $script:SoloOut

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

    # A project with a game and no artwork at all, which is what somebody who
    # never browsed for an icon or a background has. Saved rather than built,
    # because the point is what the window does on load, not what the ISO holds.
    $script:NoArtOut = Join-Path $script:Sandbox 'noart'
    New-Item -ItemType Directory -Force -Path $script:NoArtOut | Out-Null
    Save-Project @{
        Games=@((Get-GameInfo $script:GameA)); Label='No Art'
        IconPath=$null; IconIsIco=$false
        Menu=$true; BgPath=$null; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); MediaKey=''; OutDir=$script:NoArtOut
    } $script:NoArtOut

    # The same project again, but saved with a target disc on it. Reopening this
    # one has to bring the dropdown back with it.
    $script:BigOutSet = Join-Path $script:Sandbox 'bigproj-set'
    New-Item -ItemType Directory -Force -Path $script:BigOutSet | Out-Null
    Save-Project @{
        Games=@((Get-GameInfo $script:BigA),(Get-GameInfo $script:BigB)); Label='Big Set'
        IconPath=$script:Art; IconIsIco=$false
        Menu=$true; BgPath=$script:Art; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); MediaKey='CD'; OutDir=$script:BigOutSet
    } $script:BigOutSet
    # Stand-ins for a set that has already been built into this folder. Sparse, so
    # two "ISOs" cost nothing - only their names matter to the button.
    foreach ($n in 'Big Set.iso') {
        $fs = [IO.File]::Create((Join-Path $script:BigOutSet $n)); $fs.SetLength(1024); $fs.Close()
    }

    # Extras button on, no folder chosen yet: the state you are in the moment
    # before browsing for one, which is where the greying bug lived.
    $script:BigOutEx = Join-Path $script:Sandbox 'bigproj-ex'
    New-Item -ItemType Directory -Force -Path $script:BigOutEx | Out-Null
    Save-Project @{
        Games=@((Get-GameInfo $script:BigA),(Get-GameInfo $script:BigB)); Label='Needs Extras'
        IconPath=$script:Art; IconIsIco=$false
        Menu=$true; BgPath=$script:Art; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Play','Install','Extras','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); MediaKey='CD'; ExtrasEveryDisc=$false; OutDir=$script:BigOutEx
    } $script:BigOutEx

    # A project whose disc label carries the characters that used to be eaten by
    # the ASCII file the menu is written to. Saved rather than typed: SendKeys is
    # not a reliable way to enter a trademark sign, and the point of the fixture
    # is the menu, not the typing.
    $script:FancyLabel = 'Star Wars' + [char]0x2122 + ' Empire at War' + [char]0x00AE
    $script:FancyOut = Join-Path $script:Sandbox 'fancyproj'
    New-Item -ItemType Directory -Force -Path $script:FancyOut | Out-Null
    Save-Project @{
        Games=@((Get-GameInfo $script:GameA)); Label=$script:FancyLabel
        IconPath=$script:Art; IconIsIco=$false
        Menu=$true; BgPath=$script:Art; BgAsIs=$false; PanelSide='Right'
        Divider=$false; ShowTitle=$false; TitleText=''
        WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
        Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
        ExtraItems=@(); MediaKey=''; OutDir=$script:FancyOut
    } $script:FancyOut

    # Somewhere for the one build these tests actually run to land. Empty, so the
    # button reads BUILD ISO rather than REBUILD ISO and nothing is overwritten.
    $script:LockOut = Join-Path $script:Sandbox 'lockbuild'
    New-Item -ItemType Directory -Force -Path $script:LockOut | Out-Null

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
        # If the mouse happens to rest over BUILD ISO while this runs, the
        # tooltip is a child of the window like anything else - the helper skips
        # it, because covering what the pointer is on is its entire job.
        $lay = Get-CtlOverlaps $script:Win
        $lay.Count | Should -BeGreaterThan 10 -Because 'the window should have plenty of controls'
        $lay.Clashes.Count | Should -Be 0 -Because ($lay.Clashes -join '; ')
    }

    It 'shows every button the script creates' {
        # A control placed exactly on top of another is not reported as a clash,
        # because UI Automation hands back the one in front and the covered one
        # simply is not there. That is what a new button dropped onto Preview
        # menu looked like: ten failures elsewhere and nothing saying why.
        # So the window is checked against the script rather than against taste.
        $src = Get-Content -Raw -LiteralPath $script:AppPath
        $names = @([regex]::Matches($src, "AddBtn\s+'([^']+)'") |
                   ForEach-Object { $_.Groups[1].Value } |
                   Where-Object { $_ -ne 'Browse...' } | Sort-Object -Unique)
        $names.Count | Should -BeGreaterThan 5 -Because 'the window has buttons'
        $missing = @()
        foreach ($n in $names) {
            if (-not (Find-Ctl $script:Win $n)) { $missing += $n }
        }
        $missing.Count | Should -Be 0 -Because "hidden or missing: $($missing -join ', ')"
    }

    It 'shows every checkbox the script creates' {
        # The same guard as the buttons above, for the other control the window
        # keeps gaining. Two checkboxes were added in one week, "checksummed"
        # and "Game name", and nothing here would have noticed if either had
        # landed underneath something else: the button test does not look at
        # checkboxes, and the overlap test cannot see a control hidden exactly
        # behind another. Written so the next one is covered on the day it is
        # added rather than the day somebody notices it missing.
        $src = Get-Content -Raw -LiteralPath $script:AppPath
        # Declared as a checkbox, given a caption, and added to the window or to
        # one of its group boxes. A checkbox inside a dialog is not on this form
        # and is not what this is about.
        $vars = @([regex]::Matches($src, '\$(\w+)\s*=\s*New-Object System\.Windows\.Forms\.CheckBox') |
                  ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $wanted = @()
        foreach ($v in $vars) {
            if ($src -notmatch ('\$' + [regex]::Escape($v) + '\)')) { continue }
            $onForm = [regex]::IsMatch($src, '\$(?:form|grp|grpX)\.Controls\.Add\(\$' + [regex]::Escape($v) + '\)')
            if (-not $onForm) { continue }
            $t = [regex]::Match($src, '\$' + [regex]::Escape($v) + "\.Text\s*=\s*'([^']+)'")
            if ($t.Success) { $wanted += $t.Groups[1].Value }
        }
        $wanted = @($wanted | Sort-Object -Unique)
        $wanted.Count | Should -BeGreaterThan 8 -Because 'the window has a good many checkboxes'

        $missing = @()
        foreach ($n in $wanted) {
            if (-not (Find-Ctl $script:Win $n)) { $missing += $n }
        }
        $missing.Count | Should -Be 0 -Because "hidden or missing: $($missing -join ', ')"
    }

    It 'leaves <_> greyed until there is something for it to act on' -ForEach @(
        'Add-on*', 'Change*', 'Remove', 'Show disc folder', 'Preview menu', 'New disc',
        'Print artwork*', 'Burn to disc*'
    ) {
        Test-CtlEnabled $script:Win $_ | Should -BeFalse
    }

    It 'leaves Add game... available, since it is the only way to start' {
        Test-CtlEnabled $script:Win 'Add game*' | Should -BeTrue
    }
}

Describe 'Walking the folder dialog with the keyboard' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # Both of these fail without Set-FolderTreeFocus and Find-BoxRowButton, and
    # neither gap was noticed, because every other caller in this suite picks the
    # folder the app had already seeded and so only ever needed OK.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        # Named so that the shell's own sort is not in question: one, three, two.
        $script:TreeRoot = Join-Path $script:Sandbox 'tree'
        foreach ($n in 'one', 'three', 'two') {
            New-Item -ItemType Directory -Force -Path (Join-Path $script:TreeRoot $n) | Out-Null
        }
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'finds the Browse button of a step whose label is on a line of its own' {
        # Step 6 puts its label on one line and the box with its Browse on the
        # next, so the label's row holds no button at all.
        Find-RowButton -Win $script:Win -LabelLike '6)  Output folder*' | Should -BeNullOrEmpty
        Find-BoxRowButton -Win $script:Win -LabelLike '6)  Output folder*' | Should -Not -BeNullOrEmpty
    }

    It 'opens the seeded folder and steps down to the child it was asked for' {
        # Only true if the tree was given the keyboard first: the dialog opens
        # with the focus on OK, where arrow keys do nothing.
        $box = Get-BoxAfter -Win $script:Win -LabelLike '6)  Output folder*'
        Set-CtlText -Ctl $box -Text $script:TreeRoot
        Invoke-Ctl -Ctl (Find-BoxRowButton -Win $script:Win -LabelLike '6)  Output folder*') -SettleMs 1200
        Complete-FolderDialog -Win $script:Win -Expand 1 -Down 2 | Out-Null
        Start-Sleep -Milliseconds 600
        $box.Current.Name | Should -Be (Join-Path $script:TreeRoot 'three')
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
        # Artwork comes from the plan rather than from the ISO, so it is
        # available as soon as there is a game and somewhere to write to,
        # build or no build.
        Test-CtlEnabled $script:Win 'Print artwork*'   | Should -BeTrue
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

    It 'offers an add-on its detected name back, which came off the filename' {
        # Every GOG patch reports the ProductName of the game it patches - all
        # four Hollow Knight patches call themselves "Hollow Knight" - so an
        # add-on is named from its filename instead. The reset has to go back to
        # that, not to the installer's own idea of what it is called.
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        $was = (Get-AddOnInfo (Join-Path $script:GameB 'setup_second_game_2.0.exe')).GameName
        (Get-NameBox $dlg).Current.Name | Should -Be $was
        (Find-Ctl $dlg 'Use the detected name' 5).Current.IsEnabled | Should -BeFalse

        Set-CtlText -Ctl (Get-NameBox $dlg) -Text 'Not What It Was Called'
        (Find-Ctl $dlg 'Use the detected name' 5).Current.IsEnabled | Should -BeTrue
        Invoke-Ctl -Ctl (Find-Ctl $dlg 'Use the detected name' 5) -SettleMs 600
        (Get-NameBox $dlg).Current.Name | Should -Be $was
    }

    It 'renames the entry and closes' {
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        $box = Get-NameBox $dlg
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

Describe 'Renaming the only game on a disc' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # The one-game disc is the case the README leads with, and it was the one
    # case where the name could not be reached: the dialog holding it is opened
    # by Change..., and Change... wanted a second entry before it would light up.
    # This project has a single game in it.
    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:FancyOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        # What the installer reported, which is what the reset goes back to.
        $script:Detected = (Get-GameInfo $script:GameA).GameName
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'holds one entry, which is the case that used to lock the dialog' {
        Get-EntryCount $script:Win | Should -Be 1
    }

    It 'wakes Change... once that single row is selected' {
        Select-ListRow -Win $script:Win -Index 0
        Test-CtlEnabled $script:Win 'Change*' | Should -BeTrue
    }

    It 'opens, and greys the add-on choice for want of a game to attach to' {
        # The half of the old rule that was right, enforced where it belongs:
        # inside the dialog, on the one control it applies to.
        Invoke-CtlNamed $script:Win 'Change*' | Out-Null
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 8
        $dlg | Should -Not -BeNullOrEmpty
        (Find-Ctl $dlg 'An add-on*' 5).Current.IsEnabled | Should -BeFalse
        Save-WindowShot $script:Win (Join-Path $script:ShotDir 'change-dialog-one-game.png')
    }

    It 'has no two controls sitting on top of each other either' {
        # Making room for the reset button moved every row below it down by hand,
        # in a dialog laid out in the same absolute pixels as the form - which is
        # how a preview box once ended up on top of its own Browse button.
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        $lay = Get-CtlOverlaps $dlg
        $lay.Count | Should -BeGreaterThan 8 -Because 'the dialog should have its controls'
        $lay.Clashes.Count | Should -Be 0 -Because ($lay.Clashes -join '; ')
    }

    It 'starts with the reset greyed, the name being the detected one already' {
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        (Get-NameBox $dlg).Current.Name | Should -Be $script:Detected
        (Find-Ctl $dlg 'Use the detected name' 5).Current.IsEnabled | Should -BeFalse
    }

    It 'wakes the reset as soon as the name is something else' {
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        Set-CtlText -Ctl (Get-NameBox $dlg) -Text 'Something Else Entirely'
        (Find-Ctl $dlg 'Use the detected name' 5).Current.IsEnabled | Should -BeTrue
    }

    It 'puts the name back to what the installer reported' {
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        Invoke-Ctl -Ctl (Find-Ctl $dlg 'Use the detected name' 5) -SettleMs 600
        (Get-NameBox $dlg).Current.Name | Should -Be $script:Detected
        # And goes dead again, having nothing left to undo.
        (Find-Ctl $dlg 'Use the detected name' 5).Current.IsEnabled | Should -BeFalse
    }

    It 'keeps the restored name when the dialog is accepted' {
        # Restoring it into the box proves nothing on its own: OK is what writes
        # it back to the entry, and a reset that did not survive OK would look
        # identical up to this point. So it goes through OK and the dialog is
        # reopened to read what was actually kept.
        $dlg = Find-Ctl $script:Win 'Entry on the disc' 5
        Invoke-Ctl -Ctl (Find-Ctl $dlg 'OK' 5) -SettleMs 1200
        (Find-Ctl $script:Win 'Entry on the disc' 2) | Should -BeNullOrEmpty -Because 'OK closes it'

        Invoke-CtlNamed $script:Win 'Change*' | Out-Null
        $again = Find-Ctl $script:Win 'Entry on the disc' 8
        (Get-NameBox $again).Current.Name | Should -Be $script:Detected
        Invoke-Ctl -Ctl (Find-Ctl $again 'Cancel' 5) -SettleMs 1000
    }

    It 'has added and removed nothing along the way' {
        Get-EntryCount $script:Win | Should -Be 1
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

    It 'asks what to do with the add-ons before removing their game' {
        # Removing a game used to promote its add-ons to games of their own,
        # silently. On the disc that means a patch listed in the menu as a game,
        # whose Install runs it against a game that is not there. Deleting them
        # silently is no better, so the button asks - and the dialog names them,
        # because "2 add-ons" is not enough to decide on.
        Select-ListRow -Win $script:Win -Index 0
        Invoke-CtlNamed $script:Win 'Remove' | Out-Null
        # Answers No, which is the old behaviour made explicit: the add-on stays,
        # as a game of its own. Yes is covered by the unit tests and by
        # Clear-AllEntries, which uses it on every run. Answering Yes here would
        # empty the list and pull the ground out from under the test below.
        $txt = Read-MessageBox -Win $script:Win -TitleLike 'Remove add-ons too?' -Button 'No'
        $txt | Should -Not -BeNullOrEmpty -Because 'removing a game with add-ons must ask first'
        $txt | Should -Match 'add-on'
        Start-Sleep -Seconds 1
        Get-EntryCount $script:Win | Should -Be 1 -Because 'No keeps the add-on as an entry of its own'
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



Describe 'Asking for a disc set from the window' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # This Describe exists because of a bug no logic test could have found.
    #
    # BUILD ISO refuses a payload that will not fit the chosen disc, and it does
    # so before the build starts. That payload is exactly the payload a disc set
    # is for, so with the box ticked the window could not build a set at all:
    # the tick was wired through saving, loading and the build itself, and a
    # guard three screens earlier said no.
    #
    # Every logic test called Invoke-BuildDiscSet directly, so all of them
    # passed. Driving the real window to record a demonstration found it on the
    # first take, which is the argument for this file existing.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window

        # One game of 800 MB in a single file, pointed at a CD. A set cannot
        # help with this one, which is what makes it useful here: the refusal
        # has to come from the set arithmetic and name the file.
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:SoloOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        if ((Get-EntryCount $script:Win) -ne 1) { throw 'the solo project did not load' }
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'reads the target disc back off the project' {
        Get-MediaTargetText $script:Win | Should -Match 'CD-R'
    }

    It 'refuses a payload past the disc when no set was asked for' {
        # Unchanged behaviour, pinned here so the change below cannot quietly
        # turn this refusal off for everybody.
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match 'over'
        $msg | Should -Match 'Remove an entry in step 1'
    }

    It 'lets the set arithmetic answer instead, once a set is asked for' {
        # The bug. With the box ticked the old refusal must stand aside, and the
        # set's own refusal must reach the person: by name, with the room a disc
        # of that size actually leaves.
        Invoke-CtlNamed $script:Win 'disc set' | Out-Null
        Start-Sleep -Milliseconds 500
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Not -Match 'Remove an entry in step 1'
        $msg | Should -Match 'setup_big_solo'
        $msg | Should -Match 'larger disc'
    }

    It 'says which file decided it, not just that it does not fit' {
        # Same dialog, read again for the part that makes it actionable. A size
        # on its own leaves somebody to work out which file to go and look at.
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match '0\.78 GB'
        $msg | Should -Match '0\.6[0-9] GB'
    }

    It 'still refuses several games across a set' {
        # The one decision a set will not make for anybody. Two games, both of
        # which fit a CD on their own, which is why only the set rule can
        # refuse this.
        Invoke-CtlNamed $script:Win 'New disc' | Out-Null
        $yes = Find-Ctl -Root $script:Win -NameLike 'Yes' -TimeoutSec 8
        if ($yes) { Invoke-Ctl -Ctl $yes -SettleMs 1000 }
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:BigOutSet
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        (Get-EntryCount $script:Win) | Should -Be 2

        # The project did not ask for a set, so tick it here.
        Invoke-CtlNamed $script:Win 'disc set' | Out-Null
        Start-Sleep -Milliseconds 500
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null
        $msg = Read-MessageBox -Win $script:Win
        $msg | Should -Match 'one game'
        $msg | Should -Not -Match 'Remove an entry in step 1'
    }

    It 'leaves the disc alone after every one of those refusals' {
        Get-EntryCount $script:Win | Should -Be 2
    }
}

Describe 'Previewing the menu' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # The menu is ~23,000 characters of JScript that otherwise only ever runs on
    # a finished disc. tests/DiscWright.Tests.ps1 proves it parses; parsing says
    # nothing about an undefined reference, which throws only when the menu is
    # opened. This opens it.
    #
    # How the script-error assertion was arrived at, rather than guessed: a menu
    # deliberately broken with an undefined reference inside capFor puts a child
    # called "Script Error" in the HTA window, and a healthy one does not. The
    # window itself appears either way, so its presence alone proves nothing.
    #
    # What is still left for a person is layout - whether the caption sits where
    # it should. UI Automation sees the HTA's window and its title and no more:
    # the rendered document is not in the tree.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        $script:MshtaBefore = @(Get-Process mshta -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

        # Preview needs the menu ticked and a background chosen, and nothing else.
        # Reopening the fixture supplies both, and puts a label on the disc that
        # cannot survive being written to an ASCII file unescaped.
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:FancyOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2

        $script:PreviewWin = $null
        $script:PreviewProc = $null
        if ((Test-CtlEnabled $script:Win 'Preview menu') -eq $true) {
            Invoke-CtlNamed $script:Win 'Preview menu' | Out-Null
            for ($i = 0; $i -lt 40; $i++) {
                $new = @(Get-Process mshta -ErrorAction SilentlyContinue |
                         Where-Object { $script:MshtaBefore -notcontains $_.Id })
                if ($new.Count) { $script:PreviewProc = $new[0]; break }
                Start-Sleep -Milliseconds 250
            }
            if ($script:PreviewProc) {
                $script:PreviewWin = Wait-AnyWinForProcess -ProcessId $script:PreviewProc.Id -TimeoutSec 20
            }
        }
        # Read once, after the menu has had time to run its init, so every test
        # below is looking at the same settled window.
        Start-Sleep -Seconds 2
        $script:PreviewTitle = ''
        $script:PreviewKids  = @()
        if ($script:PreviewWin) {
            try { $script:PreviewTitle = $script:PreviewWin.Current.Name } catch {}
            $all = $script:PreviewWin.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            for ($i = 0; $i -lt $all.Count; $i++) {
                try { $n = $all.Item($i).Current.Name; if ($n) { $script:PreviewKids += $n } } catch {}
            }
        }
    }

    AfterAll {
        # Only what this block started. Killing every mshta would take out
        # whatever the person at the machine happened to have open.
        Get-Process mshta -ErrorAction SilentlyContinue |
            Where-Object { $script:MshtaBefore -notcontains $_.Id } |
            ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Stop-DiscWright $script:App; $script:App = $null
    }

    It 'wakes Preview menu once the menu is on and a background is chosen' {
        Test-CtlEnabled $script:Win 'Preview menu' | Should -BeTrue
    }

    It 'opens a window' {
        # The parse test cannot catch a reference that is only undefined at run
        # time. Nothing opening at all is what that looks like from outside.
        $script:PreviewWin | Should -Not -BeNullOrEmpty
    }

    It 'runs its JavaScript without a script error' {
        $script:PreviewKids | Should -Not -Contain 'Script Error'
    }

    It 'renders the trademark characters instead of question marks' {
        # The whole 0.4.3 fix, end to end and through the real HTML engine
        # rather than through cscript: the label is escaped to &#8482; on the way
        # into an ASCII file and has to come back out as the character itself.
        $script:PreviewTitle | Should -Be $script:FancyLabel
    }

    It 'does not leave the app stuck behind its own preview' {
        # The preview is launched, not shown modally. A build has to still be
        # reachable with the menu standing open.
        Test-CtlEnabled $script:Win '*BUILD ISO' | Should -BeTrue
    }
}
Describe 'Driving the menu of a disc with two games' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # The block above opens the menu and stops: a window, no script error, the
    # right title. None of that notices a chooser that lists the wrong games, a
    # game screen that leaves its add-ons off, a Back that goes nowhere or an Exit
    # that does not close. Those only fail for someone holding the disc. This
    # clicks through them.
    #
    # UI Automation cannot see inside the document, so the buttons are found on
    # screen - see Get-MenuButtonRows - and counted. The count is the assertion:
    # every screen here has a different number of buttons, so arriving on the
    # wrong one cannot pass.

    BeforeAll {
        $script:MshtaBefore = @(Get-Process mshta -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

        # A background with no flat stretch in it. Buttons are found by being a
        # flat dark block; a plain dark background would read as one tall button
        # once the menu shades the panel side.
        $bmp = New-Object System.Drawing.Bitmap(1280, 720)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        for ($y = 0; $y -lt 720; $y += 6) {
            $c = if (($y / 6) % 2) { [System.Drawing.Color]::FromArgb(210, 190, 90) } else { [System.Drawing.Color]::FromArgb(70, 140, 210) }
            $g.FillRectangle((New-Object System.Drawing.SolidBrush($c)), 0, $y, 1280, 6)
        }
        $g.Dispose()
        $script:StripeArt = Join-Path $script:Sandbox 'stripes.png'
        $bmp.Save($script:StripeArt, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

        # Two games, the first with both patches filed under it.
        $script:MenuOut = Join-Path $script:Sandbox 'menuproj'
        New-Item -ItemType Directory -Force -Path $script:MenuOut | Out-Null
        $a  = Get-GameInfo $script:GameA
        $b  = Get-GameInfo $script:GameB
        $p1 = Get-AddOnInfo $script:PatchOne; $p1.ParentIndex = 0
        $p2 = Get-AddOnInfo $script:PatchTwo; $p2.ParentIndex = 0
        Save-Project @{
            Games=@($a, $b, $p1, $p2); Label='Menu Walk'
            IconPath=$script:Art; IconIsIco=$false
            Menu=$true; BgPath=$script:StripeArt; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); MediaKey=''; OutDir=$script:MenuOut
        } $script:MenuOut

        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:MenuOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2

        $script:Menu = [IntPtr]::Zero
        if ((Test-CtlEnabled $script:Win 'Preview menu') -eq $true) {
            Invoke-CtlNamed $script:Win 'Preview menu' | Out-Null
            $proc = $null
            for ($i = 0; $i -lt 40 -and -not $proc; $i++) {
                $proc = Get-Process mshta -ErrorAction SilentlyContinue |
                        Where-Object { $script:MshtaBefore -notcontains $_.Id } | Select-Object -First 1
                if (-not $proc) { Start-Sleep -Milliseconds 250 }
            }
            if ($proc) { $script:Menu = Find-MenuWindow -ProcessId $proc.Id }
        }
    }

    AfterAll {
        Get-Process mshta -ErrorAction SilentlyContinue |
            Where-Object { $script:MshtaBefore -notcontains $_.Id } |
            ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Stop-DiscWright $script:App; $script:App = $null
    }

    It 'finds the window the menu is drawn in' {
        $script:Menu | Should -Not -Be ([IntPtr]::Zero)
    }

    It 'opens on a chooser: one button per game, and Exit' {
        Wait-MenuButtonCount -Menu $script:Menu -Expected 3 | Should -Be 3
    }

    It 'opens a game on its own screen, its add-ons under Install' {
        # Play, Install, the two patches, Back, Exit.
        Invoke-MenuButton -Menu $script:Menu -Index 0
        Wait-MenuButtonCount -Menu $script:Menu -Expected 6 | Should -Be 6
    }

    It 'goes back to the chooser from Back' {
        Invoke-MenuButton -Menu $script:Menu -Index 4
        Wait-MenuButtonCount -Menu $script:Menu -Expected 3 | Should -Be 3
    }

    It 'opens the other game without the first one''s add-ons' {
        # Play, Install, Back, Exit. A screen that carried the patches over from
        # the game before would show six.
        Invoke-MenuButton -Menu $script:Menu -Index 1
        Wait-MenuButtonCount -Menu $script:Menu -Expected 4 | Should -Be 4
    }

    It 'closes when Exit is pressed' {
        Invoke-MenuButton -Menu $script:Menu -Index 3
        $gone = $false
        for ($i = 0; $i -lt 20 -and -not $gone; $i++) {
            $gone = -not (Test-MenuWindowOpen -Menu $script:Menu)
            if (-not $gone) { Start-Sleep -Milliseconds 250 }
        }
        $gone | Should -BeTrue
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
        Get-MediaTargetText $script:Win | Should -BeLike 'Recommend a disc*'
    }

    It 'recommends a size until it is told which disc you own' {
        # The old line, unchanged: a size DiscWright picked, not a plan.
        Get-StatusText $script:Win | Should -Match 'Disc: '
    }

    It 'turns the advice line into a verdict once a disc is chosen' {
        # Index 1 is CD-R, the first real medium. A gigabyte of games does not
        # fit one, so this is a real refusal and not a relabelled success.
        Set-MediaTarget -Win $script:Win -Index 1
        # The row carries its own answer now, so match the tier it names.
        Get-MediaTargetText $script:Win | Should -BeLike 'CD-R 700 MB*'
        $s = Get-StatusText $script:Win
        $s | Should -Match 'too big for a CD-R 700 MB'
        $s | Should -Not -Match 'Disc: '
    }

    It 'says on the row itself whether that disc would hold it' {
        # The dropdown is where "which disc should I use" gets asked, so the
        # answer belongs in the list rather than only on the line underneath.
        Set-MediaTarget -Win $script:Win -Index 1
        Get-MediaTargetText $script:Win | Should -Be 'CD-R 700 MB  -  will not fit'
        Set-MediaTarget -Win $script:Win -Index 2
        Get-MediaTargetText $script:Win | Should -Be 'DVD5 4.7 GB  -  fits'
    }

    It 'fits once a bigger disc is chosen' {
        # Index 2 is DVD5. The same games fit on one of those.
        Set-MediaTarget -Win $script:Win -Index 2
        Get-MediaTargetText $script:Win | Should -BeLike 'DVD5 4.7 GB*'
        Get-StatusText $script:Win | Should -Match 'fits DVD5 4\.7 GB'
    }

    It 'goes back to recommending when the automatic setting is chosen again' {
        Set-MediaTarget -Win $script:Win -Index 0
        Get-MediaTargetText $script:Win | Should -BeLike 'Recommend a disc*'
        Get-StatusText $script:Win | Should -Match 'Disc: '
    }

    It 'is put back to the automatic setting by New disc' {
        Set-MediaTarget -Win $script:Win -Index 3
        Get-MediaTargetText $script:Win | Should -BeLike 'DVD9 8.5 GB (dual layer)*'
        Invoke-CtlNamed $script:Win 'New disc' | Out-Null
        Read-MessageBox -Win $script:Win -TitleLike 'New disc' -Button 'Yes' | Out-Null
        Start-Sleep -Seconds 1
        Get-MediaTargetText $script:Win | Should -BeLike 'Recommend a disc*'
    }
}

Describe 'Reopening a project that named a target disc' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # The bug this block exists for: Save-Project wrote the target disc correctly
    # and Import-Project never copied it into what it hands back, so every reopen
    # silently fell back to the recommendation. Writing the file had a test.
    # Reading it back did not, and the round trip only shows up from the window.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:BigOutSet
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'comes back on the disc it was planned for' {
        Get-MediaTargetText $script:Win | Should -BeLike 'CD-R 700 MB*'
    }

    It 'weighs it against that disc straight away, without touching the dropdown' {
        Get-StatusText $script:Win | Should -Match 'too big for a CD-R 700 MB'
    }

    It 'still reopens an older project as no target at all' {
        # $script:BigOut was saved with an empty MediaKey, which is what every
        # project written before 0.5 looks like.
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:BigOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        Get-MediaTargetText $script:Win | Should -BeLike 'Recommend a disc*'
        Get-StatusText $script:Win | Should -Match 'Disc: '
    }
}


Describe 'The form while a real build runs' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # 0.4.3 locked the form for the duration of a build. It shipped without ever
    # having run one from the window, because a build of these sparse fixtures
    # finishes faster than UI Automation can sample it.
    #
    # It does not have to be sampled. The "Build complete" box is shown from
    # inside the try, and Set-FormBusy $false runs in the finally after it - so
    # while that box stands, the form is still frozen and can be read at leisure.
    # That is also the case that matters: a form left disabled behind a dialog is
    # an app that looks hung.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window

        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:ProjOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
        # Somewhere empty, so this is a build rather than an overwrite.
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:LockOut
        Start-Sleep -Seconds 1

        $script:BeforeChange = Test-CtlEnabled $script:Win 'Change*'
        Invoke-CtlNamed $script:Win '*BUILD ISO' | Out-Null

        # The completion box, while it is still up. Found rather than waited out:
        # a fixed sleep would be a guess in both directions.
        $script:DoneBox = Find-Ctl -Root $script:Win -NameLike 'DiscWright' -TimeoutSec 120
        $script:DoneText = ''
        if ($script:DoneBox) {
            $kids = $script:DoneBox.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            for ($i = 0; $i -lt $kids.Count; $i++) {
                try {
                    $n = $kids.Item($i).Current.Name
                    if ($n -and $n -notin @('OK','Cancel') -and $n.Length -gt $script:DoneText.Length) {
                        $script:DoneText = $n
                    }
                } catch {}
            }
            $ok = Find-Ctl -Root $script:DoneBox -NameLike 'OK' -TimeoutSec 5
            if ($ok) { Invoke-Ctl -Ctl $ok -SettleMs 800 }
        }
        Start-Sleep -Seconds 1
        $script:ThawedAdd    = Test-CtlEnabled $script:Win 'Add game*'
        $script:ThawedOpen   = Test-CtlEnabled $script:Win 'Open existing disc*'
        $script:ThawedChange = Test-CtlEnabled $script:Win 'Change*'
        Save-WindowShot $script:Win (Join-Path $script:ShotDir 'after-build.png')
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'finishes the build and says so' {
        $script:DoneBox  | Should -Not -BeNullOrEmpty
        $script:DoneText | Should -Match 'Build complete'
    }

    It 'writes the ISO where step 6 pointed' {
        @(Get-ChildItem -Path $script:LockOut -Filter '*.iso').Count | Should -BeGreaterThan 0
    }

    It 'leaves a disc folder beside it' {
        Test-Path (Join-Path $script:LockOut 'disc') | Should -BeTrue
    }

    # There used to be a test here asserting the form was disabled while the
    # completion box stood. It passed with the lock removed entirely: a modal
    # box makes UI Automation report every control on its owner as disabled,
    # so it measured modality and not the lock. The lock is covered in
    # tests/DiscWright.Tests.ps1 instead, on real controls.

    It 'gives the form back once the box is dismissed' {
        $script:ThawedAdd  | Should -BeTrue
        $script:ThawedOpen | Should -BeTrue
    }

    It 'restores what was enabled rather than enabling everything' {
        # Set-FormBusy remembers each control's state instead of switching the
        # form back on wholesale. Change... is greyed with no row selected, and a
        # build must not be a way to wake it.
        $script:BeforeChange | Should -BeFalse
        $script:ThawedChange | Should -BeFalse
    }
}

Describe 'The question a folder with no GOG installer asks' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # This dialog cannot be reached the way the others are. Everything else here
    # is driven through the main window, but this one only opens after a folder
    # has been picked in the shell's own folder browser, and no test can steer
    # that tree to a fixture on a machine it knows nothing about. So the real
    # dialog is hosted on its own (tests/ui/DialogHost.ps1) and asked about a
    # folder of the test's choosing. What it looks like is one half; what it
    # hands back for each of the three answers is the half that decides whether
    # an entry lands on the disc, so the host writes that down and it is read.

    BeforeAll {
        $script:AskSrc = Join-Path $script:Sandbox 'src\ask_folder_game'
        $null = New-Installer $script:AskSrc 'BigGame.exe' 6
        $null = New-Installer (Join-Path $script:AskSrc 'tools') 'Helper.exe' 1
        Set-Content -LiteralPath (Join-Path $script:AskSrc 'readme.txt') -Value 'read me' -Encoding Ascii

        # The folder somebody's downloads sit in, rather than one game: two GOG
        # downloads in subfolders and no installer of its own.
        $script:AskShelf = Join-Path $script:Sandbox 'src\gog_shelf'
        $null = New-Installer (Join-Path $script:AskShelf 'first game')  'setup_first_game_1.0.exe' 2
        $null = New-Installer (Join-Path $script:AskShelf 'second game') 'setup_second_game_1.0.exe' 2

        $script:HostScript = Join-Path $PSScriptRoot 'DialogHost.ps1'
        $script:AskAnswerFile = Join-Path $script:Sandbox 'dialog-answer.txt'

        function Start-Question([string]$folder) {
            Remove-Item -LiteralPath $script:AskAnswerFile -Force -ErrorAction SilentlyContinue
            # Whatever the host says on its way out is kept: a host that failed to
            # start looks exactly like a dialog that never opened, and the two
            # need telling apart.
            $log = Join-Path $script:Sandbox 'dialog-host.log'
            $proc = Start-Process powershell.exe -WindowStyle Hidden -PassThru -RedirectStandardError $log -ArgumentList @(
                '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $script:HostScript,
                '-Folder', $folder, '-ResultFile', $script:AskAnswerFile)
            # Every top-level window is looked at, not just the first one with
            # this title, and the one that belongs to THIS host is the one taken.
            # Wait-Win hands back the first match it finds, and a dialog left
            # standing by an earlier failure answers to the same title forever -
            # which is exactly how this test first failed.
            $deadline = (Get-Date).AddSeconds(45)
            $dlg = $null
            while (-not $dlg -and (Get-Date) -lt $deadline) {
                $kids = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                    [System.Windows.Automation.TreeScope]::Children,
                    [System.Windows.Automation.Condition]::TrueCondition)
                for ($i = 0; $i -lt $kids.Count; $i++) {
                    try {
                        $k = $kids.Item($i)
                        if ($k.Current.ProcessId -eq $proc.Id -and $k.Current.Name -like 'No GOG installer*') {
                            $dlg = $k; break
                        }
                    } catch {}
                }
                if (-not $dlg) { Start-Sleep -Milliseconds 300 }
            }
            if (-not $dlg) {
                $gone = $proc.HasExited
                try { $proc.Kill() } catch {}
                $why = ([string](@(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue) -join ' ')).Trim()
                throw ("the folder question never appeared (host pid $($proc.Id), exited: $gone)" +
                       $(if ($why) { " - the host said: $why" } else { '' }))
            }
            # From here on the process must not be allowed to leak: a host left
            # running holds a dialog on the desktop that the next test finds
            # instead of its own.
            try {
                Set-DrivenWindow $dlg
                $null = Set-WindowFocus $dlg
            } catch {
                try { $proc.Kill() } catch {}
                throw
            }
            Start-Sleep -Milliseconds 500
            return [pscustomobject]@{ Process = $proc; Window = $dlg }
        }

        function Get-QuestionAnswer($Question) {
            # The host writes the answer down before it closes, so waiting for it
            # to exit is waiting for the file.
            try { $null = $Question.Process.WaitForExit(15000) } catch {}
            if (-not (Test-Path $script:AskAnswerFile)) { return '(nothing written)' }
            return (Get-Content -LiteralPath $script:AskAnswerFile -Raw).Trim()
        }

        function Select-QuestionRow($Question, [int]$Down) {
            # A WinForms list box tells UI Automation nothing about its rows: the
            # whole control arrives as one nameless pane with no children, so
            # there is no row to find and click by name. What there is, is the
            # pane itself - clicked once to put the keyboard in the list, then
            # moved down as many rows as asked. Which row that lands on is what
            # the answer proves.
            $all = $Question.Window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                                            [System.Windows.Automation.Condition]::TrueCondition)
            $list = $null
            for ($i = 0; $i -lt $all.Count; $i++) {
                try {
                    $el = $all.Item($i)
                    if ($el.Current.Name -ne '') { continue }
                    $r = $el.Current.BoundingRectangle
                    if ($r.Height -lt 100) { continue }          # the list is the tall one
                    if (-not $list -or $r.Height -gt $list.Current.BoundingRectangle.Height) { $list = $el }
                } catch {}
            }
            if (-not $list) { throw 'the list of choices is not in the dialog' }
            if (-not (Test-DrivingOurWindow)) {
                throw ('The foreground window is no longer the dialog, so this click would land ' +
                       'in whatever is in front of it. Stopped.')
            }
            $r = $list.Current.BoundingRectangle
            # Near the top left, which is the first row whatever the row height is.
            [DwInput]::ClickAt([int]($r.X + 30), [int]($r.Y + 8))
            Start-Sleep -Milliseconds 300
            for ($n = 0; $n -lt $Down; $n++) { Send-Keys '{DOWN}' 200 }
        }
    }

    AfterEach {
        if ($script:Question) { try { $script:Question.Process.Kill() } catch {}; $script:Question = $null }
    }

    It 'names the folder and says the files go on the disc either way' {
        $script:Question = Start-Question $script:AskSrc
        Save-WindowShot $script:Question.Window (Join-Path $script:ShotDir 'folder-question.png')
        (Find-Ctl -Root $script:Question.Window -NameLike '*ask_folder_game holds no setup_*') |
            Should -Not -BeNullOrEmpty
    }

    It 'says what is about to go on the disc, and how many executables it offers' {
        # The size is the mis-pick showing itself: a game folder reads as a few
        # files and a few GB, the folder holding every download somebody owns
        # reads as tens of GB, and both are visible before Add is pressed.
        $script:Question = Start-Question $script:AskSrc
        (Find-Ctl -Root $script:Question.Window -NameLike '3 file(s)*MB*2 executable(s), largest first*') |
            Should -Not -BeNullOrEmpty
    }

    It 'warns when the folder is where the downloads live, not a game' {
        # Nothing is refused: a real game folder can carry a setup_*.exe somewhere
        # underneath it too. The dialog says what it found and leaves the choice.
        $script:Question = Start-Question $script:AskShelf
        Save-WindowShot $script:Question.Window (Join-Path $script:ShotDir 'folder-question-shelf.png')
        (Find-Ctl -Root $script:Question.Window -NameLike '2 GOG download(s) sit in subfolders*"first game"*') |
            Should -Not -BeNullOrEmpty
        (Find-Ctl -Root $script:Question.Window -NameLike '*Cancel and pick that folder instead*') |
            Should -Not -BeNullOrEmpty
    }

    It 'leaves that warning off an ordinary game folder' {
        $script:Question = Start-Question $script:AskSrc
        (Find-Ctl -Root $script:Question.Window -NameLike '*sit in subfolders*' -TimeoutSec 2) |
            Should -BeNullOrEmpty
    }

    It 'answers with no installer when Add is clicked as it stands' {
        # The safe answer is the one already selected, so the fastest way through
        # the dialog is also the one that cannot point a menu button at the wrong
        # program.
        $script:Question = Start-Question $script:AskSrc
        Invoke-CtlNamed $script:Question.Window 'Add' -SettleMs 800 | Out-Null
        Get-QuestionAnswer $script:Question | Should -Be 'NONE'
    }

    It 'answers with the executable one row down, which is the biggest one' {
        # Also the ordering, proven through the window rather than through the
        # function: the row under "no installer" has to be the largest
        # executable, or a person picking the obvious one picks the wrong one.
        $script:Question = Start-Question $script:AskSrc
        Select-QuestionRow $script:Question -Down 1
        Invoke-CtlNamed $script:Question.Window 'Add' -SettleMs 800 | Out-Null
        Get-QuestionAnswer $script:Question | Should -Be "INSTALLER`t$(Join-Path $script:AskSrc 'BigGame.exe')"
    }

    It 'answers with the one two rows down, which is in a subfolder' {
        # The second row is Helper.exe, which lives in tools\ - so the dialog
        # really does look through the whole folder, and the path it hands back
        # is the executable's own and not the folder's.
        $script:Question = Start-Question $script:AskSrc
        Select-QuestionRow $script:Question -Down 2
        Invoke-CtlNamed $script:Question.Window 'Add' -SettleMs 800 | Out-Null
        Get-QuestionAnswer $script:Question |
            Should -Be "INSTALLER`t$(Join-Path $script:AskSrc 'tools\Helper.exe')"
    }

    It 'answers with nothing at all when Cancel is clicked' {
        # Cancel has to be distinguishable from "no installer", or cancelling the
        # question would add the very entry that was just refused.
        $script:Question = Start-Question $script:AskSrc
        Invoke-CtlNamed $script:Question.Window 'Cancel' -SettleMs 800 | Out-Null
        Get-QuestionAnswer $script:Question | Should -Be 'CANCELLED'
    }
}

Describe 'The printed artwork fields' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # The fields were covered only by a roll call: present, and greyed. What
    # they are for is saying what a picture will cost before it is printed, and
    # that had never been driven.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window

        Add-Type -AssemblyName System.Drawing
        $script:ArtDir = Join-Path ([IO.Path]::GetTempPath()) ('dwuiart_' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
        New-Item -ItemType Directory -Force -Path $script:ArtDir | Out-Null
        function New-Pic([int]$w, [int]$h, [string]$name) {
            $path = Join-Path $script:ArtDir "$name.png"
            $b = New-Object System.Drawing.Bitmap $w, $h
            $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
            return $path
        }
        $script:WidePic = New-Pic 1920 1080 'wide'
        $script:TallPic = New-Pic 1000 1420 'tall'
        $script:SquarePic = New-Pic 1200 1200 'square'

        # The note is one label holding a clause per picture, separated by a run
        # of spaces. Asserting on the whole string would let the disc face's
        # words answer a question about the cover, which is exactly what the
        # first version of these tests did.
        function Get-ArtNote {
            $all = $script:Win.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            for ($i = 0; $i -lt $all.Count; $i++) {
                try {
                    $n = $all.Item($i).Current.Name
                    # A dimension has to follow, or this picks up the
                    # field's own label, which is called 'Cover picture'.
                    if ($n -match '^(Cover|Disc face) \d') { return $n }
                } catch {}
            }
            return ''
        }
        function Get-NoteClause([string]$which) {
            foreach ($part in ((Get-ArtNote) -split '\s{3,}')) {
                if ($part.Trim().StartsWith($which)) { return $part.Trim() }
            }
            return ''
        }
    }

    AfterAll {
        Stop-DiscWright $script:App; $script:App = $null
        if ($script:ArtDir -and (Test-Path $script:ArtDir)) {
            Remove-Item -LiteralPath $script:ArtDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    BeforeEach {
        # Taken again before every test: the driver refuses to type into a
        # window that is not in front, and anything can have taken the
        # foreground since the last one.
        $null = Set-WindowFocus $script:Win
    }

    It 'has a box for the cover picture and one for the disc face' {
        Get-BoxAfter $script:Win 'Cover picture*'     | Should -Not -BeNullOrEmpty
        Get-BoxAfter $script:Win 'Disc face picture*' | Should -Not -BeNullOrEmpty
    }

    It 'says a wide picture is the wrong shape for a tall cover, and prints a label instead' {
        # Nothing is cropped any more, so the note no longer says how much is
        # lost. It says the picture is not that shape and will be left alone.
        Set-CtlText -Ctl (Get-BoxAfter $script:Win 'Cover picture*') -Text $script:WidePic
        Start-Sleep -Milliseconds 600
        $cover = Get-NoteClause 'Cover'
        $cover | Should -Match '1920x1080'
        $cover | Should -Match 'is not that shape'
        $cover | Should -Match 'plain label'
        $cover | Should -Not -Match 'cut off'
    }

    It 'is content once a cover-shaped picture is chosen instead' {
        Set-CtlText -Ctl (Get-BoxAfter $script:Win 'Cover picture*') -Text $script:TallPic
        Start-Sleep -Milliseconds 600
        $cover = Get-NoteClause 'Cover'
        $cover | Should -Match 'the right shape'
        $cover | Should -Match 'printed exactly as it is'
        $cover | Should -Not -Match 'plain label'
    }

    It 'treats the disc face as its own question, because a circle is not a cover' {
        # The same tall picture that suits a cover is wrong for a disc, and the
        # app has to say so about the face while leaving the cover alone.
        Set-CtlText -Ctl (Get-BoxAfter $script:Win 'Disc face picture*') -Text $script:TallPic
        Start-Sleep -Milliseconds 600
        (Get-NoteClause 'Disc face') | Should -Match 'is not that shape'
        (Get-NoteClause 'Cover')     | Should -Match 'the right shape'
    }

    It 'is content with a square picture on a round disc' {
        Set-CtlText -Ctl (Get-BoxAfter $script:Win 'Disc face picture*') -Text $script:SquarePic
        Start-Sleep -Milliseconds 600
        (Get-NoteClause 'Disc face') | Should -Match 'the right shape'
    }

    It 'claims nothing about a path that is not a picture at all' {
        Set-CtlText -Ctl (Get-BoxAfter $script:Win 'Cover picture*') -Text 'Z:\gone\missing.png'
        Start-Sleep -Milliseconds 600
        # No file, no measurement. The app must not pretend to have opened
        # something it could not, and the disc face clause stands untouched.
        (Get-NoteClause 'Cover')     | Should -BeNullOrEmpty
        (Get-NoteClause 'Disc face') | Should -Match 'the right shape'
    }
}

Describe 'Driving the menu of a disc that holds game files' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # The menu suite above drives a GOG disc, where greying Play out and pointing
    # at Install is correct. Every menu test was written that way, which is how a
    # burned disc came to show PLAY greyed with "use Install first" on a disc
    # where nothing can be installed. The suite was green throughout.
    #
    # So the same walk, on the other kind of disc. Three games rather than two,
    # so the chooser and a game screen still have different button counts and
    # arriving on the wrong screen cannot pass.

    BeforeAll {
        $script:FilesMshtaBefore = @(Get-Process mshta -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

        # Its own striped background rather than the one the GOG menu block
        # makes. Borrowing that left this with no background at all when run on
        # its own, Preview stayed greyed, and no menu ever opened: a failure
        # that said nothing about the thing under test.
        $bmp = New-Object System.Drawing.Bitmap(1280, 720)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        for ($y = 0; $y -lt 720; $y += 6) {
            $c = if (($y / 6) % 2) { [System.Drawing.Color]::FromArgb(210, 190, 90) }
                 else { [System.Drawing.Color]::FromArgb(70, 140, 210) }
            $g.FillRectangle((New-Object System.Drawing.SolidBrush($c)), 0, $y, 1280, 6)
        }
        $g.Dispose()
        $script:FilesArt = Join-Path $script:Sandbox 'files-stripes.png'
        $bmp.Save($script:FilesArt, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

        # Folders of game files: an executable named as the game, with data
        # beside it. Named nothing like setup_*, which is what makes them the
        # other kind of entry.
        $script:FilesSrc = Join-Path $script:Sandbox 'filesrc'
        $entries = @()
        foreach ($name in 'gothic', 'arcanum', 'fallout') {
            $dir = Join-Path $script:FilesSrc $name
            New-Item -ItemType Directory -Force -Path (Join-Path $dir 'data') | Out-Null
            $exe = Join-Path $dir "$name.exe"
            $fs = [IO.File]::Create($exe); $fs.SetLength(1MB); $fs.Close()
            Set-Content -LiteralPath (Join-Path $dir 'data\textures.pak') -Value 'test data'
            $info = Get-FolderInfo $dir $exe
            if (-not $info.Ok) { throw "the fixture for $name was not read as a folder of game files: $($info.Msg)" }
            $entries += , $info
        }

        $script:FilesOut = Join-Path $script:Sandbox 'filesproj'
        New-Item -ItemType Directory -Force -Path $script:FilesOut | Out-Null
        Save-Project @{
            Games = $entries; Label = 'Files Disc'
            IconPath = $script:FilesArt; IconIsIco = $false
            Menu = $true; BgPath = $script:FilesArt; BgAsIs = $false; PanelSide = 'Right'
            Divider = $false; ShowTitle = $false; TitleText = ''
            WindowBorder = $true; ButtonStyle = 'Minimal'; MusicFile = $null
            Buttons = @('Play', 'Install', 'Exit'); ManualPath = $null; ExtrasPath = $null
            ExtraItems = @(); MediaKey = ''; OutDir = $script:FilesOut
        } $script:FilesOut

        $script:FilesApp = Start-DiscWright -AppPath $script:AppPath
        $script:FilesWin = $script:FilesApp.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:FilesWin '6)  Output folder*') -Text $script:FilesOut
        Invoke-CtlNamed $script:FilesWin 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:FilesWin | Out-Null
        Start-Sleep -Seconds 2

        $script:FilesMenu = [IntPtr]::Zero
        if ((Test-CtlEnabled $script:FilesWin 'Preview menu') -eq $true) {
            Invoke-CtlNamed $script:FilesWin 'Preview menu' | Out-Null
            $proc = $null
            for ($i = 0; $i -lt 40 -and -not $proc; $i++) {
                $proc = Get-Process mshta -ErrorAction SilentlyContinue |
                        Where-Object { $script:FilesMshtaBefore -notcontains $_.Id } | Select-Object -First 1
                if (-not $proc) { Start-Sleep -Milliseconds 250 }
            }
            if ($proc) { $script:FilesMenu = Find-MenuWindow -ProcessId $proc.Id }
        }
    }

    AfterAll {
        Get-Process mshta -ErrorAction SilentlyContinue |
            Where-Object { $script:FilesMshtaBefore -notcontains $_.Id } |
            ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Stop-DiscWright $script:FilesApp; $script:FilesApp = $null
    }

    It 'reads the folders as game files rather than as GOG downloads' {
        $raw = Get-Content -Raw -LiteralPath (Join-Path $script:FilesOut 'discproject.json') | ConvertFrom-Json
        foreach ($g in $raw.Games) { $g.Source | Should -Be 'Files' }
    }

    It 'has a menu to preview at all, which is what the rest depends on' {
        # Checked first and on its own: when this block borrowed another
        # Describe's background it had none, Preview stayed greyed, and six
        # tests failed saying the menu showed no buttons.
        Test-CtlEnabled $script:FilesWin 'Preview menu' | Should -BeTrue
    }

    It 'opens on a chooser with one button per game, and Exit' {
        $script:FilesMenu | Should -Not -Be ([IntPtr]::Zero)
        Wait-MenuButtonCount -Menu $script:FilesMenu -Expected 4 | Should -Be 4
    }

    It 'offers Play from disc and Back and Exit on a game, and nothing to install' {
        # Three buttons, not the four a GOG game shows. Install is absent rather
        # than greyed: there is nothing on this disc to install.
        Invoke-MenuButton -Menu $script:FilesMenu -Index 0
        Wait-MenuButtonCount -Menu $script:FilesMenu -Expected 3 | Should -Be 3
    }

    It 'goes back to the chooser from Back' {
        # Back is the second of the three, so index 1.
        Invoke-MenuButton -Menu $script:FilesMenu -Index 1
        Wait-MenuButtonCount -Menu $script:FilesMenu -Expected 4 | Should -Be 4
    }

    It 'shows the same three buttons on another game, not just the first' {
        Invoke-MenuButton -Menu $script:FilesMenu -Index 1
        Wait-MenuButtonCount -Menu $script:FilesMenu -Expected 3 | Should -Be 3
        Invoke-MenuButton -Menu $script:FilesMenu -Index 1
        Wait-MenuButtonCount -Menu $script:FilesMenu -Expected 4 | Should -Be 4
    }

    It 'closes when Exit is pressed' {
        Invoke-MenuButton -Menu $script:FilesMenu -Index 3
        Start-Sleep -Seconds 2
        Test-MenuWindowOpen -Menu $script:FilesMenu | Should -BeFalse
    }
}


Describe 'A disc whose artwork was never chosen' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # Issue #98, points 6 and 8, which were one wall. Preview used to stay
    # greyed until a background had been chosen, so the people who had not
    # chosen one met a dead button with no explanation and concluded the
    # preview did not exist. It is the feature that would have shown them what
    # a background is for.
    #
    # Opened from a saved project rather than typed in, because the only paths
    # in the window that clear a background also clear the games, and a test
    # that cleared both would prove nothing about a disc that has one and not
    # the other.

    BeforeAll {
        $script:App = Start-DiscWright -AppPath $script:AppPath
        $script:Win = $script:App.Window
        Set-CtlText -Ctl (Get-BoxAfter $script:Win '6)  Output folder*') -Text $script:NoArtOut
        Invoke-CtlNamed $script:Win 'Open existing disc*' | Out-Null
        Complete-FolderDialog -Win $script:Win | Out-Null
        Start-Sleep -Seconds 2
    }
    AfterAll { Stop-DiscWright $script:App; $script:App = $null }

    It 'loads the game the project recorded' {
        Get-EntryCount $script:Win | Should -Be 1
    }

    It 'leaves both artwork boxes empty, as the project left them' {
        (Get-BoxAfter $script:Win '3)  Disc icon*').Current.Name | Should -BeNullOrEmpty
    }

    It 'offers Preview anyway, which is the whole of the fix' {
        # A background is no longer the price of looking at the menu.
        Test-CtlEnabled $script:Win 'Preview menu' | Should -BeTrue
    }

    It 'still offers to build, having refused to before' {
        Test-CtlEnabled $script:Win 'BUILD ISO*' | Should -BeTrue
    }
}

Describe 'Double-clicking the launcher on a finished disc' -Tag 'UI' -Skip:(-not $script:HaveDesktop) {

    # Everything else about Start Here.hta is checked without running it: the
    # script is generated, read back, and its path logic exercised in a JScript
    # engine across every spelling of the URL an HTA reports. All of that can
    # pass while the file itself does nothing at all, because none of it ever
    # asks mshta to open it.
    #
    # This is the disc the reporter would hold: AutoPlay off, no menu offered,
    # so they open the folder and double-click the one file that invites it.

    BeforeAll {
        $script:LaunchBefore = @(Get-Process mshta -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

        $bmp = New-Object System.Drawing.Bitmap(1280, 720)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(24, 48, 64)); $g.Dispose()
        $art = Join-Path $script:Sandbox 'launch-art.png'
        $bmp.Save($art, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

        $src = Join-Path $script:Sandbox 'launchsrc'
        New-Item -ItemType Directory -Force -Path $src | Out-Null
        $exe = Join-Path $src 'setup_launch_game.exe'
        $fs = [IO.File]::Create($exe); $fs.SetLength(256KB); $fs.Close()

        $script:LaunchOut = Join-Path $script:Sandbox 'launchout'
        New-Item -ItemType Directory -Force -Path $script:LaunchOut | Out-Null
        $null = Invoke-Build @{
            Games = @((Get-FolderInfo $src $exe)); OutDir = $script:LaunchOut
            Label = 'Launch Test'; IconPath = $art; IconIsIco = $false; Menu = $true
            BgPath = $art; BgAsIs = $false; PanelSide = 'Right'; Divider = $false
            ShowTitle = $false; TitleText = ''; WindowBorder = $true; ButtonStyle = 'Minimal'
            Buttons = @('Install', 'Exit'); MusicFile = ''; ManualPath = $null
            ExtrasPath = $null; ExtraItems = @(); LinuxInfo = $false; LegacyFs = $false
            Checksums = $false
        } { param($m) }
        $script:LaunchDisc = Join-Path $script:LaunchOut 'disc'
    }

    AfterAll {
        Get-Process mshta -ErrorAction SilentlyContinue |
            Where-Object { $script:LaunchBefore -notcontains $_.Id } |
            ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }

    It 'is sitting at the disc root where somebody browsing would find it' {
        Test-Path (Join-Path $script:LaunchDisc (Get-MenuLauncherName)) | Should -BeTrue
    }

    It 'opens the menu when it is run, which nothing else here proves' {
        $launcher = Join-Path $script:LaunchDisc (Get-MenuLauncherName)
        $proc = Start-Process mshta.exe -ArgumentList "`"$launcher`"" -PassThru

        # The launcher starts a second mshta for the menu and closes itself, so
        # the window to wait for belongs to a process that does not exist yet.
        $menu = [IntPtr]::Zero
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline -and $menu -eq [IntPtr]::Zero) {
            foreach ($p in @(Get-Process mshta -ErrorAction SilentlyContinue |
                             Where-Object { $script:LaunchBefore -notcontains $_.Id })) {
                $h = Find-MenuWindow -ProcessId $p.Id -TimeoutSec 1
                if ($h -ne [IntPtr]::Zero) { $menu = $h; break }
            }
            if ($menu -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 400 }
        }
        try { if (-not $proc.HasExited) { $proc.Kill() } } catch { }
        $menu | Should -Not -Be ([IntPtr]::Zero) -Because 'double-clicking the launcher has to open the menu'
    }

    It 'leaves the menu where autorun.inf still points, so AutoPlay is unchanged' {
        Test-Path (Join-Path $script:LaunchDisc 'AUTORUN\menu.hta') | Should -BeTrue
        (Get-Content (Join-Path $script:LaunchDisc 'autorun.inf') -Raw) | Should -Match 'shellexecute=AUTORUN'
    }
}
