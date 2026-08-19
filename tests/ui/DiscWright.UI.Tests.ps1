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

    It 'leaves <_> greyed until there is something for it to act on' -ForEach @(
        'Add-on*', 'Change*', 'Remove', 'Show disc folder', 'Preview menu'
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
}
