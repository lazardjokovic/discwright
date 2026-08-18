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

        It 'declares schema version 2' {
            $script:PJson.Version | Should -Be 2
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
            $hta = Get-Content -Raw (Join-Path $script:OneStage 'AUTORUN\menu.hta')
            $hta | Should -Match ('var SETUP="' + [regex]::Escape($script:One.SetupExe.Name) + '"')
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

        It 'points the menu into the first game folder' {
            $hta = Get-Content -Raw (Join-Path $script:ManyStage 'AUTORUN\menu.hta')
            $hta | Should -Match 'var SETUP="Games\\\\01 - '
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
