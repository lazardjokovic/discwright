# Pester tests for DiscWright.
#
#   Invoke-Pester tests
#   Invoke-Pester tests -ExcludeTagFilter Build     # skip the slow ISO builds
#
# DiscWright.ps1 cannot be dot-sourced: its last statement shows a WinForms
# window. So the file is parsed and only its function definitions are evaluated.
# That runs the real functions rather than copies, which is the point - a test
# that exercises a copy of the code proves nothing about the app.

BeforeDiscovery {
    # Whether the build tests can run at all is decided during discovery, so the
    # whole block can be marked skipped rather than failing on a machine (or a CI
    # runner) that has no IMAPI2FS.
    $script:CanBuildIso = $false
    try {
        $probe = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($probe)
        $script:CanBuildIso = $true
    } catch { }

    # 7-Zip reads UDF, so a finished ISO can be inspected without mounting it and
    # without an elevated shell.
    $script:SevenZip = @(
        'C:\Program Files\7-Zip\7z.exe'
        'C:\Program Files (x86)\7-Zip\7z.exe'
        (Get-Command 7z.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    # A real GKeyFile parser to check .xdg-volume-info against. WSL is the only
    # one likely to be on a Windows box; without it those tests skip rather than
    # assert that the format is right because we said so.
    $script:HasWsl = $false
    try {
        $null = & wsl.exe -- python3 -c "pass" 2>$null
        $script:HasWsl = ($LASTEXITCODE -eq 0)
    } catch { }
}

BeforeAll {
    Add-Type -AssemblyName System.Drawing

    # Discovery and execution are separate phases with separate state, so anything
    # BeforeDiscovery worked out for a -Skip decision has to be worked out again
    # here before the tests can actually use it.
    $script:SevenZip = @(
        'C:\Program Files\7-Zip\7z.exe'
        'C:\Program Files (x86)\7-Zip\7z.exe'
        (Get-Command 7z.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    # A real GKeyFile parser to check .xdg-volume-info against. WSL is the only
    # one likely to be on a Windows box; without it those tests skip rather than
    # assert that the format is right because we said so.
    $script:HasWsl = $false
    try {
        $null = & wsl.exe -- python3 -c "pass" 2>$null
        $script:HasWsl = ($LASTEXITCODE -eq 0)
    } catch { }

    $appScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($appScript, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count) { throw "DiscWright.ps1 has $($parseErrors.Count) parse errors" }
    foreach ($f in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
        . ([scriptblock]::Create($f.Extent.Text))
    }

    # Script-scope constants the functions read. Script scope, not local: the
    # functions look these up dynamically from whichever It block calls them, and
    # a local here would not be on that chain.
    $script:PROJECT_FILE = 'discproject.json'
    $script:ISO9660_MAX_FILE =
        [double][regex]::Match((Get-Content $appScript -Raw),
            '\$ISO9660_MAX_FILE\s*=\s*\[double\]([0-9]+)').Groups[1].Value

    # Read out of the app rather than hard-coded, so bumping the version does not
    # mean editing the tests - and so a test can assert the two agree.
    $script:AppVersionInSource =
        [regex]::Match((Get-Content $appScript -Raw), '\$APP_VERSION\s*=\s*''([^'']+)''').Groups[1].Value
    $script:APP_VERSION = $script:AppVersionInSource

    $script:Sandbox = Join-Path ([IO.Path]::GetTempPath()) ('dwpester_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Force -Path $script:Sandbox | Out-Null

    # A GOG folder is an installer plus its numbered .bin parts. Sparse files keep
    # the fixtures instant and cost no disk.
    function New-FixtureGame {
        param([string]$Slug, [double]$ExeMb = 3, [int]$Parts = 0, [int[]]$SkipParts = @())
        $dir = Join-Path $script:Sandbox ('src\' + $Slug)
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $stem = "setup_${Slug}_1.0_(90210)"
        $fs = [IO.File]::Create((Join-Path $dir "$stem.exe")); $fs.SetLength([long]($ExeMb * 1MB)); $fs.Close()
        for ($i = 1; $i -le $Parts; $i++) {
            if ($SkipParts -contains $i) { continue }
            $fs = [IO.File]::Create((Join-Path $dir "$stem-$i.bin")); $fs.SetLength(1MB); $fs.Close()
        }
        return $dir
    }

    function New-FixturePng {
        param([string]$Path, [int]$W = 1280, [int]$H = 720)
        $bmp = New-Object System.Drawing.Bitmap($W, $H)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(18, 38, 58)); $g.Dispose()
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        return $Path
    }

    $script:Bg  = New-FixturePng (Join-Path $script:Sandbox 'bg.png')
    $script:Art = New-FixturePng (Join-Path $script:Sandbox 'art.png') 512 512

    function New-BuildSettings {
        # LinuxInfo defaults to $false here for the same reason the checkbox does:
        # every test written before it existed has to keep describing the disc it
        # was written about.
        param([array]$Games, [string]$Label, [string]$OutDir, [switch]$LinuxInfo, [switch]$LegacyFs,
              [switch]$Checksums)
        return @{
            Games=$Games; Label=$Label; IconPath=$script:Art; IconIsIco=$false
            Menu=$true; BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$OutDir; LinuxInfo=[bool]$LinuxInfo
            LegacyFs=[bool]$LegacyFs; Checksums=[bool]$Checksums
        }
    }

    # 7-Zip reads UDF, so an ISO can be inspected without mounting it and without
    # an elevated shell. Returns the paths inside the image.
    function Get-IsoEntries {
        param([string]$IsoPath, [string]$SevenZip)
        $raw = & $SevenZip l -slt $IsoPath 2>&1
        return @($raw | Where-Object { $_ -match '^Path = ' } |
                 ForEach-Object { $_ -replace '^Path = ','' } |
                 Where-Object { $_ -ne $IsoPath })
    }

    function Get-IsoVolumeId {
        param([string]$IsoPath, [string]$SevenZip)
        $line = (& $SevenZip l -slt $IsoPath 2>&1 | Where-Object { $_ -match '^\s*VolumeId:' } | Select-Object -First 1)
        if ($line) { return ($line -replace '^\s*VolumeId:\s*','').Trim() }
        return $null
    }

    # Invoke-Build reports progress through a callback. The tests do not care what
    # it says, only that the build runs - discarding the message is the point, and
    # consuming it keeps the analyzer from reading $m as an oversight.
    $script:LogSink = { param($m) $null = $m }
}

AfterAll {
    if ($script:Sandbox -and (Test-Path $script:Sandbox)) {
        Remove-Item $script:Sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Get-GameFolderName' -Tag 'Unit' {

    It 'numbers from one and pads to two digits' {
        Get-GameFolderName 1 'Hollow Knight' | Should -Be '01 - Hollow Knight'
    }

    It 'keeps two-digit numbers intact' {
        Get-GameFolderName 12 'Doom' | Should -Be '12 - Doom'
    }

    It 'folds accents rather than deleting the letter' {
        # The Polish spelling of Wiedzmin must not come back as "Wiedmin".
        # Built from a code point so this file stays pure ASCII - the same rule
        # checks.yml enforces on every .ps1 in the repo.
        $polish = 'Wied' + [char]0x017A + 'min'
        Get-GameFolderName 2 $polish | Should -Be '02 - Wiedzmin'
    }

    It 'removes characters Windows will not accept in a folder name' {
        $n = Get-GameFolderName 3 'A/B\C:D*E?F"G<H>I|J'
        $n | Should -Not -Match '[\\/:*?"<>|]'
    }

    It 'collapses runs of whitespace' {
        Get-GameFolderName 4 "A     B" | Should -Be '04 - A B'
    }

    It 'never ends in a dot, which Windows silently strips' {
        Get-GameFolderName 5 'Fallout...' | Should -Not -Match '\.$'
    }

    It 'never ends in a dot even when the length cut lands on one' {
        # The case above only proves the dots are trimmed BEFORE the cut. Forty
        # seven letters and then a dot puts that dot at character 48, exactly
        # where the cut falls, and trimming only whitespace afterwards left it
        # on the end. Windows then created the folder without it, the menu's
        # path kept it, and that game's Install button pointed at nothing.
        # Found porting this function to the Linux version.
        $name = Get-GameFolderName 1 (('A' * 47) + '.B')
        $name | Should -Not -Match '\.$'
        $name | Should -Be ('01 - ' + ('A' * 47))
    }

    It 'names the folder exactly what Windows creates for it' {
        # The property that actually matters, checked against the filesystem
        # rather than against a rule: whatever name this returns, a folder
        # created under that name must come back with the same name.
        $root = Join-Path $script:Sandbox 'foldername-roundtrip'
        # The first title is bracketed as a whole on purpose. Without it the comma
        # binds tighter than the plus, and the list collapses into one long string.
        $titles = @((('A' * 47) + '.B'), 'Fallout...', ('X' * 300), 'Ends in space ')
        $titles.Count | Should -Be 4
        foreach ($title in $titles) {
            $name = Get-GameFolderName 1 $title
            [void][IO.Directory]::CreateDirectory((Join-Path $root $name))
            $made = @([IO.Directory]::GetDirectories($root) | ForEach-Object { Split-Path $_ -Leaf })
            $made | Should -Contain $name -Because "'$title' became '$name'"
            [IO.Directory]::Delete((Join-Path $root $made[0]))
        }
    }

    It 'caps a very long title' {
        (Get-GameFolderName 6 ('X' * 300)).Length | Should -BeLessOrEqual 53
    }

    It 'still produces a folder for an empty title' {
        Get-GameFolderName 7 '' | Should -Be '07 - Game'
    }

    It 'still produces a folder for a title with no usable characters' {
        Get-GameFolderName 8 '???' | Should -Be '08 - Game'
    }

    It 'gives identically-titled games distinct folders' {
        $names = 1..3 | ForEach-Object { Get-GameFolderName $_ 'Same Title' }
        @($names | Sort-Object -Unique).Count | Should -Be 3
    }
}

Describe 'Get-VolumeLabel' -Tag 'Unit' {

    # The ISO9660 volume identifier is 16 characters and New-Iso folds anything
    # that is not alphanumeric to an underscore. This used to reserve room for a
    # "_D2" suffix; disc sets are gone and so is the reservation, which is what
    # made the truncation edge worth pinning down.

    It 'keeps a label that already fits' {
        Get-VolumeLabel 'Alan Wake' | Should -Be 'Alan_Wake'
    }

    It 'folds anything that is not alphanumeric to an underscore' {
        # The colon and the space each fold, so this keeps both underscores.
        Get-VolumeLabel 'Broken Sword: Shadow' | Should -Be 'Broken_Sword__Sh'
    }

    It 'caps the volume id at the 16 characters ISO9660 allows' {
        (Get-VolumeLabel 'THE WITCHER ENHANCED EDITION').Length | Should -Be 16
    }

    It 'does not leave a trailing underscore when the cut lands on a word boundary' {
        # 'THE WITCHER ENH EDITION' folds to THE_WITCHER_ENH_EDITION, and the
        # first sixteen characters of that end on the separator. Trimming only
        # before truncating left the disc named THE_WITCHER_ENH_ in This PC.
        Get-VolumeLabel 'THE WITCHER ENH EDITION' | Should -Be 'THE_WITCHER_ENH'
        Get-VolumeLabel 'A B C D E F G H I'       | Should -Not -Match '_$'
    }

    It 'never reserves room for a disc number that no longer exists' {
        # Sets are gone. A leftover reservation would show up as a stray _D
        # fragment on the end of a long label.
        Get-VolumeLabel 'THE WITCHER ENHANCED EDITION' | Should -Not -Match '_D\d?$'
    }

    It 'falls back to DISC when nothing survives the fold' {
        Get-VolumeLabel '!!!' | Should -Be 'DISC'
        Get-VolumeLabel ''    | Should -Be 'DISC'
    }
}

Describe 'Locking the form while a build runs' -Tag 'Unit' {

    # Added in 0.4.3 and shipped without ever running a build from the window.
    # It cannot be tested through the window either, which is worth writing down
    # so nobody spends an afternoon rediscovering it: the only moment the form is
    # observably frozen is while the "Build complete" box is up, and a modal box
    # makes UI Automation report every control on the owner as disabled anyway.
    # A window test written that way passes with the lock removed entirely.
    #
    # So it is tested here, on real WinForms controls, plus the wiring check
    # below that the build handler still calls it.

    BeforeAll {
        Add-Type -AssemblyName System.Windows.Forms

        # Script scope for the same reason as PROJECT_FILE above: Set-FormBusy
        # looks these up dynamically from wherever it is called.
        $script:form       = New-Object System.Windows.Forms.Form
        $script:txtLog     = New-Object System.Windows.Forms.TextBox
        $script:pbBuild    = New-Object System.Windows.Forms.ProgressBar
        $script:lblElapsed = New-Object System.Windows.Forms.Label
        $script:btnOne     = New-Object System.Windows.Forms.Button
        # Greyed before the build for a reason of its own - nothing is selected.
        # This is the control the restore has to leave alone.
        $script:btnGreyed  = New-Object System.Windows.Forms.Button
        $script:btnGreyed.Enabled = $false
        $script:form.Controls.AddRange(@(
            $script:txtLog, $script:pbBuild, $script:lblElapsed,
            $script:btnOne, $script:btnGreyed))
        $script:BuildFrozen = $null
    }

    AfterAll {
        if ($script:form) { $script:form.Dispose(); $script:form = $null }
    }

    It 'disables the controls a person could otherwise touch mid-build' {
        Set-FormBusy $true
        $script:btnOne.Enabled | Should -BeFalse
    }

    It 'leaves the log, the progress bar and the elapsed label alive' {
        # They are the only things worth looking at while it runs, and a disabled
        # multiline TextBox cannot even be scrolled.
        $script:txtLog.Enabled     | Should -BeTrue
        $script:pbBuild.Enabled    | Should -BeTrue
        $script:lblElapsed.Enabled | Should -BeTrue
    }

    It 'gives back exactly what was enabled before, not everything' {
        # The bug this shape avoids: re-enabling the form wholesale would wake
        # controls that were greyed for reasons that have not changed just
        # because a build happened.
        Set-FormBusy $false
        $script:btnOne.Enabled    | Should -BeTrue
        $script:btnGreyed.Enabled | Should -BeFalse
    }

    It 'is safe to unlock a form that was never locked' {
        # The unlock lives in a finally, so it runs even on a path that threw
        # before the lock was taken.
        $script:BuildFrozen = $null
        { Set-FormBusy $false } | Should -Not -Throw
    }

    Context 'wired into the build' {

        BeforeAll {
            $src = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
            $tree = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$null, [ref]$null)
            $script:BusyCalls = @($tree.FindAll({ param($n)
                $n -is [System.Management.Automation.Language.CommandAst] -and
                $n.GetCommandName() -eq 'Set-FormBusy' }, $true))

            function Test-InFinally($node) {
                $n = $node.Parent
                while ($n) {
                    if ($n -is [System.Management.Automation.Language.TryStatementAst] -and $n.Finally -and
                        $n.Finally.Extent.StartOffset -le $node.Extent.StartOffset -and
                        $n.Finally.Extent.EndOffset   -ge $node.Extent.EndOffset) { return $true }
                    $n = $n.Parent
                }
                return $false
            }
        }

        It 'locks the form for every long job, and there is more than one now' {
            # This counted exactly one lock while the build was the only thing
            # that took the window over. Burning takes it over too, so the rule
            # is one unlock for every lock rather than a number.
            $lock = @($script:BusyCalls | Where-Object { $_.CommandElements[1].Extent.Text -eq '$true' })
            $lock.Count | Should -BeGreaterOrEqual 1
        }

        It 'unlocks in a finally every time, so a failure cannot leave it dead' {
            $lock   = @($script:BusyCalls | Where-Object { $_.CommandElements[1].Extent.Text -eq '$true' })
            $unlock = @($script:BusyCalls | Where-Object { $_.CommandElements[1].Extent.Text -eq '$false' })
            $unlock.Count | Should -Be $lock.Count -Because 'a lock with no unlock leaves a dead window'
            foreach ($u in $unlock) {
                Test-InFinally $u | Should -BeTrue -Because 'an unlock outside a finally is skipped when something throws'
            }
        }
    }
}

Describe 'Test-ReservedDiscName' -Tag 'Unit' {

    It 'reserves the names the Linux half of the disc uses' {
        # Extra content dropped at the disc root must not be able to overwrite the
        # file that tells a Linux desktop what the disc is called, nor the icon it
        # points at.
        Test-ReservedDiscName '.xdg-volume-info' 'TheWitcher.ico' | Should -BeTrue
        Test-ReservedDiscName 'TheWitcher.png'   'TheWitcher.ico' | Should -BeTrue
        Test-ReservedDiscName 'disc.png'         'TheWitcher.ico' | Should -BeTrue
        # Still not reserved: an unrelated picture is ordinary extra content.
        Test-ReservedDiscName 'screenshot.png'   'TheWitcher.ico' | Should -BeFalse
    }

    It 'reserves <Name>' -ForEach @(
        @{ Name = 'autorun.inf' }
        @{ Name = 'AUTORUN' }
        @{ Name = 'Extras' }
        @{ Name = 'Games' }
        @{ Name = 'discproject.json' }
        @{ Name = 'disc.ico' }
    ) {
        Test-ReservedDiscName $Name 'Witcher.ico' | Should -BeTrue
    }

    It 'reserves the disc icon itself' {
        Test-ReservedDiscName 'Witcher.ico' 'Witcher.ico' | Should -BeTrue
    }

    It 'reserves anything that looks like a GOG installer' {
        Test-ReservedDiscName 'setup_doom_1.0.exe' 'Witcher.ico' | Should -BeTrue
    }

    It 'leaves ordinary extra content alone' {
        Test-ReservedDiscName 'Soundtrack' 'Witcher.ico' | Should -BeFalse
    }
}

Describe 'Reading the game list out of UI state' -Tag 'Unit' {

    # These three read $state, which only the running window populates - which is
    # exactly why nothing here touched them, and exactly how the media line came to
    # announce "9 games (0 bytes)" for one game. Two separate faults produced that:
    #
    #   PowerShell unrolls a single-element array on return, so Get-Games handed back
    #   the bare hashtable. .Count on a Hashtable is its number of KEYS - Get-GameInfo
    #   has nine - and Get-FirstGame's $g[0] indexed it by the key 0 and found nothing.
    #
    #   Measure-Object -Property looks for a real property, and a hashtable key is not
    #   one, so the installer total was always zero.
    #
    # Counts of 1 are the interesting case: 0, 2 and 3 all behaved correctly while 1
    # was broken.

    BeforeAll {
        $script:StateGames = @(
            (Get-GameInfo (New-FixtureGame -Slug 'state_one'   -ExeMb 5)),
            (Get-GameInfo (New-FixtureGame -Slug 'state_two'   -ExeMb 5)),
            (Get-GameInfo (New-FixtureGame -Slug 'state_three' -ExeMb 5))
        )
        # Get-PayloadBytes reads these three controls to decide what extras to count.
        $script:cbMan    = [pscustomobject]@{ Checked = $false }
        $script:cbExtra  = [pscustomobject]@{ Checked = $false }
        $script:chkMusic = [pscustomobject]@{ Checked = $false }

        function Set-TestState([int]$n) {
            $script:state = @{
                Games      = @($script:StateGames | Select-Object -First $n)
                ExtraItems = @(); ManualPath = $null; ExtrasPath = $null; MusicFile = $null
            }
        }
    }

    It 'Get-Games counts <N> game(s) correctly' -ForEach @(
        @{ N = 0 }, @{ N = 1 }, @{ N = 2 }, @{ N = 3 }
    ) {
        Set-TestState $N
        # (Get-Games).Count, not @(Get-Games).Count - the second wraps an array that
        # is already an array and always reports 1. The first version of this test
        # made exactly that mistake, three lines below a comment warning against it.
        (Get-Games).Count | Should -Be $N
    }

    It 'Get-Games returns a list, never a bare hashtable' {
        Set-TestState 1
        (Get-Games) -is [System.Collections.Hashtable] | Should -BeFalse
    }

    It 'Get-FirstGame returns one game, not all of them' -ForEach @(
        @{ N = 1 }, @{ N = 2 }, @{ N = 3 }
    ) {
        Set-TestState $N
        $first = Get-FirstGame
        $first        | Should -Not -BeNullOrEmpty
        $first.GameName | Should -Not -BeOfType [array]
        $first.GameName | Should -Be $script:StateGames[0].GameName
    }

    It 'Get-FirstGame is null when there are no games' {
        Set-TestState 0
        Get-FirstGame | Should -BeNullOrEmpty
    }

    It 'Get-PayloadBytes totals <N> game(s) as <Mb> MB' -ForEach @(
        @{ N = 0; Mb = 0 }, @{ N = 1; Mb = 5 }, @{ N = 2; Mb = 10 }, @{ N = 3; Mb = 15 }
    ) {
        Set-TestState $N
        [int]((Get-PayloadBytes).Installer / 1MB) | Should -Be $Mb
    }
}

Describe 'Recovering from a bad folder choice' -Tag 'Unit' {

    # Reported from the window: after picking a folder with no installer in it,
    # going back to a good one left the message red and the build refusing, with
    # the right path still in the box. A selection that fails must not be able to
    # poison the next one.

    BeforeAll {
        $script:GoodFolder = New-FixtureGame -Slug 'recover_game' -ExeMb 9
        $script:EmptyFolder = Join-Path $script:Sandbox 'no-installer-here'
        New-Item -ItemType Directory -Force -Path $script:EmptyFolder | Out-Null

        # The list and its buttons are real controls, not stand-ins. A ListView
        # constructs fine with no window behind it, and using the real one means
        # Update-GameList is exercised rather than a mock of it.
        Add-Type -AssemblyName System.Windows.Forms
        $script:lvGames = New-Object System.Windows.Forms.ListView
        $script:lvGames.View = 'Details'
        foreach ($c in @('#','Name','Type','Belongs to')) { [void]$script:lvGames.Columns.Add($c,80) }
        $script:btnGameDel  = New-Object System.Windows.Forms.Button
        $script:btnAddOn    = New-Object System.Windows.Forms.Button
        $script:btnGameEdit = New-Object System.Windows.Forms.Button

        # Stand-ins for the plain labels Set-GameEntries and Update-MediaLabel write to.
        # The dropdown is a real ComboBox rather than a stub object: Update-MediaOptions
        # rebuilds its Items and moves its selection, so a fake would only prove
        # the fake works.
        $script:cmbMedia  = New-Object System.Windows.Forms.ComboBox
        $script:chkXAll   = [pscustomobject]@{ Checked = $false }
        $script:lblMan    = [pscustomobject]@{ Text = 'Manual file:' }
        $script:lblEx     = [pscustomobject]@{ Text = 'Extras folder:' }
        $script:grpX      = [pscustomobject]@{ Text = '5)  Extra content (copied to the disc root as-is)' }
        $script:lblGame   = [pscustomobject]@{ Text = ''; ForeColor = $null }
        $script:cbMan     = [pscustomobject]@{ Checked = $false }
        $script:cbExtra   = [pscustomobject]@{ Checked = $false }
        $script:chkMusic  = [pscustomobject]@{ Checked = $false }
        $script:state     = @{ Games=@(); ExtraItems=@(); ManualPath=$null; ExtrasPath=$null; MusicFile=$null }
    }

    It 'accepts a good folder' {
        $g = Set-GameFolder $script:GoodFolder
        $g.Ok | Should -BeTrue
        (Get-Games).Count | Should -Be 1
    }

    It 'rejects a folder with no installer' {
        $g = Set-GameFolder $script:EmptyFolder
        $g.Ok | Should -BeFalse
        (Get-Games).Count | Should -Be 0
    }

    It 'says why the folder was rejected' {
        # There is no path box any more, so the reason has to be on the label.
        $script:lblGame.Text | Should -Match 'setup_\*\.exe'
    }

    It 'still lists the rejected entry, marked, rather than dropping it silently' {
        # Opening a project whose folder has gone empty must show that it went
        # empty. Vanishing from the list looks like the app losing the game.
        $script:lvGames.Items.Count | Should -Be 1
        $script:lvGames.Items[0].SubItems[1].Text | Should -Be '(no installer found)'
    }

    It 'recovers when a good folder is picked again' {
        $g = Set-GameFolder $script:GoodFolder
        $g.Ok             | Should -BeTrue
        (Get-Games).Count | Should -Be 1
        Get-FirstGame     | Should -Not -BeNullOrEmpty
    }

    It 'clears the red message on recovery' {
        $script:lblGame.ForeColor | Should -Not -Be ([System.Drawing.Color]::Firebrick)
        $script:lblGame.Text      | Should -Match 'Detected:'
    }

    It 'lets the build run again' {
        # The build handler refuses on (Get-Games).Count -eq 0.
        (Get-Games).Count | Should -BeGreaterThan 0
    }

    It 'returns one game from Set-GameFolder, not a list' {
        $g = Set-GameFolder $script:GoodFolder
        $g -is [System.Collections.Hashtable] | Should -BeTrue
    }
}

Describe 'Format-Elapsed' -Tag 'Unit' {

    # Casting a double to [int] ROUNDS in PowerShell rather than truncating, so
    # [int]1.58 is 2 and 95 seconds first displayed as "02:35" - a clock reading a
    # minute ahead of itself. These are the boundaries that catch it coming back.
    It 'formats <Seconds> seconds as <Expected>' -ForEach @(
        @{ Seconds = 0;    Expected = '00:00'    }
        @{ Seconds = 9;    Expected = '00:09'    }
        @{ Seconds = 59;   Expected = '00:59'    }
        @{ Seconds = 95;   Expected = '01:35'    }   # rounds up to 02:35 if [int] is used
        @{ Seconds = 3599; Expected = '59:59'    }
        @{ Seconds = 3600; Expected = '01:00:00' }
        @{ Seconds = 3725; Expected = '01:02:05' }
        @{ Seconds = 5400; Expected = '01:30:00' }   # rounds up to 02:30:00 if [int] is used
    ) {
        Format-Elapsed ([TimeSpan]::FromSeconds($Seconds)) | Should -Be $Expected
    }
}

Describe 'The ISO writer stays acceptable to Smart App Control' -Tag 'Unit' {

    # Not reproducible in CI - a runner does not have SAC enforced - so this
    # guards the shape instead of the behaviour. Measured on a machine with SAC
    # enforced: an assembly combining unsafe pointers with a delegate invoked in
    # the copy loop was refused every time, which would stop DiscWright writing
    # ISOs at all on a clean Windows 11. Reintroducing either half of that is
    # silent until someone with SAC on tries to build a disc.
    BeforeAll {
        # Comments stripped, or this matches the comment in the startup guard that
        # explains why -CompilerParameters is no longer used and fails on its own
        # documentation. Tokenising is the reliable way to tell code from prose.
        $tok = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($appScript, [ref]$tok, [ref]$null)
        $script:AppCode = (@($tok | Where-Object { $_.Kind -ne 'Comment' } | ForEach-Object { $_.Text }) -join "`n")
    }

    # Assert on a boolean rather than the text: -Match against a 100 KB script
    # prints the whole script on failure, which buries the actual result.
    It 'does not compile with /unsafe' {
        ($script:AppCode -match 'CompilerOptions') | Should -BeFalse
    }

    It 'calls Add-Type without -CompilerParameters' {
        # This is what tied the app to Windows PowerShell 5.1, since PowerShell 6
        # removed the parameter.
        #
        # Asked of the syntax tree rather than the text: the C# helper is a
        # here-string, which the tokeniser hands back as one code token, so the
        # comment inside it explaining this very history matched a text search.
        $tree = [System.Management.Automation.Language.Parser]::ParseFile($appScript, [ref]$null, [ref]$null)
        $addTypes = $tree.FindAll({ param($n)
            $n -is [System.Management.Automation.Language.CommandAst] -and
            $n.GetCommandName() -eq 'Add-Type' }, $true)
        $addTypes.Count | Should -BeGreaterThan 0   # guard against the query silently finding nothing
        $offenders = @($addTypes | Where-Object {
            @($_.CommandElements | Where-Object {
                $_ -is [System.Management.Automation.Language.CommandParameterAst] -and
                $_.ParameterName -like 'CompilerParameters*' }).Count -gt 0 })
        $offenders.Count | Should -Be 0
    }

    It 'declares no unsafe members' {
        ($script:AppCode -match '\bpublic\s+unsafe\b') | Should -BeFalse
    }

    It 'still hands IStream.Read somewhere to put the byte count' {
        # The safe replacement for the pointer. If these vanish, the read count is
        # being taken some other way and the loop needs looking at again.
        ($script:AppCode -match 'Marshal\.AllocHGlobal') | Should -BeTrue
        ($script:AppCode -match 'Marshal\.ReadInt32')    | Should -BeTrue
    }

    It 'frees what it allocated' {
        ($script:AppCode -match 'Marshal\.FreeHGlobal') | Should -BeTrue
    }
}

Describe 'Get-GameInfo' -Tag 'Unit' {

    BeforeAll {
        $script:GameA = New-FixtureGame -Slug 'hollow_knight' -ExeMb 6 -Parts 2
        $script:GameB = New-FixtureGame -Slug 'deus_ex'       -ExeMb 4
        $script:Torn  = New-FixtureGame -Slug 'torn_download' -ExeMb 2 -Parts 4 -SkipParts @(2,3)
    }

    It 'finds the installer' {
        (Get-GameInfo $script:GameA).Ok | Should -BeTrue
    }

    It 'collects the installer and its parts' {
        (Get-GameInfo $script:GameA).Files.Count | Should -Be 3
    }

    It 'totals the payload across every file' {
        (Get-GameInfo $script:GameA).TotalBytes | Should -Be (8MB)
    }

    It 'reports no missing parts for a complete download' {
        (Get-GameInfo $script:GameA).MissingParts.Count | Should -Be 0
    }

    It 'reports no missing parts for a game that has none' {
        (Get-GameInfo $script:GameB).MissingParts.Count | Should -Be 0
    }

    It 'spots a gap in the part numbering' {
        # -1 and -4 present, -2 and -3 absent: an unfinished download, which would
        # otherwise build a disc that only fails once the installer runs.
        ((Get-GameInfo $script:Torn).MissingParts -join ',') | Should -Be '2,3'
    }

    It 'recognises a built disc folder and says so' {
        # The output folder sits next to the GOG download and is named alike, so it
        # gets picked by mistake - and the generic "no installer here" sends people
        # looking for a problem with their download rather than at which folder they
        # chose. It has the installer one level down in disc\.
        $built = Join-Path $script:Sandbox 'Some Game Disc'
        New-Item -ItemType Directory -Force -Path (Join-Path $built 'disc') | Out-Null
        $fs = [IO.File]::Create((Join-Path $built 'disc\setup_some_game_1.0_(1).exe')); $fs.SetLength(1MB); $fs.Close()
        $info = Get-GameInfo $built
        $info.Ok  | Should -BeFalse
        $info.Msg | Should -Match 'disc DiscWright built'
    }

    It 'still gives the plain message for a folder with nothing in it' {
        $bare = Join-Path $script:Sandbox 'just-an-empty-folder'
        New-Item -ItemType Directory -Force -Path $bare | Out-Null
        (Get-GameInfo $bare).Msg | Should -Match 'No GOG'
    }

    It 'is not Ok for a folder that does not exist' {
        (Get-GameInfo (Join-Path $script:Sandbox 'no-such-folder')).Ok | Should -BeFalse
    }

    It 'leaves Folder null when it found nothing' {
        (Get-GameInfo (Join-Path $script:Sandbox 'no-such-folder')).Folder | Should -BeNullOrEmpty
    }
}

Describe 'Project file' -Tag 'Unit' {

    BeforeAll {
        $script:PGames = @(
            (Get-GameInfo (New-FixtureGame -Slug 'game_one')),
            (Get-GameInfo (New-FixtureGame -Slug 'game_two')),
            (Get-GameInfo (New-FixtureGame -Slug 'game_three'))
        )
        $script:POut = Join-Path $script:Sandbox 'proj-v2'
        New-Item -ItemType Directory -Force -Path $script:POut | Out-Null
        Save-Project (New-BuildSettings -Games $script:PGames -Label 'Trilogy' -OutDir $script:POut) $script:POut
        $script:PJson = Get-Content -Raw (Join-Path $script:POut 'discproject.json') | ConvertFrom-Json
    }

    Context 'writing' {

        It 'declares schema version 13' {
            $script:PJson.Version | Should -Be 13
        }

        It 'records where each entry came from' {
            # Version 9. A GOG download and a folder of game files are told apart
            # here, because reopening has to re-read them differently: one is
            # searched for a setup_*.exe, the other is taken as it is.
            $script:PJson.Games[0].Source | Should -Be 'GOG'
        }

        It 'records which disc the set was planned for' {
            # Version 5. A project saved with the automatic setting stores an
            # empty key, which is what reopens as "fit on one disc" - the same
            # thing a version 4 file with no key at all reopens as.
            $script:PJson.PSObject.Properties.Name | Should -Contain 'MediaKey'
            $script:PJson.MediaKey | Should -Be ''
        }

        It 'hands the chosen medium back when the project is read again' {
            # The bug this pins: Save-Project wrote MediaKey correctly and
            # Import-Project never copied it into what it returns, so reopening
            # any project silently dropped the target disc. Writing the file was
            # tested; reading it back was not.
            $out = Join-Path $script:POut 'roundtrip'
            New-Item -ItemType Directory -Force -Path $out | Out-Null
            $s = New-BuildSettings -Games $script:PGames -Label 'Trilogy' -OutDir $out
            $s.MediaKey = 'BD25'
            Save-Project $s $out
            $back = Import-Project (Join-Path $out 'discproject.json')
            $back.MediaKey | Should -Be 'BD25'
        }

        It 'reads a project from before target discs existed as no medium at all' {
            $out = Join-Path $script:POut 'v4'
            New-Item -ItemType Directory -Force -Path $out | Out-Null
            $s = New-BuildSettings -Games $script:PGames -Label 'Trilogy' -OutDir $out
            Save-Project $s $out
            $f = Join-Path $out 'discproject.json'
            # Strip the key back out, which is what a 0.4.1 file looks like.
            $j = Get-Content -Raw $f | ConvertFrom-Json
            $j.PSObject.Properties.Remove('MediaKey')
            $j | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $f -Encoding UTF8
            (Import-Project $f).MediaKey | Should -Be ''
        }

        It 'keeps the chosen medium when there is one' {
            $out = Join-Path $script:POut 'withmedia'
            New-Item -ItemType Directory -Force -Path $out | Out-Null
            $s = New-BuildSettings -Games $script:PGames -Label 'Trilogy' -OutDir $out
            $s.MediaKey = 'DVD5'
            Save-Project $s $out
            $j = Get-Content -Raw (Join-Path $out 'discproject.json') | ConvertFrom-Json
            $j.MediaKey | Should -Be 'DVD5'
        }

        It 'records a Kind and a Parent for every entry' {
            @($script:PJson.Games | Where-Object { $_.Kind -eq 'Game' }).Count | Should -Be 3
            @($script:PJson.Games | Where-Object { $_.Parent -eq -1 }).Count   | Should -Be 3
        }

        It 'records every game' {
            @($script:PJson.Games).Count | Should -Be 3
        }

        It 'still writes v1 SourceFolder so the file opens in 0.1.x' {
            $script:PJson.SourceFolder | Should -Be $script:PGames[0].Folder
        }

        It 'still writes v1 GameName so the file opens in 0.1.x' {
            $script:PJson.GameName | Should -Be $script:PGames[0].GameName
        }

        It 'records which DiscWright wrote it' {
            $script:PJson.AppVersion | Should -Be $script:AppVersionInSource
        }

        It 'keeps the app version separate from the schema version' {
            # One field could not have said both: the schema went to 2 for the
            # games list while the app was independently at 0.2.0.
            $script:PJson.AppVersion | Should -Not -Be $script:PJson.Version
        }
    }

    Context 'reading back' {

        It 'returns all three folders' {
            @((Import-Project (Join-Path $script:POut 'discproject.json')).GameFolders).Count | Should -Be 3
        }

        It 'preserves their order' {
            $back = Import-Project (Join-Path $script:POut 'discproject.json')
            (@($back.GameFolders) -join '|') | Should -Be (($script:PGames | ForEach-Object { $_.Folder }) -join '|')
        }
    }

    Context 'a project written by v0.1.0' {

        BeforeAll {
            # Written by hand exactly as the old Save-Project did, so this is a
            # real compatibility test rather than a round-trip of our own output.
            $script:V1Out = Join-Path $script:Sandbox 'proj-v1'
            New-Item -ItemType Directory -Force -Path $script:V1Out | Out-Null
            @{ Version=1; SavedUtc='2026-08-16T20:30:55'
               SourceFolder=$script:PGames[1].Folder; GameName='Game Two'; Label='Game Two'
               IconPath=$script:Art; IconIsIco=$false; Menu=$true; BgPath=$script:Bg; BgAsIs=$false
               PanelSide='Left'; Divider=$true; ShowTitle=$true; TitleText='G2'
               WindowBorder=$false; ButtonStyle='Bordered'; MusicFile=$null
               Buttons=@('Play','Exit'); ManualPath=$null; ExtrasPath=$null; ExtraItems=@()
               OutDir=$script:V1Out } | ConvertTo-Json -Depth 4 |
                Set-Content (Join-Path $script:V1Out 'discproject.json') -Encoding UTF8
            $script:V1Back = Import-Project (Join-Path $script:V1Out 'discproject.json')
        }

        It 'still opens' {
            $script:V1Back | Should -Not -BeNullOrEmpty
        }

        It 'upconverts its single game into a one-element list' {
            @($script:V1Back.GameFolders).Count | Should -Be 1
        }

        It 'points at the folder the old file named' {
            @($script:V1Back.GameFolders)[0] | Should -Be $script:PGames[1].Folder
        }

        It 'carries the rest of the settings across' {
            $script:V1Back.PanelSide   | Should -Be 'Left'
            $script:V1Back.Divider     | Should -BeTrue
            $script:V1Back.ButtonStyle | Should -Be 'Bordered'
        }
    }

    Context 'a project written by v0.2.0' {

        BeforeAll {
            # Schema 2 introduced the Games array and nothing else: no Kind, no
            # Parent, no Setup, no Manual, no Extras. Written by hand for the
            # same reason as the v1 file above - a round-trip of our own output
            # proves we can read what we just wrote, not what somebody's older
            # DiscWright wrote two schemas ago.
            $script:V2Out = Join-Path $script:Sandbox 'proj-v2-old'
            New-Item -ItemType Directory -Force -Path $script:V2Out | Out-Null
            @{ Version=2; SavedUtc='2026-08-17T10:00:00'
               SourceFolder=$script:PGames[0].Folder; GameName='Game One'; Label='Trilogy'
               Games=@(
                   @{ Folder=$script:PGames[0].Folder; GameName='Game One' }
                   @{ Folder=$script:PGames[1].Folder; GameName='Game Two' }
                   @{ Folder=$script:PGames[2].Folder; GameName='Game Three' }
               )
               IconPath=$script:Art; IconIsIco=$false; Menu=$true; BgPath=$script:Bg; BgAsIs=$false
               PanelSide='Right'; Divider=$false; ShowTitle=$false; TitleText=''
               WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
               Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null; ExtraItems=@()
               OutDir=$script:V2Out } | ConvertTo-Json -Depth 4 |
                Set-Content (Join-Path $script:V2Out 'discproject.json') -Encoding UTF8
            $script:V2Back = Import-Project (Join-Path $script:V2Out 'discproject.json')
        }

        It 'still opens' {
            $script:V2Back | Should -Not -BeNullOrEmpty
        }

        It 'returns every game in the array, not just the v1 SourceFolder' {
            @($script:V2Back.GameFolders).Count | Should -Be 3
        }

        It 'reads entries with no Kind as all games and no add-ons' {
            # Which is exactly what a version 2 disc was: add-ons did not exist.
            @($script:V2Back.GameEntries | Where-Object { $_.Kind -eq 'Game' }).Count    | Should -Be 3
            @($script:V2Back.GameEntries | Where-Object { $_.ParentIndex -ne -1 }).Count | Should -Be 0
        }

        It 'leaves Setup unset so the installer is detected again' {
            # v2 recorded only the folder. Inventing a Setup here would be worse
            # than leaving it blank - re-detection finds the real one.
            @($script:V2Back.GameEntries | Where-Object { $_.Setup }).Count | Should -Be 0
        }
    }

    Context 'a project written by v0.3.x' {

        BeforeAll {
            # Schema 3 added Kind, Parent and Setup. Manual and Extras arrived in
            # 4, so an add-on parented correctly but nothing had media of its own.
            $script:V3Out = Join-Path $script:Sandbox 'proj-v3-old'
            New-Item -ItemType Directory -Force -Path $script:V3Out | Out-Null
            @{ Version=3; SavedUtc='2026-08-18T10:00:00'
               SourceFolder=$script:PGames[0].Folder; GameName='Game One'; Label='Trilogy'
               Games=@(
                   @{ Folder=$script:PGames[0].Folder; GameName='Game One'
                      Setup=$script:PGames[0].SetupExe.FullName; Kind='Game'; Parent=-1 }
                   @{ Folder=$script:PGames[0].Folder; GameName='Patch 1.1'
                      Setup=$script:PGames[0].SetupExe.FullName; Kind='AddOn'; Parent=0 }
                   @{ Folder=$script:PGames[1].Folder; GameName='Game Two'
                      Setup=$script:PGames[1].SetupExe.FullName; Kind='Game'; Parent=-1 }
               )
               IconPath=$script:Art; IconIsIco=$false; Menu=$true; BgPath=$script:Bg; BgAsIs=$false
               PanelSide='Right'; Divider=$false; ShowTitle=$false; TitleText=''
               WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
               Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null; ExtraItems=@()
               OutDir=$script:V3Out } | ConvertTo-Json -Depth 4 |
                Set-Content (Join-Path $script:V3Out 'discproject.json') -Encoding UTF8
            $script:V3Back = Import-Project (Join-Path $script:V3Out 'discproject.json')
        }

        It 'still opens' {
            $script:V3Back | Should -Not -BeNullOrEmpty
        }

        It 'keeps the add-on attached to the game it belongs to' {
            $addOns = @($script:V3Back.GameEntries | Where-Object { $_.Kind -eq 'AddOn' })
            $addOns.Count          | Should -Be 1
            $addOns[0].ParentIndex | Should -Be 0
        }

        It 'reads an entry from before per-entry media as having none of its own' {
            # Version 4 added Manual and Extras. Absent here, which has to read
            # back as blank rather than as a path that was never written.
            @($script:V3Back.GameEntries | Where-Object { $_.Manual }).Count | Should -Be 0
            @($script:V3Back.GameEntries | Where-Object { $_.Extras }).Count | Should -Be 0
        }
    }

    It 'ignores ExtrasEveryDisc left behind by the disc-sets feature' {
        # Disc sets were removed in 0.4.2. Projects written while they existed
        # carry a key nothing reads any more; the only requirement is that its
        # presence is not an error. Save-Project writes a fixed set of keys, so
        # this has to be injected into the file afterwards - passing it in the
        # settings hashtable never reaches the JSON, and a test that did that
        # would pass while proving nothing.
        $out = Join-Path $script:POut 'extraseverydisc'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        Save-Project (New-BuildSettings -Games $script:PGames -Label 'Trilogy' -OutDir $out) $out
        $f = Join-Path $out 'discproject.json'
        $j = Get-Content -Raw $f | ConvertFrom-Json
        $j | Add-Member -NotePropertyName 'ExtrasEveryDisc' -NotePropertyValue $true
        $j | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $f -Encoding UTF8
        $back = Import-Project $f
        $back                      | Should -Not -BeNullOrEmpty
        @($back.GameFolders).Count | Should -Be 3
        $back.Label                | Should -Be 'Trilogy'
    }

    Context 'a real project file written by 0.4.2' {

        BeforeAll {
            # Not a reconstruction. This is the file 0.4.2 actually wrote for a
            # five-entry disc, kept in the repo so that "does an old project still
            # open" has an answer which does not depend on what happens to be left
            # on somebody's machine. Its paths point at folders that no longer
            # exist, which is the normal state of an old project and is why
            # Import-Project does not check them.
            $script:RealPath = Join-Path $PSScriptRoot 'fixtures\discproject-0.4.2.json'
            $script:RealBack = Import-Project $script:RealPath
        }

        It 'is still in the repo' {
            Test-Path $script:RealPath | Should -BeTrue
        }

        It 'opens' {
            $script:RealBack | Should -Not -BeNullOrEmpty
        }

        It 'reads through the UTF-8 BOM every project file carries' {
            # PowerShell 5.1 writes a BOM for -Encoding UTF8, so every project
            # file in the wild has one. A reader that chokes on it opens nothing,
            # and this fixture is only evidence if it still has its BOM.
            ([IO.File]::ReadAllBytes($script:RealPath)[0..2] -join ',') | Should -Be '239,187,191'
        }

        It 'returns all five entries' {
            @($script:RealBack.GameEntries).Count | Should -Be 5
        }

        It 'keeps the two updates filed under their games rather than beside them' {
            $addOns = @($script:RealBack.GameEntries | Where-Object { $_.Kind -eq 'AddOn' })
            $addOns.Count | Should -Be 2
            (@($addOns | ForEach-Object { $_.ParentIndex }) -join ',') | Should -Be '0,2'
        }

        It 'brings back the label, the icon, the background and the music' {
            $script:RealBack.Label     | Should -Be 'Alan Wake'
            $script:RealBack.IconPath  | Should -Match 'alanwake-icon\.ico$'
            $script:RealBack.BgPath    | Should -Match 'alanwake-background\.jpg$'
            $script:RealBack.MusicFile | Should -Match '\.mp3$'
        }

        It 'reads its empty MediaKey as the automatic setting' {
            # The disc this file describes was built without picking a target
            # disc, so reopening it has to land on "recommend a disc for me" -
            # not on a medium nobody ever chose.
            $script:RealBack.MediaKey | Should -Be ''
        }
    }

    It 'survives a project file that is not valid JSON' {
        $junk = Join-Path $script:Sandbox 'junk'
        New-Item -ItemType Directory -Force -Path $junk | Out-Null
        Set-Content (Join-Path $junk 'discproject.json') -Value '{ this is not json' -Encoding UTF8
        Import-Project (Join-Path $junk 'discproject.json') | Should -BeNullOrEmpty
    }
}

Describe 'Building a disc' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    Context 'one game' {

        BeforeAll {
            $script:One    = Get-GameInfo (New-FixtureGame -Slug 'single_game' -ExeMb 3 -Parts 1)
            $script:OneOut = Join-Path $script:Sandbox 'build-one'
            New-Item -ItemType Directory -Force -Path $script:OneOut | Out-Null
            $script:OneIso   = Invoke-Build (New-BuildSettings -Games @($script:One) -Label 'Single Game' -OutDir $script:OneOut) $script:LogSink
            $script:OneStage = Join-Path $script:OneOut 'disc'
        }

        It 'writes an ISO' {
            Test-Path $script:OneIso | Should -BeTrue
        }

        It 'leaves the installer at the disc root, as it always has' {
            Test-Path (Join-Path $script:OneStage $script:One.SetupExe.Name) | Should -BeTrue
        }

        It 'creates no Games folder for a single game' {
            Test-Path (Join-Path $script:OneStage 'Games') | Should -BeFalse
        }

        It 'writes autorun.inf' {
            Test-Path (Join-Path $script:OneStage 'autorun.inf') | Should -BeTrue
        }

        It 'writes the menu' {
            Test-Path (Join-Path $script:OneStage 'AUTORUN\menu.hta') | Should -BeTrue
        }

        It 'points the menu at a bare installer filename' {
            # Asserted as a boolean, not -Match: a failed -Match against a 30 KB
            # menu prints the whole menu into the test output.
            $hta = Get-Content -Raw (Join-Path $script:OneStage 'AUTORUN\menu.hta')
            $wanted = 's:"' + $script:One.SetupExe.Name + '"'
            $hta.Contains($wanted) | Should -BeTrue
        }

        It 'opens straight on the game, with no chooser for one game' {
            $hta = Get-Content -Raw (Join-Path $script:OneStage 'AUTORUN\menu.hta')
            # One entry in GAMES is what makes cur start at 0 rather than -1.
            ([regex]::Matches($hta,'\{n:"')).Count | Should -Be 1
        }

        It 'names the icon after the disc, not disc.ico' {
            # Explorer caches drive icons by path, so a fixed name would serve the
            # previous disc's icon for the same drive letter.
            Test-Path (Join-Path $script:OneStage 'SingleGame.ico') | Should -BeTrue
        }

        It 'rebuilds immediately without a file lock' {
            # Regression test: the ISO builder used to hold COM handles on its own
            # staged files, so a second build failed until a GC happened to run.
            { Invoke-Build (New-BuildSettings -Games @($script:One) -Label 'Single Game' -OutDir $script:OneOut) $script:LogSink } |
                Should -Not -Throw
        }
    }

    Context 'three games' {

        BeforeAll {
            $script:Many = @(
                (Get-GameInfo (New-FixtureGame -Slug 'first_game'  -ExeMb 2 -Parts 1)),
                (Get-GameInfo (New-FixtureGame -Slug 'second_game' -ExeMb 2)),
                (Get-GameInfo (New-FixtureGame -Slug 'third_game'  -ExeMb 2 -Parts 2))
            )
            $script:ManyOut = Join-Path $script:Sandbox 'build-many'
            New-Item -ItemType Directory -Force -Path $script:ManyOut | Out-Null
            $script:ManyIso   = Invoke-Build (New-BuildSettings -Games $script:Many -Label 'Test Trilogy' -OutDir $script:ManyOut) $script:LogSink
            $script:ManyStage = Join-Path $script:ManyOut 'disc'
        }

        It 'writes an ISO' {
            Test-Path $script:ManyIso | Should -BeTrue
        }

        It 'moves the installers into Games' {
            Test-Path (Join-Path $script:ManyStage 'Games') | Should -BeTrue
        }

        It 'leaves no installer at the disc root' {
            @(Get-ChildItem $script:ManyStage -Filter 'setup_*' -File).Count | Should -Be 0
        }

        It 'gives each game its own folder' {
            @(Get-ChildItem (Join-Path $script:ManyStage 'Games') -Directory).Count | Should -Be 3
        }

        It 'numbers the folders in order' {
            $names = @(Get-ChildItem (Join-Path $script:ManyStage 'Games') -Directory | Sort-Object Name | ForEach-Object { $_.Name })
            $names[0] | Should -BeLike '01 - *'
            $names[1] | Should -BeLike '02 - *'
            $names[2] | Should -BeLike '03 - *'
        }

        It 'copies every installer file' {
            $expected = ($script:Many | ForEach-Object { $_.Files.Count } | Measure-Object -Sum).Sum
            @(Get-ChildItem (Join-Path $script:ManyStage 'Games') -Recurse -File).Count | Should -Be $expected
        }

        It 'points the menu into the game folders that were actually written' {
            # The staging copy and the menu work the on-disc path out separately,
            # so this checks they agree - a disagreement burns a disc whose
            # Install button is greyed out with nothing on screen explaining why.
            $hta = Get-Content -Raw (Join-Path $script:ManyStage 'AUTORUN\menu.hta')
            foreach ($m in [regex]::Matches($hta,'s:"([^"]+)"')) {
                $rel = $m.Groups[1].Value -replace '\\\\','\'
                Test-Path (Join-Path $script:ManyStage $rel) | Should -BeTrue -Because "the menu points at $rel"
            }
        }

        It 'gives the menu one entry per game' {
            $hta = Get-Content -Raw (Join-Path $script:ManyStage 'AUTORUN\menu.hta')
            ([regex]::Matches($hta,'\{n:"')).Count | Should -Be 3
        }

        It 'saves a project naming all three games' {
            @((Import-Project (Join-Path $script:ManyOut 'discproject.json')).GameFolders).Count | Should -Be 3
        }

        It 'rebuilds without duplicating the game folders' {
            $null = Invoke-Build (New-BuildSettings -Games $script:Many -Label 'Test Trilogy' -OutDir $script:ManyOut) $script:LogSink
            @(Get-ChildItem (Join-Path $script:ManyStage 'Games') -Directory).Count | Should -Be 3
        }
    }

    Context 'progress reporting' {

        BeforeAll {
            # 40 MB so the writer loops enough times for the reporting interval to
            # mean something; sparse files make it cost nothing.
            $script:ProgGame = Get-GameInfo (New-FixtureGame -Slug 'progress_game' -ExeMb 20 -Parts 20)
            $script:ProgOut  = Join-Path $script:Sandbox 'build-progress'
            New-Item -ItemType Directory -Force -Path $script:ProgOut | Out-Null

            $script:ProgCalls = New-Object System.Collections.ArrayList
            $recorder = { param($done,$total) [void]$script:ProgCalls.Add(@{ Done=$done; Total=$total }) }
            $script:ProgIso = Invoke-Build (New-BuildSettings -Games @($script:ProgGame) -Label 'Progress Test' -OutDir $script:ProgOut) $script:LogSink $recorder
        }

        It 'calls back while writing' {
            $script:ProgCalls.Count | Should -BeGreaterThan 0
        }

        It 'calls back a sane number of times, not once and not per block' {
            # The interval is derived from the image size to aim at ~200 reports,
            # because every call crosses back into PowerShell and pumps the message
            # queue. A fixed block count would fire a handful of times on a CD and
            # tens of thousands on a BD-R XL.
            $script:ProgCalls.Count | Should -BeGreaterOrEqual 5
            $script:ProgCalls.Count | Should -BeLessOrEqual 400
        }

        It 'never goes backwards' {
            $done = @($script:ProgCalls | ForEach-Object { $_.Done })
            $backwards = $false
            for ($i = 1; $i -lt $done.Count; $i++) { if ($done[$i] -lt $done[$i-1]) { $backwards = $true } }
            $backwards | Should -BeFalse
        }

        It 'never reports more than the total' {
            @($script:ProgCalls | Where-Object { $_.Done -gt $_.Total -or $_.Done -lt 0 }).Count | Should -Be 0
        }

        It 'reports the same total throughout' {
            @($script:ProgCalls | ForEach-Object { $_.Total } | Sort-Object -Unique).Count | Should -Be 1
        }

        It 'finishes on exactly 100 percent' {
            # Without the final call after the loop, the bar stops a fraction short
            # and the window looks stuck at 99% while the COM release runs.
            $last = $script:ProgCalls[$script:ProgCalls.Count - 1]
            $last.Done | Should -Be $last.Total
        }

        It 'still produces a working ISO' {
            Test-Path $script:ProgIso | Should -BeTrue
        }

        It 'builds fine with no callback at all' {
            $out = Join-Path $script:Sandbox 'build-nocallback'
            New-Item -ItemType Directory -Force -Path $out | Out-Null
            { Invoke-Build (New-BuildSettings -Games @($script:ProgGame) -Label 'No Callback' -OutDir $out) $script:LogSink } |
                Should -Not -Throw
        }
    }

    Context 'inside the finished ISO' -Skip:(-not $script:SevenZip) {

        BeforeAll {
            $script:IsoGames = @(
                (Get-GameInfo (New-FixtureGame -Slug 'iso_one' -ExeMb 2)),
                (Get-GameInfo (New-FixtureGame -Slug 'iso_two' -ExeMb 2 -Parts 1))
            )
            $script:IsoOut = Join-Path $script:Sandbox 'build-iso'
            New-Item -ItemType Directory -Force -Path $script:IsoOut | Out-Null
            $script:IsoPath  = Invoke-Build (New-BuildSettings -Games $script:IsoGames -Label 'Iso Check' -OutDir $script:IsoOut) $script:LogSink
            $script:IsoFiles = Get-IsoEntries -IsoPath $script:IsoPath -SevenZip $script:SevenZip
        }

        Context 'and again with the Linux files asked for' {

            BeforeAll {
                $script:LxOut = Join-Path $script:Sandbox 'build-iso-linux'
                New-Item -ItemType Directory -Force -Path $script:LxOut | Out-Null
                $script:LxIso = Invoke-Build (New-BuildSettings -Games $script:IsoGames -Label 'Iso Check' -OutDir $script:LxOut -LinuxInfo) $script:LogSink
                $script:LxFiles = Get-IsoEntries -IsoPath $script:LxIso -SevenZip $script:SevenZip
            }

            It 'contains the Linux half beside the Windows one' {
                # Asserted against the ISO rather than the staging folder on
                # purpose: ISO 9660 forbids a leading dot, and a filesystem that
                # quietly renamed .xdg-volume-info would leave the disc nameless
                # on Linux with everything on Windows still perfect. DiscWright
                # writes UDF only, which allows it - this proves that still holds.
                $script:LxFiles | Should -Contain '.xdg-volume-info'
                $expectedPng = [IO.Path]::ChangeExtension((Get-DiscIconName 'Iso Check'), 'png')
                $script:LxFiles | Should -Contain $expectedPng
            }

            It 'still carries everything Windows needs' {
                # The point of a hybrid disc: nothing is given up to gain it.
                $script:LxFiles | Should -Contain 'autorun.inf'
                $script:LxFiles | Should -Contain 'AUTORUN\menu.hta'
            }

            It 'gives the Linux icon a name that matches the file it points at' {
                # IconFile= resolves relative to the disc root, so the name in the
                # file and the name on the disc have to be the same string.
                $tmp = Join-Path $script:Sandbox 'xdg-from-iso'
                if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
                & $script:SevenZip e $script:LxIso ".xdg-volume-info" "-o$tmp" -y *> $null
                $txt = [IO.File]::ReadAllText((Join-Path $tmp '.xdg-volume-info'))
                $named = @($txt -split "`n" | Where-Object { $_ -like 'IconFile=*' }) -replace '^IconFile=',''
                $script:LxFiles | Should -Contain $named
            }
        }

        It 'is a real UDF image, not just a file that exists' {
            $info = & $script:SevenZip l -slt $script:IsoPath 2>&1
            ($info | Where-Object { $_ -match '^Type = Udf' }) | Should -Not -BeNullOrEmpty
        }

        It 'uses UDF 2.50, so long GOG filenames survive' {
            $info = & $script:SevenZip l -slt $script:IsoPath 2>&1
            ($info | Where-Object { $_ -match '^Version = 2\.50' }) | Should -Not -BeNullOrEmpty
        }

        It 'carries the disc label as the volume id' {
            Get-IsoVolumeId -IsoPath $script:IsoPath -SevenZip $script:SevenZip | Should -Be 'Iso_Check'
        }

        It 'contains autorun.inf at the root' {
            $script:IsoFiles | Should -Contain 'autorun.inf'
        }

        It 'carries nothing for Linux unless it was asked to' {
            # The default. A disc built without ticking the box is the disc
            # DiscWright has always built, and this is what says so.
            $script:IsoFiles | Should -Not -Contain '.xdg-volume-info'
            $expectedPng = [IO.Path]::ChangeExtension((Get-DiscIconName 'Iso Check'), 'png')
            $script:IsoFiles | Should -Not -Contain $expectedPng
        }

        It 'contains the menu' {
            $script:IsoFiles | Should -Contain 'AUTORUN\menu.hta'
        }

        It 'contains the composed background' {
            $script:IsoFiles | Should -Contain 'AUTORUN\bg.png'
        }

        It 'contains both game folders' {
            @($script:IsoFiles | Where-Object { $_ -match '^Games\\\d\d - ' -and $_ -notmatch '\.' }).Count |
                Should -BeGreaterOrEqual 2
        }

        It 'contains every installer file' {
            $expected = ($script:IsoGames | ForEach-Object { $_.Files.Count } | Measure-Object -Sum).Sum
            @($script:IsoFiles | Where-Object { $_ -match '^Games\\.*(setup_.*\.exe|\.bin)$' }).Count | Should -Be $expected
        }

        It 'passes an integrity check, not just a listing' {
            # The writer copies block by block through unmanaged memory. Listing the
            # entries only proves the directory survived; this reads the data back.
            $t = & $script:SevenZip t $script:IsoPath 2>&1
            @($t | Where-Object { $_ -match 'Everything is Ok' }).Count | Should -BeGreaterThan 0
        }
    }
}

# ---------------------------------------------------------------------------
# Multi-game chooser and add-ons
# ---------------------------------------------------------------------------

Describe 'The finished ISO as Windows itself reads it' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    # 7-Zip proves the image is well formed. It does not prove Windows will mount
    # it, and mounting is the first thing anybody does with the file - right-click,
    # Mount, look at This PC. Everything here goes through the real storage stack
    # instead: the volume label Explorer shows, the autorun.inf it reads, the icon
    # and the menu it is pointed at.
    #
    # Mounting needs no elevation - measured on Windows 11, not assumed. A machine
    # that refuses anyway is not a DiscWright defect, so these skip with the reason
    # rather than failing and blaming the code.

    BeforeAll {
        $script:MountLabel = 'THE WITCHER ENH EDITION'
        $script:MountOut   = Join-Path $script:Sandbox 'build-mount'
        New-Item -ItemType Directory -Force -Path $script:MountOut | Out-Null
        $script:MountIso = Invoke-Build (New-BuildSettings `
            -Games @((Get-GameInfo (New-FixtureGame -Slug 'mount_one' -ExeMb 2))) `
            -Label $script:MountLabel -OutDir $script:MountOut) $script:LogSink

        $script:MountVol = $null; $script:MountErr = $null; $script:MountRoot = $null
        try {
            $img = Mount-DiskImage -ImagePath $script:MountIso -StorageType ISO -PassThru -ErrorAction Stop
            # The volume can trail the mount by a moment, so this waits for a
            # drive letter rather than reading one that is not there yet and
            # reporting it as a defect in the image.
            for ($i = 0; $i -lt 20; $i++) {
                $v = $img | Get-Volume -ErrorAction SilentlyContinue
                if ($v -and $v.DriveLetter) { $script:MountVol = $v; break }
                Start-Sleep -Milliseconds 250
            }
            if (-not $script:MountVol) { $script:MountErr = 'mounted, but no drive letter appeared' }
            else { $script:MountRoot = "$($script:MountVol.DriveLetter):\" }
        } catch { $script:MountErr = $_.Exception.Message }

        function Test-Mounted {
            if (-not $script:MountVol) {
                Set-ItResult -Skipped -Because "this machine would not mount the image: $script:MountErr"
            }
        }

        $script:MountInf = ''
        if ($script:MountRoot) {
            $p = Join-Path $script:MountRoot 'autorun.inf'
            if (Test-Path $p) { $script:MountInf = Get-Content -Raw -LiteralPath $p }
        }
    }

    AfterAll {
        if ($script:MountIso) {
            Dismount-DiskImage -ImagePath $script:MountIso -ErrorAction SilentlyContinue | Out-Null
        }
    }

    It 'mounts as a drive Windows will open' {
        Test-Mounted
        $script:MountVol.DriveLetter | Should -Not -BeNullOrEmpty
    }

    It 'is UDF to Windows, not only to 7-Zip' {
        Test-Mounted
        $script:MountVol.FileSystem | Should -Be 'UDF'
    }

    It 'carries the volume id the app computed for the label' {
        # Ties Get-VolumeLabel to what the operating system actually reports,
        # which is the only place the two could ever have disagreed.
        Test-Mounted
        $script:MountVol.FileSystemLabel | Should -Be (Get-VolumeLabel $script:MountLabel)
    }

    It 'does not show a truncation artefact in This PC' {
        # The two failures the manual check told a person to look for: a stray
        # trailing underscore, and a leftover _D fragment from disc sets.
        Test-Mounted
        $script:MountVol.FileSystemLabel | Should -Not -Match '_$'
        $script:MountVol.FileSystemLabel | Should -Not -Match '_D\d?$'
    }

    It 'puts the full label in autorun.inf, which is what This PC really shows' {
        # The 16-character cap is the volume id underneath. AutoRun overrides it
        # with the label as typed, and that override is the whole reason a disc
        # called THE WITCHER ENH EDITION does not read as THE_WITCHER_ENH.
        Test-Mounted
        $script:MountInf | Should -Match ('(?m)^label=' + [regex]::Escape($script:MountLabel) + '\s*$')
    }

    It 'names an icon that is really on the disc' {
        Test-Mounted
        $script:MountInf | Should -Match '(?m)^icon='
        $icon = ([regex]::Match($script:MountInf, '(?m)^icon=(.+?)\s*$')).Groups[1].Value
        $icon | Should -Be (Get-DiscIconName $script:MountLabel)
        Test-Path (Join-Path $script:MountRoot $icon) | Should -BeTrue
    }

    It 'points AutoRun at a menu that is really on the disc' {
        # A shellexecute naming a file that is not there is a disc that opens
        # nothing when double-clicked, and no listing test can see it.
        Test-Mounted
        $script:MountInf | Should -Match '(?m)^shellexecute='
        $target = ([regex]::Match($script:MountInf, '(?m)^shellexecute=(.+?)\s*$')).Groups[1].Value
        Test-Path (Join-Path $script:MountRoot $target) | Should -BeTrue
    }

    It 'reaches the installer through the path the menu was given' {
        # The menu builds its Setup path at build time from the disc layout. If
        # that path and the files on the disc ever disagree, Install fails on a
        # burned disc and nowhere else.
        Test-Mounted
        $games = @(Get-GameInfo (Join-Path $script:Sandbox 'src\mount_one'))
        $rel = Get-DiscEntrySetup $games 0
        Test-Path (Join-Path $script:MountRoot $rel) | Should -BeTrue
    }

    It 'opens read-only, the way a burned disc does' {
        Test-Mounted
        { New-Item -ItemType File -Path (Join-Path $script:MountRoot 'nope.txt') -ErrorAction Stop } |
            Should -Throw
    }
}

Describe 'Building a two-game disc that has add-ons on it' {

    BeforeAll {
        # Two games, two patches on the first and one on the second - the shape
        # from the roadmap, staged for real so the assertions are about folders
        # that exist rather than about strings a function returned.
        $script:GrpA  = Get-GameInfo (New-FixtureGame -Slug 'grp_a' -Parts 1)
        $script:GrpA1 = Get-GameInfo (New-FixtureGame -Slug 'grp_a_patch1')
        $script:GrpA2 = Get-GameInfo (New-FixtureGame -Slug 'grp_a_patch2')
        $script:GrpB  = Get-GameInfo (New-FixtureGame -Slug 'grp_b')
        $script:GrpB1 = Get-GameInfo (New-FixtureGame -Slug 'grp_b_patch1')
        $script:GrpA1.Kind = 'AddOn'; $script:GrpA1.ParentIndex = 0
        $script:GrpA2.Kind = 'AddOn'; $script:GrpA2.ParentIndex = 0
        $script:GrpB1.Kind = 'AddOn'; $script:GrpB1.ParentIndex = 3

        $script:GrpAll = @($script:GrpA, $script:GrpA1, $script:GrpA2, $script:GrpB, $script:GrpB1)

        $script:GrpOut = Join-Path $script:Sandbox 'out-grouped'
        New-Item -ItemType Directory -Force -Path $script:GrpOut | Out-Null
        $null = Invoke-Build (New-BuildSettings -Games $script:GrpAll `
                    -Label 'Grouped Disc' -OutDir $script:GrpOut) $script:LogSink
        $script:GrpStage = Join-Path $script:GrpOut 'disc'
    }

    It 'puts one folder under Games for each game, and nothing else' {
        # Five installers, two games. Before grouping this folder held five.
        @(Get-ChildItem (Join-Path $script:GrpStage 'Games') -Directory).Count | Should -Be 2
    }

    It 'nests each game''s add-ons inside that game, numbered from 01' {
        $gameA = Join-Path $script:GrpStage (Get-DiscEntryFolder $script:GrpAll 0)
        $gameB = Join-Path $script:GrpStage (Get-DiscEntryFolder $script:GrpAll 3)
        @(Get-ChildItem (Join-Path $gameA 'Add-ons') -Directory).Count | Should -Be 2
        @(Get-ChildItem (Join-Path $gameB 'Add-ons') -Directory).Count | Should -Be 1
        @(Get-ChildItem (Join-Path $gameA 'Add-ons') -Directory | ForEach-Object { $_.Name }) |
            ForEach-Object { $_ | Should -Match '^0[12] - ' }
        (Get-ChildItem (Join-Path $gameB 'Add-ons') -Directory)[0].Name | Should -BeLike '01 - *'
    }

    It 'writes every installer to the path the menu was given' {
        # The one that matters on a burned disc. The menu's Install hands the
        # shell this path; if the staging loop wrote anywhere else, the button
        # fails and nothing on screen says why.
        foreach ($g in (Get-MenuGames $script:GrpAll)) {
            Test-Path (Join-Path $script:GrpStage $g.Setup) | Should -BeTrue -Because "the game's installer must be at $($g.Setup)"
            foreach ($a in $g.AddOns) {
                Test-Path (Join-Path $script:GrpStage $a.Setup) | Should -BeTrue -Because "the add-on's installer must be at $($a.Setup)"
            }
        }
    }

    It 'brings a game''s .bin parts along into its own folder' {
        $gameA = Join-Path $script:GrpStage (Get-DiscEntryFolder $script:GrpAll 0)
        @(Get-ChildItem $gameA -File -Filter '*.bin').Count | Should -Be 1
    }

    It 'leaves no installer at the disc root' {
        @(Get-ChildItem $script:GrpStage -File -Filter 'setup_*').Count | Should -Be 0
    }
}

Describe 'Where an entry lands on the disc' {

    BeforeAll {
        $script:LayoutOne = @( (Get-GameInfo (New-FixtureGame -Slug 'lay_one')) )
        $script:LayoutMany = @(
            (Get-GameInfo (New-FixtureGame -Slug 'lay_a'))
            (Get-GameInfo (New-FixtureGame -Slug 'lay_b'))
            (Get-GameInfo (New-FixtureGame -Slug 'lay_c'))
        )
    }

    It 'puts a lone installer at the disc root' {
        Get-DiscEntryFolder $script:LayoutOne 0 | Should -Be ''
        Get-DiscEntrySetup  $script:LayoutOne 0 | Should -Be $script:LayoutOne[0].SetupExe.Name
    }

    It 'numbers every entry once there is more than one' {
        (Get-DiscEntryFolder $script:LayoutMany 0) | Should -BeLike 'Games\01 - *'
        (Get-DiscEntryFolder $script:LayoutMany 1) | Should -BeLike 'Games\02 - *'
        (Get-DiscEntryFolder $script:LayoutMany 2) | Should -BeLike 'Games\03 - *'
    }

    It 'builds the setup path from that same folder' {
        $rel = Get-DiscEntrySetup $script:LayoutMany 1
        $rel | Should -Be (Join-Path (Get-DiscEntryFolder $script:LayoutMany 1) $script:LayoutMany[1].SetupExe.Name)
    }
}

Describe 'Filing an add-on under the game it belongs to' -Tag 'Unit' {

    BeforeAll {
        function New-Ent {
            param([string]$Name, [string]$Kind = 'Game', [int]$Parent = -1)
            return @{ Ok=$true; GameName=$Name; Kind=$Kind; ParentIndex=$Parent
                      SetupExe=@{ Name = "setup_$($Name -replace '\W','').exe" } }
        }

        # The example from the roadmap: two games, patches on each. Under the old
        # layout this came out as five sibling folders numbered 01 to 05.
        $script:Grp = @(
            (New-Ent 'Hollow Knight')
            (New-Ent 'Update 1.5.12459' 'AddOn' 0)
            (New-Ent 'Update 1.5.12618' 'AddOn' 0)
            (New-Ent 'Ori and the Blind Forest')
            (New-Ent 'Definitive Edition Upgrade' 'AddOn' 3)
        )

        # One game carrying patches. Still one game, so still a flat disc.
        $script:GrpOne = @(
            (New-Ent 'Hollow Knight')
            (New-Ent 'Update 1.5.12459' 'AddOn' 0)
            (New-Ent 'Update 1.5.12618' 'AddOn' 0)
        )
    }

    It 'puts an add-on inside its game''s folder rather than beside it' {
        $game  = Get-DiscEntryFolder $script:Grp 0
        $addOn = Get-DiscEntryFolder $script:Grp 1
        $game  | Should -Be 'Games\01 - Hollow Knight'
        $addOn | Should -Be 'Games\01 - Hollow Knight\Add-ons\01 - Update 1.5.12459'
        # Stated twice on purpose: the string above is what a person reads on the
        # disc, and this is the property that has to hold for any name at all.
        $addOn.StartsWith($game + '\Add-ons\') | Should -BeTrue
    }

    It 'numbers the games by games, so the numbers stop skipping' {
        # The second game is the fourth entry. Numbering by entry made it 04 and
        # left 02 and 03 belonging to patches that no longer have a top-level
        # folder at all - a listing that counts to five and shows two things.
        Get-DiscEntryFolder $script:Grp 3 | Should -Be 'Games\02 - Ori and the Blind Forest'
    }

    It 'numbers add-ons within their own game, starting again at 01' {
        Get-DiscEntryFolder $script:Grp 2 |
            Should -Be 'Games\01 - Hollow Knight\Add-ons\02 - Update 1.5.12618'
        Get-DiscEntryFolder $script:Grp 4 |
            Should -Be 'Games\02 - Ori and the Blind Forest\Add-ons\01 - Definitive Edition Upgrade'
    }

    It 'keeps the flat root when the disc holds one game and its patches' {
        # A game with add-ons but no second game is one game. Wrapping a Games\
        # tree around a single folder buys nothing, and this is the disc every
        # single-game build has produced since before add-ons existed.
        Get-DiscEntryFolder $script:GrpOne 0 | Should -Be ''
        Get-DiscEntryFolder $script:GrpOne 1 | Should -Be 'Add-ons\01 - Update 1.5.12459'
        Get-DiscEntryFolder $script:GrpOne 2 | Should -Be 'Add-ons\02 - Update 1.5.12618'
    }

    It 'builds the installer path from wherever the folder turned out to be' {
        Get-DiscEntrySetup $script:GrpOne 1 |
            Should -Be 'Add-ons\01 - Update 1.5.12459\setup_Update1512459.exe'
        Get-DiscEntrySetup $script:Grp 4 | Should -Be (Join-Path (Get-DiscEntryFolder $script:Grp 4) `
                                                                $script:Grp[4].SetupExe.Name)
    }

    It 'files an add-on''s own manual and extras inside the add-on''s folder' {
        Get-DiscEntryExtras $script:Grp 1 |
            Should -Be 'Games\01 - Hollow Knight\Add-ons\01 - Update 1.5.12459\Extras'
    }

    It 'leaves a lone game and a pair of plain games exactly where they were' {
        # The rule that changed is which entries count. For a disc with no add-ons
        # on it, nothing may move - those discs have been burned already.
        Get-DiscEntryFolder @( (New-Ent 'Solo') ) 0 | Should -Be ''
        $two = @( (New-Ent 'One'), (New-Ent 'Two') )
        Get-DiscEntryFolder $two 0 | Should -Be 'Games\01 - One'
        Get-DiscEntryFolder $two 1 | Should -Be 'Games\02 - Two'
    }

    It 'gives an add-on whose game is missing a top-level folder of its own' {
        # Promotion is the menu's rule for an orphan, and the disc has to follow
        # it: an entry the chooser lists as a game cannot be buried inside one.
        $e = @( (New-Ent 'Hollow Knight'), (New-Ent 'Stray Patch' 'AddOn' 7) )
        Get-DiscEntryFolder $e 1 | Should -Be 'Games\02 - Stray Patch'
        (Get-MenuGames $e).Count | Should -Be 2
    }

    It 'promotes an add-on hanging off another add-on, on the disc as in the menu' {
        $e = @( (New-Ent 'Hollow Knight'), (New-Ent 'Update 1' 'AddOn' 0),
                (New-Ent 'Patch of a patch' 'AddOn' 1) )
        Get-DiscEntryFolder $e 2 | Should -Be 'Games\02 - Patch of a patch'
        Get-DiscEntryFolder $e 1 | Should -Be 'Games\01 - Hollow Knight\Add-ons\01 - Update 1'
    }

    It 'hands the menu the same paths the layout decided' {
        # The failure this guards against is silent and only visible on a burned
        # disc: the files land in one place and the menu's Install points at
        # another. Every path the menu emits, game and add-on alike, has to be
        # the one Get-DiscEntrySetup gave for that entry.
        $fromMenu = @()
        foreach ($g in (Get-MenuGames $script:Grp)) {
            $fromMenu += $g.Setup
            foreach ($a in $g.AddOns) { $fromMenu += $a.Setup }
        }
        $fromLayout = @(0..($script:Grp.Count-1) | ForEach-Object { Get-DiscEntrySetup $script:Grp $_ })
        @($fromMenu | Sort-Object) | Should -Be @($fromLayout | Sort-Object)
    }

    It 'never lands two entries in the same folder' {
        $mixed = @(
            (New-Ent 'A'), (New-Ent 'B' 'AddOn' 0), (New-Ent 'C' 'AddOn' 7)
            (New-Ent 'D'), (New-Ent 'E' 'AddOn' 3), (New-Ent 'F' 'AddOn' 1) )
        $folders = @(0..($mixed.Count-1) | ForEach-Object { Get-DiscEntryFolder $mixed $_ })
        @($folders | Sort-Object -Unique).Count | Should -Be $mixed.Count
    }

    It 'counts the same entries as games that the menu does' {
        $mixed = @(
            (New-Ent 'A'), (New-Ent 'B' 'AddOn' 0), (New-Ent 'C' 'AddOn' 7)
            (New-Ent 'D'), (New-Ent 'E' 'AddOn' 3), (New-Ent 'F' 'AddOn' 1) )
        # No @() around the call: Get-DiscGameIndexes returns ,@(...) and
        # wrapping it rebuilds the single-element array the comma prevents.
        (Get-DiscGameIndexes $mixed).Count | Should -Be (Get-MenuGames $mixed).Count
    }

    It 'treats Add-ons as a folder the pipeline made, not as content someone added' {
        # Reopening a built disc folder lists everything at the root that the
        # pipeline does not generate as the user's own extra content. Without
        # this, Add-ons\ on a single-game disc comes back as extra content and
        # the next build copies the disc into itself.
        Test-ReservedDiscName 'Add-ons' | Should -BeTrue
    }
}

Describe 'Sorting entries into games and their add-ons' {

    BeforeAll {
        # One helper rather than three fixtures: these tests care about Kind and
        # ParentIndex, not about what is in the folder.
        function New-Entry {
            param([string]$Name, [string]$Kind = 'Game', [int]$Parent = -1)
            return @{ Ok=$true; GameName=$Name; Kind=$Kind; ParentIndex=$Parent
                      SetupExe=@{ Name = "setup_$($Name -replace '\W','').exe" } }
        }
    }

    It 'leaves a list of plain games alone' {
        $m = Get-MenuGames @( (New-Entry 'One'), (New-Entry 'Two') )
        $m.Count | Should -Be 2
        @($m | Where-Object { $_.AddOns.Count -gt 0 }).Count | Should -Be 0
    }

    It 'hangs an add-on off its parent instead of listing it as a game' {
        $m = Get-MenuGames @( (New-Entry 'Deus Ex'), (New-Entry 'GMDX' 'AddOn' 0) )
        $m.Count | Should -Be 1
        $m[0].Name | Should -Be 'Deus Ex'
        $m[0].AddOns.Count | Should -Be 1
        $m[0].AddOns[0].Name | Should -Be 'GMDX'
    }

    It 'attaches an add-on to the right game when there are several' {
        $m = Get-MenuGames @(
            (New-Entry 'First'), (New-Entry 'Second'), (New-Entry 'Patch' 'AddOn' 1) )
        $m.Count | Should -Be 2
        $m[0].AddOns.Count | Should -Be 0
        $m[1].AddOns.Count | Should -Be 1
    }

    It 'gives an add-on a menu entry of its own rather than dropping it when the parent is nonsense' {
        # Losing an installer silently is the one outcome worth ruling out: the
        # disc is burned before anybody finds out it is missing.
        foreach ($bad in @(-1, 5, 99)) {
            $m = Get-MenuGames @( (New-Entry 'Game'), (New-Entry 'Orphan' 'AddOn' $bad) )
            $m.Count | Should -Be 2 -Because "parent $bad points at nothing"
        }
    }

    It 'does not let an add-on parent itself' {
        $m = Get-MenuGames @( (New-Entry 'Game'), (New-Entry 'Self' 'AddOn' 1) )
        $m.Count | Should -Be 2
    }

    It 'does not let an add-on hang off another add-on' {
        $m = Get-MenuGames @(
            (New-Entry 'Game'), (New-Entry 'Mod' 'AddOn' 0), (New-Entry 'ModPatch' 'AddOn' 1) )
        # Mod belongs to Game; ModPatch cannot belong to Mod, so it stands alone.
        $m.Count | Should -Be 2
        $m[0].AddOns.Count | Should -Be 1
    }

    It 'never loses an installer, whatever the parents say' {
        $entries = @(
            (New-Entry 'A'), (New-Entry 'B' 'AddOn' 0), (New-Entry 'C' 'AddOn' 7)
            (New-Entry 'D'), (New-Entry 'E' 'AddOn' 3), (New-Entry 'F' 'AddOn' 1) )
        $m = Get-MenuGames $entries
        $total = $m.Count + (($m | ForEach-Object { $_.AddOns.Count }) | Measure-Object -Sum).Sum
        $total | Should -Be $entries.Count
    }
}

Describe 'Building a disc that has an add-on on it' {

    BeforeAll {
        $script:AoGame  = Get-GameInfo (New-FixtureGame -Slug 'ao_base' -Parts 1)
        $script:AoMod   = Get-GameInfo (New-FixtureGame -Slug 'ao_mod')
        $script:AoMod.Kind = 'AddOn'
        $script:AoMod.ParentIndex = 0

        $script:AoOut = Join-Path $script:Sandbox 'out-addon'
        New-Item -ItemType Directory -Force -Path $script:AoOut | Out-Null
        $null = Invoke-Build (New-BuildSettings -Games @($script:AoGame, $script:AoMod) `
                    -Label 'AddOn Disc' -OutDir $script:AoOut) $script:LogSink
        $script:AoStage = Join-Path $script:AoOut 'disc'
        $script:AoHta   = Get-Content -Raw (Join-Path $script:AoStage 'AUTORUN\menu.hta')
    }

    It 'keeps a single game flat and files its add-on underneath it' {
        # One game, so the disc keeps the flat root it has always had - there is
        # no Games\ tree to wrap around one folder. The add-on goes into Add-ons\
        # beside the installer rather than becoming a second top-level entry.
        Test-Path (Join-Path $script:AoStage 'Games') | Should -BeFalse
        Test-Path (Join-Path $script:AoStage $script:AoGame.SetupExe.Name) | Should -BeTrue
        @(Get-ChildItem (Join-Path $script:AoStage 'Add-ons') -Directory).Count | Should -Be 1
    }

    It 'copies the add-on installer too' {
        $rel = Get-DiscEntrySetup @($script:AoGame, $script:AoMod) 1
        Test-Path (Join-Path $script:AoStage $rel) | Should -BeTrue
    }

    It 'shows one game in the menu, not two' {
        # The add-on must not turn up in the chooser as if it were a game.
        ([regex]::Matches($script:AoHta,'\{n:"')).Count | Should -Be 2   # game + its add-on
        ([regex]::Matches($script:AoHta,',a:\[\{n:"')).Count | Should -Be 1
    }

    It 'greys an add-on until the game it belongs to is installed' {
        # A patch, a piece of DLC or a mod goes ON TOP of its game. An Install
        # button that is live before the game exists can only produce an error
        # from GOG's installer, several clicks later - the worst place to learn
        # the rule. Being present on the disc is necessary, not sufficient.
        $call = [regex]::Match($script:AoHta, 'setEnabled\("btn_addon_"\+j,[^;]*;')
        $call.Success | Should -BeTrue -Because 'the add-on buttons must be enabled somewhere'
        $call.Value   | Should -Match 'parentOn' -Because 'the game being installed has to be part of the condition'
        $script:AoHta | Should -Match 'var parentOn = \(findGame\(g\.m\)!=null\)'
    }

    It 'says which of the two reasons an add-on is greyed out' {
        # "Not on the disc" and "the game is not installed yet" send you to
        # completely different places. One message for both would be wrong half
        # the time.
        $script:AoHta | Should -Match "is not installed yet - use Install first"
        $script:AoHta | Should -Match "This add-on's installer is not on the disc"
    }

    It 'points every path in the menu at a file that is really there' {
        foreach ($m in [regex]::Matches($script:AoHta,'s:"([^"]+)"')) {
            $rel = $m.Groups[1].Value -replace '\\\\','\'
            Test-Path (Join-Path $script:AoStage $rel) | Should -BeTrue -Because "the menu points at $rel"
        }
    }

    It 'remembers the add-on in the project file' {
        $back = Import-Project (Join-Path $script:AoOut 'discproject.json')
        $back.GameEntries.Count | Should -Be 2
        $back.GameEntries[1].Kind | Should -Be 'AddOn'
        $back.GameEntries[1].ParentIndex | Should -Be 0
    }

    It 'reads a version 2 project back as all games' {
        # 0.2.0 wrote no Kind and no Parent. Those files must not come back with
        # an entry silently marked as an add-on of something.
        $v2 = Join-Path $script:Sandbox 'proj-v2-compat'
        New-Item -ItemType Directory -Force -Path $v2 | Out-Null
        @{ Version=2; SourceFolder=$script:AoGame.Folder; GameName='Base'; Label='Base'
           Games=@(@{ Folder=$script:AoGame.Folder; GameName='Base' }
                   @{ Folder=$script:AoMod.Folder;  GameName='Mod'  })
           IconPath=$script:Art; IconIsIco=$false; Menu=$true; BgPath=$script:Bg
           BgAsIs=$false; PanelSide='Right'; Buttons=@('Install','Exit'); OutDir=$v2 } |
            ConvertTo-Json -Depth 4 | Set-Content (Join-Path $v2 'discproject.json') -Encoding UTF8
        $back = Import-Project (Join-Path $v2 'discproject.json')
        $back.GameEntries.Count | Should -Be 2
        @($back.GameEntries | Where-Object { $_.Kind -ne 'Game' }).Count | Should -Be 0
    }
}

Describe 'Adding and removing entries in the list' {

    BeforeAll {
        Add-Type -AssemblyName System.Windows.Forms
        $script:lvGames = New-Object System.Windows.Forms.ListView
        $script:lvGames.View = 'Details'
        foreach ($c in @('#','Name','Type','Belongs to')) { [void]$script:lvGames.Columns.Add($c,80) }
        $script:btnGameDel  = New-Object System.Windows.Forms.Button
        $script:btnAddOn    = New-Object System.Windows.Forms.Button
        $script:btnGameEdit = New-Object System.Windows.Forms.Button
        $script:cmbMedia = New-Object System.Windows.Forms.ComboBox
        $script:chkXAll  = [pscustomobject]@{ Checked=$false }
        $script:lblMan   = [pscustomobject]@{ Text='Manual file:' }
        $script:lblEx    = [pscustomobject]@{ Text='Extras folder:' }
        $script:grpX     = [pscustomobject]@{ Text='5)  Extra content (copied to the disc root as-is)' }
        $script:lblGame  = [pscustomobject]@{ Text=''; ForeColor=$null }
        $script:cbMan    = [pscustomobject]@{ Checked=$false }
        $script:cbExtra  = [pscustomobject]@{ Checked=$false }
        $script:chkMusic = [pscustomobject]@{ Checked=$false }
        $script:state    = @{ Games=@(); ExtraItems=@(); ManualPath=$null; ExtrasPath=$null; MusicFile=$null }

        $script:AddA = New-FixtureGame -Slug 'add_a'
        $script:AddB = New-FixtureGame -Slug 'add_b'
        $script:AddEmpty = Join-Path $script:Sandbox 'add-empty'
        New-Item -ItemType Directory -Force -Path $script:AddEmpty | Out-Null
    }

    It 'adds a folder rather than replacing what is already there' {
        $null = Add-GameFolder $script:AddA
        $null = Add-GameFolder $script:AddB
        @($script:state.Games).Count | Should -Be 2
        $script:lvGames.Items.Count  | Should -Be 2
    }

    It 'refuses the same folder twice' {
        $g = Add-GameFolder $script:AddA
        $g | Should -BeNullOrEmpty
        @($script:state.Games).Count | Should -Be 2
        $script:lblGame.Text | Should -Match 'already on this disc'
    }

    It 'refuses a folder with no installer, and does not add a row for it' {
        $g = Add-GameFolder $script:AddEmpty
        $g | Should -BeNullOrEmpty
        @($script:state.Games).Count | Should -Be 2
    }

    It 'numbers the rows from one' {
        $script:lvGames.Items[0].Text | Should -Be '1'
        $script:lvGames.Items[1].Text | Should -Be '2'
    }

    It 'greys Change... and Remove while no row is selected to act on' {
        # The standing rule: an option that cannot be used is not left clickable.
        #
        # This says nothing about the entry COUNT any more. Change... used to
        # want two entries on the disc, because its other question is which game
        # an add-on belongs to - but the name on the menu is edited in the same
        # dialog, and one game is the ordinary disc, so that rule made the name
        # unreachable in the commonest case. What is left is a selection rule,
        # and whether a real selection lights the button is a question about
        # wiring, so it is asked of the running window instead.
        $script:lvGames.Items.Clear()
        $script:state.Games = @()
        Update-GameList
        $script:btnGameDel.Enabled  | Should -BeFalse
        $script:btnGameEdit.Enabled | Should -BeFalse
    }
}

Describe 'Removing an entry renumbers the parents' {

    BeforeAll {
        function New-Ent {
            param([string]$Name, [string]$Kind = 'Game', [int]$Parent = -1)
            return @{ Ok=$true; GameName=$Name; Kind=$Kind; ParentIndex=$Parent
                      SetupExe=@{ Name="setup_$Name.exe" } }
        }
    }

    It 'shifts a parent that pointed past the removed entry' {
        # A: 0, B: 1, C: 2, and an add-on of C. Remove B and C becomes 1, so the
        # add-on has to follow it - otherwise it silently attaches to A.
        $e = @( (New-Ent 'A'), (New-Ent 'B'), (New-Ent 'C'), (New-Ent 'Mod' 'AddOn' 2) )
        $out = Remove-GameEntry $e 1
        $out.Count | Should -Be 3
        $out[2].Kind | Should -Be 'AddOn'
        $out[$out[2].ParentIndex].GameName | Should -Be 'C'
    }

    It 'leaves a parent below the removed entry alone' {
        $e = @( (New-Ent 'A'), (New-Ent 'Mod' 'AddOn' 0), (New-Ent 'C') )
        $out = Remove-GameEntry $e 2
        $out[1].ParentIndex | Should -Be 0
    }

    It 'turns an orphaned add-on back into a game rather than deleting it' {
        $e = @( (New-Ent 'A'), (New-Ent 'Mod' 'AddOn' 0) )
        $out = Remove-GameEntry $e 0
        $out.Count | Should -Be 1
        $out[0].GameName | Should -Be 'Mod'
        $out[0].Kind | Should -Be 'Game'
        $out[0].ParentIndex | Should -Be -1
    }

    It 'ignores an index that is not in the list' {
        $e = @( (New-Ent 'A'), (New-Ent 'B') )
        (Remove-GameEntry $e -1).Count | Should -Be 2
        (Remove-GameEntry $e 9).Count  | Should -Be 2
    }

    It 'still hands back an array when one entry is left' {
        # Returning a bare hashtable here is the unrolling trap that produced
        # "9 games (0 bytes)" - a hashtable's .Count is its number of keys.
        $out = Remove-GameEntry @( (New-Ent 'A'), (New-Ent 'B') ) 0
        $out -is [array] | Should -BeTrue
        $out.Count | Should -Be 1
    }

    It 'survives a removal that leaves nothing' {
        $out = Remove-GameEntry @( (New-Ent 'Only') ) 0
        @($out).Count | Should -Be 0
    }
}

# ---------------------------------------------------------------------------
# Real GOG installers
#
# Everything above builds its fixtures from sparse files with no version
# resource, so the name always comes from the filename fallback. These run
# against actual downloads when the machine has them and skip when it does not,
# which is every CI runner. They read metadata only - nothing is executed.
# ---------------------------------------------------------------------------

BeforeDiscovery {
    # DISCWRIGHT_GOG_DIR first, so this can be pointed at wherever the downloads
    # actually live - and so the no-installers path can be exercised on a machine
    # that does have them.
    $script:GogDir = @(
        $env:DISCWRIGHT_GOG_DIR
        'C:\Program Files (x86)\GOG Galaxy\Games\Offline Installers'
        "$env:USERPROFILE\Downloads\GOG"
        'C:\GOG Offline Installers'
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    $script:GogFolders = @()
    if ($script:GogDir) {
        $script:GogFolders = @(Get-ChildItem $script:GogDir -Directory -EA SilentlyContinue |
            Where-Object { @(Get-ChildItem $_.FullName -Filter 'setup_*.exe' -File -EA SilentlyContinue).Count } |
            ForEach-Object { $_.FullName })
    }

    # -ForEach @() does not produce an empty Describe, it fails the whole FILE at
    # discovery - so on a runner with no GOG downloads every test in this file
    # would be reported as an error rather than a skip. Never hand it an empty
    # list: one placeholder case, skipped, says "not run here" instead.
    $script:GogCases = if ($script:GogFolders.Count) { $script:GogFolders }
                       else { @('(no GOG downloads on this machine)') }
}

Describe 'Reading a real GOG download' -Tag 'Real' -Skip:($script:GogFolders.Count -eq 0) {

    It 'detects <_>' -ForEach $script:GogCases {
        $info = Get-GameInfo $_
        $info.Ok | Should -BeTrue -Because "$_ holds a setup_*.exe"
        $info.GameName | Should -Not -BeNullOrEmpty
        $info.TotalBytes | Should -BeGreaterThan 0
    }

    It 'trims the padding Inno leaves on ProductName in <_>' -ForEach $script:GogCases {
        # Inno pads version strings with trailing spaces. Untrimmed they leak
        # into the disc folder name ("Alan Wake                    Disc").
        $info = Get-GameInfo $_
        $info.GameName | Should -Be $info.GameName.Trim()
        $info.GameName | Should -Not -Match '\s{2,}'
    }

    It 'produces a usable disc folder name for <_>' -ForEach $script:GogCases {
        $n = Get-GameFolderName 1 (Get-GameInfo $_).GameName
        $n | Should -Not -Match '[\\/:*?"<>|]'
        $n | Should -Not -Match '\.$'
        $n.Length | Should -BeLessOrEqual 53
    }

    It 'claims every .bin part of <_> and nothing else' -ForEach $script:GogCases {
        # A real folder holds goodies (.zip) and often patch_*.exe alongside the
        # installer. Only the installer's own numbered parts belong on the disc.
        $info = Get-GameInfo $_
        $stem = [IO.Path]::GetFileNameWithoutExtension($info.SetupExe.Name) + '-'
        $onDisk = @(Get-ChildItem $_ -Filter '*.bin' -File -EA SilentlyContinue |
                    Where-Object { $_.Name.StartsWith($stem,[StringComparison]::OrdinalIgnoreCase) })
        $info.Files.Count | Should -Be ($onDisk.Count + 1)
        @($info.Files | Where-Object { $_.Extension -eq '.zip' }).Count | Should -Be 0
    }

    It 'does not mistake a patch_*.exe for the installer in <_>' -ForEach $script:GogCases {
        # GOG ships incremental updates as patch_<game>_<from>_to_<to>.exe, and
        # some are larger than the setup stub they sit next to - so "largest exe
        # wins" would pick the patch if the filter ever loosened.
        (Get-GameInfo $_).SetupExe.Name | Should -BeLike 'setup_*'
    }

    It 'recommends media that actually holds <_>' -ForEach $script:GogCases {
        $info = Get-GameInfo $_
        $rec = Get-MediaRec $info.TotalBytes
        $rec.Text | Should -Not -BeNullOrEmpty
        $rec.Fit  | Should -BeTrue -Because 'BD-R XL is the largest and everything should fit something'
    }
}



Describe "Reading GOG's play tasks out of an installed game" -Tag 'Unit' {

    # The only tests in this file that RUN the menu's JavaScript rather than
    # reading it. The play-task reader is a regular expression picking apart JSON
    # written by somebody else, which is exactly the kind of code that passes a
    # text assertion and fails on a real file - and the case it exists for, a
    # bundle holding two games, cannot be reproduced by owning the game unless you
    # happen to own that one.
    #
    # The functions are lifted out of DiscWright.ps1 and handed to cscript, so this
    # exercises the code that ships. A copy pasted in here would prove nothing.
    #
    # Worked out twice, deliberately. -Skip: is decided during discovery and
    # BeforeAll does not run until afterwards, so a value set only there leaves
    # every test in this block skipped on a machine that has cscript. Same reason
    # $script:CanBuildIso is computed in both phases at the top of this file.
    BeforeDiscovery {
        $script:HaveCScript = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    BeforeAll {
        $script:CScript = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

        function Get-JsFunction([string]$text, [string]$name) {
            $start = $text.IndexOf("function $name(")
            if ($start -lt 0) { throw "DiscWright.ps1 has no JScript function called $name" }
            $i = $text.IndexOf('{', $start); $depth = 0
            for ($j = $i; $j -lt $text.Length; $j++) {
                if ($text[$j] -eq '{') { $depth++ }
                elseif ($text[$j] -eq '}') { $depth--; if ($depth -eq 0) { return $text.Substring($start, $j - $start + 1) } }
            }
            throw "unbalanced braces in $name"
        }

        # A synthetic .info in the shape of a file that DOES use categories -
        # which is what Hollow Knight, Resident Evil 0 and The Witcher all look
        # like. It was written as a stand-in for Star Wars: Empire at War Gold
        # Pack before anyone had that game's real file; the real one has since
        # arrived and turned out to use no categories at all, so it now has a
        # fixture of its own further down and this one keeps the job it is
        # actually good at: covering category, isHidden and missing-file
        # filtering. Its shape is copied from the four real .info files on hand
        # rather than invented:
        #
        #   - pretty-printed, one key per line, a space after every colon. GOG
        #     never writes compact JSON, and an earlier version of this fixture
        #     did - so it agreed with the parser about a format that does not
        #     occur.
        #   - "languages" is a multi-line array. It is the reason playTasks may
        #     not simply split on braces: the task-matching regex only takes
        #     innermost braces, which is safe precisely because a real task holds
        #     arrays and never a nested object. Both real files confirm that.
        #   - both path spellings, because The Witcher's own file carries both:
        #     "System\\witcher.exe" on one task and "System//witcher.exe" on the
        #     next.
        #   - a task with NO "category" key at all, which is how The Witcher
        #     ships its Safe Mode entry. It must not become a third choice.
        #
        # The folder names and executables are not guesses either. GOG's own
        # public build manifest for product 1421404887 lists GameData\sweaw.exe
        # and EAWX\swfoc.exe under an install directory called "Star Wars -
        # Empire At War Gold", and the whole depot holds exactly one .info file.
        # EAWX is not a folder name anybody would invent.
        #
        # What that manifest could not give was the CONTENTS of the .info: it
        # ships inside the depot, and the depot answers 403 without an ownership
        # token. So the playTasks below were the reconstructed part, and this
        # comment used to close by naming the one thing still unconfirmed -
        # whether the real file marks Forces of Corruption with category "game",
        # because if it did not, playTasks would drop it and Play would go on
        # launching only the base game.
        #
        # That is exactly what was happening. The reporter sent the real file on
        # 2026-08-26: not one of its eight tasks carries a category. See the
        # "no categories at all" fixture below, which is that file verbatim.
        $script:TaskDir = Join-Path $script:Sandbox 'installed-bundle'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:TaskDir 'GameData') | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $script:TaskDir 'EAWX') | Out-Null
        foreach ($f in 'GameData\sweaw.exe','EAWX\swfoc.exe','GameData\hidden.exe','Manual.pdf') {
            Set-Content -LiteralPath (Join-Path $script:TaskDir $f) -Value 'x' -Encoding Ascii
        }
        $info = @'
{
    "buildId": "58076094395251196",
    "gameId": "1421404887",
    "language": "English",
    "languages": [
        "en-US"
    ],
    "name": "STAR WARS Empire at War - Gold Pack",
    "playTasks": [
        {
            "category": "game",
            "isPrimary": true,
            "languages": [
                "en-US",
                "de-DE",
                "fr-FR"
            ],
            "name": "Empire at War",
            "path": "GameData\\sweaw.exe",
            "type": "FileTask",
            "workingDir": "GameData"
        },
        {
            "category": "game",
            "languages": [
                "*"
            ],
            "name": "Forces of Corruption",
            "path": "EAWX//swfoc.exe",
            "type": "FileTask",
            "workingDir": "EAWX"
        },
        {
            "category": "game",
            "isHidden": true,
            "languages": [
                "*"
            ],
            "name": "Raw exe",
            "path": "GameData\\hidden.exe",
            "type": "FileTask"
        },
        {
            "category": "game",
            "languages": [
                "*"
            ],
            "name": "Gone missing",
            "path": "GameData\\notthere.exe",
            "type": "FileTask"
        },
        {
            "arguments": "-dontForceMinReqs",
            "icon": "goggame-1421404887.dll",
            "languages": [
                "*"
            ],
            "name": "Safe Mode",
            "path": "GameData//sweaw.exe",
            "type": "FileTask",
            "workingDir": "GameData"
        },
        {
            "category": "document",
            "languages": [
                "en-US"
            ],
            "name": "Manual",
            "path": "Manual.pdf",
            "type": "FileTask"
        },
        {
            "category": "document",
            "languages": [
                "*"
            ],
            "link": "http://example.invalid",
            "name": "Support",
            "type": "URLTask"
        }
    ],
    "rootGameId": "1421404887",
    "version": 1
}
'@
        # No BOM: GOG's files have none, and OpenTextFile would read one as content.
        [IO.File]::WriteAllText((Join-Path $script:TaskDir 'goggame-1421404887.info'),
                                $info, (New-Object Text.UTF8Encoding($false)))

        # The REAL Star Wars: Empire at War Gold Pack .info, pasted verbatim by
        # the person who reported that Play never offers Forces of Corruption.
        # Not reconstructed, not reformatted - the \u escapes and the spacing are
        # as GOG wrote them.
        #
        # What matters about it: not one of its eight tasks carries a "category",
        # so the category test dropped all eight, playTasks returned nothing, and
        # Play fell back to the registry exe - the base game. Five of the eight
        # are manuals, which is why the fallback cannot simply be "keep
        # everything" and tests for the .pdf and .rtf entries staying out.
        #
        # Its first task has no "name" either, which is its own small bug: the
        # filename stood in and the button read "Launch Star Wars - Empire At
        # War.lnk", extension and all.
        $script:RealBundleDir = Join-Path $script:Sandbox 'installed-eaw'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:RealBundleDir 'EAWX') | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $script:RealBundleDir 'Manuals') | Out-Null
        foreach ($f in @(
            'Launch Star Wars - Empire At War.lnk'
            'EAWX\swfoc.exe'
            'Language.exe'
            'Manuals\Star_Wars_Empire_at_War_Trouble.rtf'
            'Manuals\Star Wars - Empire at War - reference_card.pdf'
            'Manuals\Star Wars - Empire at War - tech tree.pdf'
            'Manuals\Star_Wars_Empire_at_War_Manual.pdf'
            'Manuals\Star Wars Empire At War - Forces of Corruption - Manual.pdf')) {
            Set-Content -LiteralPath (Join-Path $script:RealBundleDir $f) -Value 'x' -Encoding Ascii
        }
        $realInfo = @'
{
    "gameId" : "1421404887",
    "rootGameId" : "1421404887",
    "standalone" : true,
    "dependencyGameId" : "",
    "language"         : "english",
    "name"             : "STAR WARS\u00AE: Empire At War\u2122 Gold",
    "playTasks"        : [
        {
            "isPrimary" : true,
            "type"      : "FileTask",
            "path"      : "Launch Star Wars - Empire At War.lnk",
            "workingDir" : ""
        },
        {
            "name" : "Star Wars - Empire At War - Forces of Corruption",
            "type" : "FileTask",
            "path" : "EAWX\\swfoc.exe",
            "workingDir" : "EAWX",
            "arguments"  : "LANGUAGE=ENGLISH"
        },
        {
            "name" : "Language Settings",
            "type" : "FileTask",
            "path" : "Language.exe",
            "workingDir" : ""
        },
        {
            "name" : "Troubleshooting Guide",
            "type" : "FileTask",
            "path" : "Manuals\\Star_Wars_Empire_at_War_Trouble.rtf",
            "workingDir" : "Manuals"
        },
        {
            "name" : "Reference Card",
            "type" : "FileTask",
            "path" : "Manuals\\Star Wars - Empire at War - reference_card.pdf",
            "workingDir" : "Manuals"
        },
        {
            "name" : "Tech Tree",
            "type" : "FileTask",
            "path" : "Manuals\\Star Wars - Empire at War - tech tree.pdf",
            "workingDir" : "Manuals"
        },
        {
            "name" : "Star Wars - Empire at War Manual",
            "type" : "FileTask",
            "path" : "Manuals\\Star_Wars_Empire_at_War_Manual.pdf",
            "workingDir" : "Manuals"
        },
        {
            "name" : "Star Wars - Empire at War - Forces of Corruption Manual",
            "type" : "FileTask",
            "path" : "Manuals\\Star Wars Empire At War - Forces of Corruption - Manual.pdf",
            "workingDir" : "Manuals"
        }
    ],
    "supportTasks"     : [
        {
            "name" : "Support",
            "type" : "URLTask",
            "link" : "http://www.gog.com/en/support/star_wars_empire_at_war_gold_pack"
        }
    ]
}
'@
        [IO.File]::WriteAllText((Join-Path $script:RealBundleDir 'goggame-1421404887.info'),
                                $realInfo, (New-Object Text.UTF8Encoding($false)))

        $script:PlainDir = Join-Path $script:Sandbox 'installed-plain'
        New-Item -ItemType Directory -Force -Path $script:PlainDir | Out-Null

        function Invoke-PlayTasks([string]$dir) {
            $src = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1')
            $js = @('var fso=new ActiveXObject("Scripting.FileSystemObject");')
            foreach ($fn in 'jsonStr','relPath','playTasks') { $js += (Get-JsFunction $src $fn) }
            $js += 'var t=playTasks(WScript.Arguments(0));'
            $js += 'for(var i=0;i<t.length;i++){ WScript.Echo(t[i].n+"|"+t[i].p+"|"+t[i].a+"|"+t[i].w); }'
            $tmp = Join-Path $script:Sandbox ('tasks_' + [Guid]::NewGuid().ToString('N').Substring(0,6) + '.js')
            Set-Content -LiteralPath $tmp -Value ($js -join "`r`n") -Encoding Ascii
            $out = & $script:CScript //Nologo //E:JScript $tmp $dir 2>&1
            return @($out | Where-Object { $_ -and $_ -notmatch '^\s*$' } | ForEach-Object {
                $p = ([string]$_).Split('|')
                [pscustomobject]@{ Name=$p[0]; Path=$p[1]; Args=$p[2]; WorkDir=$p[3] }
            })
        }
    }

    It 'finds both games in a bundle that ships them in one installer' -Skip:(-not $script:HaveCScript) {
        $t = Invoke-PlayTasks $script:TaskDir
        $t.Count | Should -Be 2
        $t[0].Name | Should -Be 'Empire at War'
        $t[1].Name | Should -Be 'Forces of Corruption'
    }

    It 'resolves both spellings of a relative path' -Skip:(-not $script:HaveCScript) {
        $t = Invoke-PlayTasks $script:TaskDir
        $t[0].Path | Should -Be (Join-Path $script:TaskDir 'GameData\sweaw.exe')
        # Written "EAWX//swfoc.exe" in the fixture, as GOG really does.
        $t[1].Path | Should -Be (Join-Path $script:TaskDir 'EAWX\swfoc.exe')
    }

    It 'leaves out the manual, the hidden exe and a task whose file is gone' -Skip:(-not $script:HaveCScript) {
        $t = Invoke-PlayTasks $script:TaskDir
        @($t | Where-Object { $_.Name -in @('Manual','Support','Raw exe','Gone missing') }).Count | Should -Be 0
    }

    It 'leaves out a task with no category, the way Safe Mode ships' -Skip:(-not $script:HaveCScript) {
        # The Witcher's Safe Mode entry has no "category" key at all. It points at
        # the same executable as the real one with an extra argument, so treating
        # it as a game would offer the same game twice - and would put a chooser
        # in front of a single-game disc that never had one before.
        $t = Invoke-PlayTasks $script:TaskDir
        @($t | Where-Object { $_.Name -eq 'Safe Mode' }).Count | Should -Be 0
    }

    It 'reports nothing for an install with no .info file, so Play is unchanged' -Skip:(-not $script:HaveCScript) {
        # Fewer than two tasks means doPlay uses the registry exe exactly as it
        # always has. Every disc built before this keeps behaving identically.
        (Invoke-PlayTasks $script:PlainDir).Count | Should -Be 0
    }

    It 'reaches Forces of Corruption in the real Empire at War file' -Skip:(-not $script:HaveCScript) {
        # The reported bug, in one assertion. Every task in that file is missing
        # its "category", so before the fallback this returned nothing at all and
        # Play launched the base game with no chooser.
        $t = Invoke-PlayTasks $script:RealBundleDir
        @($t | Where-Object { $_.Name -eq 'Star Wars - Empire At War - Forces of Corruption' }).Count |
            Should -Be 1
    }

    It 'offers a chooser rather than one target for the real Empire at War file' -Skip:(-not $script:HaveCScript) {
        # doPlay shows the chooser on t.length > 1 and otherwise launches the
        # registry exe, so the count is what decides whether the fix is visible.
        (Invoke-PlayTasks $script:RealBundleDir).Count | Should -BeGreaterThan 1
    }

    It 'keeps the manuals out even with no category to go on' -Skip:(-not $script:HaveCScript) {
        # Five of the eight tasks are a .rtf or a .pdf. Nothing but the extension
        # distinguishes them here, which is why the fallback tests it.
        $t = Invoke-PlayTasks $script:RealBundleDir
        @($t | Where-Object { $_.Path -match '\.(pdf|rtf)$' }).Count | Should -Be 0
        @($t | Where-Object { $_.Name -in @('Tech Tree','Reference Card','Troubleshooting Guide') }).Count |
            Should -Be 0
    }

    It 'names the unnamed primary task without its extension' -Skip:(-not $script:HaveCScript) {
        # That task carries no "name", so the filename stands in - and a button
        # reading "Launch Star Wars - Empire At War.lnk" looks like a bug.
        $t = Invoke-PlayTasks $script:RealBundleDir
        @($t | Where-Object { $_.Name -eq 'Launch Star Wars - Empire At War' }).Count | Should -Be 1
        @($t | Where-Object { $_.Name -match '\.lnk$' }).Count | Should -Be 0
    }

    It 'passes the arguments and working directory the real file asks for' -Skip:(-not $script:HaveCScript) {
        $t = @(Invoke-PlayTasks $script:RealBundleDir |
               Where-Object { $_.Name -eq 'Star Wars - Empire At War - Forces of Corruption' })
        $t[0].Args    | Should -Be 'LANGUAGE=ENGLISH'
        $t[0].WorkDir | Should -Be (Join-Path $script:RealBundleDir 'EAWX')
    }

    It 'still trusts categories when the file uses them' -Skip:(-not $script:HaveCScript) {
        # The guard that keeps the fallback from spreading. The Witcher's file
        # names categories on five tasks and omits it on exactly one - Safe Mode -
        # so there the omission means something and the extension test must not
        # run. Without this guard Safe Mode returns as a second copy of the same
        # game, and a single-game disc grows a chooser it never had.
        $t = Invoke-PlayTasks $script:TaskDir
        $t.Count | Should -Be 2
        @($t | Where-Object { $_.Name -eq 'Safe Mode' }).Count | Should -Be 0
    }
}

Describe 'Naming an add-on' -Tag 'Unit' {

    It 'puts the version a GOG patch moves TO at the front' {
        # Two patches for the same game differ only in their versions, and the
        # menu button clips at about twenty characters - so the part that tells
        # them apart has to come first or every patch reads the same.
        Get-AddOnName 'patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe' |
            Should -Be 'Update 1.5.12618 (89712)'
    }

    It 'gives two patches of one game different names' {
        $a = Get-AddOnName 'patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe'
        $b = Get-AddOnName 'patch_hollow_knight_1.5.12618_(89712)_to_1.5.12620_(89718).exe'
        $a | Should -Not -Be $b
        $a.Substring(0,18) | Should -Not -Be $b.Substring(0,18)
    }

    It 'reads a plain installer name as words' {
        Get-AddOnName 'gmdx_v10_overhaul.exe' | Should -Be 'gmdx v10 overhaul'
    }

    It 'never comes back empty' {
        foreach ($n in @('x.exe','patch_.exe','setup_.exe','_.exe')) {
            Get-AddOnName $n | Should -Not -BeNullOrEmpty -Because "'$n' still needs a label"
        }
    }
}

Describe 'Accepting an add-on installer' {

    BeforeAll {
        $script:AoDir = Join-Path $script:Sandbox 'addon-src'
        New-Item -ItemType Directory -Force -Path $script:AoDir | Out-Null
        foreach ($n in @('setup_base_1.0.exe','patch_base_1.0_to_1.1.exe','GMDX_v10.exe')) {
            $fs=[IO.File]::Create((Join-Path $script:AoDir $n)); $fs.SetLength(2MB); $fs.Close()
        }
        # A part belonging to the mod, to prove parts are collected for add-ons too.
        $fs=[IO.File]::Create((Join-Path $script:AoDir 'GMDX_v10-1.bin')); $fs.SetLength(1MB); $fs.Close()
        $fs=[IO.File]::Create((Join-Path $script:AoDir 'readme.txt')); $fs.SetLength(10); $fs.Close()
    }

    It 'accepts an installer that is not named setup_*' {
        # This is the whole point of relaxing the filter: a mod is never named
        # setup_*, and neither is a GOG patch.
        $a = Get-AddOnInfo (Join-Path $script:AoDir 'GMDX_v10.exe')
        $a.Ok   | Should -BeTrue
        $a.Kind | Should -Be 'AddOn'
    }

    It 'accepts a GOG patch' {
        (Get-AddOnInfo (Join-Path $script:AoDir 'patch_base_1.0_to_1.1.exe')).Ok | Should -BeTrue
    }

    It 'collects an add-on''s own .bin parts' {
        $a = Get-AddOnInfo (Join-Path $script:AoDir 'GMDX_v10.exe')
        $a.Files.Count | Should -Be 2
    }

    It 'does not take the base game''s files with it' {
        $a = Get-AddOnInfo (Join-Path $script:AoDir 'patch_base_1.0_to_1.1.exe')
        $a.Files.Count | Should -Be 1
        @($a.Files | Where-Object { $_.Name -like 'setup_*' }).Count | Should -Be 0
    }

    It 'refuses something that is not an installer' {
        $a = Get-AddOnInfo (Join-Path $script:AoDir 'readme.txt')
        $a.Ok  | Should -BeFalse
        $a.Msg | Should -Match 'Extra content'
    }

    It 'refuses a file that is not there' {
        (Get-AddOnInfo (Join-Path $script:AoDir 'nope.exe')).Ok | Should -BeFalse
    }
}

Describe 'An add-on survives being saved and reopened' {

    BeforeAll {
        $script:RtDir = Join-Path $script:Sandbox 'roundtrip'
        New-Item -ItemType Directory -Force -Path $script:RtDir | Out-Null
        foreach ($n in @('setup_rt_1.0.exe','patch_rt_1.0_to_1.1.exe')) {
            $fs=[IO.File]::Create((Join-Path $script:RtDir $n)); $fs.SetLength(2MB); $fs.Close()
        }
        $script:RtGame = Get-GameInfo $script:RtDir
        $script:RtAdd  = Get-AddOnInfo (Join-Path $script:RtDir 'patch_rt_1.0_to_1.1.exe')
        $script:RtAdd.ParentIndex = 0
        $script:RtAdd.GameName = 'Renamed By Hand'

        $script:RtOut = Join-Path $script:Sandbox 'roundtrip-out'
        New-Item -ItemType Directory -Force -Path $script:RtOut | Out-Null
        Save-Project (New-BuildSettings -Games @($script:RtGame,$script:RtAdd) -Label 'RT' -OutDir $script:RtOut) $script:RtOut
        $script:RtBack = Import-Project (Join-Path $script:RtOut 'discproject.json')
    }

    It 'records the exact installer, not just the folder' {
        # The add-on shares its folder with the game. Re-detecting from the folder
        # would find the game's setup_*.exe and put the game on the disc twice.
        $script:RtBack.GameEntries[1].Setup | Should -BeLike '*patch_rt_1.0_to_1.1.exe'
    }

    It 'keeps a name that was edited by hand' {
        $script:RtBack.GameEntries[1].Name | Should -Be 'Renamed By Hand'
    }

    It 'still knows it is an add-on and whose' {
        $script:RtBack.GameEntries[1].Kind | Should -Be 'AddOn'
        $script:RtBack.GameEntries[1].ParentIndex | Should -Be 0
    }
}

Describe 'A real game with its real patches' -Tag 'Real' -Skip:($script:GogFolders.Count -eq 0) {

    BeforeAll {
        # Worked out again here, not read from BeforeDiscovery: the two phases have
        # separate state, so $script:GogFolders is empty by the time this runs.
        # Same trap the SevenZip lookup at the top of this file already documents.
        $dir = @(
            $env:DISCWRIGHT_GOG_DIR
            'C:\Program Files (x86)\GOG Galaxy\Games\Offline Installers'
            "$env:USERPROFILE\Downloads\GOG"
            'C:\GOG Offline Installers'
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
        $script:HkDir = $null
        if ($dir) {
            $script:HkDir = @(Get-ChildItem $dir -Directory -EA SilentlyContinue |
                Where-Object { $_.Name -like '*hollow_knight*' } |
                ForEach-Object { $_.FullName }) | Select-Object -First 1
        }
    }

    It 'finds the game and its patches, and files them underneath it' -Skip:(-not (@($script:GogFolders | Where-Object { $_ -like '*hollow_knight*' }).Count)) {
        $game = Get-GameInfo $script:HkDir
        $game.GameName | Should -Be 'Hollow Knight'

        $patches = @(Get-ChildItem $script:HkDir -Filter 'patch_*.exe' -File)
        $patches.Count | Should -BeGreaterThan 0

        $entries = @($game)
        foreach ($p in $patches) {
            $a = Get-AddOnInfo $p.FullName
            $a.Ok | Should -BeTrue
            $a.ParentIndex = 0
            $entries += $a
        }

        # One game in the menu, every patch hanging off it, and no chooser.
        $menu = Get-MenuGames $entries
        $menu.Count | Should -Be 1
        $menu[0].Name | Should -Be 'Hollow Knight'
        $menu[0].AddOns.Count | Should -Be $patches.Count

        # Every patch reports ProductName "Hollow Knight", so if the names came
        # from version info the menu would show identical buttons.
        $names = @($menu[0].AddOns | ForEach-Object { $_.Name })
        @($names | Sort-Object -Unique).Count | Should -Be $patches.Count
        $names | ForEach-Object { $_ | Should -Not -Be 'Hollow Knight' }

        # And every installer gets its own folder on the disc, with the patches
        # filed inside the game instead of beside it. One game, so the game
        # itself keeps the disc root - real names, real patch count, real
        # version info, which is the whole reason this block runs off a folder
        # that GOG wrote rather than off a fixture.
        $folders = @(0..($entries.Count-1) | ForEach-Object { Get-DiscEntryFolder $entries $_ })
        @($folders | Sort-Object -Unique).Count | Should -Be $entries.Count
        $folders[0] | Should -Be ''
        $folders[1..($folders.Count-1)] | ForEach-Object { $_ | Should -BeLike 'Add-ons\*' }
    }
}

Describe 'Get-PathMovedAside' -Tag 'Unit' {

    It 'moves the folder itself' {
        Get-PathMovedAside 'C:\out\disc' 'C:\out\disc' 'C:\out\disc.previous-a1' |
            Should -Be 'C:\out\disc.previous-a1'
    }

    It 'moves something inside it and keeps the rest of the path' {
        Get-PathMovedAside 'C:\out\disc\Games\01 - Game\setup.exe' 'C:\out\disc' 'C:\out\disc.previous-a1' |
            Should -Be 'C:\out\disc.previous-a1\Games\01 - Game\setup.exe'
    }

    It 'leaves a path that was never in there alone' {
        Get-PathMovedAside 'D:\GOG\Hollow Knight\setup.exe' 'C:\out\disc' 'C:\out\disc.previous-a1' |
            Should -Be 'D:\GOG\Hollow Knight\setup.exe'
    }

    It 'is not fooled by a folder whose name merely starts the same' {
        # disc2 is not inside disc, and a prefix test without the separator says
        # it is - which would repoint a path at a folder that does not exist.
        Get-PathMovedAside 'C:\out\disc2\setup.exe' 'C:\out\disc' 'C:\out\disc.previous-a1' |
            Should -Be 'C:\out\disc2\setup.exe'
    }
}

Describe 'Rebuilding a disc folder that the installers themselves live in' {

    BeforeAll {
        # Open existing disc... leaves a game's folder pointing at disc\, which is
        # also where the next build stages. Add anything else to that disc and the
        # build takes the wipe-and-restage path - and the wipe used to go straight
        # through the installers it was about to copy. The game was deleted, the
        # copy then failed on files that were no longer there, and what the user
        # had opened was gone.
        $script:EatOut  = Join-Path $script:Sandbox 'out-opened'
        $script:EatDisc = Join-Path $script:EatOut 'disc'
        New-Item -ItemType Directory -Force -Path $script:EatDisc | Out-Null

        $stem = 'setup_theopened_1.0_(90210)'
        foreach ($n in @("$stem.exe", "$stem-1.bin")) {
            $fs = [IO.File]::Create((Join-Path $script:EatDisc $n)); $fs.SetLength(2MB); $fs.Close()
        }
        # An asset and an extra item picked from inside the folder as well: the
        # code this replaces rescued those by copying them to temp, and they have
        # to keep arriving on the disc.
        $script:EatIcon  = New-FixturePng (Join-Path $script:EatDisc 'cover.png') 256 256
        $script:EatExtra = Join-Path $script:EatDisc 'readme.txt'
        Set-Content -LiteralPath $script:EatExtra -Value 'kept' -Encoding Ascii

        $script:EatOpened = Get-GameInfo $script:EatDisc
        $script:EatSecond = Get-GameInfo (New-FixtureGame -Slug 'opened_second')
        $script:EatAddOn  = Get-GameInfo (New-FixtureGame -Slug 'opened_patch')
        $script:EatAddOn.Kind = 'AddOn'; $script:EatAddOn.ParentIndex = 0
        $script:EatGames = @($script:EatOpened, $script:EatSecond, $script:EatAddOn)

        # What the installer files were called before any of this ran, so a test
        # can look for them by name wherever they ended up.
        $script:EatNames = @($script:EatOpened.Files | ForEach-Object { $_.Name })

        $script:EatIso = Invoke-Build @{
            Games=$script:EatGames; Label='Opened Disc'
            IconPath=$script:EatIcon; IconIsIco=$false; Menu=$true
            BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@($script:EatExtra); OutDir=$script:EatOut } $script:LogSink

        # And again, the way the window does it: a fresh settings hashtable each
        # time, holding the same entry objects the list still holds.
        $script:EatErr2 = $null
        try {
            $script:EatIso2 = Invoke-Build @{
                Games=$script:EatGames; Label='Opened Disc'
                IconPath=$script:Art; IconIsIco=$false; Menu=$true
                BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
                Divider=$false; ShowTitle=$false; TitleText=''
                WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
                Buttons=@('Install','Exit'); ManualPath=$null; ExtrasPath=$null
                ExtraItems=@($script:EatExtra); OutDir=$script:EatOut } $script:LogSink
        } catch { $script:EatErr2 = $_ }
    }

    It 'does not destroy the installers it is about to copy' {
        # The one that matters. Before this, the exe and its .bin part were gone
        # from the disk entirely and the build threw on the way to noticing.
        foreach ($n in $script:EatNames) {
            Test-Path (Join-Path $script:EatDisc (Join-Path (Get-DiscEntryFolder $script:EatGames 0) $n)) |
                Should -BeTrue -Because "$n has to survive the rebuild"
        }
    }

    It 'writes the ISO it was asked for' {
        $script:EatIso | Should -Not -BeNullOrEmpty
        Test-Path $script:EatIso | Should -BeTrue
    }

    It 'points the games list at where the files ended up' {
        # The entry object belongs to the window's own list, so leaving it naming
        # a path inside a folder that has just been deleted breaks the NEXT build
        # rather than this one - the worst kind of delay between cause and effect.
        Test-Path $script:EatOpened.SetupExe.FullName | Should -BeTrue
        Test-SubPath $script:EatOpened.SetupExe.FullName $script:EatDisc | Should -BeTrue
        foreach ($f in $script:EatOpened.Files) { Test-Path $f.FullName | Should -BeTrue }
    }

    It 'builds a second time, straight after the first' {
        $script:EatErr2 | Should -BeNullOrEmpty
        Test-Path $script:EatIso2 | Should -BeTrue
    }

    It 'clears the folder it set aside once the ISO is written' {
        @(Get-ChildItem $script:EatOut -Directory -Filter 'disc.previous-*').Count | Should -Be 0
    }

    It 'still lands the icon that was picked from inside the folder' {
        Test-Path (Join-Path $script:EatDisc (Get-DiscIconName 'Opened Disc')) | Should -BeTrue
    }

    It 'still lands the extra content that was picked from inside the folder' {
        # Renaming the folder aside has to preserve everything the copy-to-temp
        # rescue used to preserve, or this fix trades one kind of loss for another.
        Test-Path (Join-Path $script:EatDisc 'readme.txt') | Should -BeTrue
    }

    It 'leaves the other two entries where they always went' {
        foreach ($i in 1, 2) {
            Test-Path (Join-Path $script:EatDisc (Get-DiscEntrySetup $script:EatGames $i)) |
                Should -BeTrue
        }
    }
}

Describe 'A folder that is not a GOG download' -Tag 'Unit' {

    # Asked for publicly: somebody burned a 17 GB GOG disc with this and then
    # wanted the same disc from game files GOG never packaged. Until now a game
    # had to be a folder holding a setup_*.exe, which ruled out an unpacked zip,
    # an itch.io download and anything portable.
    #
    # Everything in the folder goes on the disc, keeping its shape, and the
    # installer is whichever executable was picked in the dialog, or none.

    BeforeAll {
        $script:Loose = Join-Path $script:Sandbox 'src\loose-game'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:Loose 'data\textures') | Out-Null
        $fs = [IO.File]::Create((Join-Path $script:Loose 'Game.exe')); $fs.SetLength(3MB); $fs.Close()
        $fs = [IO.File]::Create((Join-Path $script:Loose 'CrashHandler.exe')); $fs.SetLength(64KB); $fs.Close()
        Set-Content -LiteralPath (Join-Path $script:Loose 'readme.txt') -Value 'read me' -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $script:Loose 'data\config.ini') -Value 'x=1' -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $script:Loose 'data\textures\wall.dds') -Value 'dds' -Encoding Ascii
    }

    It 'is refused by the GOG reader, which is what asks the question' {
        $g = Get-GameInfo $script:Loose
        $g.Ok  | Should -BeFalse
        $g.Msg | Should -BeLike 'No GOG*'
    }

    It 'takes the folder, with everything in it' {
        $g = Get-FolderInfo $script:Loose
        $g.Ok     | Should -BeTrue
        $g.Source | Should -Be 'Files'
        @($g.Files).Count | Should -Be 5
        $g.TotalBytes | Should -BeGreaterThan 3MB
    }

    It 'names it after the folder when no installer was picked' {
        (Get-FolderInfo $script:Loose).GameName | Should -Be 'loose-game'
        (Get-FolderInfo $script:Loose).SetupExe | Should -BeNullOrEmpty
    }

    It 'installs with the executable that was picked' {
        $exe = Join-Path $script:Loose 'Game.exe'
        $g = Get-FolderInfo $script:Loose $exe
        $g.SetupExe.FullName | Should -Be $exe
    }

    It 'offers every executable, biggest first, because an installer is rarely the smallest' {
        $exes = Get-FolderExecutables $script:Loose
        @($exes).Count | Should -Be 2
        $exes[0].Name  | Should -Be 'Game.exe'
    }

    It 'orders a list it was handed exactly as one it read itself' {
        # The dialog reads the folder once and hands the list over, because a cold
        # 20 GB game folder makes a second walk visible. Same rule either way, or
        # the list a person sees stops matching the one under test.
        $files = @(Get-ChildItem $script:Loose -Recurse -File -Force)
        $given = Get-FolderExecutables $script:Loose 25 $files
        $read  = Get-FolderExecutables $script:Loose
        @($given | ForEach-Object { $_.FullName }) | Should -Be @($read | ForEach-Object { $_.FullName })
    }

    It 'still caps the list, whoever read the folder' {
        $many = Join-Path $script:Sandbox 'src\many-exes'
        New-Item -ItemType Directory -Force -Path $many | Out-Null
        foreach ($i in 1..30) {
            $fs = [IO.File]::Create((Join-Path $many "tool$i.exe")); $fs.SetLength(1KB * $i); $fs.Close()
        }
        # Assigned, never wrapped: the list comes back with a leading comma, and
        # @() around the call collapses it to one element holding the list.
        $read = Get-FolderExecutables $many
        $read.Count | Should -Be 25
        $given = Get-FolderExecutables $many 25 (Get-ChildItem $many -Recurse -File -Force)
        $given.Count | Should -Be 25
    }

    It 'spots the folder that holds the downloads rather than a game' {
        # The likeliest way to reach the question by mistake: C:\GOG Games has no
        # setup_*.exe of its own, so it is not a GOG download, and taking it whole
        # would put every game on one entry named after the folder.
        $shelf = Join-Path $script:Sandbox 'src\gog-shelf'
        $one = Join-Path $shelf 'game one'
        foreach ($g in @($one, (Join-Path $shelf 'game two'))) {
            New-Item -ItemType Directory -Force -Path $g | Out-Null
            $fs = [IO.File]::Create((Join-Path $g 'setup_a_game_1.0_(90210).exe')); $fs.SetLength(2MB); $fs.Close()
        }
        # A folder that is not a download sits beside them and must not be counted.
        New-Item -ItemType Directory -Force -Path (Join-Path $shelf 'artwork') | Out-Null
        $hits = Get-GogSubfolders $shelf
        $hits.Count | Should -Be 2
        ($hits | ForEach-Object { $_.Name }) -join ',' | Should -Be 'game one,game two'
        # And the game folder itself is not one of those, or every ordinary folder
        # would carry the warning.
        $inside = Get-GogSubfolders $one
        $inside.Count | Should -Be 0
        $plain = Get-GogSubfolders $script:Loose
        $plain.Count | Should -Be 0
    }

    It 'says nothing about a folder that is not there' {
        $gone = Get-GogSubfolders (Join-Path $script:Sandbox 'src\nowhere-at-all')
        $gone.Count | Should -Be 0
    }

    It 'refuses a folder with nothing in it' {
        $empty = Join-Path $script:Sandbox 'src\empty-folder'
        New-Item -ItemType Directory -Force -Path $empty | Out-Null
        $g = Get-FolderInfo $empty
        $g.Ok  | Should -BeFalse
        $g.Msg | Should -Match 'no files'
    }

    It 'keeps the shape of the folder on the disc' {
        # A game that expects data\textures\wall.dds beside its exe arrives broken
        # if the disc flattens it, which is what a GOG download gets, having no
        # shape to keep.
        $g = Get-FolderInfo $script:Loose
        $deep = @($g.Files | Where-Object { $_.Name -eq 'wall.dds' })[0]
        Get-EntryFileRelative $g $deep | Should -Be 'data\textures\wall.dds'
        $flat = Get-GameInfo (New-FixtureGame -Slug 'shape_gog')
        Get-EntryFileRelative $flat $flat.Files[0] | Should -Be $flat.Files[0].Name
    }

    It 'tells the menu there is nothing to install, and where the files are' {
        $entries = @((Get-FolderInfo $script:Loose), (Get-GameInfo (New-FixtureGame -Slug 'menu_gog')))
        $menu = Get-MenuGames $entries
        $menu[0].Setup  | Should -Be ''
        $menu[0].Folder | Should -Be (Get-DiscEntryFolder $entries 0)
        $menu[1].Setup  | Should -Not -Be ''
    }

    It 'gives that game an Open Folder button instead of Install' {
        $hta = Join-Path $script:Sandbox 'loose-menu.hta'
        $entries = @((Get-FolderInfo $script:Loose))
        New-MenuHta @{ GameName='LOOSE'; Games=(Get-MenuGames $entries); Buttons=@('Play','Install','Exit')
                       MusicFile=''; ManualFile=''; PanelSide='Right'; IconName='disc.ico'
                       WindowBorder=$true; ButtonStyle='Minimal' } $hta
        $text = Get-Content -LiteralPath $hta -Raw
        $text | Should -Match 'btn_Open'
        $text | Should -Match 'function doOpenFolder'
        $text | Should -Match 'd:"'
    }

    It 'still saves and reopens as a folder of files' {
        $out = Join-Path $script:Sandbox 'loose-project'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $g = Get-FolderInfo $script:Loose (Join-Path $script:Loose 'Game.exe')
        Save-Project @{ Games=@($g); Label='LOOSE'; IconPath=$script:Art; IconIsIco=$false; Menu=$true
                        BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'; Divider=$false
                        ShowTitle=$false; TitleText=''; WindowBorder=$true; ButtonStyle='Minimal'
                        MusicFile=$null; Buttons=@('Install','Exit'); ManualPath=$null; ExtrasPath=$null
                        ExtraItems=@(); MediaKey=''; LinuxInfo=$false; LegacyFs=$false } $out
        $back = Import-Project (Join-Path $out $PROJECT_FILE)
        $back.GameEntries[0].Source | Should -Be 'Files'
        $back.GameEntries[0].Setup  | Should -Be (Join-Path $script:Loose 'Game.exe')
    }
}

Describe 'Adding a folder the file dialog came back with' -Tag 'Unit' {

    # The wiring between picking a folder and the question that follows it. The
    # question itself is a window, so what it looks like and what it offers is
    # asked of the running dialog in the window suite; here it is stubbed, and
    # what is under test is which answers add an entry and which do not.

    BeforeAll {
        Add-Type -AssemblyName System.Windows.Forms
        function Update-GameList {}
        function Update-MediaLabel {}
        function Show-FolderInstallerDialog([string]$folder) {
            $script:AskedAbout = $folder
            return $script:AskAnswer
        }

        $script:lblGame = [pscustomobject]@{ Text=''; ForeColor=$null }
        $script:state   = @{ Games=@(); ExtraItems=@() }

        $script:PickFiles = Join-Path $script:Sandbox 'src\picked-files'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:PickFiles 'bin') | Out-Null
        $fs = [IO.File]::Create((Join-Path $script:PickFiles 'bin\Game.exe')); $fs.SetLength(2MB); $fs.Close()
        Set-Content -LiteralPath (Join-Path $script:PickFiles 'readme.txt') -Value 'read me' -Encoding Ascii

        $script:PickNothing = Join-Path $script:Sandbox 'src\picked-nothing'
        New-Item -ItemType Directory -Force -Path $script:PickNothing | Out-Null
    }

    BeforeEach {
        $script:state.Games  = @()
        $script:AskedAbout   = $null
        $script:AskAnswer    = ''
        $script:lblGame.Text = ''
    }

    It 'asks about a folder that has files but no GOG installer' {
        $g = Add-GameFolder $script:PickFiles
        $script:AskedAbout | Should -Be $script:PickFiles
        $g.Source   | Should -Be 'Files'
        $g.GameName | Should -Be 'picked-files'
        @($script:state.Games).Count | Should -Be 1
    }

    It 'takes the installer the question came back with' {
        $script:AskAnswer = Join-Path $script:PickFiles 'bin\Game.exe'
        $g = Add-GameFolder $script:PickFiles
        $g.SetupExe.FullName | Should -Be $script:AskAnswer
    }

    It 'adds nothing when the question is cancelled' {
        # Cancel is $null; '' is an answer, and it means put the files on the
        # disc with nothing to install. Treating the two alike would leave an
        # entry on the list that had just been refused.
        $script:AskAnswer = $null
        $g = Add-GameFolder $script:PickFiles
        $g | Should -BeNullOrEmpty
        @($script:state.Games).Count | Should -Be 0
    }

    It 'never asks about a folder with nothing in it' {
        # A dialog offering a choice between none of nought executables is not a
        # question. It is also what hung the suite once, so it is asked about.
        $g = Add-GameFolder $script:PickNothing
        $g | Should -BeNullOrEmpty
        $script:AskedAbout   | Should -BeNullOrEmpty
        $script:lblGame.Text | Should -BeLike 'No GOG*'
    }

    It 'refuses the same folder of files twice' {
        # Nothing to compare installers on when neither entry has one, so the
        # folders are compared instead.
        $null = Add-GameFolder $script:PickFiles
        $g = Add-GameFolder $script:PickFiles
        $g | Should -BeNullOrEmpty
        $script:lblGame.Text | Should -Match 'already on this disc'
        @($script:state.Games).Count | Should -Be 1
    }
}

Describe 'Building a disc from a folder of game files' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    # The whole point of taking folders that are not GOG downloads: the disc has
    # to carry them as they are. A GOG download is an installer and its parts in
    # one folder, so it has no shape to lose; a game folder does, and a game
    # whose data\ subfolder was flattened onto the disc root is a broken game.

    BeforeAll {
        $script:FilesOut = Join-Path $script:Sandbox 'out-files'
        New-Item -ItemType Directory -Force -Path $script:FilesOut | Out-Null
        $src = Join-Path $script:Sandbox 'src\files-game'
        New-Item -ItemType Directory -Force -Path (Join-Path $src 'data\textures') | Out-Null
        $fs = [IO.File]::Create((Join-Path $src 'Game.exe')); $fs.SetLength(2MB); $fs.Close()
        Set-Content -LiteralPath (Join-Path $src 'data\config.ini') -Value 'x=1' -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $src 'data\textures\wall.dds') -Value 'dds' -Encoding Ascii
        # A game's own icons, which the stale-icon cleanup used to eat.
        Set-Content -LiteralPath (Join-Path $src 'gamething.ico') -Value 'ico' -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $src 'screenshot.png') -Value 'png' -Encoding Ascii

        $script:FilesGame = Get-FolderInfo $src
        $script:FilesIso = Invoke-Build @{
            Games=@($script:FilesGame); Label='Files Disc'
            IconPath=$script:Art; IconIsIco=$false; Menu=$true
            BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$script:FilesOut } $script:LogSink
        $script:FilesDisc = Join-Path $script:FilesOut 'disc'
    }

    It 'writes the ISO' {
        Test-Path $script:FilesIso | Should -BeTrue
    }

    It 'keeps the subfolders the game expects' {
        Test-Path (Join-Path $script:FilesDisc 'Game.exe')                  | Should -BeTrue
        Test-Path (Join-Path $script:FilesDisc 'data\config.ini')           | Should -BeTrue
        Test-Path (Join-Path $script:FilesDisc 'data\textures\wall.dds')    | Should -BeTrue
    }

    It 'puts nothing where the flat copy would have put it' {
        Test-Path (Join-Path $script:FilesDisc 'wall.dds') | Should -BeFalse
    }

    It "leaves the game's own icons on the disc" {
        # The build clears icons a previous build left at the disc root, and a
        # folder of game files lands there too. The installed Hollow Knight
        # carries gog.ico, support.ico and its own goggame-*.ico, and all three
        # were swept off the disc before this rule existed.
        Test-Path (Join-Path $script:FilesDisc 'gamething.ico')  | Should -BeTrue
        Test-Path (Join-Path $script:FilesDisc 'screenshot.png') | Should -BeTrue
        Test-Path (Join-Path $script:FilesDisc (Get-DiscIconName 'Files Disc')) | Should -BeTrue
    }

    It 'gives the menu the folder instead of an installer' {
        $hta = Get-Content -LiteralPath (Join-Path $script:FilesDisc 'AUTORUN\menu.hta') -Raw
        $hta | Should -Match 's:""'
        $hta | Should -Match 'btn_Open'
    }

    It 'saves a project that reopens as a folder of files' {
        $back = Import-Project (Join-Path $script:FilesOut $PROJECT_FILE)
        $back.GameEntries[0].Source | Should -Be 'Files'
    }
}

Describe "A game folder that already holds the disc's own names" -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    # A GOG download is setup_*.exe and its .bin parts, which can never be called
    # autorun.inf or the disc's icon. A folder of game files can be called
    # anything, and plenty of games ship an autorun.inf of their own - so since
    # 0.8.0 a one-game disc, where the files land at the root, can have the
    # disc's own files land on top of the game's.
    #
    # The disc has to win: its autorun.inf is what opens the menu. What it must
    # not do is win silently, because the promise is that the folder goes on the
    # disc as it stands, and here part of it did not.

    BeforeAll {
        $script:ClashOut = Join-Path $script:Sandbox 'out-clash'
        New-Item -ItemType Directory -Force -Path $script:ClashOut | Out-Null
        $src = Join-Path $script:Sandbox 'src\clashing-game'
        New-Item -ItemType Directory -Force -Path (Join-Path $src 'AUTORUN') | Out-Null
        $fs = [IO.File]::Create((Join-Path $src 'Game.exe')); $fs.SetLength(1MB); $fs.Close()
        Set-Content -LiteralPath (Join-Path $src 'autorun.inf') -Value "[autorun]`r`nopen=THEIRS.EXE" -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $src 'AUTORUN\theirs.txt') -Value 'the game has one too' -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $src (Get-DiscIconName 'Clash Disc')) -Value 'not an icon' -Encoding Ascii

        $script:ClashLog = New-Object System.Collections.ArrayList
        $sink = { param($m) $null = $script:ClashLog.Add([string]$m) }
        $script:ClashIso = Invoke-Build @{
            Games=@(Get-FolderInfo $src); Label='Clash Disc'
            IconPath=$script:Art; IconIsIco=$false; Menu=$true
            BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$script:ClashOut } $sink
        $script:ClashDisc = Join-Path $script:ClashOut 'disc'
        $script:ClashSaid = ($script:ClashLog -join "`n")
    }

    It 'still writes a disc that opens its own menu' {
        (Get-Content -LiteralPath (Join-Path $script:ClashDisc 'autorun.inf') -Raw) |
            Should -Match 'AUTORUN'
    }

    It 'says which of the game files the disc replaced' {
        $script:ClashSaid | Should -Match "the disc's own autorun\.inf replaced"
    }

    It 'names the icon it replaced as well, not just the first one' {
        $ico = [regex]::Escape((Get-DiscIconName 'Clash Disc'))
        $script:ClashSaid | Should -Match "the disc's own $ico replaced"
    }

    It 'says nothing of the sort for a folder with no such names in it' {
        # The one thing that would make the note worthless: a warning on every
        # disc. The sink writes to a script-scoped list on purpose - a local
        # $log is shadowed by Invoke-Build's own parameter of that name when the
        # scriptblock runs inside it.
        $script:PlainLog = New-Object System.Collections.ArrayList
        $sink = { param($m) $null = $script:PlainLog.Add([string]$m) }
        $out = Join-Path $script:Sandbox 'out-noclash'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $src = Join-Path $script:Sandbox 'src\plain-game'
        New-Item -ItemType Directory -Force -Path $src | Out-Null
        $fs = [IO.File]::Create((Join-Path $src 'Game.exe')); $fs.SetLength(1MB); $fs.Close()
        $null = Invoke-Build @{
            Games=@(Get-FolderInfo $src); Label='Plain Disc'
            IconPath=$script:Art; IconIsIco=$false; Menu=$true
            BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$out } $sink
        ($script:PlainLog -join "`n") | Should -Not -Match "the disc's own .* replaced"
    }

    It "keeps the game's own AUTORUN folder alongside the menu, and says so" {
        Test-Path (Join-Path $script:ClashDisc 'AUTORUN\theirs.txt') | Should -BeTrue
        Test-Path (Join-Path $script:ClashDisc 'AUTORUN\menu.hta')   | Should -BeTrue
        $script:ClashSaid | Should -Match 'brings an AUTORUN folder'
    }
}

Describe 'Reopening a built disc and rebuilding it' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    # Open existing disc... leaves the icon, the background and the extra content
    # pointing at files inside disc\, because that is where a built disc keeps
    # them. Rebuilding sets that folder aside, points the settings at the copy,
    # writes the ISO, saves the project and then deletes the folder - so the
    # project it just saved named files that no longer existed, and reopening it
    # lost the icon and the background. Found porting Save-Project to the Linux
    # version, and measured here before it was fixed.

    BeforeAll {
        $script:ReOut = Join-Path $script:Sandbox 'out-reopened'
        New-Item -ItemType Directory -Force -Path $script:ReOut | Out-Null
        $script:ReSettings = @{
            Games=@((Get-GameInfo (New-FixtureGame -Slug 'reopened'))); Label='Reopened Disc'
            IconPath=$script:Art; IconIsIco=$false; Menu=$true
            BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$script:ReOut }
        $null = Invoke-Build $script:ReSettings $script:LogSink

        # Now the window's Open existing disc...: every asset named on the disc.
        $script:ReDisc = Join-Path $script:ReOut 'disc'
        $reopened = $script:ReSettings.Clone()
        $reopened.Games     = @((Get-GameInfo (New-FixtureGame -Slug 'reopened')))
        $reopened.IconPath  = Join-Path $script:ReDisc (Get-DiscIconName 'Reopened Disc')
        $reopened.IconIsIco = $true
        $reopened.BgPath    = Join-Path $script:ReDisc 'AUTORUN\bg.png'
        $reopened.BgAsIs    = $true
        $null = Invoke-Build $reopened $script:LogSink
        $script:ReProject = Get-Content -Raw (Join-Path $script:ReOut $script:PROJECT_FILE) | ConvertFrom-Json
    }

    It 'saves a project naming the icon where it actually is' {
        Test-Path $script:ReProject.IconPath | Should -BeTrue -Because $script:ReProject.IconPath
        Test-SubPath $script:ReProject.IconPath $script:ReDisc | Should -BeTrue
    }

    It 'saves a project naming the background where it actually is' {
        Test-Path $script:ReProject.BgPath | Should -BeTrue -Because $script:ReProject.BgPath
    }

    It 'reopens that project with both assets intact' {
        # The whole point: the file has to survive a round trip through Open.
        $back = Import-Project (Join-Path $script:ReOut $script:PROJECT_FILE)
        $back | Should -Not -BeNullOrEmpty
        Test-Path $back.IconPath | Should -BeTrue
        Test-Path $back.BgPath   | Should -BeTrue
    }

    It 'still clears the folder it set aside' {
        @(Get-ChildItem $script:ReOut -Directory -Filter 'disc.previous-*').Count | Should -Be 0
    }
}

Describe 'The comma-return convention is not undone at the call sites' -Tag 'Unit' {

    # Several functions return ,@(...) so that a one-element result survives
    # PowerShell unrolling it on the way out. Wrapping such a call in @() again
    # rebuilds the very thing the comma prevents: a one-element array holding the
    # real array. It reads as harmless defensive code, which is why it keeps
    # happening - it shipped in Get-FirstGame, in Set-GameFolder, and again in the
    # Remove button, where deleting one of five entries left a single row whose
    # name was all four survivors run together.
    #
    # Testing the functions cannot catch it, because the fault is in the caller.
    # This reads the source instead.

    BeforeAll {
        $script:CommaReturners = @(
            'Get-Games', 'Get-MenuGames', 'Remove-GameEntry', 'Get-EntryAddOns',
            'Set-GameEntries', 'Set-GameFolders', 'Set-GameFolder', 'Get-FolderExecutables',
            'Get-GogSubfolders'
        )
        $appFile = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
        $tree = [System.Management.Automation.Language.Parser]::ParseFile($appFile, [ref]$null, [ref]$null)

        # Only a BARE call counts: @(Get-Games) rebuilds the wrapper, but
        # @(Get-Games | Where-Object {...}) does not, because the pipeline has
        # already unrolled the result and the @() is what puts it back. So the
        # array expression must hold exactly one statement, that statement must be
        # a pipeline of exactly one element, and that element must be the call.
        $script:Wrapped = @()
        foreach ($arr in $tree.FindAll({ param($n)
                $n -is [System.Management.Automation.Language.ArrayExpressionAst] }, $true)) {
            $stmts = $arr.SubExpression.Statements
            if ($stmts.Count -ne 1) { continue }
            $pipe = $stmts[0]
            if ($pipe -isnot [System.Management.Automation.Language.PipelineAst]) { continue }
            if ($pipe.PipelineElements.Count -ne 1) { continue }
            $el = $pipe.PipelineElements[0]
            if ($el -isnot [System.Management.Automation.Language.CommandAst]) { continue }
            $name = $el.GetCommandName()
            if ($name -and $script:CommaReturners -contains $name) {
                $script:Wrapped += [pscustomobject]@{
                    Command = $name
                    Line    = $arr.Extent.StartLineNumber
                    Text    = $arr.Extent.Text
                }
            }
        }
    }

    It 'wraps no comma-returning call in @()' {
        $detail = ($script:Wrapped | ForEach-Object { "line $($_.Line): $($_.Text)" }) -join '; '
        $script:Wrapped.Count | Should -Be 0 -Because "these rebuild the unrolling the comma exists to prevent -> $detail"
    }
}

Describe 'Removing an entry the way the button does it' -Tag 'Unit' {

    BeforeAll {
        function New-E {
            param([string]$Name, [string]$Kind = 'Game', [int]$Parent = -1)
            return @{ Ok=$true; GameName=$Name; Kind=$Kind; ParentIndex=$Parent
                      SetupExe=@{ Name="setup_$Name.exe" } }
        }
        # One game and four add-ons: the real Hollow Knight disc.
        $script:Five = @(
            (New-E 'Hollow Knight'),
            (New-E 'Update A' 'AddOn' 0), (New-E 'Update B' 'AddOn' 0),
            (New-E 'Update C' 'AddOn' 0), (New-E 'Update D' 'AddOn' 0)
        )
    }

    It 'hands back four separate entries, not one entry holding four' {
        # Assigned exactly as the Remove handler assigns it.
        $after = Remove-GameEntry $script:Five 0
        $after.Count | Should -Be 4
        foreach ($e in $after) {
            $e | Should -BeOfType [hashtable] -Because 'each element is one entry, not a nested list'
            $e.GameName | Should -Not -Match ' Update '
        }
    }

    It 'promotes all four orphans rather than one' {
        $after = Remove-GameEntry $script:Five 0
        @($after | Where-Object { $_.Kind -eq 'Game' }).Count | Should -Be 4
        @($after | Where-Object { $_.ParentIndex -ne -1 }).Count | Should -Be 0
    }

    # Each of these builds its own list. Remove-GameEntry rewrites Kind and
    # ParentIndex on the entries it is handed rather than on copies, so a shared
    # fixture arrives at the second test already promoted - which is exactly how
    # the first draft of these tests "passed" while proving nothing.
    It 'takes the add-ons with the game when asked to' {
        $five = @(
            (New-E 'Hollow Knight'),
            (New-E 'Update A' 'AddOn' 0), (New-E 'Update B' 'AddOn' 0),
            (New-E 'Update C' 'AddOn' 0), (New-E 'Update D' 'AddOn' 0)
        )
        $after = Remove-GameEntry $five 0 $true
        @($after).Count | Should -Be 0
    }

    It 'takes only that game''s add-ons, not somebody else''s' {
        $two = @(
            (New-E 'Alan Wake'), (New-E 'Hollow Knight'),
            (New-E 'AW patch' 'AddOn' 0), (New-E 'HK patch' 'AddOn' 1)
        )
        $after = Remove-GameEntry $two 0 $true
        @($after).Count      | Should -Be 2
        $after[0].GameName   | Should -Be 'Hollow Knight'
        $after[1].GameName   | Should -Be 'HK patch'
    }

    It 'repoints the survivors after several rows go at once' {
        # The reason removal builds an old-to-new index map. With one row going,
        # "subtract one if the parent sat after it" holds. With a game and its two
        # add-ons going, it does not - and a stale ParentIndex silently reattaches
        # a patch to whatever game slid into the gap.
        $mix = @(
            (New-E 'Alan Wake'), (New-E 'AW patch 1' 'AddOn' 0), (New-E 'AW patch 2' 'AddOn' 0),
            (New-E 'Hollow Knight'), (New-E 'HK patch' 'AddOn' 3)
        )
        $after = Remove-GameEntry $mix 0 $true
        @($after).Count       | Should -Be 2
        $after[0].GameName    | Should -Be 'Hollow Knight'
        $after[1].GameName    | Should -Be 'HK patch'
        $after[1].ParentIndex | Should -Be 0 -Because 'Hollow Knight is index 0 now, not 3'
        $after[1].Kind        | Should -Be 'AddOn'
    }

    It 'still promotes when not asked to take them' {
        $five = @(
            (New-E 'Hollow Knight'),
            (New-E 'Update A' 'AddOn' 0), (New-E 'Update B' 'AddOn' 0)
        )
        $after = Remove-GameEntry $five 0
        @($after).Count | Should -Be 2
        @($after | Where-Object { $_.Kind -eq 'Game' }).Count | Should -Be 2
    }

    It 'finds the add-ons belonging to one entry' {
        $five = @(
            (New-E 'Hollow Knight'),
            (New-E 'Update A' 'AddOn' 0), (New-E 'Update B' 'AddOn' 0)
        )
        $kids = Get-EntryAddOns $five 0
        $kids -join ',' | Should -Be '1,2'
        $none = Get-EntryAddOns $five 1
        @($none).Count  | Should -Be 0
    }

    It 'leaves the list alone for an index that is not there' {
        $five = @((New-E 'Hollow Knight'), (New-E 'Update A' 'AddOn' 0))
        $a = Remove-GameEntry $five 99 $true
        $b = Remove-GameEntry $five -1 $true
        @($a).Count | Should -Be 2
        @($b).Count | Should -Be 2
    }
}

Describe 'Where the game picker opens when the form points nowhere' -Tag 'Unit' {

    # FolderBrowserDialog is the old SHBrowseForFolder tree and does NOT remember
    # its last folder between openings, so the first pick of a session has to be
    # aimed by the app or it lands wherever the shell feels like. This is the
    # aiming, and the only part of it that is a function rather than a click
    # handler.

    It 'names GOG Galaxy own download folder, or nothing at all' {
        $p = Get-DefaultGameBrowseFolder
        $p | Should -BeOfType [string]
        if ($p) {
            # Never a guess that does not exist - a SelectedPath pointing at a
            # missing folder is silently ignored, which is the same as no aim at
            # all but harder to notice.
            Test-Path $p -PathType Container | Should -BeTrue
            $p | Should -BeLike '*Offline Installers'
        }
    }

    It 'never throws, whatever the machine looks like' {
        # Runs on CI, where no GOG Galaxy exists and ProgramFiles(x86) may not
        # either. Returning '' is the answer there; an exception would take the
        # Add game button down with it.
        { Get-DefaultGameBrowseFolder } | Should -Not -Throw
    }
}

Describe 'Whether the disc label is ours to take back' -Tag 'Unit' {

    # Adding a game to an empty form seeds the disc label from its name. Removing
    # that game has to take the name back, or the next game added never replaces
    # it - seeding only fires into an EMPTY box - and the disc is built carrying
    # the previous game's name in This PC.
    #
    # The click handler cannot be driven from a test: getting a game onto the form
    # means the folder picker, whose tree UI Automation cannot see. So the rule
    # lives in a function and the function is what is tested here.

    It 'takes back a label it typed itself' {
        Test-LabelIsSeeded 'Alan Wake' 'Alan Wake' | Should -BeTrue
    }

    It 'leaves a label the user typed over the top of the seeded one' {
        Test-LabelIsSeeded 'ALAN WAKE DISC' 'Alan Wake' | Should -BeFalse
    }

    It 'leaves a label that was never seeded, however it looks' {
        # A project file's label, or one typed into an empty box before any game
        # was added. Nothing was seeded, so nothing is ours.
        Test-LabelIsSeeded 'UI Fixture' ''    | Should -BeFalse
        Test-LabelIsSeeded 'Alan Wake'  ''    | Should -BeFalse
        Test-LabelIsSeeded 'Alan Wake'  $null | Should -BeFalse
    }

    It 'leaves a label the user typed that happens to match the game name' {
        # They typed it, so they own it - even though the text is identical to
        # what seeding would have produced. Only a recorded seed makes it ours,
        # which is why this compares against the seed and not against the entry.
        Test-LabelIsSeeded 'Alan Wake' $null | Should -BeFalse
    }

    It 'is not fooled by an empty box' {
        Test-LabelIsSeeded '' 'Alan Wake' | Should -BeFalse
        Test-LabelIsSeeded '' ''          | Should -BeFalse
    }
}

Describe 'Aiming the file pickers' -Tag 'Unit' {

    # Same wart the game picker had, on four more dialogs. An OpenFileDialog with no
    # InitialDirectory opens wherever the shell last left it - on a machine with a
    # redirected Desktop that is somebody's OneDrive, which is how a username ends
    # up on screen in a screen recording.
    #
    # The click handlers cannot be driven from a test, so the folder-resolving is a
    # function and the function is what is tested.

    BeforeAll {
        $script:PickDir  = Join-Path $script:Sandbox 'pickers'
        $script:PickFile = Join-Path $script:PickDir 'artwork.png'
        New-Item -ItemType Directory -Force -Path $script:PickDir | Out-Null
        Set-Content -LiteralPath $script:PickFile -Value 'x' -Encoding Ascii
    }

    It 'takes the folder a file sits in' {
        Get-ExistingFolderOf $script:PickFile | Should -Be $script:PickDir
    }

    It 'takes a folder as itself' {
        Get-ExistingFolderOf $script:PickDir | Should -Be $script:PickDir
    }

    It 'falls back one level when the leaf is gone, rather than giving up' {
        # A box can hold a path to something since deleted - a manual that moved, a
        # disc folder cleaned out. Landing in the parent puts you next to where you
        # were, which beats being dropped wherever the shell feels like.
        Get-ExistingFolderOf (Join-Path $script:Sandbox 'no-such-folder') | Should -Be $script:Sandbox
    }

    It 'gives up when the parent is gone too' {
        # Two levels missing leaves nothing sensible to aim at. A dialog pointed at
        # a folder that does not exist is silently ignored by Windows, which looks
        # exactly like not aiming it at all, only harder to notice later - so return
        # nothing and let the caller fall through to the next guess.
        Get-ExistingFolderOf (Join-Path $script:Sandbox 'no-such-folder\gone.png') | Should -Be ''
    }

    It 'ignores nothing at all' {
        Get-ExistingFolderOf ''      | Should -Be ''
        Get-ExistingFolderOf '   '   | Should -Be ''
        Get-ExistingFolderOf $null   | Should -Be ''
    }

    It 'trims, because a pasted path often carries a space' {
        Get-ExistingFolderOf ("  $($script:PickDir)  ") | Should -Be $script:PickDir
    }
}

Describe 'Whether BUILD is about to overwrite anything' -Tag 'Unit' {

    # The ISO is named from the disc label, so two discs built into one output
    # folder are two files. That is correct - DiscWright cannot tell its own
    # leftovers from something you put there, so it never deletes an ISO it did not
    # write.
    #
    # What was wrong is what the button SAID. Test-AlreadyBuilt used to answer "is
    # there any .iso in here", so building ALAN WAKE and then THE WITCHER into the
    # same folder left the button reading REBUILD ISO, offering to replace an ISO
    # that was never touched.

    BeforeAll {
        $script:OutA = Join-Path $script:Sandbox 'outA'
        New-Item -ItemType Directory -Force -Path $script:OutA | Out-Null
        Set-Content -LiteralPath (Join-Path $script:OutA 'ALAN WAKE.iso') -Value 'x' -Encoding Ascii
    }

    It 'names the file from the label, the way the build does' {
        Get-IsoPath $script:OutA 'ALAN WAKE' | Should -Be (Join-Path $script:OutA 'ALAN WAKE.iso')
    }

    It 'strips what a filename cannot carry' {
        # Same fold the build applies. A label is allowed characters a path is not.
        Get-IsoPath $script:OutA 'The Witcher: Enhanced' | Should -Be (Join-Path $script:OutA 'The Witcher_ Enhanced.iso')
    }

    It 'names nothing when there is nothing to name' {
        Get-IsoPath ''             'ALAN WAKE' | Should -Be ''
        Get-IsoPath $script:OutA   ''          | Should -Be ''
        Get-IsoPath $script:OutA   '   '       | Should -Be ''
    }

    It 'replaces what it cannot use rather than dropping it' {
        # A label of nothing but punctuation still yields a name - the fold swaps
        # each character for an underscore instead of removing it. Odd label, odd
        # filename, but the button and the build agree on it, which is the only
        # property that matters here.
        Get-IsoPath $script:OutA '***' | Should -Be (Join-Path $script:OutA '___.iso')
    }

    It 'says REBUILD only for the ISO this label writes' {
        Test-AlreadyBuilt $script:OutA 'ALAN WAKE' | Should -BeTrue
    }

    It 'says BUILD when the label changed, because nothing of that name is there' {
        # The regression this block exists for.
        Test-AlreadyBuilt $script:OutA 'The Witcher - Enhanced Edition' | Should -BeFalse
    }

    It 'is not fooled by a staging folder left behind' {
        # The disc folder is rebuilt from scratch by every build and holds nothing
        # that is not also in the ISO, so it must not make the button claim an ISO
        # is about to be replaced.
        $out = Join-Path $script:Sandbox 'outB'
        New-Item -ItemType Directory -Force -Path (Join-Path $out 'disc') | Out-Null
        Test-AlreadyBuilt $out 'ALAN WAKE' | Should -BeFalse
    }

    It 'says nothing about a folder that is not there' {
        Test-AlreadyBuilt (Join-Path $script:Sandbox 'never-made') 'ALAN WAKE' | Should -BeFalse
    }
}

Describe 'Control characters cannot escape into what a disc carries' -Tag 'Unit' {

    # Everything the build writes is line-based or quoted. autorun.inf is one
    # directive per line; the menu's JScript puts names inside string literals. So a
    # CR or LF does not corrupt the text - it ends the line and starts a new one.
    #
    # The UI cannot produce one: the label box is single line. A project file can,
    # and assigning to a single-line TextBox does NOT strip them, which is the exact
    # path a loaded project takes to reach the build. That assumption - "the box is
    # single line, so it cannot happen" - is what let this through in the first
    # place.

    It 'strips the characters that end a line' {
        Remove-ControlChars ("a" + [char]13 + [char]10 + "b") | Should -Be 'ab'
        Remove-ControlChars ("a" + [char]9  + "b")            | Should -Be 'ab'
        Remove-ControlChars ("a" + [char]0  + "b")            | Should -Be 'ab'
        Remove-ControlChars ("a" + [char]27 + "b")            | Should -Be 'ab'
        Remove-ControlChars ("a" + [char]127 + "b")           | Should -Be 'ab'
    }

    It 'leaves everything a real disc label needs' {
        # Accents and typographic dashes are all over GOG titles and must survive.
        Remove-ControlChars 'The Witcher - Enhanced Edition' | Should -Be 'The Witcher - Enhanced Edition'
        Remove-ControlChars 'Uber Alles'                     | Should -Be 'Uber Alles'
        Remove-ControlChars 'STAR WARS: Empire at War'       | Should -Be 'STAR WARS: Empire at War'
    }

    It 'survives nothing at all' {
        Remove-ControlChars ''    | Should -Be ''
        Remove-ControlChars $null | Should -BeNullOrEmpty
    }

    It 'writes no extra directive when the label carries a newline' {
        # The proof. Before this, "label=<newline>open=..." wrote open= as a
        # directive of its own - twice, because the label is also used for
        # action=Run.
        $out = Join-Path $script:Sandbox 'poisoned-autorun.inf'
        New-AutorunInf ("My Game" + [char]13 + [char]10 + "open=Extras\payload.exe") 'game.ico' $true $out
        $txt = Get-Content -LiteralPath $out -Raw
        $txt | Should -Not -Match '(?m)^open='
        # Compared as whole lines, not by regex. The payload is full of backslashes
        # and dots, and a pattern that has to escape them is a pattern that can be
        # wrong in a way the test cannot see - which is exactly what happened on the
        # first attempt at this test.
        $lines = @($txt -split "`r`n")
        $lines | Should -Contain 'label=My Gameopen=Extras\payload.exe'
        $lines | Should -Contain 'action=Run My Gameopen=Extras\payload.exe'
        @($lines | Where-Object { $_ -eq '[autorun]' }).Count | Should -Be 1
    }

    It 'keeps the menu parsable when a name carries a newline' {
        # A newline inside a JS string literal is not a character to encode, it is
        # the end of the literal - JScript rejects the whole file, so one bad name
        # would take the entire menu with it.
        $js = ConvertTo-JsString ("Hollow" + [char]13 + [char]10 + "Knight")
        $js | Should -Be 'HollowKnight'
        $js | Should -Not -Match "[`r`n]"
    }

    It 'still escapes the characters that matter, unchanged' {
        # The stripping is additive - it must not have loosened anything.
        ConvertTo-JsString 'He said "hi" \ <script>' | Should -Be 'He said \"hi\" \\ \x3cscript\x3e'
        ConvertTo-HtmlText '<b>&"'                   | Should -Be '&lt;b&gt;&amp;&quot;'
    }

    It 'escapes what is above ASCII rather than letting the file eat it' {
        # menu.hta is written as an ASCII file, so an unescaped character above
        # 7-bit does not reach the menu at all - Set-Content replaces it with a
        # literal "?". Built from code points because this file has to stay pure
        # ASCII itself.
        $tm = [char]0x2122
        ConvertTo-JsString ('Empire at War' + $tm) | Should -Be 'Empire at War\u2122'
        ConvertTo-HtmlText ('Empire at War' + $tm) | Should -Be 'Empire at War&#8482;'
        # A surrogate pair is one character to an HTML entity, which names a code
        # point, and two escapes to JavaScript, which counts UTF-16 units.
        $astral = [string][char]0xD83D + [char]0xDE00
        ConvertTo-HtmlText $astral | Should -Be '&#128512;'
        ConvertTo-JsString $astral | Should -Be '\ud83d\ude00'
    }
}

Describe 'Which disc sizes DiscWright knows about' {

    It 'hands back a capacity for every tier it lists' {
        foreach ($t in Get-MediaTiers) {
            (Get-MediaCapacity $t.Key) | Should -Be ([double]$t.Gib * 1GB) -Because "$($t.Key) is in the table"
        }
    }

    It 'lists the tiers smallest first, so the recommendation stops at the first fit' {
        $gib = @(Get-MediaTiers | ForEach-Object { $_.Gib })
        $sorted = @($gib | Sort-Object)
        ($gib -join ',') | Should -Be ($sorted -join ',')
    }

    It 'returns nothing for a medium it has never heard of' {
        Get-MediaCapacity 'LASERDISC' | Should -Be 0
        Get-MediaCapacity ''          | Should -Be 0
    }

    It 'still recommends what it always did, and now says which tier that was' {
        $cd = Get-MediaRec (0.5 * 1GB)
        $cd.Text | Should -Be 'fits CD-R 700 MB'
        $cd.Key  | Should -Be 'CD'

        (Get-MediaRec (5 * 1GB)).Text  | Should -Be 'needs DVD9 8.5 GB (dual layer)'
        (Get-MediaRec (10 * 1GB)).Text | Should -Be 'too big for DVD - needs BD-R 25 GB'
    }

    It 'admits when a payload is past the largest disc there is' {
        $r = Get-MediaRec (200 * 1GB)
        $r.Fit | Should -BeFalse
        $r.Key | Should -Be ''
    }
}

Describe 'Turning what the dropdown says into a medium key' {

    It 'round-trips every tier through its name' {
        foreach ($t in Get-MediaTiers) {
            Get-MediaKeyFromName $t.Name | Should -Be $t.Key
            Get-MediaNameFromKey $t.Key  | Should -Be $t.Name
        }
    }

    It 'annotates a row with whether the payload fits that medium' {
        $tier = @(Get-MediaTiers | Where-Object { $_.Key -eq 'DVD5' })[0]
        Get-MediaOptionText $tier @{ Ok=$true }  | Should -Be 'DVD5 4.7 GB  -  fits'
        Get-MediaOptionText $tier @{ Ok=$false } | Should -Be 'DVD5 4.7 GB  -  will not fit'
    }

    It 'leaves a row bare when there is nothing to plan' {
        # An empty form has no answer to give, so the list says only what the
        # media are - which is what it said before any of this existed.
        $tier = @(Get-MediaTiers | Where-Object { $_.Key -eq 'DVD5' })[0]
        Get-MediaOptionText $tier $null | Should -Be 'DVD5 4.7 GB'
    }

    It 'still reads an annotated row back as the medium it names' {
        # The row text changes with the form; the key it stands for must not.
        Get-MediaKeyFromName 'DVD5 4.7 GB  -  fits'         | Should -Be 'DVD5'
        Get-MediaKeyFromName 'BD-R 25 GB  -  will not fit'  | Should -Be 'BD25'
        Get-MediaKeyFromName 'BD-R DL 50 GB (dual layer)  -  fits' | Should -Be 'BD50'
        Get-MediaKeyFromName 'DVD5 4.7 GB'                  | Should -Be 'DVD5'
    }

    It 'reads the automatic setting as no medium at all' {
        # This is what keeps a single disc building exactly as it always has.
        Get-MediaKeyFromName (Get-MediaAutoText) | Should -Be ''
        Get-MediaKeyFromName 'something else'    | Should -Be ''
    }

    It 'falls back to the automatic setting for a key it cannot place' {
        Get-MediaNameFromKey 'LASERDISC' | Should -Be (Get-MediaAutoText)
        Get-MediaNameFromKey ''          | Should -Be (Get-MediaAutoText)
    }
}

Describe 'Knowing what a build is about to overwrite' {

    BeforeAll {
        $script:BtDir = Join-Path $script:Sandbox 'buildtargets'
        New-Item -ItemType Directory -Force -Path $script:BtDir | Out-Null
        function New-EmptyIso([string]$name) {
            $fs = [IO.File]::Create((Join-Path $script:BtDir $name)); $fs.SetLength(1024); $fs.Close()
        }
    }

    It 'names one file, from the label as typed' {
        $bt = Get-BuildTargets $script:BtDir 'RETRO NIGHT'
        $bt.Count    | Should -Be 1
        $bt.Labels[0] | Should -Be 'RETRO NIGHT'
        Split-Path $bt.Isos[0] -Leaf | Should -Be 'RETRO NIGHT.iso'
    }

    It 'counts nothing as built in an empty folder' {
        (Get-BuildTargets $script:BtDir 'DISC A').Existing | Should -Be 0
    }

    It 'notices the ISO it is about to replace' {
        New-EmptyIso 'DISC B.iso'
        (Get-BuildTargets $script:BtDir 'DISC B').Existing | Should -Be 1
    }

    It 'ignores an ISO belonging to a different disc in the same folder' {
        New-EmptyIso 'SOMETHING ELSE.iso'
        (Get-BuildTargets $script:BtDir 'DISC C').Existing | Should -Be 0
    }

    It 'no longer suffixes anything with a disc number' {
        # Sets used to write "RETRO NIGHT D1.iso". Nothing does now, so an old
        # set sitting in the folder must not be mistaken for this build.
        New-EmptyIso 'DISC D D1.iso'
        (Get-BuildTargets $script:BtDir 'DISC D').Existing | Should -Be 0
    }
}

Describe "The menu's JavaScript is valid JavaScript" {

    # The menu is ~23,000 characters of JScript living inside a PowerShell
    # here-string. Nothing used to check it. A stray brace or a half-deleted
    # function would sail through every test here - the parser only sees a
    # string - and would then break the menu at runtime, on the disc, after a
    # burn. The Play-task tests pull three functions out and run them, which
    # says nothing about the other forty-two.
    #
    # new Function(src) parses without executing, so document, window and
    # ActiveX are never touched. It throws on a syntax error, which is exactly
    # and only what is being asked.

    BeforeDiscovery {
        $script:HaveCScriptMenu = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    BeforeAll {
        $script:CScriptMenu = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

        $appSrc = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1')
        $m = [regex]::Match($appSrc, "(?s)\`$tpl = @'\r?\n(.*?)\r?\n'@")
        if (-not $m.Success) { throw 'the menu template is no longer a $tpl here-string' }
        $tpl = $m.Groups[1].Value

        # Values only have to be syntactically valid - nothing is executed. The
        # test fails loudly on an unsubstituted placeholder rather than quietly
        # parse-checking a template with %%NEWTHING%% still in it.
        $subs = @{
            '%%APPNAME%%'  = 'DiscMenu_Test'; '%%ICONFILE%%' = 'disc.ico'; '%%TITLE%%' = 'Test'
            '%%STAGEBORDER%%' = 'border:0;';  '%%BTNBORDER%%' = 'border:0;'; '%%PANELLEFT%%' = '40'
            '%%GAMES%%'    = '[{n:"A",m:"A",s:"setup.exe",man:"",ext:"",a:[]}]'
            '%%BTNS%%'     = '["Play","Install","Exit"]'
            '%%MANUAL%%'   = 'manual.pdf'; '%%MUSIC%%' = 'music.mp3'; '%%PREVIEW%%' = 'false'
            '%%SHOWCAP%%'  = 'true'
            # A disc that is not part of a set, which is what the template
            # produces for every disc built before sets existed.
            '%%SET%%'      = 'null'; '%%SETFILE%%' = 'Disc set.txt'
        }
        foreach ($k in $subs.Keys) { $tpl = $tpl.Replace($k, $subs[$k]) }
        $script:MenuLeftover = [regex]::Match($tpl, '%%[A-Z]+%%').Value

        $js = [regex]::Match($tpl, '(?s)<script[^>]*>(.*?)</script>')
        if (-not $js.Success) { throw 'no <script> block in the menu template' }
        $script:MenuJs = $js.Groups[1].Value
    }

    It 'has no placeholder the substitution table has forgotten' {
        # Otherwise a new %%TOKEN%% would be parse-checked as literal text and
        # this whole block would quietly stop testing what it claims to.
        $script:MenuLeftover | Should -BeNullOrEmpty
    }

    Context 'opening things on a shell too old to have ShellExecute' {

        # Shell.ShellExecute needs shell32.dll 5.0 and is documented as Windows
        # 2000 or newer. Windows 98 has the Shell.Application object and not the
        # method, so every button on the menu threw and nothing opened. Reported
        # from a real 98 machine: the disc read fine and no button did anything.
        #
        # openThing tries ShellExecute and falls back to WScript.Shell, which has
        # been present since Windows Scripting Host shipped with 98. These read
        # the source rather than run it, because the fault they guard against is
        # in the CALLER: a new button written next year that calls ShellExecute
        # directly would work everywhere the author can test and break 98 again,
        # silently, on a burned disc.

        It 'routes every open through the one helper' {
            # Exactly one mention, and it is the attempt inside openThing.
            @([regex]::Matches($script:MenuJs, 'shell\.ShellExecute')).Count |
                Should -Be 1 -Because 'buttons must call openThing, not ShellExecute'
        }

        It 'defines that helper' {
            $script:MenuJs | Should -Match 'function openThing\s*\('
        }

        It 'falls back to something Windows 98 actually has' {
            $script:MenuJs | Should -Match 'WScript\.Shell'
        }

        It 'tries the modern call first, so nothing newer changes behaviour' {
            # Order matters: on Windows 2000 and later ShellExecute succeeds and
            # the fallback is never reached, which is what keeps this invisible
            # everywhere it is not needed.
            $helper = [regex]::Match($script:MenuJs,
                '(?s)function openThing\s*\(.*?\n  \}').Value
            $helper | Should -Not -BeNullOrEmpty
            $helper.IndexOf('ShellExecute') | Should -BeLessThan $helper.IndexOf('WScript.Shell')
        }
    }

    It 'parses' -Skip:(-not $script:HaveCScriptMenu) {
        $probe = @'
var src = WScript.StdIn.ReadAll();
try { new Function(src); WScript.Echo("OK"); }
catch (e) { WScript.Echo("SYNTAX ERROR: " + e.message); }
'@
        $pf = Join-Path $script:Sandbox 'jsparse.js'
        $bf = Join-Path $script:Sandbox 'menubody.js'
        Set-Content -LiteralPath $pf -Value $probe -Encoding Ascii
        Set-Content -LiteralPath $bf -Value $script:MenuJs -Encoding Ascii
        $out = (cmd /c "`"$script:CScriptMenu`" //Nologo //E:JScript `"$pf`" < `"$bf`"" 2>&1) -join ' '
        $out.Trim() | Should -Be 'OK'
    }

    It 'still defines the functions the menu is built out of' {
        # A parse check passes on an empty string too. This is the guard that the
        # extraction above actually found the menu and not some other <script>.
        foreach ($fn in 'init','show','doPlay','doInstall','playTasks','capFor','btnHtml') {
            $script:MenuJs | Should -Match ("function\s+" + $fn + "\s*\(")
        }
    }

    It 'has no leftovers of the disc-set caption' {
        # capFor used to append "Disc 2 of 3" from DISCNUM/DISCOF. Disc sets are
        # gone; a half-removal would leave an undefined reference that parses
        # fine and throws only when the menu is opened.
        foreach ($dead in 'DISCNUM','DISCOF','discLine','capd') {
            $script:MenuJs | Should -Not -Match $dead
        }
    }
}

Describe 'A game name with characters outside plain ASCII' {

    # Reported against 0.4.2 by someone who built the Star Wars pack: the menu
    # offered "Star Wars?: Empire at War?". menu.hta is written with
    # Set-Content -Encoding ASCII, so the name was destroyed by the file rather
    # than by the menu - every character above 7-bit became a literal "?".
    #
    # Escaping, not transliterating, is what the fix does, and the last test
    # here is the reason: the menu compares these strings against real names,
    # MatchName against what the installer registered and Setup against a folder
    # on the disc. A "(TM)" that only looked right would break both.
    #
    # The name is built from code points because this test file has to stay pure
    # ASCII itself - the repo is BOM-less, so PowerShell 5.1 would read a
    # non-ASCII byte in the ANSI codepage, and CI rejects one.

    BeforeDiscovery {
        $script:HaveCScriptFancy = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    BeforeAll {
        $script:CScriptFancy = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

        $script:Fancy = 'Star Wars' + [char]0x2122 + ': Empire at War' + [char]0x00AE
        $script:FancyHta = Join-Path $script:Sandbox 'fancy-menu.hta'
        New-MenuHta @{
            GameName = $script:Fancy
            Games    = @(@{ Name=$script:Fancy; MatchName=$script:Fancy
                            Setup=('Games\01 - ' + $script:Fancy + '\setup.exe'); AddOns=@() })
            Buttons  = @('Play','Install','Exit')
            MusicFile = ''; ManualFile = ''; PanelSide = 'Right'; IconName = 'disc.ico'
            WindowBorder = $true; ButtonStyle = 'Minimal'
        } $script:FancyHta
        $script:FancyText = Get-Content -LiteralPath $script:FancyHta -Raw
    }

    It 'writes a menu with nothing left for an ASCII file to destroy' {
        $bytes = [IO.File]::ReadAllBytes($script:FancyHta)
        @($bytes | Where-Object { $_ -gt 127 }).Count | Should -Be 0
        # The reported symptom, named directly. Checked here rather than by
        # looking for "?" anywhere, because the menu's JavaScript is full of
        # ternaries and always has been.
        $script:FancyText | Should -Not -Match 'Star Wars\?'
        $script:FancyText | Should -Not -Match 'Empire at War\?'
    }

    It 'escapes for markup and for JavaScript in their own syntaxes' {
        # HTML entities are not decoded inside <script>, so one escape cannot
        # serve both places the name lands.
        $script:FancyText | Should -Match '<title>Star Wars&#8482;: Empire at War&#174;</title>'
        $script:FancyText | Should -Match 'n:"Star Wars\\u2122: Empire at War\\u00ae"'
    }

    It 'rebuilds the original characters exactly when the menu runs' -Skip:(-not $script:HaveCScriptFancy) {
        # The assertion that matters. Compared as code points rather than as
        # text, so nothing is riding on how a console pipe encodes its output -
        # which is the very thing that caused the bug.
        $m = [regex]::Match($script:FancyText, '(?m)^\s*var GAMES=(.+?);\s')
        $m.Success | Should -BeTrue -Because 'the GAMES array has to be findable to be evaluated'
        $probe = @'
var GAMES = eval(WScript.StdIn.ReadAll());
var s = GAMES[0].n, out = [];
for (var i = 0; i < s.length; i++) out.push(s.charCodeAt(i));
WScript.Echo(out.join(","));
'@
        $pf = Join-Path $script:Sandbox 'fancyparse.js'
        $bf = Join-Path $script:Sandbox 'fancygames.js'
        Set-Content -LiteralPath $pf -Value $probe -Encoding Ascii
        Set-Content -LiteralPath $bf -Value $m.Groups[1].Value -Encoding Ascii
        $out = (cmd /c "`"$script:CScriptFancy`" //Nologo //E:JScript `"$pf`" < `"$bf`"" 2>&1) -join ' '
        $want = ($script:Fancy.ToCharArray() | ForEach-Object { [int]$_ }) -join ','
        $out.Trim() | Should -Be $want
    }
}

Describe "A name that looks like one of the menu's own placeholders" {

    # New-MenuHta fills %%GAMES%%, %%BTNS%% and the rest into its template, and
    # used to do it with eleven chained replaces. Text already filled in was then
    # filled in again: a game renamed "Game %%BTNS%% Edition" got the button list
    # pasted inside its string literal, which ended the literal, and the whole
    # menu failed to compile. GOG never names a game like that, but a game can be
    # renamed to anything. Found porting New-MenuHta to the Linux version.

    BeforeDiscovery {
        $script:HaveCScriptPct = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    BeforeAll {
        $script:CScriptPct = @(
            "$env:SystemRoot\System32\cscript.exe"
            (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

        $script:PctHta = Join-Path $script:Sandbox 'placeholder-menu.hta'
        New-MenuHta @{
            GameName = '%%GAMES%%'
            Games    = @(@{ Name='Game %%BTNS%% Edition'; MatchName='%%TITLE%%'
                            Setup='setup.exe'; AddOns=@(@{ Name='%%MUSIC%% Pack'; Setup='addon.exe' }) })
            Buttons  = @('Play','Install','Exit')
            MusicFile = 'music.mp3'; ManualFile = ''; PanelSide = 'Right'; IconName = 'disc.ico'
            WindowBorder = $true; ButtonStyle = 'Minimal'
        } $script:PctHta
        $script:PctText = Get-Content -LiteralPath $script:PctHta -Raw
    }

    It 'keeps each name exactly as it was typed' {
        $script:PctText | Should -Match ([regex]::Escape('n:"Game %%BTNS%% Edition",m:"%%TITLE%%"'))
        $script:PctText | Should -Match ([regex]::Escape('a:[{n:"%%MUSIC%% Pack"'))
        $script:PctText | Should -Match ([regex]::Escape('<title>%%GAMES%%</title>'))
    }

    It 'still fills in the placeholders themselves' {
        $script:PctText | Should -Match ([regex]::Escape('var BTNS=["Play","Install","Exit"];'))
        $script:PctText | Should -Match ([regex]::Escape('var MUSIC="music.mp3";'))
    }

    It "leaves the menu's script parsing" -Skip:(-not $script:HaveCScriptPct) {
        $js = [regex]::Match($script:PctText, '(?s)<script[^>]*>(.*?)</script>').Groups[1].Value
        $probe = @'
var src = WScript.StdIn.ReadAll();
try { new Function(src); WScript.Echo("OK"); }
catch (e) { WScript.Echo("SYNTAX ERROR: " + e.message); }
'@
        $pf = Join-Path $script:Sandbox 'pctparse.js'
        $bf = Join-Path $script:Sandbox 'pctbody.js'
        Set-Content -LiteralPath $pf -Value $probe -Encoding Ascii
        Set-Content -LiteralPath $bf -Value $js -Encoding Ascii
        $out = (cmd /c "`"$script:CScriptPct`" //Nologo //E:JScript `"$pf`" < `"$bf`"" 2>&1) -join ' '
        $out.Trim() | Should -Be 'OK'
    }
}

Describe 'Renaming a game for the menu' -Tag 'Unit' {

    # Show-EntryKindDialog has offered "Name on the menu" since multi-game discs
    # arrived, and the menu matched on whatever was typed into it. The GOG
    # registry holds the name GOG registered, so a rename that reworded a title
    # stopped Play finding the installed copy: Install still worked, Play stayed
    # grey, and nothing on screen said why.
    #
    # Worth writing down because both ROADMAP.md and docs/MANUAL-CHECKS.md
    # already described the split as though it existed - "a game renamed for the
    # menu must still match on its original name". It did not. The name shown and
    # the name matched are two fields now, and these tests are what makes those
    # documents true.

    BeforeAll {
        # What GOG's own installer registers for The Witcher, and the string
        # findGame reads back out of HKLM.
        $script:RegName = 'The Witcher: Enhanced Edition'

        # Omitting -Match leaves the key off the hashtable entirely, which is how
        # an entry out of a pre-version-6 project arrives.
        function New-RenameEntry {
            param([string]$Shown, [string]$Match, [string]$Setup = 'setup_the_witcher_1.0.exe')
            $e = @{ GameName=$Shown; Kind='Game'; ParentIndex=-1
                    ManualPath=$null; ExtrasPath=$null
                    SetupExe=[pscustomobject]@{ Name=$Setup } }
            if ($PSBoundParameters.ContainsKey('Match')) { $e.MatchName = $Match }
            return $e
        }
    }

    It 'starts a freshly detected game with both names the same' {
        $g = Get-GameInfo (New-FixtureGame -Slug 'rename_fresh')
        $g.Ok        | Should -BeTrue
        $g.MatchName | Should -Not -BeNullOrEmpty
        $g.MatchName | Should -Be $g.GameName
    }

    It 'shows the chosen name and matches the registered one' {
        $m = (Get-MenuGames @((New-RenameEntry -Shown 'The Witcher 1' -Match $script:RegName)))[0]
        $m.Name      | Should -Be 'The Witcher 1'
        $m.MatchName | Should -Be $script:RegName
    }

    It 'lets the folder on the disc follow the chosen name' {
        # Deliberate, and the whole point of splitting the fields: the new name is
        # the user's everywhere a person reads it - menu, folder, label - and only
        # the string handed to the registry stays GOG's.
        $entries = @(
            (New-RenameEntry -Shown 'The Witcher 1' -Match $script:RegName),
            (New-RenameEntry -Shown 'Hollow Knight' -Match 'Hollow Knight' -Setup 'setup_hk.exe'))
        Get-DiscEntryFolder $entries 0 | Should -Be 'Games\01 - The Witcher 1'
    }

    It 'falls back to the shown name when an entry carries no match name' {
        # A version 5 project whose source folder has since gone: there is no
        # registered name left anywhere to recover, and the shown name is the best
        # guess available. This is the old behaviour, kept for exactly that case
        # and no other.
        (Get-MenuGames @((New-RenameEntry -Shown 'The Witcher 1')))[0].MatchName |
            Should -Be 'The Witcher 1'
    }

    It 'writes the two names into the menu as separate strings' {
        $hta = Join-Path $script:Sandbox 'rename-menu.hta'
        New-MenuHta @{
            GameName = 'The Witcher 1'
            Games    = @(Get-MenuGames @((New-RenameEntry -Shown 'The Witcher 1' -Match $script:RegName)))
            Buttons  = @('Play','Install','Exit')
            MusicFile = ''; ManualFile = ''; PanelSide = 'Right'; IconName = 'disc.ico'
            WindowBorder = $true; ButtonStyle = 'Minimal'
        } $hta
        # The GAMES line only, not the whole file: a failure here should print
        # one line of JavaScript rather than the entire menu.
        $games = [regex]::Match((Get-Content -LiteralPath $hta -Raw), '(?m)^\s*var GAMES=(.+?);\s').Groups[1].Value
        $games | Should -Match 'n:"The Witcher 1"'
        $games | Should -Match 'm:"The Witcher: Enhanced Edition"'
    }

    Context 'against the matcher that ships in the menu' {

        # norm and nameHit are lifted out of DiscWright.ps1 and run under cscript,
        # so what is exercised is the code that reaches the disc rather than a
        # transcription of it here. A transcription agrees with itself forever.

        BeforeDiscovery {
            $script:HaveCScriptRename = @(
                "$env:SystemRoot\System32\cscript.exe"
                (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
            ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
        }

        BeforeAll {
            $script:CScriptRename = @(
                "$env:SystemRoot\System32\cscript.exe"
                (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
            ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

            function Get-JsFn([string]$text, [string]$name) {
                $start = $text.IndexOf("function $name(")
                if ($start -lt 0) { throw "DiscWright.ps1 has no JScript function called $name" }
                $i = $text.IndexOf('{', $start); $depth = 0
                for ($j = $i; $j -lt $text.Length; $j++) {
                    if ($text[$j] -eq '{') { $depth++ }
                    elseif ($text[$j] -eq '}') { $depth--; if ($depth -eq 0) { return $text.Substring($start, $j - $start + 1) } }
                }
                throw "unbalanced braces in $name"
            }
            $appText = Get-Content $appScript -Raw
            $script:MatcherJs = (Get-JsFn $appText 'norm') + "`r`n" + (Get-JsFn $appText 'nameHit') + "`r`n"
        }

        It 'finds the installed copy by the registered name and not by the new one' -Skip:(-not $script:HaveCScriptRename) {
            # 1,0 is the whole bug in two numbers. The first is what the menu asks
            # now; the second is what it used to ask after a rename.
            $probe = $script:MatcherJs +
                'var reg = "The Witcher: Enhanced Edition";' + "`r`n" +
                'WScript.Echo([nameHit(reg, "The Witcher: Enhanced Edition") ? 1 : 0,' + "`r`n" +
                '              nameHit(reg, "The Witcher 1") ? 1 : 0].join(","));' + "`r`n"
            $pf = Join-Path $script:Sandbox 'matcher.js'
            Set-Content -LiteralPath $pf -Value $probe -Encoding Ascii
            $out = (cmd /c "`"$script:CScriptRename`" //Nologo //E:JScript `"$pf`"" 2>&1) -join ' '
            $out.Trim() | Should -Be '1,0'
        }

        It 'still finds a copy when the name was only trimmed' -Skip:(-not $script:HaveCScriptRename) {
            # nameHit is a substring test in both directions, so shortening a title
            # always worked and the bug only bit on a reword. Recorded so the
            # fallback above is not mistaken for the thing that made trimming safe.
            $probe = $script:MatcherJs +
                'WScript.Echo(nameHit("The Witcher: Enhanced Edition", "The Witcher") ? 1 : 0);' + "`r`n"
            $pf = Join-Path $script:Sandbox 'matcher-trim.js'
            Set-Content -LiteralPath $pf -Value $probe -Encoding Ascii
            $out = (cmd /c "`"$script:CScriptRename`" //Nologo //E:JScript `"$pf`"" 2>&1) -join ' '
            $out.Trim() | Should -Be '1'
        }
    }

    Context 'through the project file' {

        BeforeAll {
            $script:RenameOut = Join-Path $script:Sandbox 'renameproj'
            New-Item -ItemType Directory -Force -Path $script:RenameOut | Out-Null
            $script:RenameSrc = New-FixtureGame -Slug 'witcher_ee'
            $g = Get-GameInfo $script:RenameSrc
            $script:DetectedName = $g.GameName
            # What Show-EntryKindDialog does when somebody types a new name: it
            # writes GameName, and nothing else.
            $g.GameName = 'The Witcher 1'
            Save-Project (New-BuildSettings -Games @($g) -Label 'The Witcher 1' -OutDir $script:RenameOut) $script:RenameOut
            $script:RenameJson = Join-Path $script:RenameOut 'discproject.json'
            $script:RenameRaw  = Get-Content -Raw -LiteralPath $script:RenameJson | ConvertFrom-Json
        }

        It 'writes schema version 13' {
            $script:RenameRaw.Version | Should -Be 13
        }

        It 'stores the registered name beside the chosen one' {
            $script:RenameRaw.Games[0].GameName  | Should -Be 'The Witcher 1'
            $script:RenameRaw.Games[0].MatchName | Should -Be $script:DetectedName
            $script:DetectedName | Should -Not -Be 'The Witcher 1'
        }

        It 'gives both names back when the project is reopened' {
            $e = @((Import-Project $script:RenameJson).GameEntries)[0]
            $e.Name  | Should -Be 'The Witcher 1'
            $e.Match | Should -Be $script:DetectedName
        }

        It 'reads a version 5 file as carrying no match name at all' {
            # Not as carrying the edited one. That difference is what lets the
            # reopen below tell "never had one" from "has one, use it".
            $old = Join-Path $script:RenameOut 'v5.json'
            ((Get-Content -Raw -LiteralPath $script:RenameJson) -replace
                '"MatchName":\s*"[^"]*",?\s*', '') | Set-Content -LiteralPath $old -Encoding UTF8
            $e = @((Import-Project $old).GameEntries)[0]
            $e.Name  | Should -Be 'The Witcher 1'
            $e.Match | Should -BeNullOrEmpty
        }
    }

    Context 'a version 5 project that was renamed under the old code' {

        # The repair. A file written before this fix carries no match name, but it
        # still names the source folder - so re-detection on open reads the
        # registered name straight off the installer again while the edited name
        # stays the user's. Nobody has to know the file was ever wrong.
        #
        # Set-GameEntries writes to the list and the labels, so those are here as
        # real controls and stand-ins, the same way 'Recovering from a bad folder
        # choice' does it further up.

        BeforeAll {
            Add-Type -AssemblyName System.Windows.Forms
            $script:lvGames = New-Object System.Windows.Forms.ListView
            $script:lvGames.View = 'Details'
            foreach ($c in @('#','Name','Type','Belongs to')) { [void]$script:lvGames.Columns.Add($c,80) }
            $script:btnGameDel  = New-Object System.Windows.Forms.Button
            $script:btnAddOn    = New-Object System.Windows.Forms.Button
            $script:btnGameEdit = New-Object System.Windows.Forms.Button
            $script:cmbMedia    = New-Object System.Windows.Forms.ComboBox
            $script:chkXAll     = [pscustomobject]@{ Checked = $false }
            $script:lblMan      = [pscustomobject]@{ Text = 'Manual file:' }
            $script:lblEx       = [pscustomobject]@{ Text = 'Extras folder:' }
            $script:grpX        = [pscustomobject]@{ Text = '5)  Extra content' }
            $script:lblGame     = [pscustomobject]@{ Text = ''; ForeColor = $null }
            $script:cbMan       = [pscustomobject]@{ Checked = $false }
            $script:cbExtra     = [pscustomobject]@{ Checked = $false }
            $script:chkMusic    = [pscustomobject]@{ Checked = $false }
            $script:state       = @{ Games=@(); ExtraItems=@(); ManualPath=$null; ExtrasPath=$null; MusicFile=$null }

            $src = New-FixtureGame -Slug 'witcher_v5'
            $script:V5Detected = (Get-GameInfo $src).GameName
            # Exactly what Import-Project hands back for a version 5 file: a name
            # the user edited, and no Match key at all.
            Set-GameEntries @(@{ Folder=$src; Kind='Game'; ParentIndex=-1; Setup=$null
                                 Name='The Witcher 1'; Manual=$null; Extras=$null }) | Out-Null
            $script:V5Entry = @($script:state.Games)[0]
        }

        It 'keeps the name the user chose' {
            $script:V5Entry.GameName | Should -Be 'The Witcher 1'
        }

        It 'recovers the registered name off the installer' {
            $script:V5Entry.MatchName | Should -Be $script:V5Detected
            $script:V5Entry.MatchName | Should -Not -Be 'The Witcher 1'
        }

        It 'hands the menu the recovered name' {
            (Get-MenuGames @($script:V5Entry))[0].MatchName | Should -Be $script:V5Detected
        }
    }

    Context 'wired so a rename cannot reach the match name' {

        BeforeAll {
            $tree = [System.Management.Automation.Language.Parser]::ParseFile($appScript, [ref]$null, [ref]$null)
            $assigns = @($tree.FindAll({ param($n)
                $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $n.Left -is [System.Management.Automation.Language.MemberExpressionAst] -and
                "$($n.Left.Member)" -eq 'MatchName' }, $true))

            function Get-EnclosingFunction($node) {
                $n = $node.Parent
                while ($n) {
                    if ($n -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $n.Name }
                    $n = $n.Parent
                }
                return '<top level>'
            }
            $script:MatchWriters = @($assigns | ForEach-Object { Get-EnclosingFunction $_ } | Sort-Object -Unique)
        }

        It 'is written in exactly the three places that are allowed to write it' {
            # Set-InstallerFacts reads it off a GOG installer, Get-FolderInfo off a
            # folder that is not a GOG download, and Set-GameEntries restores one
            # a project file carried. Anywhere else - and Show-EntryKindDialog
            # above all - is the bug coming back.
            $script:MatchWriters | Should -Be @('Get-FolderInfo', 'Set-GameEntries', 'Set-InstallerFacts')
        }

        It 'is never written by the dialog that renames a game' {
            $script:MatchWriters | Should -Not -Contain 'Show-EntryKindDialog'
        }
    }
}

Describe 'The disc introduces itself on Linux too' -Tag 'Unit' {

    # autorun.inf is a Windows file and no Linux desktop reads it. .xdg-volume-info
    # is what gvfs looks for at the root of anything it mounts, so the two sit side
    # by side and each system reads the one it understands. These tests are about
    # the file being one gvfs will actually accept - the format is a GKeyFile, and
    # GKeyFile is unforgiving in two specific ways that are easy to get wrong and
    # invisible when you do.

    BeforeAll {
        $script:XdgOut = Join-Path $script:Sandbox 'xdg-volume-info'
    }

    It 'writes the group and the two keys gvfs reads' {
        New-XdgVolumeInfo 'The Witcher' 'TheWitcher.png' $script:XdgOut
        $lines = @([IO.File]::ReadAllText($script:XdgOut) -split "`n")
        $lines | Should -Contain '[Volume Info]'
        $lines | Should -Contain 'Name=The Witcher'
        $lines | Should -Contain 'IconFile=TheWitcher.png'
    }

    It 'writes no byte order mark' {
        # PowerShell 5.1's -Encoding UTF8 writes one. GKeyFile reads the BOM as
        # part of the first group name, so the group stops being "Volume Info",
        # every key belongs to a group nothing looks for, and the file parses to
        # nothing at all - with no error anywhere to say why the disc came up
        # nameless.
        New-XdgVolumeInfo 'The Witcher' 'TheWitcher.png' $script:XdgOut
        $bytes = [IO.File]::ReadAllBytes($script:XdgOut)
        @($bytes[0], $bytes[1], $bytes[2]) | Should -Not -Be @(0xEF, 0xBB, 0xBF)
        $bytes[0] | Should -Be ([byte][char]'[')
    }

    It 'ends its lines the way a Unix file does' {
        New-XdgVolumeInfo 'The Witcher' 'TheWitcher.png' $script:XdgOut
        $raw = [IO.File]::ReadAllText($script:XdgOut)
        $raw | Should -Not -Match "`r"
        $raw | Should -Match "`n"
    }

    It 'doubles a backslash exactly once' {
        # A GKeyFile value escapes with backslashes, so a lone one eats the
        # character after it. The first attempt used -replace with a quadrupled
        # replacement and wrote FOUR backslashes, because a backslash in a .NET
        # regex replacement is an ordinary character rather than an escape. This
        # is that bug, kept.
        $bs = [char]92
        New-XdgVolumeInfo ('a' + $bs + 'b') 'x.png' $script:XdgOut
        $lines = @([IO.File]::ReadAllText($script:XdgOut) -split "`n")
        $lines | Should -Contain ('Name=a' + $bs + $bs + 'b')
    }

    It 'keeps a label that carries a newline to one key' {
        # The same hazard New-AutorunInf has: a newline in the label would start a
        # line, and in a GKeyFile a line is a key.
        New-XdgVolumeInfo ("My Game" + [char]13 + [char]10 + "Icon=evil.png") 'good.png' $script:XdgOut
        $lines = @([IO.File]::ReadAllText($script:XdgOut) -split "`n")
        @($lines | Where-Object { $_ -like 'Name=*' }).Count | Should -Be 1
        @($lines | Where-Object { $_ -like 'Icon=*' }).Count | Should -Be 0
        $lines | Should -Contain 'IconFile=good.png'
    }

    It 'is accepted by a real GKeyFile parser' -Skip:(-not $script:HasWsl) {
        # The tests above assert what the format ought to be. This one asks
        # something that actually implements it, because every claim above is a
        # claim about somebody else's parser.
        New-XdgVolumeInfo 'The Witcher' 'TheWitcher.png' $script:XdgOut
        $wslPath = & wsl.exe wslpath -a ($script:XdgOut -replace '\\','/') 2>$null
        # GLib's GKeyFile where it is available, because that is literally the
        # parser gvfs calls - configparser only agrees with it by coincidence,
        # and the two differ on exactly the escaping this file has to get right.
        $py = @'
import sys
try:
    from gi.repository import GLib
    kf = GLib.KeyFile()
    kf.load_from_file(sys.argv[1], GLib.KeyFileFlags.NONE)
    name = kf.get_locale_string("Volume Info", "Name", None)
    icon = kf.get_string("Volume Info", "IconFile")
except ImportError:
    import configparser
    c = configparser.ConfigParser(interpolation=None)
    c.read(sys.argv[1], encoding="utf-8")
    name = c["Volume Info"]["Name"]
    icon = c["Volume Info"]["IconFile"]
print(name + "|" + icon)
'@
        $got = $py | & wsl.exe -- python3 - "$wslPath" 2>$null
        "$got".Trim() | Should -Be 'The Witcher|TheWitcher.png'
    }
}

Describe 'The PNG icon the Linux side needs' -Tag 'Unit' {

    # gvfs turns IconFile= into a GFileIcon and hands it to GdkPixbuf, whose ICO
    # support is for favicons rather than for the seven-frame icons Convert-ToIco
    # writes. So the disc carries the same picture twice, in both formats.

    BeforeAll {
        $script:SrcImg = Join-Path $script:Sandbox 'source-art.png'
        # Deliberately not square and not 256, so a center-crop and a resize both
        # have to happen for the result to come out right.
        $bmp = New-Object System.Drawing.Bitmap(600, 400)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::DarkGoldenrod)
        $g.Dispose()
        $bmp.Save($script:SrcImg, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }

    It 'writes a real 256x256 PNG' {
        $out = Join-Path $script:Sandbox 'icon-from-image.png'
        Convert-ToPng $script:SrcImg $out
        Test-Path $out | Should -BeTrue
        $img = [System.Drawing.Image]::FromFile($out)
        try {
            $img.Width  | Should -Be 256
            $img.Height | Should -Be 256
            $img.RawFormat.Guid | Should -Be ([System.Drawing.Imaging.ImageFormat]::Png.Guid)
        } finally { $img.Dispose() }
    }

    It 'can take its picture from an .ico when that is all the disc has' {
        # Reopening a built disc gives back an .ico as the icon source, so this is
        # the ordinary path on a rebuild, not an edge case.
        $ico = Join-Path $script:Sandbox 'from-image.ico'
        Convert-ToIco $script:SrcImg $ico
        $out = Join-Path $script:Sandbox 'icon-from-ico.png'
        Convert-ToPng $ico $out
        $img = [System.Drawing.Image]::FromFile($out)
        try {
            $img.Width  | Should -Be 256
            $img.Height | Should -Be 256
        } finally { $img.Dispose() }
    }
}

Describe 'Reading the picture out of an .ico' -Tag 'Unit' {

    # The two tests above convert a single flat colour and check only the size of
    # what comes out, so they could not see what went wrong here. GDI+'s Icon
    # class cannot decode a frame stored as a PNG, which from Vista on is how an
    # icon's 256px frame is normally stored. Asked for one it throws, or on some
    # real game icons returns noise; offered another frame it quietly uses that
    # instead, so even the icons that "worked" came out upscaled from 48 or 128
    # pixels. Found porting this function to the Linux version.
    #
    # So these build icons laid out the way real ones are, from a picture with
    # detail in it, and check what the result actually shows.

    BeforeAll {
        # A picture with detail at every scale: a colour gradient, a disc and a
        # small black square. Upscaled from a small frame, the square's edges go
        # soft, which a flat colour would never show.
        function New-DetailBitmap([int]$size) {
            $b = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($b)
            $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
                [System.Drawing.Color]::FromArgb(255, 30, 60, 220), [System.Drawing.Color]::FromArgb(255, 230, 40, 40), 45.0)
            $g.FillRectangle($grad, 0, 0, $size, $size)
            $g.FillEllipse([System.Drawing.Brushes]::Gold, [int]($size * 0.25), [int]($size * 0.25), [int]($size * 0.5), [int]($size * 0.5))
            $g.FillRectangle([System.Drawing.Brushes]::Black, [int]($size * 0.44), [int]($size * 0.44), [int]($size * 0.12), [int]($size * 0.12))
            $g.Dispose(); $grad.Dispose()
            return $b
        }

        # An .ico with exactly the frames asked for, in the order given, each
        # stored as a PNG or as a 32-bit bitmap - the two ways real icons store
        # them. Assembled here rather than with Convert-ToIco, which only ever
        # writes one layout.
        function New-TestIco([string]$path, [array]$frames) {
            $blobs = @()
            foreach ($f in $frames) {
                $b = New-DetailBitmap $f.Size
                if ($f.Png) {
                    $ms = New-Object System.IO.MemoryStream
                    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                    $blobs += ,@($f.Size, $ms.ToArray()); $ms.Dispose()
                } else {
                    $blobs += ,@($f.Size, (Get-DibBytes $b))
                }
                $b.Dispose()
            }
            $ms = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter($ms)
            $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$blobs.Count)
            $off = 6 + 16 * $blobs.Count
            foreach ($bl in $blobs) {
                $dim = if ($bl[0] -ge 256) { 0 } else { $bl[0] }
                $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
                $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$bl[1].Length); $w.Write([int]$off)
                $off += $bl[1].Length
            }
            foreach ($bl in $blobs) { $w.Write($bl[1], 0, $bl[1].Length) }
            $w.Flush(); [IO.File]::WriteAllBytes($path, $ms.ToArray()); $w.Dispose()
        }

        # How far a 256px result is from the picture the icon's 256px frame holds,
        # as a mean per channel over a grid of samples, skipping the outermost
        # pixel. Measured: exactly 0.00 when the 256px frame is used, for all four
        # layouts below. Before the fix, 1.74 when a 128px frame was upscaled and
        # 9.82 when a 16px one was, and an exception for the PNG layouts. The
        # limit of 0.5 sits well clear of both.
        function Get-DistanceFromTruth([string]$png) {
            $truth = New-DetailBitmap 256
            $got = New-Object System.Drawing.Bitmap($png)
            try {
                $sum = 0; $n = 0
                for ($y = 2; $y -lt 254; $y += 3) { for ($x = 2; $x -lt 254; $x += 3) {
                    $a = $got.GetPixel($x, $y); $t = $truth.GetPixel($x, $y)
                    $sum += [math]::Abs($a.R - $t.R) + [math]::Abs($a.G - $t.G) + [math]::Abs($a.B - $t.B); $n += 3
                } }
                return ($sum / $n)
            } finally { $got.Dispose(); $truth.Dispose() }
        }

        $script:IcoDir = Join-Path $script:Sandbox 'ico-layouts'
        New-Item -ItemType Directory -Force -Path $script:IcoDir | Out-Null
    }

    It 'reads an icon whose only frame is a PNG' {
        $ico = Join-Path $script:IcoDir 'png-only.ico'
        New-TestIco $ico @(@{ Size = 256; Png = $true })
        $out = Join-Path $script:IcoDir 'png-only.png'
        Convert-ToPng $ico $out
        Get-DistanceFromTruth $out | Should -BeLessThan 0.5
    }

    It 'reads an icon laid out like a real game''s: 256 and 48 as PNG, first' {
        # The layout of the Alan Wake icon, which is taken from the installed game
        # and which the Windows app turned into noise.
        $ico = Join-Path $script:IcoDir 'game-layout.ico'
        New-TestIco $ico @(@{ Size = 256; Png = $true }, @{ Size = 48; Png = $true },
                           @{ Size = 32; Png = $false }, @{ Size = 16; Png = $false })
        $out = Join-Path $script:IcoDir 'game-layout.png'
        Convert-ToPng $ico $out
        Get-DistanceFromTruth $out | Should -BeLessThan 0.5
    }

    It 'uses the 256px frame rather than upscaling a smaller one' {
        # The layout Convert-ToIco writes, 256 as PNG and last. It never failed,
        # which is why nothing looked wrong: it upscaled the 128px bitmap instead.
        $ico = Join-Path $script:IcoDir 'ours.ico'
        New-TestIco $ico @(@{ Size = 16; Png = $false }, @{ Size = 32; Png = $false },
                           @{ Size = 128; Png = $false }, @{ Size = 256; Png = $true })
        $out = Join-Path $script:IcoDir 'ours.png'
        Convert-ToPng $ico $out
        Get-DistanceFromTruth $out | Should -BeLessThan 0.5
    }

    It 'still reads an icon whose largest frame is a plain bitmap' {
        $ico = Join-Path $script:IcoDir 'bitmaps.ico'
        New-TestIco $ico @(@{ Size = 16; Png = $false }, @{ Size = 256; Png = $false })
        $out = Join-Path $script:IcoDir 'bitmaps.png'
        Convert-ToPng $ico $out
        Get-DistanceFromTruth $out | Should -BeLessThan 0.5
    }
}

Describe 'The edges of a scaled picture' -Tag 'Unit' {

    # GDI+ samples past the edge of a picture while scaling it and blends what it
    # finds there - nothing, so transparency - into the outermost pixels. Every
    # icon frame and the menu background came out with a faint see-through rim,
    # even from a picture that is opaque to its edges. On the menu's dark backdrop
    # that shows as a thin line round the artwork. Found porting the icon code to
    # the Linux version, which does not do it.
    #
    # Every picture here is opaque to its edges, so every edge pixel of every
    # result must be too.

    BeforeAll {
        function New-OpaqueImage([string]$path, [int]$w, [int]$h) {
            $b = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($b)
            $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                (New-Object System.Drawing.Rectangle(0, 0, $w, $h)),
                [System.Drawing.Color]::FromArgb(255, 200, 180, 40), [System.Drawing.Color]::FromArgb(255, 20, 90, 200), 30.0)
            $g.FillRectangle($grad, 0, 0, $w, $h)
            $g.Dispose(); $grad.Dispose()
            $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
            return $path
        }

        # How many pixels round the edge of a picture are not fully opaque.
        function Get-SoftEdges([System.Drawing.Bitmap]$b) {
            $soft = 0
            for ($x = 0; $x -lt $b.Width; $x++) {
                if ($b.GetPixel($x, 0).A -lt 255) { $soft++ }
                if ($b.GetPixel($x, $b.Height - 1).A -lt 255) { $soft++ }
            }
            for ($y = 1; $y -lt $b.Height - 1; $y++) {
                if ($b.GetPixel(0, $y).A -lt 255) { $soft++ }
                if ($b.GetPixel($b.Width - 1, $y).A -lt 255) { $soft++ }
            }
            return $soft
        }

        # Every frame of an .ico, as bitmaps, read from the file rather than through
        # System.Drawing.Icon so the frames checked are exactly the stored ones.
        function Get-IcoFrames([string]$path) {
            $bytes = [IO.File]::ReadAllBytes($path)
            $count = [BitConverter]::ToUInt16($bytes, 4)
            $out = @()
            for ($i = 0; $i -lt $count; $i++) {
                $e = 6 + 16 * $i
                $w = [int]$bytes[$e]; if ($w -eq 0) { $w = 256 }
                $size = [int][BitConverter]::ToUInt32($bytes, $e + 8)
                $off = [int][BitConverter]::ToUInt32($bytes, $e + 12)
                if ($bytes[$off] -eq 0x89) {
                    $ms = New-Object System.IO.MemoryStream($bytes, $off, $size)
                    $img = [System.Drawing.Image]::FromStream($ms)
                    $out += ,(New-Object System.Drawing.Bitmap($img)); $img.Dispose(); $ms.Dispose()
                } else {
                    $px = $off + [BitConverter]::ToInt32($bytes, $off)
                    $b = New-Object System.Drawing.Bitmap($w, $w, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    for ($y = 0; $y -lt $w; $y++) { for ($x = 0; $x -lt $w; $x++) {
                        $p = $px + (($w - 1 - $y) * $w + $x) * 4
                        $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($bytes[$p+3], $bytes[$p+2], $bytes[$p+1], $bytes[$p]))
                    } }
                    $out += ,$b
                }
            }
            return ,$out
        }

        $script:EdgeDir = Join-Path $script:Sandbox 'edges'
        New-Item -ItemType Directory -Force -Path $script:EdgeDir | Out-Null
        $script:WideSrc = New-OpaqueImage (Join-Path $script:EdgeDir 'wide.png') 600 400
    }

    It 'keeps every edge of the Linux icon opaque' {
        $out = Join-Path $script:EdgeDir 'icon.png'
        Convert-ToPng $script:WideSrc $out
        $b = New-Object System.Drawing.Bitmap($out)
        try { Get-SoftEdges $b | Should -Be 0 } finally { $b.Dispose() }
    }

    It 'keeps every edge of every icon frame opaque' {
        $ico = Join-Path $script:EdgeDir 'icon.ico'
        Convert-ToIco $script:WideSrc $ico
        # Not @(Get-IcoFrames ...): it returns its list with a leading comma.
        $frames = Get-IcoFrames $ico
        $frames.Count | Should -Be 7
        foreach ($f in $frames) {
            try { Get-SoftEdges $f | Should -Be 0 -Because "the $($f.Width)px frame" } finally { $f.Dispose() }
        }
    }

    It 'keeps every edge of the menu background opaque, <Name>' -ForEach @(
        @{ Name = 'when the picture overflows sideways';   W = 1000; H = 480 }
        @{ Name = 'when the picture overflows vertically'; W = 400;  H = 300 }
        @{ Name = 'when the picture fits exactly';         W = 760;  H = 480 }
    ) {
        $src = New-OpaqueImage (Join-Path $script:EdgeDir "bg-$W-$H.png") $W $H
        $out = Join-Path $script:EdgeDir "bg-$W-$H-out.png"
        New-Background $src '' $out 'Right' $false $false
        $b = New-Object System.Drawing.Bitmap($out)
        try { Get-SoftEdges $b | Should -Be 0 } finally { $b.Dispose() }
    }
}

Describe 'The menu background, darkened to its edges and titled within its room' -Tag 'Unit' {

    # Two defects found porting New-Background to the Linux version.
    #
    # GDI+ antialiases its rectangle fills with pixel centres on whole numbers, so
    # a fill starting at 0 covers only half of pixel 0. The darkening over the
    # whole picture and the panel behind the buttons were both drawn that way: the
    # top row and the left column got half the darkening, and so did the panel's
    # first column. On bright artwork that was a light line along the top of the
    # button panel.
    #
    # And the title stopped shrinking at 12pt whether it fitted or not, with
    # nothing limiting its length, so a long GOG title ran under the panel or off
    # the edge of the menu.

    BeforeAll {
        $script:BgDir = Join-Path $script:Sandbox 'bg-edges'
        New-Item -ItemType Directory -Force -Path $script:BgDir | Out-Null

        function New-FlatImage([string]$path, [int]$w, [int]$h, [int]$grey) {
            $b = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($b)
            $g.Clear([System.Drawing.Color]::FromArgb(255, $grey, $grey, $grey))
            $g.Dispose()
            $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
            return $path
        }

        # The largest difference in any one channel between two pixels.
        function Get-Step($a, $b) {
            return [math]::Max([math]::Max([math]::Abs($a.R - $b.R), [math]::Abs($a.G - $b.G)),
                               [math]::Max([math]::Abs($a.B - $b.B), [math]::Abs($a.A - $b.A)))
        }

        # Where the title's bright ink sits, from the leftmost to the rightmost
        # column in the top 80 rows that is much lighter than the same picture
        # untitled. The shadow only darkens, so it is not counted.
        function Get-TitleInkColumns([string]$titled, [string]$plain) {
            $t = New-Object System.Drawing.Bitmap($titled); $p = New-Object System.Drawing.Bitmap($plain)
            try {
                $first = -1; $last = -1
                for ($x = 0; $x -lt $t.Width; $x++) {
                    for ($y = 0; $y -lt 80; $y++) {
                        if ($t.GetPixel($x, $y).GetBrightness() - $p.GetPixel($x, $y).GetBrightness() -gt 0.25) {
                            if ($first -lt 0) { $first = $x }
                            $last = $x; break
                        }
                    }
                }
                return @($first, $last)
            } finally { $t.Dispose(); $p.Dispose() }
        }

        $script:Bright = New-FlatImage (Join-Path $script:BgDir 'bright.png') 760 480 220
    }

    It 'darkens the top row as much as the row under it, <Side> panel' -ForEach @(
        @{ Side = 'Right' }, @{ Side = 'Left' }
    ) {
        # With the divider on: it is antialiased too, and a line starting at 0
        # covers only half of row 0 the same way.
        $out = Join-Path $script:BgDir "rows-$Side.png"
        New-Background $script:Bright '' $out $Side $true $false
        $b = New-Object System.Drawing.Bitmap($out)
        try {
            $lighter = 0
            for ($x = 0; $x -lt $b.Width; $x++) { if ((Get-Step $b.GetPixel($x, 0) $b.GetPixel($x, 1)) -gt 0) { $lighter++ } }
            $lighter | Should -Be 0 -Because 'a fill or a line starting at 0 must cover all of row 0'
        } finally { $b.Dispose() }
    }

    It 'darkens the left column as much as the one beside it, <Side> panel' -ForEach @(
        @{ Side = 'Right' }, @{ Side = 'Left' }
    ) {
        $out = Join-Path $script:BgDir "cols-$Side.png"
        New-Background $script:Bright '' $out $Side $false $false
        $b = New-Object System.Drawing.Bitmap($out)
        # The panel's gradient moves a level or two per column; half the
        # darkening is off by tens.
        try { Get-Step $b.GetPixel(0, 240) $b.GetPixel(1, 240) | Should -BeLessOrEqual 2 } finally { $b.Dispose() }
    }

    It 'gives the column where the panel starts wholly to one side, <Side> panel' -ForEach @(
        @{ Side = 'Right'; Start = 470 }, @{ Side = 'Left'; Start = 290 }
    ) {
        $out = Join-Path $script:BgDir "panel-$Side.png"
        New-Background $script:Bright '' $out $Side $false $false
        $b = New-Object System.Drawing.Bitmap($out)
        try {
            $here = $b.GetPixel($Start, 240)
            $near = [math]::Min((Get-Step $here $b.GetPixel(($Start - 1), 240)), (Get-Step $here $b.GetPixel(($Start + 1), 240)))
            $near | Should -BeLessOrEqual 2 -Because 'half a panel is neither the panel nor the artwork'
        } finally { $b.Dispose() }
    }

    # 451px wide at 12pt, in 416px of room.
    It 'keeps even a very long title in its room, <Side> panel' -ForEach @(
        @{ Side = 'Right'; Tx = 27 }, @{ Side = 'Left'; Tx = 330 }
    ) {
        $title  = 'Warhammer 40,000: Dawn of War - Game of the Year Edition'
        $titled = Join-Path $script:BgDir "long-$Side.png"
        $plain  = Join-Path $script:BgDir "plain-$Side.png"
        New-Background $script:Bright $title $titled $Side $false $true
        New-Background $script:Bright ''     $plain  $Side $false $false
        $ink = Get-TitleInkColumns $titled $plain
        $ink[0] | Should -BeGreaterOrEqual $Tx -Because 'the title was drawn'
        $ink[1] | Should -BeLessThan ($Tx + 416) -Because 'the title has 416px of room'
    }

    It 'still draws a title that fits at full size' {
        $titled = Join-Path $script:BgDir 'short.png'
        $plain  = Join-Path $script:BgDir 'short-plain.png'
        New-Background $script:Bright 'ALAN WAKE' $titled 'Right' $false $true
        New-Background $script:Bright ''          $plain  'Right' $false $false
        $ink = Get-TitleInkColumns $titled $plain
        # Measured off 0.7.4: 30pt, ink from column 35 to 256.
        ($ink[1] - $ink[0]) | Should -BeGreaterThan 200
    }
}

Describe 'Asking for the Linux files, and changing your mind' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    BeforeAll {
        $script:TogGames = @((Get-GameInfo (New-FixtureGame -Slug 'tog_one' -ExeMb 2)))
        $script:TogOut   = Join-Path $script:Sandbox 'build-toggle'
        New-Item -ItemType Directory -Force -Path $script:TogOut | Out-Null
        $script:TogStage = Join-Path $script:TogOut 'disc'
        $script:TogPng   = [IO.Path]::ChangeExtension((Get-DiscIconName 'Toggle Disc'), 'png')
    }

    It 'writes neither file when it was not asked' {
        $null = Invoke-Build (New-BuildSettings -Games $script:TogGames -Label 'Toggle Disc' -OutDir $script:TogOut) $script:LogSink
        Test-Path (Join-Path $script:TogStage '.xdg-volume-info') | Should -BeFalse
        Test-Path (Join-Path $script:TogStage $script:TogPng)     | Should -BeFalse
    }

    It 'writes both when it is asked' {
        $null = Invoke-Build (New-BuildSettings -Games $script:TogGames -Label 'Toggle Disc' -OutDir $script:TogOut -LinuxInfo) $script:LogSink
        Test-Path (Join-Path $script:TogStage '.xdg-volume-info') | Should -BeTrue
        Test-Path (Join-Path $script:TogStage $script:TogPng)     | Should -BeTrue
    }

    It 'takes them both away again when the box is unticked and the disc rebuilt' {
        # The half that is easy to forget. Leaving .xdg-volume-info behind would
        # point a Linux desktop at a .png that the icon cleanup has just removed,
        # which is worse than never having written either.
        $null = Invoke-Build (New-BuildSettings -Games $script:TogGames -Label 'Toggle Disc' -OutDir $script:TogOut) $script:LogSink
        Test-Path (Join-Path $script:TogStage '.xdg-volume-info') | Should -BeFalse
        Test-Path (Join-Path $script:TogStage $script:TogPng)     | Should -BeFalse
    }

    It 'leaves the Windows disc untouched either way' {
        Test-Path (Join-Path $script:TogStage 'autorun.inf') | Should -BeTrue
        Test-Path (Join-Path $script:TogStage (Get-DiscIconName 'Toggle Disc')) | Should -BeTrue
    }
}

Describe 'The Linux setting in a project file' -Tag 'Unit' {

    BeforeAll {
        $script:ProjDir = Join-Path $script:Sandbox 'linux-proj'
        New-Item -ItemType Directory -Force -Path $script:ProjDir | Out-Null
    }

    It 'is saved and comes back the way it went in' {
        $s = @{ Games=@(); Label='Round Trip'; IconPath=$script:Art; IconIsIco=$false
                Menu=$true; BgPath=$null; BgAsIs=$true; PanelSide='Right'
                Divider=$false; ShowTitle=$false; TitleText=''
                WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
                Buttons=@('Play'); ManualPath=$null; ExtrasPath=$null
                ExtraItems=@(); MediaKey=''; LinuxInfo=$true }
        Save-Project $s $script:ProjDir
        $raw = Get-Content -Raw (Join-Path $script:ProjDir 'discproject.json') | ConvertFrom-Json
        $raw.LinuxInfo | Should -BeTrue
        (Import-Project (Join-Path $script:ProjDir 'discproject.json')).LinuxInfo | Should -BeTrue
    }

    It 'reads back as off from a project saved before it existed' {
        # The compatibility promise. Reopening an older project and rebuilding has
        # to produce the disc it produced before, not one with files added to it.
        $old = Join-Path $script:ProjDir 'old.json'
        @{ Version=6; Label='Older Disc'; Games=@(); Buttons=@('Play')
           Menu=$true; BgAsIs=$true; PanelSide='Right'; ButtonStyle='Minimal'
           WindowBorder=$true } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $old -Encoding UTF8
        $p = Import-Project $old
        $p | Should -Not -BeNullOrEmpty
        [bool]$p.LinuxInfo | Should -BeFalse
    }

    It 'works out from the disc itself when there is no project file' {
        # Import-DiscFolder rebuilds settings by looking at a built disc. Whether
        # it was named for Linux is knowable by looking rather than by guessing.
        $d = Join-Path $script:Sandbox 'bare-disc'
        New-Item -ItemType Directory -Force -Path $d | Out-Null
        New-AutorunInf 'Bare Disc' 'BareDisc.ico' $true (Join-Path $d 'autorun.inf')
        (Import-DiscFolder $d).LinuxInfo | Should -BeFalse

        New-XdgVolumeInfo 'Bare Disc' 'BareDisc.png' (Join-Path $d '.xdg-volume-info')
        (Import-DiscFolder $d).LinuxInfo | Should -BeTrue
    }
}


Describe 'Get-EntriesMaxFileBytes' -Tag 'Unit' {

    # The decision behind the greying: does this disc clear the ISO9660 ceiling?
    # Tested here rather than through the checkbox, because the checkbox half
    # cannot run without a real form.

    It 'is zero when there is nothing on the disc' {
        $script:state = @{ Games = @() }
        Get-EntriesMaxFileBytes | Should -Be 0
    }

    It 'is the largest single file across every entry, not the total' {
        $script:state = @{ Games = @(
            @{ Ok = $true; MaxFileBytes = 1000; TotalBytes = 9000; Kind = 'Game' }
            @{ Ok = $true; MaxFileBytes = 7000; TotalBytes = 8000; Kind = 'Game' }
            @{ Ok = $true; MaxFileBytes = 2000; TotalBytes = 2000; Kind = 'AddOn' }
        ) }
        Get-EntriesMaxFileBytes | Should -Be 7000
    }

    It 'counts an add-on like anything else' {
        # A patch is as capable of holding a 4 GiB file as a game is.
        $script:state = @{ Games = @(
            @{ Ok = $true; MaxFileBytes = 10; TotalBytes = 10; Kind = 'Game' }
            @{ Ok = $true; MaxFileBytes = 99; TotalBytes = 99; Kind = 'AddOn' }
        ) }
        Get-EntriesMaxFileBytes | Should -Be 99
    }

    It 'puts a real GOG part over the ceiling, at twice it' {
        # This assertion used to read the other way, on the reasoning the source
        # carried: GOG splits its installers at 4,294,967,294 bytes to stay under
        # the identical FAT32 limit, one byte under ISO9660's own 32-bit ceiling,
        # so a GOG disc was thought to clear it.
        #
        # IMAPI stops at 2 GiB, half of that, so it does not. A build with the box
        # ticked failed on a 4,294,040,574 byte Alan Wake part after copying
        # 7.79 GB. tools\Measure-IsoFileCeiling.ps1 is where the number comes from.
        $script:state = @{ Games = @(@{ Ok = $true; MaxFileBytes = 4294967294; TotalBytes = 4294967294; Kind = 'Game' }) }
        ((Get-EntriesMaxFileBytes) -le $script:ISO9660_MAX_FILE) | Should -BeFalse
    }

    It 'takes a file of exactly the ceiling and refuses one byte more' {
        $script:state = @{ Games = @(@{ Ok = $true; MaxFileBytes = 2147483648; TotalBytes = 2147483648; Kind = 'Game' }) }
        ((Get-EntriesMaxFileBytes) -le $script:ISO9660_MAX_FILE) | Should -BeTrue

        $script:state = @{ Games = @(@{ Ok = $true; MaxFileBytes = 2147483649; TotalBytes = 2147483649; Kind = 'Game' }) }
        ((Get-EntriesMaxFileBytes) -le $script:ISO9660_MAX_FILE) | Should -BeFalse
    }

    It 'holds the ceiling at the measured number' {
        # Pinned deliberately. This constant is not a property of the ISO9660
        # format, which would allow 4 GiB minus a byte. It is what the image
        # writer accepts, so changing it should be a decision somebody made with
        # the measuring script in front of them.
        $script:ISO9660_MAX_FILE | Should -Be 2147483648
    }
}

Describe 'Get-ItemsMaxFile' -Tag 'Unit' {

    BeforeAll {
        $script:MaxDir = Join-Path $script:Sandbox 'maxfile'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:MaxDir 'sub') | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $script:MaxDir 'small.bin'),        (New-Object byte[] 100))
        [IO.File]::WriteAllBytes((Join-Path $script:MaxDir 'sub\bigger.bin'),   (New-Object byte[] 5000))
        [IO.File]::WriteAllBytes((Join-Path $script:MaxDir 'sub\middling.bin'), (New-Object byte[] 900))
    }

    It 'finds the largest file anywhere under a folder' {
        Get-ItemsMaxFile @($script:MaxDir) | Should -Be 5000
    }

    It 'takes a single file as itself' {
        Get-ItemsMaxFile @((Join-Path $script:MaxDir 'small.bin')) | Should -Be 100
    }

    It 'is the maximum across several paths, not their sum' {
        # The distinction that matters: Get-ItemsSize adds, this one does not.
        Get-ItemsMaxFile @(
            (Join-Path $script:MaxDir 'small.bin')
            (Join-Path $script:MaxDir 'sub')
        ) | Should -Be 5000
    }

    It 'reports nothing as zero rather than failing' {
        Get-ItemsMaxFile @()                | Should -Be 0
        Get-ItemsMaxFile @($null)           | Should -Be 0
        Get-ItemsMaxFile @('X:\no\such\path') | Should -Be 0
    }
}

Describe 'A disc that older Windows can read' -Tag 'Build' -Skip:(-not ($script:CanBuildIso -and $script:SevenZip)) {

    # Everything before Windows Vista reads UDF 2.01 at best, and DiscWright writes
    # UDF 2.50, so those systems cannot mount the disc at all. Adding ISO9660 and
    # Joliet beside the UDF gives them something they can read. These tests assert
    # against the finished image, because whether a filesystem is really in there
    # is not something the staging folder can answer.

    BeforeAll {
        $script:LegGames = @((Get-GameInfo (New-FixtureGame -Slug 'legacy_one' -ExeMb 2)))
        $script:LegOut   = Join-Path $script:Sandbox 'build-legacy'
        New-Item -ItemType Directory -Force -Path $script:LegOut | Out-Null
        $script:LegOff = Invoke-Build (New-BuildSettings -Games $script:LegGames -Label 'Legacy Off' -OutDir $script:LegOut) $script:LogSink

        $script:LegOut2 = Join-Path $script:Sandbox 'build-legacy-on'
        New-Item -ItemType Directory -Force -Path $script:LegOut2 | Out-Null
        $script:LegOn = Invoke-Build (New-BuildSettings -Games $script:LegGames -Label 'Legacy On' -OutDir $script:LegOut2 -LegacyFs) $script:LogSink

        # Read out of the image itself rather than asked of a tool.
        #
        # Two earlier versions of this got it wrong. Deciding on $LASTEXITCODE
        # from 7-Zip passed alone and failed about one run in three in the full
        # suite. Deciding on whether the listing named autorun.inf passed on this
        # machine and failed on the CI runner, because `7z -tiso` on a different
        # 7-Zip version will happily fall back to the UDF tree and list the file
        # anyway. Both were asking a tool to guess at a question the bytes answer.
        #
        # The Volume Recognition Sequence starts at sector 16 (byte 32768) and is
        # a run of 2048-byte descriptors, each carrying a five-byte identifier at
        # offset 1: CD001 for ISO 9660, BEA01 / NSR0x / TEA01 for UDF. A UDF-only
        # image DiscWright builds reads "BEA01 NSR03 TEA01"; add ISO9660 and it
        # reads "CD001 BEA01 NSR03 TEA01". No external tool, and the same answer
        # on every machine.
        function Test-HasIso9660 {
            param([string]$IsoPath)
            $fs = [IO.File]::OpenRead($IsoPath)
            try {
                [void]$fs.Seek(32768, [IO.SeekOrigin]::Begin)
                $buf = New-Object byte[] 2048
                for ($i = 0; $i -lt 16; $i++) {
                    if ($fs.Read($buf, 0, 2048) -lt 7) { break }
                    if ([Text.Encoding]::ASCII.GetString($buf, 1, 5) -eq 'CD001') { return $true }
                }
            } finally { $fs.Dispose() }
            return $false
        }
    }

    It 'is UDF only when the box is not ticked' {
        # The default, and what every disc before this was.
        Test-HasIso9660 -IsoPath $script:LegOff | Should -BeFalse
    }

    It 'reads as ISO9660 when the box is ticked' {
        Test-HasIso9660 -IsoPath $script:LegOn | Should -BeTrue
    }

    It 'is still UDF 2.50 as well, so nothing is given up to gain it' {
        # The point of the hybrid: Windows 11 keeps reading exactly what it read
        # before, because 7-Zip and Windows both prefer the UDF tree.
        $info = & $script:SevenZip l -slt $script:LegOn 2>&1
        ($info | Where-Object { $_ -match '^Type = Udf' })     | Should -Not -BeNullOrEmpty
        ($info | Where-Object { $_ -match '^Version = 2\.50' }) | Should -Not -BeNullOrEmpty
    }

    It 'keeps a long GOG filename intact in the ISO9660 tree' {
        # The reason the other filesystems were left off originally was Joliet's
        # 64-character limit. Measured against a real GOG name, it does not bite:
        # IMAPI writes the long name into the ISO9660 tree regardless.
        $long = 'patch_the_witcher_enhanced_edition_directors_cut_1.5_(A)_(10712)_to_1.5_(CS)_GOG_0.2_(77554).exe'
        $long.Length | Should -BeGreaterThan 64
        $dir = Join-Path $script:Sandbox 'legacy-longname'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $dir $long), (New-Object byte[] 2048))
        $out = Join-Path $script:Sandbox 'build-legacy-long'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $set = New-BuildSettings -Games $script:LegGames -Label 'Long Name' -OutDir $out -LegacyFs
        $set.ExtraItems = @((Join-Path $dir $long))
        $iso = Invoke-Build $set $script:LogSink
        $names = @(& $script:SevenZip l -tiso $iso 2>&1)
        ($names | Where-Object { $_ -match [regex]::Escape($long) }) | Should -Not -BeNullOrEmpty
    }

    It 'falls back to UDF alone when a file is too big for ISO9660' {
        # ISO9660 keeps a file's length in 32 bits. Rather than stage 4 GiB to
        # prove it, the ceiling is lowered for this one build - the code path
        # under test is the real one, only the number it compares against moves.
        $realMax = $script:ISO9660_MAX_FILE
        try {
            $script:ISO9660_MAX_FILE = [double]1024
            $out = Join-Path $script:Sandbox 'build-legacy-toobig'
            New-Item -ItemType Directory -Force -Path $out | Out-Null
            $said = @()
            $iso = Invoke-Build (New-BuildSettings -Games $script:LegGames -Label 'Too Big' -OutDir $out -LegacyFs) { param($m) $script:said += $m }
            Test-HasIso9660 -IsoPath $iso | Should -BeFalse
        } finally { $script:ISO9660_MAX_FILE = $realMax }
    }
}

Describe 'The older-Windows setting in a project file' -Tag 'Unit' {

    BeforeAll {
        $script:LegProj = Join-Path $script:Sandbox 'legacy-proj'
        New-Item -ItemType Directory -Force -Path $script:LegProj | Out-Null
    }

    It 'is written as the current schema and comes back the way it went in' {
        $s = @{ Games=@(); Label='Legacy Trip'; IconPath=$script:Art; IconIsIco=$false
                Menu=$true; BgPath=$null; BgAsIs=$true; PanelSide='Right'
                Divider=$false; ShowTitle=$false; TitleText=''
                WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
                Buttons=@('Play'); ManualPath=$null; ExtrasPath=$null
                ExtraItems=@(); MediaKey=''; LinuxInfo=$false; LegacyFs=$true }
        Save-Project $s $script:LegProj
        $raw = Get-Content -Raw (Join-Path $script:LegProj 'discproject.json') | ConvertFrom-Json
        $raw.Version  | Should -Be 13
        $raw.LegacyFs | Should -BeTrue
        (Import-Project (Join-Path $script:LegProj 'discproject.json')).LegacyFs | Should -BeTrue
    }

    It 'reads back as off from a project saved before it existed' {
        $old = Join-Path $script:LegProj 'older.json'
        @{ Version=7; Label='Older'; Games=@(); Buttons=@('Play'); Menu=$true
           BgAsIs=$true; PanelSide='Right'; ButtonStyle='Minimal'; WindowBorder=$true
           LinuxInfo=$true } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $old -Encoding UTF8
        $p = Import-Project $old
        [bool]$p.LinuxInfo | Should -BeTrue      # still read, so the file is really v7
        [bool]$p.LegacyFs  | Should -BeFalse
    }
}

Describe "The installer's choice of launcher" -Tag 'Unit' {

    # 0.8.0's Start menu shortcut pointed at wscript.exe and DiscWright.vbs. On
    # a machine without VBScript that opens a Windows Script Host error box and
    # nothing else, and vbscript.dll is not on a current Windows 11 image:
    # VBScript became a Feature on Demand in 24H2 and Microsoft has said it will
    # be disabled by default and then removed. Found by installing 0.8.0 in
    # Windows Sandbox, which is where a clean machine can be had.
    #
    # The installer now picks per machine. The half where VBScript is missing is
    # proved end to end by packaging\sandbox\Test-Installer.ps1, on an image that
    # really lacks it. The half where it is present cannot be proved here - this
    # machine's Smart App Control refuses to run the installer at all, and the
    # sandbox could not fetch the Feature on Demand (dism error 12006) - so what
    # is checked here is that the installer still asks the question, and that
    # both answers name a launcher that would work.

    BeforeAll {
        $script:Iss = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'packaging\DiscWright.iss') -Raw
        # An Inno entry is one logical line written over several with trailing
        # backslashes, so the continuations are joined before anything is asked
        # of them. What is collected is every entry that starts the app: the two
        # shortcuts and the tick box at the end of the install, each in both of
        # its versions.
        $flat = $script:Iss -replace '\\\s*\r?\n\s*', ' '
        $script:Launches = @($flat -split '\r?\n' | Where-Object {
            $_ -match '^(Name|Description):' -and $_ -match 'wscript\.exe|powershell\.exe' })
    }

    It 'has a line for every way the app is started' {
        # The Start menu shortcut, the optional desktop icon, and the tick box,
        # times two launchers. Miss one and that is where somebody meets the
        # error box.
        $script:Launches.Count | Should -Be 6
    }

    It 'asks about VBScript before choosing, every time' {
        foreach ($line in $script:Launches) {
            $line | Should -Match 'Check:\s*(not\s+)?HasVBScript' -Because "of: $line"
        }
    }

    It 'pairs each launcher with its opposite, so neither case is left out' {
        $withVbs = @($script:Launches | Where-Object { $_ -match 'Check:\s*HasVBScript' })
        $without = @($script:Launches | Where-Object { $_ -match 'Check:\s*not\s+HasVBScript' })
        $withVbs.Count | Should -Be 3
        $without.Count | Should -Be 3
        # And each pair points at a different launcher, which is the whole point.
        foreach ($line in $withVbs) { $line | Should -Match 'wscript\.exe' }
        foreach ($line in $without) { $line | Should -Match 'powershell\.exe' }
    }

    It 'starts the app the same way on either branch' {
        foreach ($line in @($script:Launches | Where-Object { $_ -match 'not\s+HasVBScript' })) {
            # -STA because the window is WinForms, hidden because the console is
            # what the .vbs existed to avoid, and -File pointing at the app.
            $line | Should -Match '-STA'
            $line | Should -Match '-WindowStyle Hidden'
            $line | Should -Match 'DiscWright\.ps1'
        }
        foreach ($line in @($script:Launches | Where-Object { $_ -match 'Check:\s*HasVBScript' })) {
            $line | Should -Match 'DiscWright\.vbs'
        }
    }

    It 'defines the check it asks with' {
        $script:Iss | Should -Match 'function HasVBScript\(\): Boolean'
        # Looked for in {sys}, which is where the wscript.exe these shortcuts
        # name lives, so the answer is about the same pair of files.
        $script:Iss | Should -Match ([regex]::Escape("FileExists(ExpandConstant('{sys}\vbscript.dll'))"))
    }
}

Describe 'Saying whether a picture is the right shape' -Tag 'Unit' {

    # This used to report how much of a picture would be cut off, back when a
    # picture was fitted into a layout. Nothing is cropped or stretched any
    # more, so the question changed: is the picture the shape of the thing being
    # printed? Close enough and it is printed exactly as it is. Anything else is
    # not cover art, and a plain label is printed with the picture left alone.
    #
    # The change was found by regenerating a real project's artwork and looking
    # at it: a 16:9 menu background was being printed across the whole wrap as
    # though it were a finished cover.

    BeforeAll {
        Add-Type -AssemblyName System.Drawing
        $src = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$null, [ref]$null)
        $fn = $ast.Find({
            param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                      $n.Name -eq 'Get-ArtFitNote'
        }, $true)
        $fn | Should -Not -BeNullOrEmpty
        . ([scriptblock]::Create($fn.Extent.Text))

        $script:ArtDir = Join-Path ([IO.Path]::GetTempPath()) ('dwart_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Force -Path $script:ArtDir | Out-Null

        function New-SizedPicture([int]$w, [int]$h) {
            $path = Join-Path $script:ArtDir "pic-${w}x${h}.png"
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            return $path
        }

        # The real panel and the real disc face, in pixels at 300 dpi.
        $script:PanelW = 1530
        $script:PanelH = 2161
    }

    AfterAll {
        if ($script:ArtDir -and (Test-Path $script:ArtDir)) {
            Remove-Item -LiteralPath $script:ArtDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'says a 16:9 background is not a cover shape, and that a label is printed instead' {
        $note = Get-ArtFitNote (New-SizedPicture 1920 1080) $script:PanelW $script:PanelH 'Cover'
        $note | Should -Match '1920x1080'
        $note | Should -Match 'not that shape'
        $note | Should -Match 'plain label'
    }

    It 'says the same of the menu background size DiscWright itself asks for' {
        $note = Get-ArtFitNote (New-SizedPicture 760 480) $script:PanelW $script:PanelH 'Cover'
        $note | Should -Match 'not that shape'
    }

    It 'says a cover-shaped picture is printed exactly as it is' {
        $note = Get-ArtFitNote (New-SizedPicture 1000 1420) $script:PanelW $script:PanelH 'Cover'
        $note | Should -Match 'right shape'
        $note | Should -Match 'exactly as it is'
        $note | Should -Not -Match 'plain label'
    }

    It 'accepts a square picture for a round disc face' {
        $note = Get-ArtFitNote (New-SizedPicture 1200 1200) 1394 1394 'Disc face'
        $note | Should -Match 'right shape'
    }

    It 'refuses a wide picture for a disc face, which is round' {
        $note = Get-ArtFitNote (New-SizedPicture 1920 1080) 1394 1394 'Disc face'
        $note | Should -Match 'not that shape'
    }

    It 'allows a few per cent either way, because a real cover is rarely exact' {
        # 1530x2161 is the panel itself; 1500x2120 is a couple of per cent out
        # and is plainly the same thing.
        (Get-ArtFitNote (New-SizedPicture 1500 2120) $script:PanelW $script:PanelH 'Cover') |
            Should -Match 'right shape'
    }

    It 'says nothing at all when there is no picture to talk about' {
        Get-ArtFitNote '' $script:PanelW $script:PanelH 'Cover' | Should -Be ''
        Get-ArtFitNote 'Z:\gone.png' $script:PanelW $script:PanelH 'Cover' | Should -Be ''
    }

    It 'says nothing rather than throwing when the file is not a picture' {
        $notPic = Join-Path $script:ArtDir 'notapicture.png'
        Set-Content -LiteralPath $notPic -Value 'this is not a png'
        Get-ArtFitNote $notPic $script:PanelW $script:PanelH 'Cover' | Should -Be ''
    }
}

Describe 'Keeping the printed pictures in the project' -Tag 'Unit' {

    It 'writes both paths, so reopening a disc does not ask again' {
        $out = Join-Path $script:Sandbox 'cover-project'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        Save-Project @{
            Games = @(); Label = 'ART'; IconPath = $script:Art; IconIsIco = $false
            Menu = $true; BgPath = $script:Bg; BgAsIs = $false; PanelSide = 'Right'
            Divider = $false; ShowTitle = $false; TitleText = ''
            WindowBorder = $true; ButtonStyle = 'Minimal'; MusicFile = $null
            Buttons = @('Play', 'Exit'); ManualPath = $null; ExtrasPath = $null
            ExtraItems = @(); MediaKey = ''; OutDir = $out
            CoverPath = 'C:\art\cover.png'; DiscArtPath = 'C:\art\face.png'
        } $out
        $raw = Get-Content (Join-Path $out 'discproject.json') -Raw | ConvertFrom-Json
        $raw.Version     | Should -Be 13
        $raw.CoverPath   | Should -Be 'C:\art\cover.png'
        $raw.DiscArtPath | Should -Be 'C:\art\face.png'
    }

    It 'reads an older project back without them, rather than refusing it' {
        # Version 9 and earlier had no such fields, and those discs were built
        # from the background alone. They have to keep opening.
        $out = Join-Path $script:Sandbox 'old-project'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $old = [ordered]@{
            Version = 9; AppVersion = '0.8.1'; Label = 'OLD'; TitleText = 'Old disc'
            Games = @(); BgPath = $script:Bg; ShowTitle = $false; OutDir = $out
        }
        $old | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'discproject.json') -Encoding UTF8
        $read = Import-Project (Join-Path $out 'discproject.json')
        $read | Should -Not -BeNullOrEmpty
        $read.CoverPath | Should -BeNullOrEmpty
        $read.DiscArtPath | Should -BeNullOrEmpty
    }
}

Describe 'What the installer promises to ship' -Tag 'Unit' {

    BeforeAll {
        $script:Root = Split-Path $PSScriptRoot -Parent
        $script:IssText = Get-Content -Raw -LiteralPath (Join-Path $script:Root 'packaging\DiscWright.iss')
        # Source: "..\path"; ... one per [Files] line.
        $script:Sources = @([regex]::Matches($script:IssText, 'Source:\s*"([^"]+)"') |
                            ForEach-Object { $_.Groups[1].Value })
    }

    It 'lists some files at all' {
        $script:Sources.Count | Should -BeGreaterThan 4
    }

    It 'points every one of them at a file that exists' {
        # A path typed wrong here is not found until release day, when the
        # installer either fails to compile or quietly ships without it. The
        # second is worse: it is how a button becomes a dead end on somebody
        # else's machine.
        $missing = @()
        foreach ($src in $script:Sources) {
            # Paths are relative to packaging\, which is where the .iss lives.
            $full = Join-Path (Join-Path $script:Root 'packaging') $src
            if (-not (Test-Path -LiteralPath $full)) { $missing += $src }
        }
        $missing.Count | Should -Be 0 -Because "these are named but not in the repo: $($missing -join ', ')"
    }

    It 'ships the two modules the buttons depend on' {
        # Print artwork and Burn to disc dot-source these at the moment they are
        # pressed. Left out of the installer, both buttons are dead on an
        # installed copy while working perfectly from a checkout.
        $script:IssText | Should -Match ([regex]::Escape('..\print\DiscWright.Print.ps1'))
        $script:IssText | Should -Match ([regex]::Escape('..\burn\DiscWright.Burn.ps1'))
    }

    It 'puts them where the app looks for them' {
        # The app builds the path from $PSScriptRoot, so the folder names in the
        # install have to match the folder names in the repo.
        $script:IssText | Should -Match 'DestDir:\s*"\{app\}\\print"'
        $script:IssText | Should -Match 'DestDir:\s*"\{app\}\\burn"'
    }

    It 'matches the paths the app actually dot-sources' {
        $app = Get-Content -Raw -LiteralPath (Join-Path $script:Root 'DiscWright.ps1')
        $app | Should -Match ([regex]::Escape("Join-Path `$PSScriptRoot 'print\DiscWright.Print.ps1'"))
        $app | Should -Match ([regex]::Escape("Join-Path `$PSScriptRoot 'burn\DiscWright.Burn.ps1'"))
    }
}

Describe 'Executable files stay pure ASCII' -Tag 'Unit' {

    # CI has checked this since before any of these features existed, and the
    # local suite did not, so a full green run here could still fail there.
    # That is exactly what happened: writing a few files with a UTF-8 BOM
    # passed 635 tests locally and failed CI on the first push.
    #
    # A BOM is three bytes above 127 at offset 0. PowerShell 5.1 reads a BOM-less
    # file as ANSI, so the rule is not decoration either: a stray accented
    # character in a script changes meaning depending on the machine's codepage.

    BeforeAll {
        $script:RepoRoot = Split-Path $PSScriptRoot -Parent
        # Filtered by extension rather than with -Include, which is ignored
        # beside -LiteralPath and silently widened this to every file in the
        # repository: pictures, markdown and all.
        $script:Scripts = @(Get-ChildItem -LiteralPath $script:RepoRoot -Recurse -File |
                            Where-Object { $_.Extension -in '.ps1', '.vbs', '.cmd' -and
                                           $_.FullName -notmatch '\\\.git\\' })
    }

    It 'finds the scripts to check in the first place' {
        $script:Scripts.Count | Should -BeGreaterThan 10
    }

    It 'has no byte above 127 in any of them' {
        $bad = @()
        foreach ($f in $script:Scripts) {
            $bytes = [IO.File]::ReadAllBytes($f.FullName)
            for ($i = 0; $i -lt $bytes.Length; $i++) {
                if ($bytes[$i] -gt 127) {
                    $rel = $f.FullName.Substring($script:RepoRoot.Length).TrimStart('\')
                    $what = if ($i -eq 0) { 'a UTF-8 BOM' } else { "byte $($bytes[$i])" }
                    $bad += "$rel ($what at offset $i)"
                    break
                }
            }
        }
        $bad.Count | Should -Be 0 -Because "CI fails on these: $($bad -join '; ')"
    }

    It 'leaves the files that are meant to carry a BOM alone' {
        # The fixture exists to prove DiscWright reads a project file written by
        # 0.4.2, which carried one. Stripping it would delete the test's point,
        # and a blanket de-BOM script did exactly that once.
        $fixture = Join-Path $script:RepoRoot 'tests\fixtures\discproject-0.4.2.json'
        ([IO.File]::ReadAllBytes($fixture)[0..2] -join ',') | Should -Be '239,187,191'
    }
}

Describe 'A disc of game files, not installers' -Tag 'Unit' {

    # Found by burning one and looking at the screen: PLAY was greyed out with
    # "use Install first" while INSTALL was the enabled button, on a disc where
    # nothing can be installed because the executable is the game. Every menu
    # test had been written around GOG discs, where that behaviour is right.

    BeforeAll {
        function New-FilesEntry([string]$name, [string]$exe) {
            return @{
                GameName = $name; MatchName = $name; Kind = 'Game'; Source = 'Files'
                SetupExe = [pscustomobject]@{ FullName = $exe }
                Folder = (Split-Path $exe -Parent); ParentIndex = -1; Ok = $true
            }
        }
        function New-GogEntry([string]$name, [string]$exe) {
            $e = New-FilesEntry $name $exe
            $e.Source = 'GOG'
            return $e
        }
    }

    It 'carries the source through to the menu, which cannot work it out alone' {
        $m = Get-MenuGames @( (New-FilesEntry 'Gothic' 'C:\g\gothic.exe') )
        $m[0].Source | Should -Be 'Files'
        $g = Get-MenuGames @( (New-GogEntry 'Gothic' 'C:\g\setup_gothic.exe') )
        $g[0].Source | Should -Be 'GOG'
    }

    It 'treats anything that does not say Files as a GOG installer' {
        # Projects written before Source existed have no such field, and those
        # discs were all GOG downloads.
        $old = @{ GameName = 'Old'; MatchName = 'Old'; Kind = 'Game'; Ok = $true
                  SetupExe = [pscustomobject]@{ FullName = 'C:\g\setup.exe' }
                  Folder = 'C:\g'; ParentIndex = -1 }
        (Get-MenuGames @($old))[0].Source | Should -Be 'GOG'
    }
}

Describe 'What the menu does with a folder of game files' -Tag 'Unit' {

    BeforeAll {
        $script:MenuSrc = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1')
    }

    It 'writes a files flag into the menu for every game' {
        # Asserted in pieces rather than as one long escaped line, which is
        # unreadable and matched the wrong quote the first time it was written.
        $script:MenuSrc | Should -Match ([regex]::Escape('",files:'))
        $script:MenuSrc | Should -Match ([regex]::Escape("if (`$_.Source -eq 'Files') { '1' } else { '0' }"))
    }

    It 'says Play from disc rather than Play, so nobody has to infer it' {
        # The wording is the whole fix for somebody reading the screen: it
        # explains both what the button does and why Install is not there.
        $script:MenuSrc | Should -Match 'Play from disc'
        $script:MenuSrc | Should -Match 'Run "\+g\.n\+" straight from this disc\. Nothing is installed\.'
    }

    It 'offers no Install button at all on a files entry' {
        $script:MenuSrc | Should -Match 'if\(has\("Install"\) && !g\.files\)\{'
    }

    It 'never greys Play out waiting for an install that cannot happen' {
        $script:MenuSrc | Should -Match 'setEnabled\("btn_Play", \(g\.files \? true : parentOn\)'
    }

    It 'runs the executable off the disc rather than hunting the registry' {
        # findGame looks an installed game up in the registry. A game played
        # from the disc was never installed, so there is nothing to find.
        $script:MenuSrc | Should -Match 'if\(g\.files\)\{'
        $script:MenuSrc | Should -Match 'var exe=fso\.BuildPath\(root,g\.s\);'
    }

    It 'says so when the file is not where the menu expects, rather than failing quietly' {
        $script:MenuSrc | Should -Match 'is not on this disc where the menu expected it'
    }

    It 'still tells a GOG game to install first, because that is true there' {
        $script:MenuSrc | Should -Match "isn't installed yet"
    }
}

Describe 'What the zip ships' -Tag 'Unit' {

    # The installer's file list was tested and the zip's was not, so the zip
    # went out holding half an app: Print artwork and Burn to disc both
    # dot-source a module at the moment they are pressed, and neither module
    # was in it. Found by unpacking the built artifact during a release, which
    # is late. The same mistake in the installer is what 0.8.1 exists to fix.

    BeforeAll {
        $script:Root = Split-Path $PSScriptRoot -Parent
        $script:BuildSrc = Get-Content -Raw -LiteralPath (Join-Path $script:Root 'packaging\Build-Release.ps1')
        $block = [regex]::Match($script:BuildSrc, '(?s)\$payload = @\((.*?)
\)').Groups[1].Value
        # Comment lines dropped first. The comment inside that array mentions
        # the buttons by name, and an apostrophe in prose reads as a quoted
        # entry otherwise, which made this list nonsense.
        $lines = @($block -split '?
' | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') })
        $script:Payload = @($lines | ForEach-Object {
            $m = [regex]::Match($_, "'([^']+)'")
            if ($m.Success) { $m.Groups[1].Value }
        })
    }

    It 'lists files that are actually in the repository' {
        $missing = @($script:Payload | Where-Object { -not (Test-Path -LiteralPath (Join-Path $script:Root $_)) })
        $missing.Count | Should -Be 0 -Because "named but not here: $($missing -join ', ')"
    }

    It 'ships every module the app dot-sources at run time' {
        # Taken from the app rather than listed again here, so a third module
        # added later is covered by this test without anybody remembering to.
        $app = Get-Content -Raw -LiteralPath (Join-Path $script:Root 'DiscWright.ps1')
        $needed = @([regex]::Matches($app, "Join-Path \`$PSScriptRoot '([^']+\.ps1)'") |
                    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $needed.Count | Should -BeGreaterThan 0 -Because 'the app dot-sources something'
        foreach ($n in $needed) {
            $script:Payload | Should -Contain $n -Because "the zip is the main download and $n is loaded at run time"
        }
    }

    It 'ships the same modules the installer does' {
        $iss = Get-Content -Raw -LiteralPath (Join-Path $script:Root 'packaging\DiscWright.iss')
        foreach ($mod in 'print\DiscWright.Print.ps1', 'burn\DiscWright.Burn.ps1') {
            $script:Payload | Should -Contain $mod
            # The .iss writes its sources relative to packaging\, so the path
            # there carries a leading ..\ that the payload list does not.
            $iss | Should -Match ([regex]::Escape('..\' + $mod))
        }
    }

    It 'makes the folder for a payload entry that lives in one' {
        # Copy-Item will not create a directory on the way, so a nested entry
        # silently failed to copy before this was added.
        $script:BuildSrc | Should -Match 'New-Item -ItemType Directory -Path \$dstDir'
    }
}

Describe 'The artwork the app draws when nothing was chosen' -Tag 'Unit' {

    # Reported from the outside as two complaints: the app halts asking you to
    # browse for an icon and a background, and there is no way to preview the
    # menu. They were the same wall. The Preview button stayed disabled until a
    # background had been chosen, so the people who had not chosen one could
    # never see the feature that would have shown them why it mattered.
    #
    # The artwork is drawn rather than shipped. A picture in the zip is a
    # picture to license, and one built from the menu's own palette looks
    # deliberate rather than like a placeholder.

    BeforeAll {
        $script:ArtDir = Join-Path $script:Sandbox 'default-art'
        New-Item -ItemType Directory -Force -Path $script:ArtDir | Out-Null
    }

    It 'draws a disc icon, square and large enough for the converter' {
        $p = Get-DefaultArt -Kind Icon
        Test-Path $p | Should -BeTrue
        $img = [System.Drawing.Image]::FromFile($p)
        try {
            $img.Width | Should -Be 512
            $img.Height | Should -Be $img.Width
        } finally { $img.Dispose() }
    }

    It 'leaves the hub of that icon actually transparent' {
        # Not cosmetic. A hub filled with the background colour looks like a
        # hole on a dark page and like a blob on a light one, and Explorer
        # draws icons on both.
        $bmp = New-Object System.Drawing.Bitmap((Get-DefaultArt -Kind Icon))
        try {
            $bmp.GetPixel(($bmp.Width/2), ($bmp.Height/2)).A | Should -Be 0
            $bmp.GetPixel(5, 5).A | Should -Be 0                       # outside the disc
            $bmp.GetPixel(($bmp.Width/2), 110).A | Should -Be 255       # the disc face
        } finally { $bmp.Dispose() }
    }

    It 'passes the icon through the real converter' {
        $ico = Join-Path $script:ArtDir 'default.ico'
        Convert-ToIco (Get-DefaultArt -Kind Icon) $ico
        Test-Path $ico | Should -BeTrue
        $i = New-Object System.Drawing.Icon($ico)
        try { $i.Width | Should -BeGreaterThan 0 } finally { $i.Dispose() }
    }

    It 'draws a background that composes like any chosen picture' {
        # It is a source image, not a finished background, so the panel, the
        # divider and the title keep behaving exactly as they always have.
        $out = Join-Path $script:ArtDir 'composed.png'
        New-Background (Get-DefaultArt -Kind Background) 'DEFAULT' $out 'Right' $false $true
        $img = [System.Drawing.Image]::FromFile($out)
        try { $img.Width | Should -Be 760; $img.Height | Should -Be 480 } finally { $img.Dispose() }
    }

    It 'keeps the background symmetrical, because the panel can sit on either side' {
        # Anything off-centre would be half covered on one of the two settings.
        $bmp = New-Object System.Drawing.Bitmap((Get-DefaultArt -Kind Background))
        try {
            $y = [int]($bmp.Height/2)
            # The gradient runs corner to corner, so the two sides are not
            # identical. The rings are what must be centred, so compare points
            # an equal distance either side of the middle.
            $mid = [int]($bmp.Width/2)
            $bmp.GetPixel(($mid-300), $y).A | Should -Be $bmp.GetPixel(($mid+300), $y).A
        } finally { $bmp.Dispose() }
    }

    It 'redraws a file that was left empty' {
        # An interrupted first run left a zero byte PNG behind, and a zero byte
        # PNG fails later and further away, where it looks like a broken build.
        $p = Get-DefaultArt -Kind Icon
        Set-Content -LiteralPath $p -Value '' -NoNewline
        (Get-Item $p).Length | Should -Be 0
        $again = Get-DefaultArt -Kind Icon
        (Get-Item $again).Length | Should -BeGreaterThan 0
    }

    It 'reuses what it already drew' {
        $first = Get-DefaultArt -Kind Background
        $stamp = (Get-Item $first).LastWriteTimeUtc
        Start-Sleep -Milliseconds 20
        $second = Get-DefaultArt -Kind Background
        $second | Should -Be $first
        (Get-Item $second).LastWriteTimeUtc | Should -Be $stamp
    }
}

Describe 'Building a disc when nobody chose an icon or a background' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    # The complaint this answers, in the reporter's words: the application halts
    # and asks you to browse for the files. It did, twice, and the second refusal
    # also kept the Preview button greyed, which is how the preview came to look
    # like a missing feature rather than a blocked one.
    #
    # Checked by building a real disc with both left empty, because every other
    # test here hands the build an icon and a background and so could never have
    # caught this.

    BeforeAll {
        $script:BareGame = Get-GameInfo (New-FixtureGame -Slug 'bare_disc' -ExeMb 2)
        $script:BareOut  = Join-Path $script:Sandbox 'build-bare'
        New-Item -ItemType Directory -Force -Path $script:BareOut | Out-Null

        $s = New-BuildSettings -Games @($script:BareGame) -Label 'Bare Disc' -OutDir $script:BareOut
        $s.IconPath = $null
        $s.IconIsIco = $false
        $s.BgPath = $null

        $script:BareSaid = @()
        $script:BareIso   = Invoke-Build $s { param($m) $script:BareSaid += [string]$m }
        $script:BareStage = Join-Path $script:BareOut 'disc'
        $script:BareSettings = $s
    }

    It 'builds the ISO instead of refusing' {
        Test-Path $script:BareIso | Should -BeTrue
    }

    It 'puts a disc icon on the disc anyway' {
        Test-Path (Join-Path $script:BareStage (Get-DiscIconName 'Bare Disc')) | Should -BeTrue
    }

    It 'writes the menu background it was never given' {
        Test-Path (Join-Path $script:BareStage 'AUTORUN\bg.png') | Should -BeTrue
    }

    It 'still writes the menu itself' {
        Test-Path (Join-Path $script:BareStage 'AUTORUN\menu.hta') | Should -BeTrue
    }

    It 'says in the log that it used its own artwork' {
        # Silence here would be worse than the refusal was. Somebody who never
        # chose a disc face should be able to find out where this one came from.
        ($script:BareSaid -join "`n") | Should -Match 'built-in'
    }

    It 'leaves the settings pointing at real files, not at nothing' {
        # Invoke-Build fills these in. A project saved after a build must carry
        # a usable path rather than the null it started with.
        Test-Path $script:BareSettings.IconPath | Should -BeTrue
        Test-Path $script:BareSettings.BgPath   | Should -BeTrue
    }
}

Describe 'The hint inside the empty artwork boxes' -Tag 'Unit' {

    # The rendering cannot be asserted from outside the process. UI Automation
    # does not expose a cue banner at all (measured: HelpText comes back
    # empty), and EM_GETCUEBANNER writes into a buffer in the caller's address
    # space, so reading one across processes returns nothing. Both were tried.
    # What the window actually shows was checked by eye, in the window suite's
    # own screenshot.
    #
    # So this asserts the wiring rather than the pixels: that the app still
    # tells both boxes what to say, and still says the useful thing. That is
    # enough to catch the realistic regression, which is somebody deleting the
    # call or the import while tidying.

    BeforeAll {
        $script:AppText = Get-Content $appScript -Raw
    }

    It 'imports the message the hint is set with' {
        $script:AppText | Should -Match 'SendMessageW'
    }

    It 'sets a hint on the disc icon box and on the background box' {
        $script:AppText | Should -Match 'Set-CueText \$txtIcon'
        $script:AppText | Should -Match 'Set-CueText \$txtBg'
    }

    It 'tells people the box can be left empty, in both of them' {
        # The wording is what the person reads, so an empty or vague hint is
        # the same bug as no hint at all.
        @([regex]::Matches($script:AppText, "Set-CueText \`$txt\w+\s+'([^']+)'")) |
            ForEach-Object { $_.Groups[1].Value } |
            ForEach-Object { $_ | Should -Match 'built-in' }
    }

    It 'sets them only once the form is on screen' {
        # A box has no window handle before that, and the message goes nowhere.
        $shown = [regex]::Match($script:AppText, '\$form\.Add_Shown\(\{(?s).*?\}\)').Value
        $shown | Should -Match 'Set-CueText \$txtIcon'
        $shown | Should -Match 'Set-CueText \$txtBg'
    }
}

Describe 'The thing to double-click when AutoPlay does not offer itself' -Tag 'Unit' {

    # AutoPlay is switched off on a great many machines, and on those the disc
    # looks like a folder of installers with no obvious way in: the menu sits in
    # AUTORUN, which nobody browsing a disc would think to open. Reported from
    # the outside by somebody holding the disc.
    #
    # The launcher is generated, and generated code full of backslashes is how
    # three separate bugs got written in one sitting here: a path separator
    # eaten into the filename, a regular expression that matched a plus sign,
    # and a newline escape that became a real newline and split a string in
    # half. So the rule is that the generated script contains no backslash at
    # all, and this is what keeps it that way.

    BeforeAll {
        $script:LaunchDir = Join-Path $script:Sandbox 'launcher'
        New-Item -ItemType Directory -Force -Path $script:LaunchDir | Out-Null
        $script:LaunchFile = Join-Path $script:LaunchDir 'Start Here.hta'
        New-MenuLauncher $script:LaunchFile
        $script:LaunchText = Get-Content $script:LaunchFile -Raw
        $script:LaunchJs = [regex]::Match($script:LaunchText,
            '(?s)<script language="JScript">(.*?)</script>').Groups[1].Value
    }

    It 'writes a file at all, in pure ASCII like every other disc file' {
        Test-Path $script:LaunchFile | Should -BeTrue
        @([IO.File]::ReadAllBytes($script:LaunchFile) | Where-Object { $_ -gt 127 }).Count | Should -Be 0
    }

    It 'contains no backslash, which is the whole defence' {
        # Every backslash bug here came from one surviving into the output.
        $script:LaunchJs.Contains([char]92) | Should -BeFalse
    }

    It 'builds the separator and the path from parts instead' {
        $script:LaunchJs | Should -Match 'String\.fromCharCode\(92\)'
        $script:LaunchJs | Should -Match 'BuildPath'
    }

    It 'runs the menu rather than carrying a second copy of it' {
        # Two menus would be two to keep in step.
        $script:LaunchJs | Should -Match 'mshta'
        $script:LaunchJs | Should -Match 'menu\.hta'
    }

    It 'works out the disc from its own location, for every spelling of the URL' {
        # Run in a real JScript engine, because this is the part that cannot be
        # proved by reading: a burned disc, both URL forms an HTA reports, a
        # staged folder on a hard disk, and a share.
        $probe = Join-Path $script:LaunchDir 'probe.js'
        $here = [regex]::Match($script:LaunchJs, '(?s)^(.*?)(?=\r?\ntry\{)').Groups[1].Value
        $cases = @(
            @{ Url = 'file://D:/Start Here.hta';                       Menu = 'D:\AUTORUN\menu.hta' }
            @{ Url = 'file:///D:/Start Here.hta';                      Menu = 'D:\AUTORUN\menu.hta' }
            @{ Url = 'file://C:/out/disc/Start Here.hta';              Menu = 'C:\out\disc\AUTORUN\menu.hta' }
            @{ Url = 'file://///server/share/d/Start Here.hta';        Menu = ([char]92 + [char]92 + 'server\share\d\AUTORUN\menu.hta') }
        )
        foreach ($c in $cases) {
            $js = @"
var document = { URL: "$($c.Url)" };
$here
var fso = new ActiveXObject("Scripting.FileSystemObject");
var dir = fso.GetParentFolderName(here());
WScript.Echo(fso.BuildPath(fso.BuildPath(dir, "AUTORUN"), "menu.hta"));
"@
            Set-Content -LiteralPath $probe -Value $js -Encoding Ascii
            $got = (& cscript.exe //nologo //E:JScript $probe 2>&1 | Select-Object -First 1).ToString().Trim()
            $got | Should -Be $c.Menu -Because "a launcher at $($c.Url) has to find the menu beside it"
        }
    }

    It 'is a name the disc owns, so extra content cannot overwrite it' {
        # Assert the name first. An earlier version of this passed while the
        # name was empty, because the list it checks was built from the same empty
        # value and empty matched empty.
        Get-MenuLauncherName | Should -Be 'Start Here.hta'
        Test-ReservedDiscName (Get-MenuLauncherName) | Should -BeTrue
    }
}

Describe 'The launcher on a disc that was really built' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    # The tests above prove the launcher is generated correctly. None of them
    # prove the build writes it, which is the part a regression would silently
    # remove: take the call out of Invoke-Build and every one of them still
    # passes while no disc ever carries the file again.

    BeforeAll {
        $script:LnGame = Get-GameInfo (New-FixtureGame -Slug 'launcher_disc' -ExeMb 2)
        $script:LnOut  = Join-Path $script:Sandbox 'build-launcher'
        New-Item -ItemType Directory -Force -Path $script:LnOut | Out-Null
        $s = New-BuildSettings -Games @($script:LnGame) -Label 'Launcher Disc' -OutDir $script:LnOut
        $script:LnSaid = @()
        $script:LnIso = Invoke-Build $s { param($m) $script:LnSaid += [string]$m }
        $script:LnStage = Join-Path $script:LnOut 'disc'
    }

    It 'leaves the launcher at the disc root, where it can be seen' {
        Test-Path (Join-Path $script:LnStage (Get-MenuLauncherName)) | Should -BeTrue
    }

    It 'leaves the menu where autorun.inf still points' {
        # The launcher is an addition, not a move: AutoPlay must behave as before.
        Test-Path (Join-Path $script:LnStage 'AUTORUN\menu.hta') | Should -BeTrue
        (Get-Content (Join-Path $script:LnStage 'autorun.inf') -Raw) |
            Should -Match 'shellexecute=AUTORUN'
    }

    It 'says so in the log, so the build is accountable for it' {
        ($script:LnSaid -join "`n") | Should -Match 'launcher'
    }
}

Describe 'The menu working out which folder is the disc' -Tag 'Unit' {

    # It used to go two levels up and assume it sat in AUTORUN. A copy anywhere
    # else resolved one level too high, and that fails quietly: Install, Manual
    # and Extras grey out as though the disc were empty, and Open Folder reports
    # a folder that is not there. Both placements are checked here because the
    # launcher makes the second one reachable.

    BeforeAll {
        $script:RootDir = Join-Path $script:Sandbox 'discroot'
        New-Item -ItemType Directory -Force -Path $script:RootDir | Out-Null
        $menu = Join-Path $script:RootDir 'menu.hta'
        New-MenuHta @{ GameName='Root Test'; Games=@(); Buttons=@('Exit'); MusicFile=''
                       ManualFile=''; PanelSide='Right'; IconName='x.ico'
                       WindowBorder=$true; ButtonStyle='Minimal' } $menu
        $script:MenuJs = Get-Content $menu -Raw
    }

    It 'asks where it is instead of counting levels' {
        $script:MenuJs | Should -Match 'function discRoot'
        # The old form, which is what this replaced.
        $script:MenuJs | Should -Not -Match 'GetParentFolderName\(fso\.GetParentFolderName\(htaPath\(\)\)\)'
    }

    It 'answers the disc root from AUTORUN, and from the root itself' {
        # Run in a real JScript engine: this is the part reading cannot settle.
        $fn = [regex]::Match($script:MenuJs,
            '(?s)(function discRoot\(hta\)\{.*?\r?\n\s*\})').Groups[1].Value
        $fn | Should -Not -BeNullOrEmpty
        $cases = @(
            @{ Hta = 'D:\AUTORUN\menu.hta';               Root = 'D:\' }
            @{ Hta = 'D:\autorun\menu.hta';               Root = 'D:\' }
            @{ Hta = 'D:\menu.hta';                       Root = 'D:\' }
            @{ Hta = 'C:\out\disc\AUTORUN\menu.hta';      Root = 'C:\out\disc' }
            @{ Hta = 'C:\out\disc\menu.hta';              Root = 'C:\out\disc' }
        )
        $probe = Join-Path $script:RootDir 'probe.js'
        foreach ($c in $cases) {
            $js = @"
var fso = new ActiveXObject("Scripting.FileSystemObject");
$fn
WScript.Echo(discRoot("$($c.Hta.Replace([string][char]92, [string][char]92 + [string][char]92))"));
"@
            Set-Content -LiteralPath $probe -Value $js -Encoding Ascii
            $got = (& cscript.exe //nologo //E:JScript $probe 2>&1 | Select-Object -First 1).ToString().Trim()
            $got | Should -Be $c.Root -Because "a menu at $($c.Hta) sees the disc at $($c.Root)"
        }
    }
}

Describe 'A list of what every file on the disc should hash to' -Tag 'Unit' {

    # Asked for as being able to restore a disc back to its original bin and exe
    # structure, matching the original hash values. The structure already comes
    # back byte for byte; what was missing was any way to prove it.
    #
    # sha256sum format so that nothing from DiscWright is needed to check it,
    # which is the whole point of a file meant to be read in twenty years.

    BeforeAll {
        $script:SumDir = Join-Path $script:Sandbox 'sums'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:SumDir 'AUTORUN') | Out-Null
        Set-Content (Join-Path $script:SumDir 'setup_game_(64bit)_(1).exe') 'installer' -Encoding Ascii
        Set-Content (Join-Path $script:SumDir 'AUTORUN\menu.hta') 'menu' -Encoding Ascii
        $script:SumFile = New-ChecksumManifest $script:SumDir 'HASH TEST' $null
        $script:SumText = [IO.File]::ReadAllText($script:SumFile)
        $script:SumLines = @(($script:SumText -split "`n") | Where-Object { $_ -and $_ -notmatch '^#' })
    }

    It 'lists every file, and does not try to list itself' {
        $script:SumLines.Count | Should -Be 2
        # The header names the file, in the line telling you how to check it, so
        # the claim is about the hashed lines rather than the whole text.
        @($script:SumLines | Where-Object { $_ -match 'checksums\.sha256' }).Count | Should -Be 0
    }

    It 'writes the hash the rest of the world computes' {
        foreach ($line in $script:SumLines) {
            $h, $rel = $line -split ' \*', 2
            $full = Join-Path $script:SumDir ($rel -replace '/', [string][char]92)
            # Checked against the shipped cmdlet, not against the same code path
            # that wrote it, which would agree with itself whatever it did.
            $h | Should -Be (Get-FileHash $full -Algorithm SHA256).Hash.ToLower()
        }
    }

    It 'is laid out the way sha256sum writes it' {
        foreach ($line in $script:SumLines) {
            $line | Should -Match '^[0-9a-f]{64} \*'
        }
    }

    It 'separates folders with a forward slash, which both sides accept' {
        $script:SumText | Should -Match 'AUTORUN/menu\.hta'
    }

    It 'ends its lines with a newline alone' {
        # Not a detail. sha256sum -c reads a carriage return as part of the
        # filename and then reports every single line as a missing file, which
        # is exactly what the first version of this did.
        $script:SumText | Should -Not -Match "`r"
    }

    It 'says in its header how to check it without DiscWright' {
        $script:SumText | Should -Match 'sha256sum -c'
        $script:SumText | Should -Match 'Get-FileHash'
    }

    It 'is a name the disc owns, so extra content cannot overwrite it' {
        Get-ChecksumFileName | Should -Be 'checksums.sha256'
        Test-ReservedDiscName (Get-ChecksumFileName) | Should -BeTrue
    }

    It 'notices a file that changed by a single byte' {
        $copy = Join-Path $script:Sandbox 'sums-tamper'
        Copy-Item $script:SumDir $copy -Recurse -Force
        Add-Content (Join-Path $copy 'AUTORUN\menu.hta') 'x'
        $line = @(($script:SumText -split "`n") | Where-Object { $_ -match 'menu\.hta' })[0]
        $h, $rel = $line -split ' \*', 2
        $now = Get-FileSha256 (Join-Path $copy ($rel -replace '/', [string][char]92))
        $now | Should -Not -Be $h
    }
}

Describe 'The checksum list on a disc that was really built' -Tag 'Build' -Skip:(-not $script:CanBuildIso) {

    BeforeAll {
        $script:CsGame = Get-GameInfo (New-FixtureGame -Slug 'checksum_disc' -ExeMb 2)
        $script:CsOn  = Join-Path $script:Sandbox 'build-sums-on'
        $script:CsOff = Join-Path $script:Sandbox 'build-sums-off'
        New-Item -ItemType Directory -Force -Path $script:CsOn, $script:CsOff | Out-Null
        $script:CsSaid = @()
        $null = Invoke-Build (New-BuildSettings -Games @($script:CsGame) -Label 'Sums On' -OutDir $script:CsOn -Checksums) { param($m) $script:CsSaid += [string]$m }
        $null = Invoke-Build (New-BuildSettings -Games @($script:CsGame) -Label 'Sums Off' -OutDir $script:CsOff) $script:LogSink
        $script:CsOnStage  = Join-Path $script:CsOn 'disc'
        $script:CsOffStage = Join-Path $script:CsOff 'disc'
    }

    It 'is on the disc when it was asked for' {
        Test-Path (Join-Path $script:CsOnStage (Get-ChecksumFileName)) | Should -BeTrue
    }

    It 'is not on the disc when it was not' {
        # Off by default, like the other two things a disc can also be: what
        # every disc carries is not a decision to make on somebody's behalf.
        Test-Path (Join-Path $script:CsOffStage (Get-ChecksumFileName)) | Should -BeFalse
    }

    It 'covers every file the disc ends up carrying' {
        $onDisc = @(Get-ChildItem $script:CsOnStage -Recurse -File |
                    Where-Object { $_.Name -ne (Get-ChecksumFileName) })
        $listed = @((Get-Content (Join-Path $script:CsOnStage (Get-ChecksumFileName))) |
                    Where-Object { $_ -and $_ -notmatch '^#' })
        $listed.Count | Should -Be $onDisc.Count
    }

    It 'matches the files that are actually sitting there' {
        $root = $script:CsOnStage
        foreach ($line in @((Get-Content (Join-Path $root (Get-ChecksumFileName))) | Where-Object { $_ -and $_ -notmatch '^#' })) {
            $h, $rel = $line -split ' \*', 2
            $full = Join-Path $root ($rel -replace '/', [string][char]92)
            Test-Path -LiteralPath $full | Should -BeTrue -Because "$rel is listed"
            (Get-FileSha256 $full) | Should -Be $h
        }
    }

    It 'says what it did in the log' {
        ($script:CsSaid -join "`n") | Should -Match 'Checksum list written'
    }
}

Describe 'Remembering whether the disc was asked to be checksummed' -Tag 'Unit' {

    BeforeAll {
        $script:CsProj = Join-Path $script:Sandbox 'proj-sums'
        New-Item -ItemType Directory -Force -Path $script:CsProj | Out-Null
    }

    It 'saves the answer and reads it back' {
        $s = @{ Games=@(); Label='Sums'; IconPath=''; IconIsIco=$false; Menu=$true
                BgPath=''; BgAsIs=$false; PanelSide='Right'
                Divider=$false; ShowTitle=$false; TitleText=''
                WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
                Buttons=@('Play'); ManualPath=$null; ExtrasPath=$null
                ExtraItems=@(); MediaKey=''; LinuxInfo=$false; LegacyFs=$false
                Checksums=$true }
        Save-Project $s $script:CsProj
        $raw = Get-Content -Raw (Join-Path $script:CsProj 'discproject.json') | ConvertFrom-Json
        $raw.Version   | Should -Be 13
        $raw.Checksums | Should -BeTrue
        (Import-Project (Join-Path $script:CsProj 'discproject.json')).Checksums | Should -BeTrue
    }

    It 'reads back as off from a project saved before it existed' {
        # Reopening an old project and rebuilding has to produce the disc it
        # produced before, not one with a file quietly added to it.
        $old = Join-Path $script:CsProj 'older.json'
        @{ Version=10; Label='Older'; Games=@(); Buttons=@('Play'); Menu=$true
           BgAsIs=$true; PanelSide='Right'; ButtonStyle='Minimal'; WindowBorder=$true
        } | ConvertTo-Json -Depth 4 | Set-Content $old -Encoding UTF8
        [bool](Import-Project $old).Checksums | Should -BeFalse
    }
}

Describe 'A long game name on the menu' -Tag 'Unit' {

    # Reported as: the title was not put on the artwork, but the game name still
    # showed and was cut off. Both halves of that are real. The name on a game's
    # screen is the caption above the buttons, which has nothing to do with the
    # artwork title, and every name was cut at twenty characters by a clip in
    # the menu's own script, so "The Witcher 3 Wild Hunt - Game of the Year
    # Edition" arrived as "THE WITCHER 3 WILD ...".

    BeforeAll {
        $script:LongDir = Join-Path $script:Sandbox 'longname'
        New-Item -ItemType Directory -Force -Path $script:LongDir | Out-Null
        $menu = Join-Path $script:LongDir 'menu.hta'
        New-MenuHta @{ GameName='The Witcher 3 Wild Hunt - Game of the Year Edition'
                       Games=@(); Buttons=@('Play','Exit'); MusicFile=''; ManualFile=''
                       PanelSide='Right'; IconName='x.ico'; WindowBorder=$true
                       ButtonStyle='Minimal' } $menu
        $script:LongJs = Get-Content $menu -Raw
    }

    It 'keeps the whole name in the file, however long it is' {
        $script:LongJs | Should -Match 'Game of the Year Edition'
    }

    It 'no longer forces the spaces to non-breaking, which is what stopped it wrapping' {
        $script:LongJs | Should -Not -Match 'replace\(/ /g,"&nbsp;"\)'
    }

    It 'lets the buttons and the caption wrap' {
        # Both carried white-space:nowrap, so neither could ever use a second line.
        $script:LongJs | Should -Match '\.btn\{[^}]*' 
        ($script:LongJs -split "`n" | Where-Object { $_ -match 'white-space:nowrap' }).Count | Should -Be 0
    }

    It 'wraps and shrinks rather than cutting at twenty characters' {
        # Run the menu's own two functions in a real JScript engine, because what
        # matters is what they return, not that the source looks right.
        $probe = Join-Path $script:LongDir 'probe.js'
        $fit  = [regex]::Match($script:LongJs, '(?s)(function fitStyle\(s\)\{.*?\r?\n  \})').Groups[1].Value
        $clip = [regex]::Match($script:LongJs, '(?m)^  (function clip\(s\).*)$').Groups[1].Value
        $fit  | Should -Not -BeNullOrEmpty
        $clip | Should -Not -BeNullOrEmpty

        $witcher = 'The Witcher 3 Wild Hunt - Game of the Year Edition'   # 50
        $monster = 'Tom Clancy Splinter Cell Chaos Theory Deluxe Anniversary Collection Remastered Edition'
        $js = @"
$fit
$clip
WScript.Echo("short|" + clip("Hollow Knight"));
WScript.Echo("witcher|" + clip("$witcher"));
WScript.Echo("monster|" + clip("$monster").length);
WScript.Echo("size_short|" + fitStyle("Hollow Knight"));
WScript.Echo("size_mid|" + fitStyle("Baldurs Gate II Enhanced"));
WScript.Echo("size_long|" + fitStyle("$witcher"));
"@
        Set-Content -LiteralPath $probe -Value $js -Encoding Ascii
        $out = @(& cscript.exe //nologo //E:JScript $probe 2>&1 | ForEach-Object { $_.ToString().Trim() })
        $got = @{}
        foreach ($line in $out) { $k, $v = $line -split '\|', 2; $got[$k] = $v }

        $got['short']   | Should -Be 'Hollow Knight' -Because 'a short name is untouched'
        $got['witcher'] | Should -Be $witcher -Because 'fifty characters now survive whole'
        [int]$got['monster'] | Should -BeLessOrEqual 64 -Because 'something past two lines is still cut'
        $got['size_short'] | Should -BeNullOrEmpty -Because 'a short name keeps the original size'
        $got['size_mid']   | Should -Match '13px'
        $got['size_long']  | Should -Match '12px'
    }

    It 'measures how many lines a label really took, rather than counting characters' {
        # Bahnschrift is not on every machine and the fallback is wider, so the
        # line count has to come from what was drawn.
        $script:LongJs | Should -Match 'offsetHeight/16'
        $script:LongJs | Should -Match 'Math\.floor\(bh/lines\)'
    }
}

Describe 'Which buttons a game screen offers' -Tag 'Unit' {

    # Reported as: adding nine folders of game files, choosing "no installer" for
    # each, then pressing Play on the built disc and being told the game was not
    # on the disc. It was: every one of them was there.
    #
    # Two faults, next to each other. Play was offered for an entry with nothing
    # to run, and doPlay builds its path from the entry's setup, which is empty
    # for such an entry, so it resolved to the disc root and a folder is not a
    # file. And the Open Folder button meant to replace it was written behind
    # !g.files, so the only kind of entry that can be given no installer was the
    # only kind that never saw it.
    #
    # The existing menu tests drive by counting buttons, and both the broken and
    # the fixed screen show three, so they could not have caught this. These run
    # the real renderGame and read the buttons back by name.

    BeforeAll {
        $script:BtnDir = Join-Path $script:Sandbox 'buttons'
        New-Item -ItemType Directory -Force -Path $script:BtnDir | Out-Null
        $menu = Join-Path $script:BtnDir 'menu.hta'
        New-MenuHta @{ GameName='Button Test'; Games=@(); Buttons=@('Play','Install','Exit')
                       MusicFile=''; ManualFile=''; PanelSide='Right'; IconName='x.ico'
                       WindowBorder=$true; ButtonStyle='Minimal' } $menu
        $script:BtnJs = Get-Content $menu -Raw

        function Get-ScreenButtons {
            param([hashtable]$Game, [string[]]$OnMenu = @('Play','Install','Exit'),
                  [string]$Set = 'null')
            $fn = [regex]::Match($script:BtnJs,
                '(?s)(  function renderGame\(\)\{.*?\r?\n  \})').Groups[1].Value
            if (-not $fn) { throw 'renderGame was not found in the generated menu' }
            $addOns = (@($Game.AddOns) | ForEach-Object { '{n:"' + $_ + '"}' }) -join ','
            $probe = Join-Path $script:BtnDir 'probe.js'
            $js = @"
var GAMES=[{ n:"$($Game.Name)", files:$(if($Game.Files){'true'}else{'false'}),
             s:"$($Game.Setup)", d:"disc/folder", a:[$addOns], m:"$($Game.Name)" }];
var cur=0;
// Declared because renderGame reads it, the same as the real menu does.
var SET=$Set;
function renderSet(){ CAPTURED="|SETPANEL"; }
var ON="$($OnMenu -join ',')";
function has(b){ return (","+ON+",").indexOf(","+b+",") >= 0; }
var CAPTURED="";
function setPanel(h,cap){ CAPTURED=h; }
function capFor(n){ return ""; }
function btnHtml(id,cls,label,fn,tip){ return "|"+label; }
$fn
renderGame();
WScript.Echo(CAPTURED);
"@
            Set-Content -LiteralPath $probe -Value $js -Encoding Ascii
            $out = (& cscript.exe //nologo //E:JScript $probe 2>&1 | Select-Object -First 1)
            return @(([string]$out).Split('|') | Where-Object { $_ })
        }
    }

    It 'offers Open Folder, and no Play, for a folder of files with no installer' {
        # The reported case, exactly.
        $b = Get-ScreenButtons @{ Name='GOG1'; Files=$true; Setup=''; AddOns=@() }
        $b | Should -Contain 'Open Folder'
        $b | Should -Not -Contain 'Play from disc'
        $b | Should -Not -Contain 'Play'
    }

    It 'still offers Play from disc when the folder does have an executable' {
        # The case the existing menu tests cover, which must not change.
        $b = Get-ScreenButtons @{ Name='Gothic'; Files=$true; Setup='Games/01 - Gothic/gothic.exe'; AddOns=@() }
        $b | Should -Contain 'Play from disc'
        $b | Should -Not -Contain 'Open Folder'
    }

    It 'still offers Play and Install for a GOG download' {
        $b = Get-ScreenButtons @{ Name='Hollow Knight'; Files=$false; Setup='setup_hk.exe'; AddOns=@() }
        $b | Should -Contain 'Play'
        $b | Should -Contain 'Install'
    }

    It 'offers the folder for a GOG entry that somehow has no installer' {
        # The branch that always worked, kept honest.
        $b = Get-ScreenButtons @{ Name='Odd'; Files=$false; Setup=''; AddOns=@() }
        $b | Should -Contain 'Open Folder'
        $b | Should -Not -Contain 'Install'
    }

    It 'still offers the folder when only Install is on the menu' {
        # Open Folder stands in for both buttons here, so unticking one must not
        # leave the disc with no way to reach the files.
        $b = Get-ScreenButtons -Game @{ Name='GOG1'; Files=$true; Setup=''; AddOns=@() } -OnMenu @('Install','Exit')
        $b | Should -Contain 'Open Folder'
    }

    It 'still offers the folder when only Play is on the menu' {
        $b = Get-ScreenButtons -Game @{ Name='GOG1'; Files=$true; Setup=''; AddOns=@() } -OnMenu @('Play','Exit')
        $b | Should -Contain 'Open Folder'
    }

    It 'offers it once, not twice' {
        $b = Get-ScreenButtons @{ Name='GOG1'; Files=$true; Setup=''; AddOns=@() }
        @($b | Where-Object { $_ -eq 'Open Folder' }).Count | Should -Be 1
    }

    It 'offers none of it on a disc that holds part of a set' {
        # A set disc cannot play or install anything until every disc has been
        # copied into one folder, so it must not offer either. This is item 7
        # again in a new place: a button that cannot work is worse than no
        # button, because the person presses it and is told the game is not
        # there when it is.
        $b = Get-ScreenButtons -Game @{ Name='Split'; Files=$false; Setup='setup.exe'; AddOns=@() } `
                               -Set '{n:2,of:3,label:"Split"}'
        $b | Should -Not -Contain 'Play'
        $b | Should -Not -Contain 'Install'
        $b | Should -Not -Contain 'Open Folder'
        # It draws its own panel instead.
        $b | Should -Contain 'SETPANEL'
    }

    It 'offers all of it again on a disc that is not part of a set' {
        # The same entry, with no set, has to behave exactly as it always did.
        $b = Get-ScreenButtons -Game @{ Name='Split'; Files=$false; Setup='setup.exe'; AddOns=@() }
        $b | Should -Contain 'Install'
        $b | Should -Not -Contain 'SETPANEL'
    }
}

Describe 'Hiding the game name above the menu buttons' -Tag 'Unit' {

    # Asked for after the long-name fix, by the same reporter, once he worked
    # out what "title on artwork" actually meant: "Can the text directly above
    # the menu buttons be hidden?"
    #
    # It is a different thing from the artwork title and always was. Unticking
    # that box draws nothing on the picture and leaves this line alone, which is
    # why he thought the box was broken. On a one-game disc this is the only
    # place the name appears, so it stays on unless somebody turns it off.

    BeforeAll {
        $script:CapDir = Join-Path $script:Sandbox 'caption'
        New-Item -ItemType Directory -Force -Path $script:CapDir | Out-Null

        function Get-MenuCaption {
            param($ShowCaption = '__absent__')
            $cfg = @{ GameName='Disc'; Games=@(); Buttons=@('Play','Exit'); MusicFile=''
                      ManualFile=''; PanelSide='Right'; IconName='x.ico'
                      WindowBorder=$true; ButtonStyle='Minimal' }
            if ($ShowCaption -ne '__absent__') { $cfg.ShowCaption = $ShowCaption }
            $menu = Join-Path $script:CapDir 'menu.hta'
            New-MenuHta $cfg $menu
            $js = Get-Content $menu -Raw
            $flag = [regex]::Match($js, 'var SHOWCAP=(\w+);').Groups[1].Value
            $fn = [regex]::Match($js, '(?s)(  function capFor\(name\)\{.*?\r?\n  \})').Groups[1].Value
            $probe = Join-Path $script:CapDir 'probe.js'
            $t = @"
var SHOWCAP=$flag;
function esc(s){ return s; }
function clip(s){ return s; }
function fitStyle(s){ return ""; }
$fn
var out = capFor("Hollow Knight");
WScript.Echo(out ? out.replace(/<[^>]*>/g,"") : "");
"@
            Set-Content -LiteralPath $probe -Value $t -Encoding Ascii
            return ([string](& cscript.exe //nologo //E:JScript $probe 2>&1 | Select-Object -First 1)).Trim()
        }
    }

    It 'prints the name when it is on' {
        Get-MenuCaption $true | Should -Be 'Hollow Knight'
    }

    It 'prints nothing at all when it is off' {
        Get-MenuCaption $false | Should -BeNullOrEmpty
    }

    It 'prints the name when nothing said either way' {
        # A disc built by code that never heard of this option.
        Get-MenuCaption | Should -Be 'Hollow Knight'
    }

    It 'saves the answer, and an older project still shows the name' {
        $proj = Join-Path $script:CapDir 'proj'
        New-Item -ItemType Directory -Force -Path $proj | Out-Null
        $s = @{ Games=@(); Label='P'; IconPath=''; IconIsIco=$false; Menu=$true; BgPath=''
                BgAsIs=$false; PanelSide='Right'; Divider=$false; ShowTitle=$false; TitleText=''
                WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null; Buttons=@('Play')
                ManualPath=$null; ExtrasPath=$null; ExtraItems=@(); MediaKey=''
                LinuxInfo=$false; LegacyFs=$false; Checksums=$false; ShowCaption=$false }
        Save-Project $s $proj
        $raw = Get-Content -Raw (Join-Path $proj 'discproject.json') | ConvertFrom-Json
        $raw.Version | Should -Be 13
        $raw.ShowCaption | Should -BeFalse
        (Import-Project (Join-Path $proj 'discproject.json')).ShowCaption | Should -BeFalse

        # The opposite default to every other flag here, and the reason this
        # test exists: absent must mean on, or reopening a project written
        # before today would quietly take the name off the menu.
        $old = Join-Path $proj 'older.json'
        @{ Version=11; Label='Old'; Games=@(); Buttons=@('Play'); Menu=$true; BgAsIs=$true
           PanelSide='Right'; ButtonStyle='Minimal'; WindowBorder=$true
        } | ConvertTo-Json -Depth 4 | Set-Content $old -Encoding UTF8
        (Import-Project $old).ShowCaption | Should -BeTrue
    }

    It 'is not the artwork title, which is a separate setting' {
        # The confusion that produced the report: these two are unrelated, and
        # turning the artwork title off must not touch this line.
        Get-MenuCaption $true | Should -Be 'Hollow Knight'
        $cfg = @{ GameName='Disc'; Games=@(); Buttons=@('Play'); MusicFile=''; ManualFile=''
                  PanelSide='Right'; IconName='x.ico'; WindowBorder=$true
                  ButtonStyle='Minimal'; ShowCaption=$true }
        $menu = Join-Path $script:CapDir 'notitle.hta'
        New-MenuHta $cfg $menu
        (Get-Content $menu -Raw) | Should -Match 'var SHOWCAP=true'
    }
}

Describe 'Nothing is added to the form and then forgotten' -Tag 'Unit' {

    # These do not test a feature. They test that a feature added next year
    # cannot quietly arrive without being saved, reloaded, or checked.
    #
    # The repository already works this way in two places: the window suite
    # reads every AddBtn out of the source and insists each one is on screen,
    # and the menu template test keeps its own substitution table so a new
    # %%TOKEN%% fails loudly rather than being parse-checked as literal text.
    # That second one caught %%SHOWCAP%% the day it was written. These extend
    # the same idea to settings, which is where the gaps have actually been.

    It 'writes every setting the form collects into the project file' {
        # Catches: a new checkbox wired into the build and forgotten in
        # Save-Project, so the disc builds correctly and reopening the project
        # silently rebuilds a different one.
        # Worked out here rather than borrowed: the harness keeps its copy in a
        # local that does not reach this far.
        $app = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
        $src = Get-Content -Raw -LiteralPath $app
        $gather = [regex]::Match($src,
            '(?s)\$s=@\{ Games=\(Get-Games\);(.*?)\r?\n    \$btnBuild\.Enabled').Groups[1].Value
        $gather | Should -Not -BeNullOrEmpty -Because 'the settings the form builds must be findable'
        $gathered = @([regex]::Matches($gather, '(?:^|[;\s{])([A-Za-z]\w*)=') |
                      ForEach-Object { $_.Groups[1].Value }) + 'Games' | Sort-Object -Unique

        $saveBody = [regex]::Match($src, '(?s)\$o = \[ordered\]@\{(.*?)\r?\n    \}').Groups[1].Value
        $saveBody = [regex]::Replace($saveBody, '(?m)^\s*#.*$', '')
        $saved = @([regex]::Matches($saveBody, '(?m)^\s*(\w+)\s*=') | ForEach-Object { $_.Groups[1].Value })

        $missing = @($gathered | Where-Object { $saved -notcontains $_ })
        $missing.Count | Should -Be 0 -Because "collected from the form but never written to the project file: $($missing -join ', ')"
    }

    It 'reads back every setting it writes' {
        # Catches the other half: written by Save-Project and forgotten in
        # Import-Project, so reopening a project loses it.
        $dir = Join-Path $script:Sandbox 'roundtrip-all'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $s = @{ Games=@(); Label='Round Trip'; IconPath='C:\art\icon.png'; IconIsIco=$false
                Menu=$true; BgPath='C:\art\bg.png'; BgAsIs=$true; PanelSide='Left'
                Divider=$true; ShowTitle=$true; TitleText='A Title'; ShowCaption=$false
                WindowBorder=$false; ButtonStyle='Bordered'; MusicFile='C:\a\music.mp3'
                Buttons=@('Play','Exit'); ManualPath='C:\a\manual.pdf'; ExtrasPath='C:\a\extras'
                ExtraItems=@('C:\a\readme.txt'); MediaKey='DVD'; LinuxInfo=$true
                LegacyFs=$true; Checksums=$true
                DiscSet=$true }
        Save-Project $s $dir
        $json = Get-Content -Raw (Join-Path $dir 'discproject.json') | ConvertFrom-Json
        $back = Import-Project (Join-Path $dir 'discproject.json')

        # Everything else in the file is bookkeeping or belongs to a game entry,
        # and a new one has to be added here on purpose rather than by accident.
        $notSettings = @('Version','AppVersion','SavedUtc','SourceFolder','GameName',
                         'Games','OutDir','CoverPath','DiscArtPath')
        $checked = 0
        foreach ($k in $json.PSObject.Properties.Name) {
            if ($notSettings -contains $k) { continue }
            $back.ContainsKey($k) | Should -BeTrue -Because "$k is saved, so reopening a project must bring it back"
            $want = $s[$k]
            if ($want -is [array]) { @($back[$k]) | Should -Be @($want) -Because "$k must survive the round trip" }
            else                   { $back[$k]   | Should -Be $want   -Because "$k must survive the round trip" }
            $checked++
        }
        $checked | Should -BeGreaterThan 15 -Because 'this should be checking most of the settings, not two of them'
    }

    It 'checks the checksum list with the tool it claims to be compatible with' -Skip:(-not (Get-Command sha256sum -ErrorAction SilentlyContinue)) {
        # The format claim is the whole point of the file, and the first version
        # failed it: CRLF made sha256sum read the carriage return as part of
        # every filename. A test on the line endings is a proxy; this is the
        # real thing, run whenever the real tool happens to be present.
        $dir = Join-Path $script:Sandbox 'sha-real'
        New-Item -ItemType Directory -Force -Path (Join-Path $dir 'sub') | Out-Null
        Set-Content (Join-Path $dir 'a.bin') 'one' -Encoding Ascii
        Set-Content (Join-Path $dir 'sub\b.bin') 'two' -Encoding Ascii
        $null = New-ChecksumManifest $dir 'REAL TOOL' $null
        Push-Location $dir
        try {
            $out = & sha256sum -c (Get-ChecksumFileName) 2>&1
            $code = $LASTEXITCODE
        } finally { Pop-Location }
        $code | Should -Be 0 -Because "sha256sum -c rejected it: $($out -join '; ')"
        ($out -join "`n") | Should -Match 'a\.bin: OK'
        ($out -join "`n") | Should -Match 'b\.bin: OK'
    }
}

Describe 'Every tick box and list says what it is for' -Tag 'Unit' {

    # A caption has about three words to work with, and "checksummed" is not
    # three words that explain anything. Thirteen of the eighteen tick boxes and
    # drop-downs on this form had no hover text at all, including both of the
    # ones added most recently, which is how the gap keeps happening: a control
    # is added, it works, and nothing ever says it is unexplained.

    BeforeAll {
        $app = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
        $script:TipSrc = Get-Content -Raw -LiteralPath $app
        $script:Tipped = @([regex]::Matches($script:TipSrc, 'SetToolTip\(\$(\w+)') |
                           ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    }

    It 'has hover text on every checkbox and dropdown the window shows' {
        $want = @()
        foreach ($m in [regex]::Matches($script:TipSrc,
                 '\$(\w+)\s*=\s*New-Object System\.Windows\.Forms\.(CheckBox|ComboBox)')) {
            $n = $m.Groups[1].Value
            # Only the ones on the main window. A control inside a dialog is
            # explained by the dialog it is in.
            if ($script:TipSrc -match ('\$(?:form|grp|grpX)\.Controls\.Add\(\$' + [regex]::Escape($n) + '\)')) {
                $want += $n
            }
        }
        $want = @($want | Sort-Object -Unique)
        $want.Count | Should -BeGreaterThan 12 -Because 'the form has a good many of them'

        $bare = @($want | Where-Object { $script:Tipped -notcontains $_ })
        $bare.Count | Should -Be 0 -Because "no hover text on: $($bare -join ', ')"
    }

    It 'gives the pop-up long enough to be read' {
        # The default is five seconds, which cuts the longer explanations off
        # part way through a sentence.
        $m = [regex]::Match($script:TipSrc, '\$tips\.AutoPopDelay\s*=\s*(\d+)')
        $m.Success | Should -BeTrue -Because 'the pop-up timeout should be set deliberately'
        [int]$m.Groups[1].Value | Should -BeGreaterThan 15000
    }

    It 'explains the two that a caption cannot' {
        # These two were reported as baffling, in as many words: "all I see is a
        # checkbox, what is the exact point", and the artwork title confusion
        # that produced half of issue 98 item 5.
        $sums = [regex]::Match($script:TipSrc, '(?s)SetToolTip\(\$chkSums,(.*?)\)\)').Groups[1].Value
        $sums | Should -Match 'checksums\.sha256'
        $sums | Should -Match 'sha256sum'

        $cap = [regex]::Match($script:TipSrc, '(?s)SetToolTip\(\$chkCaption,(.*?)\)\)').Groups[1].Value
        $cap | Should -Match 'not the title on the artwork'
    }
}

Describe 'The installer check tells the truth about what it tested' -Tag 'Unit' {

    # Smart App Control refuses to start an unsigned binary. Whether a given
    # Windows Sandbox comes up with it ON or in evaluation varies run to run,
    # and only the second will run the installer. The check reported the first
    # as five failures, which is false: it had not found a fault, it had
    # declined to look.
    #
    # The record, from this machine's own sandbox logs:
    #   0.9.0   SAC ON           "5 check(s) failed"
    #   0.9.1   SAC ON           "5 check(s) failed"
    #   0.9.2   SAC evaluation   every check passed
    #   0.10.0  SAC ON           "5 check(s) failed"
    #
    # Same installer shape every time. Two of those releases shipped anyway,
    # which is what a gate that cries wolf buys you.

    BeforeAll {
        $pkg = Join-Path (Split-Path $PSScriptRoot -Parent) 'packaging\sandbox'
        $script:CheckSrc = Get-Content -Raw -LiteralPath (Join-Path $pkg 'Install-Check.ps1')
        $script:HostSrc  = Get-Content -Raw -LiteralPath (Join-Path $pkg 'Test-Installer.ps1')
        # Lifted out and loaded on its own, because the script around it
        # installs things and is not something a test suite should run.
        $fn = [regex]::Match($script:CheckSrc,
            '(?s)(function Test-PolicyBlocked\(\[string\]\$message\) \{.*?\r?\n\})').Groups[1].Value
        if (-not $fn) { throw 'Test-PolicyBlocked was not found in Install-Check.ps1' }
        . ([scriptblock]::Create($fn))
    }

    It 'knows the refusal Windows actually produced' {
        # Copied from the log of the run that stopped the 0.10.0 release.
        Test-PolicyBlocked 'This command cannot be run due to the error: An Application Control policy has blocked this file.' |
            Should -BeTrue
    }

    It 'does not mistake an ordinary failure for a policy block' {
        Test-PolicyBlocked 'The system cannot find the file specified.' | Should -BeFalse
        Test-PolicyBlocked 'exit 1'                                      | Should -BeFalse
        Test-PolicyBlocked ''                                            | Should -BeFalse
        Test-PolicyBlocked $null                                         | Should -BeFalse
    }

    It 'reports what it could not test as skipped, not as failed' {
        # One guard in Check covers every downstream check at once, so a new
        # check added later is covered without anybody remembering to.
        $script:CheckSrc | Should -Match 'SKIP'
        $script:CheckSrc | Should -Match 'not tested: the installer never ran'
        $script:CheckSrc | Should -Match '\$script:Blocked -and -not \$Always'
    }

    It 'still reports the image checks, which a block does not affect' {
        # Whether mshta and JScript are present is true or false regardless of
        # whether the installer was allowed to run, so that one is not skipped.
        $script:CheckSrc | Should -Match "Check -Always .mshta and JScript"
    }

    It 'only shuts the machine down when the machine is disposable' {
        # Run on the test VM once, it turned the VM off. The shutdown now sits
        # behind the one thing that identifies a sandbox.
        $script:CheckSrc | Should -Match "WDAGUtilityAccount"
        $shutdown = [regex]::Match($script:CheckSrc, '(?s)if \(\$script:InSandbox\) \{\s*\r?\n\s*shutdown /s /t 0')
        $shutdown.Success | Should -BeTrue -Because 'the shutdown must be inside the sandbox test'
        # And nowhere else in the file.
        @([regex]::Matches($script:CheckSrc, 'shutdown /s')).Count | Should -Be 1
    }

    It 'says where it is running rather than asserting a sandbox' {
        $script:CheckSrc | Should -Not -Match 'Say "DiscWright installer check, inside Windows Sandbox"'
        $script:CheckSrc | Should -Match 'InSandbox'
    }

    It 'gives a blocked run its own answer, distinct from pass and from fail' {
        $script:HostSrc | Should -Match 'BLOCKED'
        $script:HostSrc | Should -Match 'Could not test'
        $script:HostSrc | Should -Match 'exit 2'
    }
}

Describe 'Handing the ISO to another program' -Tag 'Unit' {

    # Asked for as "being able to link your software to ImgBurn or similar
    # software for an instant burn". Not ImgBurn in particular: Windows already
    # records what can open an ISO and the answer differs per machine. On the
    # machine this was written on the registered program is Nero, which is a
    # decent argument against hardcoding anybody's favourite, and on the test VM
    # there is nothing installed at all and two entries still come back.

    BeforeAll {
        $burn = Join-Path (Split-Path $PSScriptRoot -Parent) 'burn\DiscWright.Burn.ps1'
        . $burn
        $script:BurnSrc = Get-Content -Raw -LiteralPath $burn
    }

    It 'finds something on any machine, because Windows always has two' {
        # Windows.IsoFile carries burn and mount whatever else is installed, so
        # an empty list means the discovery is broken rather than the machine
        # being bare.
        $found = @(Get-IsoHandoffs)
        $found.Count | Should -BeGreaterThan 1
        @($found | Where-Object { $_.Name -match 'Disc Image' }).Count | Should -Be 1
        @($found | Where-Object { $_.Verb -eq 'mount' }).Count | Should -Be 1
    }

    It 'names each one the way the program names itself' {
        # Not a path and not a registry key: "Nero Burning ROM", not
        # Nero.BurningROM.2023.iso.1 and not nero.exe.
        foreach ($h in Get-IsoHandoffs) {
            $h.Name | Should -Not -BeNullOrEmpty
            $h.Name | Should -Not -Match '\.exe$'
            $h.Name | Should -Not -Match ('^[A-Za-z]:' + [char]92 + [char]92)
            $h.What | Should -Not -BeNullOrEmpty
        }
    }

    It 'points every entry at a program that is really there' {
        foreach ($h in Get-IsoHandoffs) {
            if ($h.Verb) { continue }   # a shell verb has no exe of its own
            Test-Path -LiteralPath $h.Exe | Should -BeTrue -Because "$($h.Name) was listed"
        }
    }

    It 'reads a shell command the way the shell does' {
        $q = Split-ShellCommand '"C:\Program Files\A B\tool.exe" /burn "%1"'
        $q.Exe  | Should -Be 'C:\Program Files\A B\tool.exe'
        $q.Args | Should -Be '/burn "%1"'
        $bare = Split-ShellCommand 'C:\Windows\System32\isoburn.exe "%1"'
        $bare.Exe | Should -Be 'C:\Windows\System32\isoburn.exe'
        Split-ShellCommand '' | Should -BeNullOrEmpty
    }

    It 'falls back to the filename when a program has no description' {
        $tmp = Join-Path $script:Sandbox 'nodesc.exe'
        Set-Content -LiteralPath $tmp -Value 'not really an exe' -Encoding Ascii
        Get-ExeFriendlyName $tmp | Should -Be 'nodesc'
        Get-ExeFriendlyName 'C:\nope\missing.exe' | Should -Be ''
    }

    It 'lists the same program once, not twice' {
        # The registered handler can be Windows' own burner, and then it would
        # appear under both rules.
        $names = @(Get-IsoHandoffs | ForEach-Object { $_.Exe } | Where-Object { $_ })
        ($names | Sort-Object -Unique).Count | Should -Be $names.Count
    }

    It 'hands the file over and does not drive the other program' {
        # Every burner spells its switches differently and getting one wrong
        # costs a disc, so nothing here builds somebody else's command line
        # beyond the %1 the shell itself would fill in.
        $script:BurnSrc | Should -Match "replace '%1'"
        $script:BurnSrc | Should -Not -Match '/MODE|/WRITE|/START|--burn'
    }
}


Describe 'Laying one game out across several discs' -Tag 'Unit' {

    # Asked for as splitting large games across more than one disc, for
    # archiving. Not the spanning in docs/research/spanning, which was running
    # the installer off the discs and was tested and refused: nothing here is
    # ever installed from a disc, the parts are copied back into one folder
    # first.
    #
    # Whole files only, so the plan never has to be joined back together. A file
    # bigger than the disc is refused by name.

    BeforeAll {
        function F([string]$rel, [double]$gb) { @{ Rel = $rel; Bytes = [double]($gb * 1GB) } }
        # A real GOG shape: an installer, then parts just under 4 GiB, which is
        # where GOG cuts them to stay under the FAT32 ceiling.
        $script:Gog = @(
            (F 'setup_big_game.exe'   0.96),
            (F 'setup_big_game-1.bin' 3.99),
            (F 'setup_big_game-2.bin' 3.99),
            (F 'setup_big_game-3.bin' 3.99),
            (F 'setup_big_game-4.bin' 1.20)
        )
    }

    It 'fills each disc until the next file does not fit' {
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'DVD9') 6MB
        $plan.Ok | Should -BeTrue
        # 0.96 + 3.99 fits; adding another 3.99 does not, and so on.
        @($plan.Discs).Count | Should -Be 3
        @($plan.Discs[0].Files | ForEach-Object { $_.Rel }) |
            Should -Be @('setup_big_game.exe', 'setup_big_game-1.bin')
    }

    It 'keeps the files in the order they were given' {
        # A cleverer packing would scatter an installer and its parts for no
        # reason anybody could follow.
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'DVD5') 6MB
        $flat = @($plan.Discs | ForEach-Object { $_.Files } | ForEach-Object { $_.Rel })
        $flat | Should -Be @($script:Gog | ForEach-Object { $_.Rel })
    }

    It 'puts every file on exactly one disc' {
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'DVD5') 6MB
        $flat = @($plan.Discs | ForEach-Object { $_.Files } | ForEach-Object { $_.Rel })
        $flat.Count | Should -Be $script:Gog.Count
        ($flat | Sort-Object -Unique).Count | Should -Be $script:Gog.Count
    }

    It 'never overfills a disc' {
        foreach ($key in 'DVD5', 'DVD9', 'BD25') {
            $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity $key) 6MB
            foreach ($d in $plan.Discs) {
                $d.Bytes | Should -BeLessOrEqual $plan.Room -Because "disc $($d.Number) of a $key set"
            }
        }
    }

    It 'numbers the discs from one, with no gaps' {
        # On a DVD9, not a DVD5. Five files onto DVD5 happen to make five discs
        # whether the packing works or not, so the DVD5 version of this passed
        # with the packing deliberately broken and proved nothing.
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'DVD9') 6MB
        @($plan.Discs | ForEach-Object { $_.Number }) | Should -Be @(1, 2, 3)
    }

    It 'refuses a file no disc of that size could hold, and names it' {
        # The answer is a bigger blank, so the person has to be told which file
        # decided that rather than just "it will not fit".
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'CD') 6MB
        $plan.Ok | Should -BeFalse
        $plan.Why | Should -Match 'setup_big_game\.exe'
        $plan.Why | Should -Match 'larger disc'
        @($plan.Discs).Count | Should -Be 0
    }

    It 'counts the room the menu and artwork take' {
        # Same payload, same blank, more overhead: it has to need more discs.
        # Both fit the room on their own either way, so the only thing that can
        # change the disc count is the overhead.
        $one = @((F 'a.bin' 2.0), (F 'b.bin' 2.2))
        $tight = Get-DiscSetPlan $one (Get-MediaCapacity 'DVD5') 0
        $loose = Get-DiscSetPlan $one (Get-MediaCapacity 'DVD5') 400MB
        @($tight.Discs).Count | Should -Be 1
        @($loose.Discs).Count | Should -Be 2
    }

    It 'says so rather than dividing by nothing when there is no room at all' {
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'CD') 5GB
        $plan.Ok | Should -BeFalse
        $plan.Why | Should -Match 'no room'
    }

    It 'handles a payload that needs only one disc, and an empty one' {
        # One disc is a plan, not a set. Whoever asked decides that.
        $plan = Get-DiscSetPlan $script:Gog (Get-MediaCapacity 'BD25') 6MB
        $plan.Ok | Should -BeTrue
        @($plan.Discs).Count | Should -Be 1
        $empty = Get-DiscSetPlan @() (Get-MediaCapacity 'DVD5') 6MB
        $empty.Ok | Should -BeTrue
        @($empty.Discs).Count | Should -Be 0
    }
}

Describe 'The file that tells you what the whole set holds' -Tag 'Unit' {

    # The same file goes on every disc, so any disc can say what the set is,
    # what is on this one, what is still missing and whether the folder somebody
    # copied the discs into is complete. The folder is the only state there is:
    # nothing is remembered between sessions anywhere else.

    BeforeAll {
        $script:Entries = @(
            @{ Disc = 1; Rel = 'setup_game.exe';   Bytes = [double]1000; Sha256 = ('a' * 64) }
            @{ Disc = 1; Rel = 'docs\manual.pdf';  Bytes = [double]2000; Sha256 = ('b' * 64) }
            @{ Disc = 2; Rel = 'setup_game-1.bin'; Bytes = [double]3000; Sha256 = ('c' * 64) }
        )
        $script:Man = New-DiscSetManifest $script:Entries 'BIG GAME' 2 2
    }

    It 'says which disc this is, and how many there are' {
        $script:Man | Should -Match 'BIG GAME'
        $script:Man | Should -Match 'disc 2 of 2'
    }

    It 'says plainly that this disc cannot install the game' {
        # The old disc sets labelled discs D1 and D2 as though one continued the
        # other, which described a relationship that did not exist. This one
        # really is part of a set, and has to say what that costs.
        $script:Man | Should -Match 'cannot install the game on its own'
        $script:Man | Should -Match 'never from a disc'
    }

    It 'lists every file in the set, not just the ones on this disc' {
        foreach ($e in $script:Entries) {
            $leaf = Split-Path $e.Rel -Leaf
            $script:Man | Should -Match ([regex]::Escape($leaf))
        }
    }

    It 'separates folders with a forward slash, like the checksum list' {
        $script:Man | Should -Match 'docs/manual\.pdf'
        $script:Man | Should -Not -Match ([regex]::Escape('docs' + [char]92 + 'manual'))
    }

    It 'can be read back to the same answer it was written from' {
        # The machine-readable half, parsed the way the menu will parse it.
        $rows = @()
        foreach ($line in ($script:Man -split "`r`n")) {
            if ($line -notmatch '^[0-9]') { continue }
            $d, $h, $b, $f = $line -split ' +', 4
            $rows += @{ Disc = [int]$d; Sha256 = $h; Bytes = [long]$b; Rel = $f.Substring(1) }
        }
        $rows.Count | Should -Be $script:Entries.Count
        for ($i = 0; $i -lt $rows.Count; $i++) {
            $rows[$i].Disc | Should -Be $script:Entries[$i].Disc
            $rows[$i].Sha256 | Should -Be $script:Entries[$i].Sha256
            $rows[$i].Bytes | Should -Be ([long]$script:Entries[$i].Bytes)
        }
    }

    It 'is plain ASCII, so it opens anywhere' {
        [int[]][char[]]$script:Man | Where-Object { $_ -gt 127 } | Should -BeNullOrEmpty
    }

    It 'is a name the disc owns' {
        Get-DiscSetFileName | Should -Be 'Disc set.txt'
    }
}

Describe 'Checking the folder the discs were copied into' -Tag 'Unit' {

    BeforeAll {
        $script:RestDir = Join-Path $script:Sandbox 'restore'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:RestDir 'docs') | Out-Null
        Set-Content (Join-Path $script:RestDir 'setup_game.exe') 'one' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:RestDir 'docs\manual.pdf') 'two' -Encoding Ascii -NoNewline
        $script:Want = @(
            @{ Disc = 1; Rel = 'setup_game.exe'; Bytes = [double]3
               Sha256 = (Get-FileSha256 (Join-Path $script:RestDir 'setup_game.exe')) }
            @{ Disc = 1; Rel = 'docs/manual.pdf'; Bytes = [double]3
               Sha256 = (Get-FileSha256 (Join-Path $script:RestDir 'docs\manual.pdf')) }
            @{ Disc = 2; Rel = 'setup_game-1.bin'; Bytes = [double]3; Sha256 = ('c' * 64) }
        )
    }

    It 'knows which discs are still needed' {
        $r = Test-DiscSetRestore $script:RestDir $script:Want
        $r.Complete | Should -BeFalse
        $r.Ok | Should -Be 2
        @($r.Missing).Count | Should -Be 1
        $r.DiscsNeeded | Should -Be @(2)
    }

    It 'says complete only when every file is there and right' {
        $all = @($script:Want[0], $script:Want[1])
        (Test-DiscSetRestore $script:RestDir $all).Complete | Should -BeTrue
    }

    It 'calls a file that changed damaged rather than missing' {
        # A disc that copied badly is a different problem from a disc nobody has
        # put in yet, and it needs a different sentence.
        $bent = @(@{ Disc = 3; Rel = 'setup_game.exe'; Bytes = [double]3; Sha256 = ('d' * 64) })
        $r = Test-DiscSetRestore $script:RestDir $bent
        $r.Complete | Should -BeFalse
        @($r.Damaged).Count | Should -Be 1
        @($r.Missing).Count | Should -Be 0
        $r.DiscsNeeded | Should -Be @(3)
    }
}


Describe 'The volume id of a disc that belongs to a set' -Tag 'Unit' {

    # The ISO9660 volume identifier is sixteen characters. Truncating a finished
    # "LONG NAME D2" at sixteen takes the number off the end, and then every
    # disc in the set carries the same volume id, so Windows shows three
    # identical drives and nothing can tell them apart. The disc sets DiscWright
    # built in 2026 and removed had this handled; the fix is recovered here from
    # the deleted version rather than rediscovered the hard way.

    BeforeAll {
        $script:LongName = 'The Witcher Enhanced Edition Directors Cut'
    }

    It 'gives every disc in a set a different id, even for a long name' {
        $ids = 1..3 | ForEach-Object { Get-VolumeLabel $script:LongName $_ 3 }
        @($ids | Sort-Object -Unique).Count | Should -Be 3
    }

    It 'keeps every one of them inside the sixteen characters' {
        foreach ($n in 1..9) {
            (Get-VolumeLabel $script:LongName $n 9).Length | Should -BeLessOrEqual 16
        }
    }

    It 'keeps the number, cutting the name instead' {
        # The number is what makes the ids different, so it is the part that
        # cannot be the one that gets cut.
        foreach ($n in 1..3) {
            Get-VolumeLabel $script:LongName $n 3 | Should -Match "_D$n$"
        }
    }

    It 'leaves a single disc exactly as it was' {
        # Everything that is not a set must come out byte for byte the same, or
        # rebuilding an old project would produce a differently named disc.
        Get-VolumeLabel $script:LongName | Should -Be 'The_Witcher_Enha'
        Get-VolumeLabel 'ALPHA' | Should -Be 'ALPHA'
        Get-VolumeLabel '' | Should -Be 'DISC'
    }

    It 'treats a set of one as not a set' {
        Get-VolumeLabel $script:LongName 1 1 | Should -Be (Get-VolumeLabel $script:LongName)
    }

    It 'still folds anything that is not a letter or a digit' {
        Get-VolumeLabel 'Tom Clancy: Splinter Cell!' 2 3 | Should -Match '^[A-Za-z0-9_]+$'
    }

    It 'does not leave a trailing underscore where the cut landed on a space' {
        # 'THE_WITCHER_ENH_' was the bug this guards, and a set has to keep that
        # fixed as well: the underscore is trimmed before the number goes on.
        foreach ($n in 1..3) {
            Get-VolumeLabel $script:LongName $n 3 | Should -Not -Match '__D[0-9]+$'
        }
    }
}


Describe 'Deciding what each disc in a set gets built with' -Tag 'Unit' {

    # The decisions, checked without writing any discs. The building itself is
    # the ordinary single-disc build, run once per disc, so what is worth
    # testing here is what it gets told each time.

    BeforeAll {
        function Step-Set([int]$discs) {
            # A plan shaped like the real one, without needing files that size.
            $plan = @{ Ok = $true; Discs = @() }
            for ($i = 1; $i -le $discs; $i++) {
                $plan.Discs += , @{ Number = $i; Bytes = [double]1000
                                    Files = @(@{ Rel = "part-$i.bin"; Bytes = [double]1000
                                                 Path = "C:\src\part-$i.bin" }) }
            }
            $entries = @()
            foreach ($d in $plan.Discs) {
                foreach ($f in $d.Files) {
                    $entries += , @{ Disc = $d.Number; Rel = $f.Rel; Bytes = $f.Bytes; Sha256 = ('a' * 64) }
                }
            }
            $s = @{ Label = 'The Witcher Enhanced Edition'; OutDir = 'C:\out'; Games = @(); MediaKey = 'DVD5'
                    IconPath = 'C:\art\i.png'; Menu = $true; BgPath = 'C:\art\b.png' }
            return @(Get-DiscSetSteps $s $plan $entries)
        }
        $script:Three = Step-Set 3
    }

    It 'makes one build out of each disc in the plan' {
        @($script:Three).Count | Should -Be 3
        @(Step-Set 1).Count | Should -Be 1
    }

    It 'gives every disc its own ISO filename' {
        # The ISO name comes from the label, so without this all three discs
        # would be written to the same file and only the last would survive.
        $names = @($script:Three | ForEach-Object { Split-Path (Get-IsoPath $_.OutDir $_.Label) -Leaf })
        $names | Should -Be @('The Witcher Enhanced Edition D1.iso',
                              'The Witcher Enhanced Edition D2.iso',
                              'The Witcher Enhanced Edition D3.iso')
    }

    It 'gives every disc its own staging folder' {
        # Sharing one would leave the last disc standing and nothing to look at
        # for the others.
        $dirs = @($script:Three | ForEach-Object { $_.StageDir })
        ($dirs | Sort-Object -Unique).Count | Should -Be 3
        $dirs[0] | Should -BeLike '*disc D1'
    }

    It 'gives every disc its own icon filename' {
        # Explorer caches disc icons by filename, so two discs sharing one name
        # show the first disc's face for the second disc.
        $icons = @($script:Three | ForEach-Object { Get-DiscIconName $_.Label })
        ($icons | Sort-Object -Unique).Count | Should -Be 3
    }

    It 'gives every disc its own volume id, with the number kept' {
        $vols = @($script:Three | ForEach-Object { $_.VolumeLabel })
        ($vols | Sort-Object -Unique).Count | Should -Be 3
        foreach ($v in $vols) { $v.Length | Should -BeLessOrEqual 16 }
        $vols[1] | Should -Match '_D2$'
    }

    It 'tells each build only the files that belong on that disc' {
        foreach ($i in 0..2) {
            $only = $script:Three[$i].OnlyFiles
            $only.Count | Should -Be 1
            $only.ContainsKey("C:\src\part-$($i + 1).bin") | Should -BeTrue
            $only.ContainsKey("C:\src\part-$(($i + 2) % 3 + 1).bin") | Should -BeFalse
        }
    }

    It 'puts the same whole-set file on every disc, with the disc number changed' {
        # Any disc has to be able to answer what the whole set is, so the list
        # is identical everywhere and only "disc N of M" differs.
        foreach ($i in 0..2) {
            $script:Three[$i].SetManifest | Should -Match ("disc {0} of 3" -f ($i + 1))
            foreach ($part in 1..3) {
                $script:Three[$i].SetManifest | Should -Match "part-$part\.bin"
            }
        }
    }

    It "does not let one disc's settings leak into the next" {
        # The build fills in defaults on the hashtable it is handed, so each
        # disc has to get its own copy or disc 1's answers become disc 2's.
        $script:Three[0].IconPath = 'CHANGED'
        $script:Three[1].IconPath | Should -Be 'C:\art\i.png'
    }

    It 'tells each disc which number it is, and how many there are' {
        @($script:Three | ForEach-Object { $_.DiscNum }) | Should -Be @(1, 2, 3)
        @($script:Three | ForEach-Object { $_.DiscOf }) | Should -Be @(3, 3, 3)
    }
}

Describe 'Refusing to build a set that would not make sense' -Tag 'Unit' {

    BeforeAll {
        $script:Quiet = { param($m) }
        function Fake-Game([string]$folder) {
            return @{ Folder = $folder; SetupExe = $null; Files = @(); Buttons = @('Install') }
        }
    }

    It 'will not put several games across a set' {
        # The 2026 disc sets did exactly this, packing whatever order the rows
        # sat in. Which games belong together is the person's call.
        $s = @{ Games = @((Fake-Game 'C:\a'), (Fake-Game 'C:\b')); Label = 'Two'; MediaKey = 'DVD5' }
        $r = Invoke-BuildDiscSet $s $script:Quiet
        $r.Ok | Should -BeFalse
        $r.Why | Should -Match 'one game'
        @($r.Isos).Count | Should -Be 0
    }

    It 'asks which disc is going to be burned before working out a set' {
        $g = Fake-Game 'C:\a'
        $g.Files = @([IO.FileInfo]'C:\a\setup.exe')
        $s = @{ Games = @($g); Label = 'One'; MediaKey = '' }
        $r = Invoke-BuildDiscSet $s $script:Quiet
        $r.Ok | Should -BeFalse
        $r.Why | Should -Match 'Choose the disc'
    }

    It 'says so when the game has no files at all' {
        $s = @{ Games = @((Fake-Game 'C:\a')); Label = 'Empty'; MediaKey = 'DVD5' }
        $r = Invoke-BuildDiscSet $s $script:Quiet
        $r.Ok | Should -BeFalse
        $r.Why | Should -Match 'no files'
    }
}

Describe 'Building a disc that carries part of a set' -Tag 'Build' {

    # The two changes a set needs from the ordinary build: stage somewhere of
    # its own, and copy only the files it was given. Tested on a real ISO with
    # small files rather than on a real set, because what is in question is the
    # filtering, not the arithmetic.

    BeforeAll {
        $script:SetSrc = Join-Path $script:Sandbox 'setsrc'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:SetSrc 'data') | Out-Null
        foreach ($n in 'setup.exe', 'part-1.bin', 'part-2.bin') {
            Set-Content (Join-Path $script:SetSrc $n) "contents of $n" -Encoding Ascii -NoNewline
        }
        Set-Content (Join-Path $script:SetSrc 'data\textures.pak') 'pak' -Encoding Ascii -NoNewline

        $script:SetGame = @{
            Folder = $script:SetSrc; SetupExe = $null
            Files = @(Get-ChildItem -Recurse -File $script:SetSrc)
            Buttons = @('Install'); Name = 'Split Game'
            # Source is what tells the build that a folder of game files keeps
            # its subfolders, where a GOG download has no shape to keep. Left
            # out of this fixture at first, which made the test claim the build
            # had flattened a subfolder when it was doing exactly as told.
            Source = 'Files'
        }
        $script:SetOut = Join-Path $script:Sandbox 'setout'
        New-Item -ItemType Directory -Force -Path $script:SetOut | Out-Null

        # Disc 2 of an imagined pair: the installer stays behind on disc 1.
        $keep = @{}
        foreach ($f in $script:SetGame.Files) {
            if ($f.Name -in 'part-2.bin', 'textures.pak') { $keep[$f.FullName] = $true }
        }
        $s = New-BuildSettings -Games @($script:SetGame) -Label 'Split Game D2' -OutDir $script:SetOut -Checksums
        $s.StageDir    = Join-Path $script:SetOut 'disc D2'
        $s.OnlyFiles   = $keep
        $s.SetManifest = "DiscWright disc set`r`nthis is disc 2`r`n"
        $script:SetIso = Invoke-Build $s $script:LogSink
        $script:SetStage = $s.StageDir
    }

    It 'stages into the folder it was told to, not the usual one' {
        Test-Path $script:SetStage | Should -BeTrue
        Test-Path (Join-Path $script:SetOut 'disc') | Should -BeFalse
    }

    It 'copies only the files it was given' {
        $got = @(Get-ChildItem -Recurse -File $script:SetStage |
                 ForEach-Object { $_.Name } | Sort-Object)
        $got | Should -Not -Contain 'setup.exe'
        $got | Should -Not -Contain 'part-1.bin'
        $got | Should -Contain 'part-2.bin'
        $got | Should -Contain 'textures.pak'
    }

    It 'keeps the shape of the folders it does copy' {
        # A game that wants data\textures.pak beside its exe arrives broken if
        # the filter flattens what it keeps.
        Test-Path (Join-Path $script:SetStage 'data\textures.pak') | Should -BeTrue
    }

    It 'writes the set file onto the disc' {
        $f = Join-Path $script:SetStage (Get-DiscSetFileName)
        Test-Path $f | Should -BeTrue
        (Get-Content $f -Raw) | Should -Match 'this is disc 2'
    }

    It 'includes the set file in the checksum list, so it is covered too' {
        $sums = Get-Content (Join-Path $script:SetStage (Get-ChecksumFileName)) -Raw
        $sums | Should -Match ([regex]::Escape((Get-DiscSetFileName)))
    }

    It 'leaves the files it skipped out of the checksum list as well' {
        $sums = Get-Content (Join-Path $script:SetStage (Get-ChecksumFileName)) -Raw
        $sums | Should -Not -Match 'part-1\.bin'
        $sums | Should -Match 'part-2\.bin'
    }

    It 'writes an ISO named for this disc' {
        $script:SetIso | Should -Match 'Split Game D2\.iso$'
        Test-Path $script:SetIso | Should -BeTrue
    }

    It "puts only this disc's files inside the image" {
        if (-not $script:SevenZip) { Set-ItResult -Skipped -Because '7-Zip is not installed'; return }
        $inside = @(Get-IsoEntries -IsoPath $script:SetIso -SevenZip $script:SevenZip)
        ($inside -join '|') | Should -Not -Match 'part-1\.bin'
        ($inside -join '|') | Should -Match 'part-2\.bin'
    }
}


Describe 'The check the set file tells people to paste' -Tag 'Unit' {

    # Published text somebody will paste into PowerShell on a machine that has
    # never heard of DiscWright, so it is lifted out of the file and run rather
    # than read. A retyped copy would not prove anything about what the disc
    # actually says.

    BeforeAll {
        $script:SnipDir = Join-Path $script:Sandbox 'snippet'
        New-Item -ItemType Directory -Force -Path (Join-Path $script:SnipDir 'docs') | Out-Null
        Set-Content (Join-Path $script:SnipDir 'setup.exe') 'installer' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:SnipDir 'docs\manual.pdf') 'the manual' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:SnipDir 'part-1.bin') 'first part' -Encoding Ascii -NoNewline

        $ent = @()
        foreach ($rel in 'setup.exe', 'docs\manual.pdf', 'part-1.bin') {
            $f = Join-Path $script:SnipDir $rel
            $ent += , @{ Disc = $(if ($rel -eq 'part-1.bin') { 2 } else { 1 }); Rel = $rel
                         Bytes = (Get-Item $f).Length; Sha256 = (Get-FileSha256 $f) }
        }
        Set-Content (Join-Path $script:SnipDir (Get-DiscSetFileName)) `
            (New-DiscSetManifest $ent 'SNIPPET TEST' 1 2) -Encoding Ascii -NoNewline

        # Out of the file, between the line that starts it and the lone brace
        # that ends it, with the two-space indent taken off.
        $all = (Get-Content (Join-Path $script:SnipDir (Get-DiscSetFileName)) -Raw) -split "`r`n"
        $from = ($all | Select-String -SimpleMatch "Get-Content '$(Get-DiscSetFileName)'").LineNumber - 1
        $to = $from
        while ($all[$to].Trim() -ne '}') { $to++ }
        $script:Snippet = (($all[$from..$to]) | ForEach-Object { $_ -replace '^  ', '' }) -join "`n"
    }

    It 'is a complete, parseable piece of PowerShell' {
        { [scriptblock]::Create($script:Snippet) } | Should -Not -Throw
        $script:Snippet | Should -Match 'Get-FileHash'
    }

    It 'says nothing at all when every file is there and right' {
        Push-Location $script:SnipDir
        try { $out = & ([scriptblock]::Create($script:Snippet)) } finally { Pop-Location }
        $out | Should -BeNullOrEmpty
    }

    It 'names a file that is not there, and which disc it was on' {
        $gone = Join-Path $script:SnipDir 'docs\manual.pdf'
        $keep = Get-Content $gone -Raw
        Remove-Item $gone -Force
        Push-Location $script:SnipDir
        try { $out = @(& ([scriptblock]::Create($script:Snippet))) } finally { Pop-Location }
        Set-Content $gone $keep -Encoding Ascii -NoNewline
        ($out -join '|') | Should -Match 'MISSING \(disc 1\)'
        ($out -join '|') | Should -Match 'docs/manual\.pdf'
    }

    It 'catches a file that grew, before bothering to hash it' {
        $f = Join-Path $script:SnipDir 'part-1.bin'
        Set-Content $f 'first part plus junk' -Encoding Ascii -NoNewline
        Push-Location $script:SnipDir
        try { $out = @(& ([scriptblock]::Create($script:Snippet))) } finally { Pop-Location }
        Set-Content $f 'first part' -Encoding Ascii -NoNewline
        ($out -join '|') | Should -Match 'WRONG SIZE \(disc 2\)'
    }

    It 'catches a file changed without changing size, which the size cannot' {
        # The reason the hashes are in the file at all. A disc read error or a
        # bad cable changes bytes, not length.
        $f = Join-Path $script:SnipDir 'setup.exe'
        Set-Content $f 'INSTALLER' -Encoding Ascii -NoNewline
        Push-Location $script:SnipDir
        try { $out = @(& ([scriptblock]::Create($script:Snippet))) } finally { Pop-Location }
        Set-Content $f 'installer' -Encoding Ascii -NoNewline
        ($out -join '|') | Should -Match 'DAMAGED \(disc 1\)'
    }
}


Describe 'Where the manual and the extras go in a set' -Tag 'Unit' {

    # The music rides every disc, because every disc has its own menu to play
    # it, and it is already counted in the overhead. The manual, the extras and
    # the loose files are different: one copy for the set.
    #
    # The form has always treated them as overhead, and the comment on that code
    # says the "on every disc" and "on disc 1 alone" split was removed as a
    # distinction only a set could have. A set exists again, so it is back - on
    # the LAST disc, where first fit leaves the slack.

    BeforeAll {
        function X([string]$rel, [double]$gb) { @{ Rel = $rel; Bytes = [double]($gb * 1GB); Path = "C:\src\$rel" } }
        $script:Pay = @((X 'setup.exe' 0.96), (X 'p1.bin' 3.99), (X 'p2.bin' 3.99),
                        (X 'p3.bin' 3.99), (X 'p4.bin' 1.20))
        $script:Cap = Get-MediaCapacity 'DVD9'
    }

    It 'says nobody carries them when there are none' {
        (Get-DiscSetPlan $script:Pay $script:Cap 6MB 0).ExtrasDisc | Should -Be 0
    }

    It 'puts them on the last disc when they fit the slack there' {
        $p = Get-DiscSetPlan $script:Pay $script:Cap 6MB 1GB
        @($p.Discs).Count | Should -Be 3
        $p.ExtrasDisc | Should -Be 3
    }

    It 'does not add a disc it did not need' {
        # The whole reason for choosing the last disc over the first.
        $bare = Get-DiscSetPlan $script:Pay $script:Cap 6MB 0
        $with = Get-DiscSetPlan $script:Pay $script:Cap 6MB 1GB
        @($with.Discs).Count | Should -Be @($bare.Discs).Count
    }

    It 'gives them a disc of their own when they do not fit the slack' {
        $p = Get-DiscSetPlan $script:Pay $script:Cap 6MB 4GB
        @($p.Discs).Count | Should -Be 4
        $p.ExtrasDisc | Should -Be 4
        @($p.Discs[3].Files).Count | Should -Be 0
    }

    It 'refuses extras no disc could hold, and says what they came to' {
        $p = Get-DiscSetPlan $script:Pay $script:Cap 6MB 9GB
        $p.Ok | Should -BeFalse
        $p.Why | Should -Match 'manual and extras'
        $p.Why | Should -Match 'larger disc'
    }

    It 'still answers with a disc when there is nothing but extras' {
        $p = Get-DiscSetPlan @() $script:Cap 6MB 1GB
        $p.Ok | Should -BeTrue
        @($p.Discs).Count | Should -Be 1
        $p.ExtrasDisc | Should -Be 1
    }

    It 'leaves them off every disc except the one that carries them' {
        $p = Get-DiscSetPlan $script:Pay $script:Cap 6MB 1GB
        $ent = @()
        foreach ($d in $p.Discs) {
            foreach ($f in $d.Files) {
                $ent += , @{ Disc = $d.Number; Rel = $f.Rel; Bytes = $f.Bytes; Sha256 = ('a' * 64) }
            }
        }
        $s = @{ Label = 'Big Game'; OutDir = 'C:\out'; Games = @(); MediaKey = 'DVD9'
                ManualPath = 'C:\art\manual.pdf'; ExtrasPath = 'C:\art\extras'
                ExtraItems = @('C:\art\readme.txt'); IconPath = 'C:\art\i.png'; BgPath = 'C:\art\b.png' }
        $steps = @(Get-DiscSetSteps $s $p $ent)
        for ($i = 0; $i -lt $steps.Count; $i++) {
            if (($i + 1) -eq $p.ExtrasDisc) {
                $steps[$i].ManualPath | Should -Be 'C:\art\manual.pdf'
                $steps[$i].ExtrasPath | Should -Be 'C:\art\extras'
                @($steps[$i].ExtraItems).Count | Should -Be 1
            } else {
                $steps[$i].ManualPath | Should -BeNullOrEmpty
                $steps[$i].ExtrasPath | Should -BeNullOrEmpty
                @($steps[$i].ExtraItems).Count | Should -Be 0
            }
        }
    }

    It 'keeps the music on every disc, because every disc has a menu' {
        $s = @{ Label = 'Big Game'; OutDir = 'C:\out'; Games = @(); MediaKey = 'DVD9'
                MusicFile = 'C:\art\tune.mp3'; IconPath = 'C:\art\i.png'; BgPath = 'C:\art\b.png' }
        $p = Get-DiscSetPlan $script:Pay $script:Cap 6MB 0
        $ent = @()
        foreach ($d in $p.Discs) { foreach ($f in $d.Files) { $ent += , @{ Disc = $d.Number; Rel = $f.Rel; Bytes = $f.Bytes; Sha256 = ('a' * 64) } } }
        foreach ($st in (Get-DiscSetSteps $s $p $ent)) {
            $st.MusicFile | Should -Be 'C:\art\tune.mp3'
        }
    }
}


Describe 'The set-mode code in the menu, run by a real script engine' -Tag 'Unit' {

    # The menu is JScript in an HTA running in IE7 mode, and nothing in the
    # PowerShell suite can execute it. Asserting on the text of the generated
    # file would only prove the text was generated.
    #
    # So the block between the two markers is lifted out of the menu this build
    # produces and run under cscript, which has the same JScript engine and a
    # real FileSystemObject. What decides whether a set is complete is therefore
    # the code that ships, against a folder on disk.

    BeforeAll {
        $script:JsDir = Join-Path $script:Sandbox 'jsset'
        $script:JsDisc = Join-Path $script:JsDir 'disc'
        $script:JsRest = Join-Path $script:JsDir 'restore'
        New-Item -ItemType Directory -Force -Path $script:JsDisc | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $script:JsRest 'data') | Out-Null

        # A set of three discs. Disc 2 is the one we are pretending to be in.
        $script:JsEntries = @(
            @{ Disc = 1; Rel = 'setup.exe';          Bytes = [double]9; Sha256 = ('a' * 64) }
            @{ Disc = 2; Rel = 'part-1.bin';         Bytes = [double]9; Sha256 = ('b' * 64) }
            @{ Disc = 2; Rel = 'data\textures.pak';  Bytes = [double]9; Sha256 = ('c' * 64) }
            @{ Disc = 3; Rel = 'part-2.bin';         Bytes = [double]9; Sha256 = ('d' * 64) }
        )
        Set-Content (Join-Path $script:JsDisc (Get-DiscSetFileName)) `
            (New-DiscSetManifest $script:JsEntries 'Split Game' 2 3) -Encoding Ascii -NoNewline

        # The real menu for a disc in a set, then the block out of it.
        $hta = Join-Path $script:JsDir 'menu.hta'
        New-MenuHta @{ GameName='Split Game D2'
                       Games=@(@{ n='Split Game'; m='split'; s='setup.exe'; man=''; ext=''; a=@() })
                       Buttons=@('Install','Exit'); MusicFile=''; ManualFile=''; PanelSide='Right'
                       IconName='i.ico'; WindowBorder=$true; ButtonStyle='Minimal'; ShowCaption=$true
                       DiscNum=2; DiscOf=3; SetLabel='Split Game' } $hta
        $all = Get-Content $hta
        $from = ($all | Select-String -SimpleMatch 'disc set: begin').LineNumber
        $to   = ($all | Select-String -SimpleMatch 'disc set: end').LineNumber - 2
        $script:JsBlock = ($all[$from..$to]) -join "`r`n"

        function Invoke-SetJs([string]$restoreDir, [string]$tail) {
            # cscript is given the block exactly as the menu carries it, with
            # only the globals the menu would have supplied around it.
            $js = @()
            $js += 'var fso = new ActiveXObject("Scripting.FileSystemObject");'
            $js += 'var root = ' + (ConvertTo-Json $script:JsDisc) + ';'
            $js += 'var SETFILE = ' + (ConvertTo-Json (Get-DiscSetFileName)) + ';'
            $js += 'var SET = {n:2, of:3, label:"Split Game"};'
            $js += $script:JsBlock
            $js += 'var DIR = ' + (ConvertTo-Json $restoreDir) + ';'
            $js += $tail
            $f = Join-Path $script:JsDir 'harness.js'
            Set-Content -LiteralPath $f -Value ($js -join "`r`n") -Encoding Ascii
            $out = & cscript.exe //Nologo //E:JScript $f 2>&1
            return (@($out) -join "`n")
        }
    }

    It 'reads every row of the set file, whatever the disc' {
        $r = Invoke-SetJs $script:JsRest 'WScript.Echo("rows=" + setRows().length);'
        $r | Should -Match 'rows=4'
    }

    It 'keeps a name that has a folder in it, turning the slashes round' {
        $r = Invoke-SetJs $script:JsRest @'
var rows = setRows();
for (var i = 0; i < rows.length; i++) {
  if (rows[i].f.indexOf("textures") >= 0) { WScript.Echo("rel=" + rows[i].f); }
}
WScript.Echo("built=" + setPath("C:" + String.fromCharCode(92) + "x", "data/textures.pak"));
'@
        $r | Should -Match 'rel=data/textures\.pak'
        $r | Should -Match ([regex]::Escape('built=C:\x\data\textures.pak'))
    }

    It 'counts an empty folder as nothing present and every disc still needed' {
        $empty = Join-Path $script:JsDir 'empty'
        New-Item -ItemType Directory -Force -Path $empty | Out-Null
        $r = Invoke-SetJs $empty 'var s=setScan(DIR); WScript.Echo("ok="+s.ok+" missing="+s.missing+" need="+s.need+" complete="+s.complete);'
        $r | Should -Match 'ok=0 missing=4'
        $r | Should -Match 'need=1, 2, 3'
        $r | Should -Match 'complete=false'
    }

    It 'counts what is there and names only the discs still missing' {
        # Disc 2's two files copied, disc 1 and 3 not yet.
        Set-Content (Join-Path $script:JsRest 'part-1.bin') 'nine char' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:JsRest 'data\textures.pak') 'nine char' -Encoding Ascii -NoNewline
        $r = Invoke-SetJs $script:JsRest 'var s=setScan(DIR); WScript.Echo("ok="+s.ok+" missing="+s.missing+" need="+s.need);'
        $r | Should -Match 'ok=2 missing=2'
        $r | Should -Match 'need=1, 3'
    }

    It 'calls a half-copied file bad rather than present' {
        # The whole reason the sizes are in the set file.
        Set-Content (Join-Path $script:JsRest 'part-1.bin') 'short' -Encoding Ascii -NoNewline
        $r = Invoke-SetJs $script:JsRest 'var s=setScan(DIR); WScript.Echo("ok="+s.ok+" bad="+s.bad+" need="+s.need);'
        Set-Content (Join-Path $script:JsRest 'part-1.bin') 'nine char' -Encoding Ascii -NoNewline
        $r | Should -Match 'bad=1'
        $r | Should -Match 'need=1, 2, 3'
    }

    It 'says complete only when every file in the set is there and the right size' {
        Set-Content (Join-Path $script:JsRest 'setup.exe') 'nine char' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:JsRest 'part-2.bin') 'nine char' -Encoding Ascii -NoNewline
        $r = Invoke-SetJs $script:JsRest 'var s=setScan(DIR); WScript.Echo("complete="+s.complete+" ok="+s.ok+" sentence="+setSentence(s));'
        $r | Should -Match 'complete=true'
        $r | Should -Match 'ok=4'
        $r | Should -Match 'All 4 files are here'
    }

    It 'proposes a folder named for the game, on a drive that exists' {
        $r = Invoke-SetJs $script:JsRest 'WScript.Echo("dir=" + setDefaultDir());'
        $r | Should -Match 'dir=[A-Z]:'
        $r | Should -Match ([regex]::Escape('DiscWright restore'))
        $r | Should -Match 'Split Game'
    }

    It 'survives a set file that is not there at all' {
        # A disc whose set file was deleted must not throw: it has to say so.
        $bare = Join-Path $script:JsDir 'bare'
        New-Item -ItemType Directory -Force -Path $bare | Out-Null
        $js = @('var fso = new ActiveXObject("Scripting.FileSystemObject");',
                'var root = ' + (ConvertTo-Json $bare) + ';',
                'var SETFILE = ' + (ConvertTo-Json (Get-DiscSetFileName)) + ';',
                'var SET = {n:1, of:2, label:"Gone"};',
                $script:JsBlock,
                'var s = setScan(' + (ConvertTo-Json $bare) + ');',
                'WScript.Echo("rows=" + setRows().length + " total=" + s.total);',
                'WScript.Echo("sentence=" + setSentence(s));') -join "`r`n"
        $f = Join-Path $script:JsDir 'bare.js'
        Set-Content -LiteralPath $f -Value $js -Encoding Ascii
        $out = (@(& cscript.exe //Nologo //E:JScript $f 2>&1) -join "`n")
        $out | Should -Match 'rows=0 total=0'
        $out | Should -Match 'list of files is missing'
    }
}


Describe 'What a set disc does when asked to put the game back' -Tag 'Unit' {

    # The same arrangement as the scan tests: the block out of the real menu,
    # run under cscript. These cover the parts that decide WHAT to do, which is
    # where quoting and path joining go wrong. Pressing the buttons needs a
    # window and belongs to the window suite.

    BeforeAll {
        $script:AcDir  = Join-Path $script:Sandbox 'jsact'
        $script:AcDisc = Join-Path $script:AcDir 'disc'
        $script:AcRest = Join-Path $script:AcDir 'put back here'   # a space, on purpose
        New-Item -ItemType Directory -Force -Path (Join-Path $script:AcDisc 'data') | Out-Null
        New-Item -ItemType Directory -Force -Path $script:AcRest | Out-Null

        $ent = @(
            @{ Disc = 1; Rel = 'setup.exe';         Bytes = [double]9; Sha256 = ('a' * 64) }
            @{ Disc = 2; Rel = 'part-1.bin';        Bytes = [double]9; Sha256 = ('b' * 64) }
            @{ Disc = 2; Rel = 'data\textures.pak'; Bytes = [double]9; Sha256 = ('c' * 64) }
        )
        Set-Content (Join-Path $script:AcDisc (Get-DiscSetFileName)) `
            (New-DiscSetManifest $ent 'Split Game' 2 2) -Encoding Ascii -NoNewline

        $hta = Join-Path $script:AcDir 'menu.hta'
        New-MenuHta @{ GameName='Split Game D2'
                       Games=@(@{ n='Split Game'; m='split'; s='setup.exe'; man=''; ext=''; a=@() })
                       Buttons=@('Install','Exit'); MusicFile=''; ManualFile=''; PanelSide='Right'
                       IconName='i.ico'; WindowBorder=$true; ButtonStyle='Minimal'; ShowCaption=$true
                       DiscNum=2; DiscOf=2; SetLabel='Split Game' } $hta
        $all = Get-Content $hta
        $from = ($all | Select-String -SimpleMatch 'disc set: begin').LineNumber
        $to   = ($all | Select-String -SimpleMatch 'disc set: end').LineNumber - 2
        $script:AcBlock = ($all[$from..$to]) -join "`r`n"

        function Invoke-ActJs([string]$tail) {
            $js = @('var fso = new ActiveXObject("Scripting.FileSystemObject");',
                    'var root = ' + (ConvertTo-Json $script:AcDisc) + ';',
                    'var SETFILE = ' + (ConvertTo-Json (Get-DiscSetFileName)) + ';',
                    'var SET = {n:2, of:2, label:"Split Game"};',
                    $script:AcBlock,
                    'var DIR = ' + (ConvertTo-Json $script:AcRest) + ';',
                    $tail) -join "`r`n"
            $f = Join-Path $script:AcDir 'act.js'
            Set-Content -LiteralPath $f -Value $js -Encoding Ascii
            return ((@(& cscript.exe //Nologo //E:JScript $f 2>&1) -join "`n"))
        }
    }

    It 'knows which files are its own share' {
        $r = Invoke-ActJs 'var m=setMine(); WScript.Echo("mine="+m.length+" bytes="+setMineBytes());'
        $r | Should -Match 'mine=2'
        $r | Should -Match 'bytes=18'
    }

    It 'quotes every path in the copy command, so a folder with a space survives' {
        # The restore folder in this test has a space in it on purpose. An
        # unquoted robocopy argument would take the first word and fail.
        $r = Invoke-ActJs 'WScript.Echo("cmd=" + setCopyCmd(root, DIR, "part-1.bin"));'
        $r | Should -Match 'cmd=robocopy "'
        $r | Should -Match ([regex]::Escape('put back here'))
        # Three quoted arguments: from, to, and the file name on its own.
        ([regex]::Matches($r, '"')).Count | Should -BeGreaterOrEqual 6
    }

    It 'copies a file in a subfolder into the same subfolder, not the root' {
        $r = Invoke-ActJs 'WScript.Echo("cmd=" + setCopyCmd(root, DIR, "data/textures.pak"));'
        $r | Should -Match ([regex]::Escape('disc\data'))
        $r | Should -Match ([regex]::Escape('put back here\data'))
        $r | Should -Match '"textures\.pak"'
    }

    It 'makes every folder on the way down, not just the last' {
        $deep = Join-Path $script:AcDir 'a\b\c'
        $r = Invoke-ActJs ('WScript.Echo("made=" + setMakeDir(' + (ConvertTo-Json $deep) + '));')
        $r | Should -Match 'made=true'
        Test-Path $deep | Should -BeTrue
    }

    It 'lists only the files the set file names when asked to delete' {
        # The safety property. Anything else in that folder is somebody else's.
        # The subfolder has to exist before a file can be put in it: robocopy
        # makes it on a real copy, and nothing here has run robocopy.
        New-Item -ItemType Directory -Force -Path (Join-Path $script:AcRest 'data') | Out-Null
        Set-Content (Join-Path $script:AcRest 'part-1.bin') 'nine char' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:AcRest 'data\textures.pak') 'nine char' -Encoding Ascii -NoNewline
        Set-Content (Join-Path $script:AcRest 'my tax return.pdf') 'not yours' -Encoding Ascii -NoNewline
        $r = Invoke-ActJs @'
var l = setDeleteList(DIR);
WScript.Echo("files=" + l.files.length);
for (var i = 0; i < l.files.length; i++) { WScript.Echo("rm=" + l.files[i]); }
'@
        $r | Should -Match 'files=2'
        $r | Should -Match 'part-1\.bin'
        $r | Should -Match 'textures\.pak'
        $r | Should -Not -Match 'tax return'
    }

    It 'leaves a file it never put there alone, even with the same name as a folder' {
        $r = Invoke-ActJs 'var l=setDeleteList(DIR); WScript.Echo("dirs=" + l.dirs.length + " first=" + (l.dirs.length?l.dirs[0]:""));'
        # Only the data subfolder the set filled, never the restore folder itself.
        $r | Should -Match 'dirs=1'
        $r | Should -Match ([regex]::Escape('put back here\data'))
        $r | Should -Not -Match ([regex]::Escape('first=' + $script:AcRest) + '$')
    }

    It 'measures the room needed against the drive being copied to' {
        $r = Invoke-ActJs 'var k=setRoomFor(DIR); WScript.Echo("need="+k.need+" ok="+k.ok);'
        $r | Should -Match 'need=18'
        $r | Should -Match 'ok=true'
    }

    It 'does not claim there is no room when the drive cannot be read' {
        # An unreadable drive must not block a copy that would have worked.
        $r = Invoke-ActJs 'var k=setRoomFor("\\\\nowhere\\share\\x"); WScript.Echo("ok=" + k.ok + " free=" + k.free);'
        $r | Should -Match 'ok=true'
        $r | Should -Match 'free=-1'
    }
}

Describe 'The test file is shaped the way Pester needs' -Tag 'Unit' {

    # An It inside another It is valid PowerShell and meaningless to Pester: the
    # inner one never runs, the outer one reports nothing wrong, and the whole
    # Describe is recorded as a block error with no failed tests. That happened
    # here, and the runner called the run "all good" while nine tests sat out.
    #
    # The runner now catches it after the fact. This catches it at the source,
    # which is cheaper than reading a NotRun count and noticing it moved by two.

    BeforeAll {
        $script:SuiteAst = [System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $PSScriptRoot 'DiscWright.Tests.ps1'), [ref]$null, [ref]$null)
        function Get-Commands([string]$name) {
            return @($script:SuiteAst.FindAll({
                param($n) $n -is [System.Management.Automation.Language.CommandAst] -and
                          $n.GetCommandName() -eq $name }, $true))
        }
    }

    It 'has no test nested inside another test' {
        $bad = @()
        foreach ($it in (Get-Commands 'It')) {
            $p = $it.Parent
            while ($p) {
                if ($p -is [System.Management.Automation.Language.CommandAst] -and
                    $p.GetCommandName() -eq 'It') {
                    $bad += ($it.CommandElements[1].Extent.Text)
                    break
                }
                $p = $p.Parent
            }
        }
        $bad | Should -BeNullOrEmpty -Because 'a nested It never runs and nothing reports it'
    }

    It 'has no setup block nested inside a test' {
        # Same class of mistake: a BeforeAll inside an It runs nothing useful.
        $bad = @()
        foreach ($name in 'BeforeAll', 'BeforeEach', 'AfterAll', 'AfterEach') {
            foreach ($b in (Get-Commands $name)) {
                $p = $b.Parent
                while ($p) {
                    if ($p -is [System.Management.Automation.Language.CommandAst] -and
                        $p.GetCommandName() -eq 'It') { $bad += $name; break }
                    $p = $p.Parent
                }
            }
        }
        $bad | Should -BeNullOrEmpty
    }

    It 'finds the tests it claims to be checking' {
        # Without this the two guards above pass on an empty list for ever if the
        # AST walk ever stops matching.
        (Get-Commands 'It').Count | Should -BeGreaterThan 400
        (Get-Commands 'Describe').Count | Should -BeGreaterThan 40
    }
}


Describe 'Asking for a disc set from the window' -Tag 'Unit' {

    # The setting has to survive the whole round trip, which is what the
    # structural guards in this file already insist on for every other setting.
    # These add the parts those cannot see: the schema number, what an older
    # project reads back as, and that asking for a set actually builds one.

    It 'is version 13 of the project file' {
        $src = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1') -Raw
        $src | Should -Match 'Version\s+=\s+13'
        $src | Should -Match 'Version 13 adds DiscSet'
    }

    It 'reads back as off in a project written before it existed' {
        # Those versions refused a payload past the biggest disc outright, so
        # reopening one of their projects must not quietly turn one disc into
        # five. Off is the only answer that describes what they did.
        $dir = Join-Path $script:Sandbox 'v12project'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $old = Join-Path $dir 'discproject.json'
        @{ Version = 12; Games = @(); Label = 'Old'; Checksums = $true } |
            ConvertTo-Json -Depth 6 | Set-Content $old -Encoding UTF8
        # Import-Project is the reader with no window attached. Open-Project is
        # the one the button calls, and it clears the log box.
        $p = Import-Project $old
        [bool]$p.DiscSet | Should -BeFalse
        # And the setting beside it still reads back as it did.
        [bool]$p.Checksums | Should -BeTrue
    }

    It 'survives being written and read back' {
        $dir = Join-Path $script:Sandbox 'setproject'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Save-Project @{ Games = @(); Label = 'Set Test'; DiscSet = $true } $dir
        [bool](Import-Project (Join-Path $dir 'discproject.json')).DiscSet | Should -BeTrue
    }
}

Describe 'Building a real set from end to end' -Tag 'Build' {

    # The whole way through: a payload that will not fit one disc, asked for as
    # a set, producing two ISOs that each hold their own share and both carry
    # the same list of the whole thing.
    #
    # A CD is the smallest disc DiscWright offers, so two files of 400 MB are
    # the cheapest honest way to need two of them. It writes ~800 MB, which is
    # why it is tagged Build and not Unit.

    BeforeAll {
        $script:E2E = Join-Path $script:Sandbox 'e2e'
        $script:E2ESrc = Join-Path $script:E2E 'src'
        $script:E2EOut = Join-Path $script:E2E 'out'
        New-Item -ItemType Directory -Force -Path $script:E2ESrc | Out-Null
        New-Item -ItemType Directory -Force -Path $script:E2EOut | Out-Null

        # Two files that cannot share a CD, written as sparse-ish blocks rather
        # than 800 MB of random, which would dominate the run.
        foreach ($n in 'part-1.bin', 'part-2.bin') {
            $fs = [IO.File]::Create((Join-Path $script:E2ESrc $n))
            $fs.SetLength(400MB)
            $fs.Close()
        }
        $script:E2EGame = @{
            Folder = $script:E2ESrc; SetupExe = $null; Source = 'Files'
            Files = @(Get-ChildItem -File $script:E2ESrc)
            Buttons = @('Install'); Name = 'Two Disc Game'
        }
        $s = New-BuildSettings -Games @($script:E2EGame) -Label 'Two Disc Game' -OutDir $script:E2EOut
        $s.MediaKey = 'CD'
        $s.DiscSet  = $true
        $script:E2EResult = Invoke-BuildDiscSet $s $script:LogSink
    }

    It 'says it worked, and says how many discs' {
        $script:E2EResult.Ok | Should -BeTrue
        $script:E2EResult.Discs | Should -Be 2
        @($script:E2EResult.Isos).Count | Should -Be 2
    }

    It 'writes an ISO per disc, each named for its own disc' {
        foreach ($n in 1, 2) {
            $iso = Join-Path $script:E2EOut "Two Disc Game D$n.iso"
            Test-Path $iso | Should -BeTrue -Because "disc $n should have been written"
            (Get-Item $iso).Length | Should -BeGreaterThan 100MB
        }
    }

    It 'puts each file on exactly one of the discs' {
        $d1 = @(Get-ChildItem -Recurse -File (Join-Path $script:E2EOut 'disc D1') | ForEach-Object { $_.Name })
        $d2 = @(Get-ChildItem -Recurse -File (Join-Path $script:E2EOut 'disc D2') | ForEach-Object { $_.Name })
        $d1 | Should -Contain 'part-1.bin'
        $d1 | Should -Not -Contain 'part-2.bin'
        $d2 | Should -Contain 'part-2.bin'
        $d2 | Should -Not -Contain 'part-1.bin'
    }

    It 'puts the same whole-set list on both discs' {
        $a = Get-Content (Join-Path $script:E2EOut ('disc D1\' + (Get-DiscSetFileName))) -Raw
        $b = Get-Content (Join-Path $script:E2EOut ('disc D2\' + (Get-DiscSetFileName))) -Raw
        foreach ($t in $a, $b) {
            $t | Should -Match 'part-1\.bin'
            $t | Should -Match 'part-2\.bin'
            $t | Should -Match 'cannot install the game on its own'
        }
        $a | Should -Match 'disc 1 of 2'
        $b | Should -Match 'disc 2 of 2'
    }

    It 'gives the two discs different volume ids' {
        # Identical ids would show as two indistinguishable drives in Explorer.
        $v1 = Get-VolumeLabel 'Two Disc Game' 1 2
        $v2 = Get-VolumeLabel 'Two Disc Game' 2 2
        $v1 | Should -Not -Be $v2
        $v1.Length | Should -BeLessOrEqual 16
        $v2.Length | Should -BeLessOrEqual 16
    }

    It 'tells each menu which disc it is on' {
        foreach ($n in 1, 2) {
            $hta = Join-Path $script:E2EOut "disc D$n\AUTORUN\menu.hta"
            Test-Path $hta | Should -BeTrue
            (Get-Content $hta -Raw) | Should -Match "var SET=\{n:$n,of:2,"
        }
    }

    It 'refuses the same payload with no disc chosen' {
        $s = New-BuildSettings -Games @($script:E2EGame) -Label 'No Disc' -OutDir $script:E2EOut
        $s.DiscSet = $true
        $s.MediaKey = ''
        $r = Invoke-BuildDiscSet $s $script:LogSink
        $r.Ok | Should -BeFalse
        $r.Why | Should -Match 'Choose the disc'
    }
}

Describe 'What a restored set offers for a game that is not a GOG download' -Tag 'Unit' {

    # A GOG download is an installer and its parts, so a finished restore
    # installs. A folder of game files IS the game: it is played from the
    # folder, or just opened when nothing in it was picked to run.
    #
    # The set panel said "Install" for all three and then opened Explorer for
    # the last one. That is item 7 in a third place, and it was spotted by
    # being asked how a non-GOG game works rather than by any test here.

    BeforeAll {
        $script:VerbDir = Join-Path $script:Sandbox 'verbs'
        New-Item -ItemType Directory -Force -Path $script:VerbDir | Out-Null
        $hta = Join-Path $script:VerbDir 'menu.hta'
        New-MenuHta @{ GameName='Verb Test D1'
                       Games=@(@{ n='Verb Test'; m='verb'; s='setup.exe'; man=''; ext=''; a=@() })
                       Buttons=@('Install','Exit'); MusicFile=''; ManualFile=''; PanelSide='Right'
                       IconName='i.ico'; WindowBorder=$true; ButtonStyle='Minimal'; ShowCaption=$true
                       DiscNum=1; DiscOf=2; SetLabel='Verb Test' } $hta
        $all = Get-Content $hta
        $from = ($all | Select-String -SimpleMatch 'disc set: begin').LineNumber
        $to   = ($all | Select-String -SimpleMatch 'disc set: end').LineNumber - 2
        $block = ($all[$from..$to]) -join "`r`n"

        function Get-DoneVerb([string]$GameJs) {
            $js = @('var fso = new ActiveXObject("Scripting.FileSystemObject");',
                    'var root = ' + (ConvertTo-Json $script:VerbDir) + ';',
                    'var SETFILE = "Disc set.txt";',
                    'var SET = {n:1, of:2, label:"Verb Test"};',
                    $block,
                    'var g = ' + $GameJs + ';',
                    'WScript.Echo("verb=" + setDoneVerb(g));',
                    'WScript.Echo("tip=" + setDoneTip(g, "D:/here"));') -join "`r`n"
            $f = Join-Path $script:VerbDir 'verb.js'
            Set-Content -LiteralPath $f -Value $js -Encoding Ascii
            return ((@(& cscript.exe //Nologo //E:JScript $f 2>&1) -join "`n"))
        }
    }

    It 'installs a GOG download' {
        $r = Get-DoneVerb '{files:false, s:"setup_game.exe"}'
        $r | Should -Match 'verb=Install'
        $r | Should -Match 'tip=Run the installer from'
    }

    It 'plays a folder of game files that has something to run' {
        # Nothing to install: the folder is the game.
        $r = Get-DoneVerb '{files:true, s:"Game.exe"}'
        $r | Should -Match 'verb=Play'
        $r | Should -Match 'tip=Run the game from'
    }

    It 'opens the folder when nothing in it was picked to run' {
        # The "no installer" case, which is the one that used to say Install and
        # then open Explorer.
        $r = Get-DoneVerb '{files:true, s:""}'
        $r | Should -Match 'verb=Open Folder'
        $r | Should -Match 'tip=Open the rebuilt game in'
    }

    It 'says Install rather than nothing when the entry is missing' {
        $r = Get-DoneVerb 'null'
        $r | Should -Match 'verb=Install'
    }

    It 'does not promise an install in the status line either' {
        $hta = Join-Path $script:VerbDir 'menu.hta'
        (Get-Content $hta -Raw) | Should -Not -Match 'can be installed from that folder'
        (Get-Content $hta -Raw) | Should -Match 'ready in that folder'
    }
}

Describe 'The set panel does not dress as a button' -Tag 'Unit' {

    # The menu's buttons are one exact flat colour, and the GIF recorder finds
    # them by scanning three columns of the panel for it. An information box
    # painted the same colour was found as a fourth button, which would have
    # driven a click into a paragraph of text.
    #
    # The recorder is the cheap reason. The real one is that a person reads a
    # flat dark block in a column of flat dark blocks as something to press.

    BeforeAll {
        $script:MenuCss = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1') -Raw
    }

    It 'keeps the buttons on their own colour' {
        # If this changes, the recorder's scan colours change with it.
        $script:MenuCss | Should -Match '\.btn\{[^}]*background:#0a1519'
    }

    It 'paints the set information box something else' {
        $m = [regex]::Match($script:MenuCss, '\.setinfo\{[^}]*background:(#[0-9a-f]{6})')
        $m.Success | Should -BeTrue -Because 'the set panel must declare its own background'
        $m.Groups[1].Value | Should -Not -Be '#0a1519'
    }

    It 'stays clear of the colour the recorder scans for, hover included' {
        # The scan matches within eight per channel of either button state, so
        # being merely different is not enough.
        $m = [regex]::Match($script:MenuCss, '\.setinfo\{[^}]*background:#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})')
        $r = [Convert]::ToInt32($m.Groups[1].Value, 16)
        $g = [Convert]::ToInt32($m.Groups[2].Value, 16)
        $b = [Convert]::ToInt32($m.Groups[3].Value, 16)
        foreach ($btn in @(@(10, 21, 25), @(18, 36, 43))) {
            $near = ([Math]::Abs($r - $btn[0]) -le 8) -and
                    ([Math]::Abs($g - $btn[1]) -le 8) -and
                    ([Math]::Abs($b - $btn[2]) -le 8)
            $near | Should -BeFalse -Because "rgb($r,$g,$b) is within the scan tolerance of rgb($($btn -join ','))"
        }
    }
}


Describe 'No control on the form is laid out on top of another' -Tag 'Unit' {

    # The window is positioned in absolute pixels, and a new control put at a
    # free-looking x can land inside a neighbour that is wider than it reads.
    #
    # That happened: 'disc set' was placed at x=380 on the row of disc options,
    # where 'readable on Windows XP and older' is 240 wide and runs from 265 to
    # 505. The new box sat entirely inside it, every click went to the
    # neighbour, and the checkbox never ticked - so the window could not ask for
    # a disc set at all while everything behind it worked.
    #
    # The window suite's own overlap check could not see it. It allows one
    # rectangle to contain another, because a group box legitimately contains
    # its children, and full containment is exactly what this was.
    #
    # Read off the source rather than off a running window, so it fails on the
    # machine that made the change instead of on the one with a desktop.

    BeforeAll {
        $src = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1') -Raw

        $kind = @{}
        foreach ($m in [regex]::Matches($src, '\$(\w+)\s*=\s*New-Object System\.Windows\.Forms\.(\w+)')) {
            $kind[$m.Groups[1].Value] = $m.Groups[2].Value
        }
        $pos = @{}
        foreach ($m in [regex]::Matches($src, '\$(\w+)\.Location\s*=\s*New-Object System\.Drawing\.Point\((\d+),\s*(\d+)\)')) {
            $pos[$m.Groups[1].Value] = @([int]$m.Groups[2].Value, [int]$m.Groups[3].Value)
        }
        $dim = @{}
        foreach ($m in [regex]::Matches($src, '\$(\w+)\.Size\s*=\s*New-Object System\.Drawing\.Size\((\d+),\s*(\d+)\)')) {
            $dim[$m.Groups[1].Value] = @([int]$m.Groups[2].Value, [int]$m.Groups[3].Value)
        }

        # Only what is added straight to the main form. A control added to a
        # group box is positioned relative to that box, so its numbers cannot be
        # compared with these.
        $onForm = @()
        foreach ($m in [regex]::Matches($src, '\$form\.Controls\.Add\(\$(\w+)\)')) {
            $onForm += $m.Groups[1].Value
        }
        $onForm = @($onForm | Sort-Object -Unique)

        $script:Boxes = @()
        foreach ($n in $onForm) {
            if (-not $pos.ContainsKey($n) -or -not $dim.ContainsKey($n)) { continue }
            # A group box is a container, so things inside it are meant to be.
            if ($kind[$n] -eq 'GroupBox') { continue }
            $script:Boxes += , @{ Name = $n; Kind = [string]$kind[$n]
                                  X = $pos[$n][0]; Y = $pos[$n][1]
                                  W = $dim[$n][0]; H = $dim[$n][1] }
        }
    }

    It 'found the controls it is supposed to be checking' {
        # Without this the test below passes for ever on an empty list the day
        # the source stops matching these patterns.
        @($script:Boxes).Count | Should -BeGreaterThan 8
        @($script:Boxes | Where-Object { $_.Kind -eq 'CheckBox' }).Count | Should -BeGreaterThan 3
    }

    It 'has no two of them sharing a pixel' {
        $clashes = @()
        for ($i = 0; $i -lt $script:Boxes.Count; $i++) {
            for ($j = $i + 1; $j -lt $script:Boxes.Count; $j++) {
                $a = $script:Boxes[$i]; $b = $script:Boxes[$j]
                $overX = ($a.X -lt ($b.X + $b.W)) -and ($b.X -lt ($a.X + $a.W))
                $overY = ($a.Y -lt ($b.Y + $b.H)) -and ($b.Y -lt ($a.Y + $a.H))
                if ($overX -and $overY) {
                    $clashes += ('{0} ({1}..{2} x {3}..{4}) runs through {5} ({6}..{7} x {8}..{9})' -f
                        $a.Name, $a.X, ($a.X + $a.W), $a.Y, ($a.Y + $a.H),
                        $b.Name, $b.X, ($b.X + $b.W), $b.Y, ($b.Y + $b.H))
                }
            }
        }
        $clashes | Should -BeNullOrEmpty
    }

    It 'keeps the row of disc options clear of each other' {
        # The specific row the mistake was made on, named so a future reader
        # knows which one is crowded.
        $row = @($script:Boxes | Where-Object { $_.Y -ge 330 -and $_.Y -le 370 } | Sort-Object { $_.X })
        $row.Count | Should -BeGreaterThan 2
        for ($i = 1; $i -lt $row.Count; $i++) {
            $row[$i].X | Should -BeGreaterOrEqual ($row[$i-1].X + $row[$i-1].W) `
                -Because "$($row[$i].Name) starts before $($row[$i-1].Name) ends"
        }
    }
}

Describe 'The set panel fits the window, and says when it cannot tidy up' -Tag 'Unit' {

    # Two faults found by recording a demonstration of the feature, neither of
    # which any test here would have caught. Both are in the menu's JScript,
    # inside functions that need a document and a real window, so they cannot
    # be run headlessly the way the arithmetic further up can. These read the
    # source instead, which is weaker, and is why each one says what it is
    # guarding rather than only that a string is present.

    BeforeAll {
        $script:App = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1') -Raw
    }

    It 'takes the set panel out of the room the buttons get' {
        # setPanel shrinks buttons so they cannot run off a 480px window, but it
        # counted only <A> elements. The set panel puts a block of text above
        # them, so on a two-disc set Choose folder sat half over the bottom edge
        # and Exit was not drawn at all.
        $script:App | Should -Match 'var si=document\.getElementById\("setinfo"\)'
        $script:App | Should -Match 'var siH=si \? si\.offsetHeight\+12 : 0'
        $script:App | Should -Match 'var bh=46, gap=12, avail=440-capH-siH'
    }

    It 'counts the set panel when centring what is left' {
        # The same height has to be in the block being centred, or the panel is
        # centred as though the text were not there and rides high.
        $script:App | Should -Match 'var block=capH\+siH\+n\*bh'
    }

    It 'gives the set panel a handle to be measured by' {
        $script:App | Should -Match '<div class="setinfo" id="setinfo">'
    }

    It 'does not throw away a failed delete' {
        # Every removal was wrapped in a catch that said nothing. A real run
        # left two of four files behind, still held open by whatever had just
        # copied them, and the menu reported the folder as emptied.
        $script:App | Should -Match 'catch\(ex\)\{ failed\[failed\.length\]=list\.files\[i\]; \}'
        $script:App | Should -Match 'if\(failed\.length\)\{'
        $script:App | Should -Match 'could not be removed'
    }

    It 'still has the silent catches it is allowed to have' {
        # The sweep that removes empty folders afterwards may fail harmlessly:
        # a folder someone else put a file in is not this set's to delete. That
        # one stays quiet on purpose, so the guard above must not be read as
        # "no catch may ever be silent".
        $script:App | Should -Match 'if\(d\.Files\.Count===0&&d\.SubFolders\.Count===0\)'
    }
}

Describe 'Working out whether a set would help, before refusing' -Tag 'Unit' {

    # The build used to answer "too big for that disc" and stop there, leaving
    # the person to discover the tick box for themselves. Knowing a feature
    # exists should not be a condition of using it, so the button asks this
    # first and offers the set.
    #
    # Zero means "do not offer", and every reason for that is its own case
    # below: offering a set that would then be refused is worse than not
    # offering one.

    BeforeAll {
        $script:WouldDir = Join-Path $script:Sandbox 'would'
        New-Item -ItemType Directory -Force -Path $script:WouldDir | Out-Null
        function New-Sparse([string]$dir, [string]$name, [double]$mb) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            $fs = [IO.File]::Create((Join-Path $dir $name)); $fs.SetLength([long]($mb * 1MB)); $fs.Close()
        }
        # One game of two 400 MB files: too big for a CD, fine as two of them.
        $script:TwoDisc = Join-Path $script:WouldDir 'two_disc_game'
        New-Sparse $script:TwoDisc 'setup_two_disc_game.exe' 400
        New-Sparse $script:TwoDisc 'setup_two_disc_game-1.bin' 400
        # One game whose single file no CD could hold.
        $script:TooBig = Join-Path $script:WouldDir 'one_huge_game'
        New-Sparse $script:TooBig 'setup_one_huge_game.exe' 900

        function Would([string]$folder, [string]$media, [hashtable]$extra = @{}) {
            $s = @{ Games = @((Get-GameInfo $folder)); MediaKey = $media
                    IconPath = $script:Art; BgPath = $script:Art; Menu = $true }
            foreach ($k in $extra.Keys) { $s[$k] = $extra[$k] }
            return (Get-DiscSetWouldNeed $s)
        }
    }

    It 'says how many discs a payload that will not fit would take' {
        Would $script:TwoDisc 'CD' | Should -Be 2
    }

    It 'says one for a payload that fits, so nothing is offered' {
        # A set is not wrong here, it is just not worth asking about.
        Would $script:TwoDisc 'DVD5' | Should -Be 1
    }

    It 'offers nothing when a single file is bigger than the disc' {
        # A set cannot help, and offering one would end in a refusal.
        Would $script:TooBig 'CD' | Should -Be 0
    }

    It 'offers nothing when no disc has been chosen' {
        Would $script:TwoDisc '' | Should -Be 0
    }

    It 'offers nothing for several games' {
        $s = @{ Games = @((Get-GameInfo $script:TwoDisc), (Get-GameInfo $script:TooBig))
                MediaKey = 'CD'; IconPath = $script:Art; BgPath = $script:Art; Menu = $true }
        Get-DiscSetWouldNeed $s | Should -Be 0
    }

    It 'offers nothing when the extras alone are bigger than the disc' {
        $big = Join-Path $script:WouldDir 'bigextras'
        New-Sparse $big 'soundtrack.flac' 900
        Would $script:TwoDisc 'CD' @{ ExtrasPath = $big } | Should -Be 0
    }

    It 'counts the extras when they do fit' {
        # 800 MB of game plus 400 MB of extras needs one more CD than the game
        # alone, and the offer has to say the real number.
        $small = Join-Path $script:WouldDir 'smallextras'
        New-Sparse $small 'wallpapers.zip' 400
        (Would $script:TwoDisc 'CD' @{ ExtrasPath = $small }) | Should -BeGreaterThan 2
    }

    It 'offers nothing for a game with no files' {
        $empty = Join-Path $script:WouldDir 'empty_game'
        New-Item -ItemType Directory -Force -Path $empty | Out-Null
        $s = @{ Games = @(@{ Folder = $empty; SetupExe = $null; Files = @(); Source = 'Files' })
                MediaKey = 'CD'; IconPath = $script:Art; BgPath = $script:Art; Menu = $true }
        Get-DiscSetWouldNeed $s | Should -Be 0
    }
}

Describe 'Walking a set to the burner, one disc at a time' -Tag 'Unit' {

    # A set is only a set once every disc is burned, and the easy mistake is
    # burning them out of order or losing count. The build offers to walk them.
    #
    # It asks rather than detects, on purpose: DiscWright hands an ISO to
    # another program and that program says nothing back, so there is no way to
    # know a burn finished or worked. Reporting a set as burned when disc 2
    # never wrote would be worse than asking.
    #
    # The walk itself puts dialogs on the screen, so what can be checked here
    # is its shape and the sentences it leaves behind. The dialogs themselves
    # belong to the window suite.

    BeforeAll {
        $src = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1') -Raw
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$null, [ref]$null)
        $script:Walk = ($ast.FindAll({ param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $n.Name -eq 'Invoke-SetBurnWalk' }, $false) | Select-Object -First 1)
        $script:WalkText = [string]$script:Walk.Extent.Text
        $script:Src = $src
    }

    It 'exists, and the build calls it only for a set of more than one disc' {
        $script:Walk | Should -Not -BeNullOrEmpty
        $script:Src | Should -Match 'if \(\$s\.DiscSet -and @\(\$isos\)\.Count -gt 1\) \{'
    }

    It 'does nothing for a single disc' {
        # Called with one ISO it must return before asking anything at all: a
        # one-disc build is not a set and must not grow a burning interview.
        $script:WalkText | Should -Match 'if \(\$n -lt 2\) \{ return \}'
    }

    It 'asks which program once, not once per disc' {
        # Being asked before every disc is the kind of help nobody wants.
        ([regex]::Matches($script:WalkText, 'Select-IsoHandoff')).Count | Should -Be 1
    }

    It 'says it cannot tell when a burn has finished' {
        # The whole reason it asks. If this sentence goes, somebody will assume
        # the app knows, and a set reported as burned is a set that may not be.
        $script:WalkText | Should -Match 'cannot tell when it has'
    }

    It 'stops where it was told to stop, and says so' {
        # Every way out writes a line saying which disc it reached, because a
        # set half burned is something somebody comes back to tomorrow.
        $script:WalkText | Should -Match 'Stopped at disc \$d of \$n'
        $script:WalkText | Should -Match 'Stopped after disc \$d of \$n'
        $script:WalkText | Should -Match 'can be burned later'
    }

    It 'does not ask about the disc after the last one' {
        $script:WalkText | Should -Match 'if \(\$d -lt \$n\) \{'
    }

    It 'gives up quietly when there is nothing registered to open an ISO' {
        # Not an error: the ISOs are written and can be burned by hand.
        $script:WalkText | Should -Match 'Nothing on this machine is registered to open an ISO'
        $script:WalkText | Should -Match 'The burning files are not installed'
    }

    It 'tells the person where the discs are when they decline' {
        $script:WalkText | Should -Match 'Burn each one to its own disc, in order, and label them'
    }
}
