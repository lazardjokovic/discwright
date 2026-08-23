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
        param([array]$Games, [string]$Label, [string]$OutDir)
        return @{
            Games=$Games; Label=$Label; IconPath=$script:Art; IconIsIco=$false
            Menu=$true; BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$OutDir
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

Describe 'Test-ReservedDiscName' -Tag 'Unit' {

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

        It 'declares schema version 4' {
            $script:PJson.Version | Should -Be 4
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

    It 'stages both installers under Games' {
        @(Get-ChildItem (Join-Path $script:AoStage 'Games') -Directory).Count | Should -Be 2
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

    It 'greys Change... until there is something to be an add-on of' {
        # The standing rule: an option that cannot be used is not left clickable.
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

        # A stand-in for Star Wars: Empire at War Gold Pack - one installer, two
        # games. Nobody here owns that game, so this is a reconstruction; what
        # makes it worth trusting is that its SHAPE is copied from the four real
        # .info files on hand rather than invented:
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
        # What that manifest cannot give is the CONTENTS of the .info: it ships
        # inside the depot, and the depot answers 403 without an ownership token.
        # So the playTasks below are still the reconstructed part, and one thing
        # is still unconfirmed - whether the real file marks Forces of Corruption
        # with category "game". If it does not, playTasks drops it and Play goes
        # on launching only the base game.
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

    It 'finds the game and its patches side by side' -Skip:(-not (@($script:GogFolders | Where-Object { $_ -like '*hollow_knight*' }).Count)) {
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

        # And every installer gets its own folder on the disc.
        $folders = @(0..($entries.Count-1) | ForEach-Object { Get-DiscEntryFolder $entries $_ })
        @($folders | Sort-Object -Unique).Count | Should -Be $entries.Count
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
            'Get-Games', 'Get-MenuGames', 'Remove-GameEntry',
            'Set-GameEntries', 'Set-GameFolders', 'Set-GameFolder'
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
