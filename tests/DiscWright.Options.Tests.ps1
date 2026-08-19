<#
    Every option on the form, against the disc it produces.

    These are the settings people actually touch - the music, the manual, the
    extras, which buttons the menu has, which side they sit on - and until now
    not one of them was asserted anywhere. The disc either carries what was asked
    for or it does not, and that is only visible on the finished disc.

    WHY THESE ARE NOT WINDOW TESTS
    The question each one asks is "did this land on the disc", which is a build
    question. Answered here it costs a few seconds and says exactly which file is
    missing. Answered by clicking checkboxes it costs about six seconds a case,
    and five menu checkboxes alone are thirty-two cases before anything else is
    varied. Combinations belong where they are cheap. tests/ui is for whether the
    CONTROLS behave - what is enabled, which dialog opens, what it refuses.
#>

# The probe below must not report a machine with no IMAPI2FS as an error - the
# absence IS the answer, and the tests skip themselves on it.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'Probing for IMAPI2FS: a throw here means the runner has no disc imaging, which is the fact being measured, not a fault to log.')]
param()

BeforeDiscovery {
    $script:CanBuild = $false
    try {
        $probe = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($probe)
        $script:CanBuild = $true
    } catch { }
}

BeforeAll {
    Add-Type -AssemblyName System.Drawing

    $appScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($appScript, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count) { throw "DiscWright.ps1 has $($parseErrors.Count) parse errors" }
    foreach ($f in $ast.FindAll({ param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
        . ([scriptblock]::Create($f.Extent.Text))
    }
    $script:PROJECT_FILE = 'discproject.json'
    $script:APP_VERSION  = 'test'

    $script:Box = Join-Path ([IO.Path]::GetTempPath()) ('dwopt_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Force -Path $script:Box | Out-Null

    $src = Join-Path $script:Box 'src\one_game'
    New-Item -ItemType Directory -Force -Path $src | Out-Null
    $fs = [IO.File]::Create((Join-Path $src 'setup_one_game_1.0.exe')); $fs.SetLength(3MB); $fs.Close()
    $script:Game = Get-GameInfo $src

    function New-Png([string]$path, [int]$w = 1280, [int]$h = 720) {
        $b = New-Object System.Drawing.Bitmap($w, $h)
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.Clear([System.Drawing.Color]::FromArgb(24, 32, 48)); $g.Dispose()
        $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
        return $path
    }
    $script:Art     = New-Png (Join-Path $script:Box 'art.png') 512 512
    $script:Bg      = New-Png (Join-Path $script:Box 'bg.png')
    $script:BgReady = New-Png (Join-Path $script:Box 'ready.png') 760 480

    # A manual, a soundtrack and a folder of extras. Content does not matter -
    # only whether each reaches the disc.
    $script:Manual = Join-Path $script:Box 'handbook.pdf'
    Set-Content -LiteralPath $script:Manual -Value 'not really a pdf' -Encoding ASCII
    $script:Music = Join-Path $script:Box 'theme.mp3'
    $fs = [IO.File]::Create($script:Music); $fs.SetLength(64KB); $fs.Close()
    $script:ExtrasDir = Join-Path $script:Box 'extras'
    New-Item -ItemType Directory -Force -Path $script:ExtrasDir | Out-Null
    Set-Content -LiteralPath (Join-Path $script:ExtrasDir 'wallpaper.txt') -Value 'x' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $script:ExtrasDir 'notes.txt') -Value 'y' -Encoding ASCII
    $script:LooseFile = Join-Path $script:Box 'READ ME FIRST.txt'
    Set-Content -LiteralPath $script:LooseFile -Value 'hello' -Encoding ASCII
    $script:LooseDir = Join-Path $script:Box 'Soundtrack'
    New-Item -ItemType Directory -Force -Path $script:LooseDir | Out-Null
    Set-Content -LiteralPath (Join-Path $script:LooseDir 'track01.txt') -Value 'z' -Encoding ASCII

    $script:Sink = { param($m) $null = $m }
    $script:BuildNo = 0

    # One build per set of options. Returns where it landed, so each test can look
    # at the disc rather than at what the code was asked to do.
    function New-TestDisc {
        param([hashtable]$Override = @{})
        $script:BuildNo++
        $out = Join-Path $script:Box ('out{0:D2}' -f $script:BuildNo)
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $s = @{
            Games=@($script:Game); Label='Option Disc'; IconPath=$script:Art; IconIsIco=$false
            Menu=$true; BgPath=$script:Bg; BgAsIs=$false; PanelSide='Right'
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Bordered'; MusicFile=$null
            Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
            ExtraItems=@(); OutDir=$out
        }
        foreach ($k in $Override.Keys) { $s[$k] = $Override[$k] }
        $null = Invoke-Build $s $script:Sink
        # Read the folder back out of the settings, not off the variable above: a
        # test that overrides OutDir builds somewhere else entirely, and returning
        # the pre-override path sends every assertion to an empty directory.
        $out   = $s.OutDir
        $stage = Join-Path $out 'disc'
        return [pscustomobject]@{
            Out   = $out
            Stage = $stage
            Hta   = Join-Path $stage 'AUTORUN\menu.hta'
            Inf   = Join-Path $stage 'autorun.inf'
            Menu  = $(if (Test-Path (Join-Path $stage 'AUTORUN\menu.hta')) {
                        Get-Content -Raw (Join-Path $stage 'AUTORUN\menu.hta') } else { '' })
        }
    }
}

AfterAll {
    if ($script:Box -and (Test-Path $script:Box)) {
        Remove-Item $script:Box -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Background music' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    BeforeAll { $script:WithMusic = New-TestDisc @{ MusicFile = $script:Music } }

    It 'puts the track on the disc beside the menu' {
        Test-Path (Join-Path $script:WithMusic.Stage 'AUTORUN\music.mp3') | Should -BeTrue
    }

    It 'keeps the original extension, since the menu shell-executes it' {
        $script:WithMusic.Menu.Contains('var MUSIC="music.mp3"') | Should -BeTrue
    }

    It 'leaves the menu silent when no track was chosen' {
        (New-TestDisc).Menu.Contains('var MUSIC=""') | Should -BeTrue
    }
}

Describe 'The manual' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    BeforeAll {
        $script:WithManual = New-TestDisc @{
            ManualPath = $script:Manual; Buttons = @('Play','Install','Manual','Exit') }
    }

    It 'lands in Extras, where the menu looks for it' {
        Test-Path (Join-Path $script:WithManual.Stage 'Extras\handbook.pdf') | Should -BeTrue
    }

    It 'is named to the menu by filename only' {
        $script:WithManual.Menu.Contains('var MANUAL="handbook.pdf"') | Should -BeTrue
    }

    It 'gets a button when it was asked for' {
        $script:WithManual.Menu | Should -Match '"Manual"'
    }
}

Describe 'Extra content' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    BeforeAll {
        $script:WithExtras = New-TestDisc @{
            ExtrasPath = $script:ExtrasDir
            Buttons    = @('Play','Install','Extras','Exit')
            ExtraItems = @($script:LooseFile, $script:LooseDir)
        }
    }

    It 'copies the Extras folder contents, not the folder itself' {
        Test-Path (Join-Path $script:WithExtras.Stage 'Extras\wallpaper.txt') | Should -BeTrue
        Test-Path (Join-Path $script:WithExtras.Stage 'Extras\notes.txt')     | Should -BeTrue
    }

    It 'puts a loose file at the disc root under its own name' {
        Test-Path (Join-Path $script:WithExtras.Stage 'READ ME FIRST.txt') | Should -BeTrue
    }

    It 'puts a loose folder at the disc root under its own name' {
        Test-Path (Join-Path $script:WithExtras.Stage 'Soundtrack\track01.txt') | Should -BeTrue
    }

    It 'refuses extra content that would overwrite the disc''s own files' {
        # A file called autorun.inf at the root would replace the one that makes
        # the disc work at all.
        $clash = Join-Path $script:Box 'autorun.inf'
        Set-Content -LiteralPath $clash -Value 'imposter' -Encoding ASCII
        $d = New-TestDisc @{ ExtraItems = @($clash) }
        (Get-Content -Raw (Join-Path $d.Stage 'autorun.inf')) | Should -Not -Match 'imposter'
    }
}

Describe 'How the menu looks' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    It 'puts the panel on the right by default' {
        (New-TestDisc).Menu | Should -Match '\.panel\{position:absolute;left:490px'
    }

    It 'puts the panel on the left when asked' {
        (New-TestDisc @{ PanelSide = 'Left' }).Menu | Should -Match '\.panel\{position:absolute;left:20px'
    }

    It 'draws bordered buttons by default' {
        (New-TestDisc).Menu.Contains('border:1px solid #16545a;border-left:5px') | Should -BeTrue
    }

    It 'drops the outline for the minimal style' {
        (New-TestDisc @{ ButtonStyle = 'Minimal' }).Menu.Contains('border:0;border-left:5px') | Should -BeTrue
    }

    It 'outlines the window by default' {
        (New-TestDisc).Menu.Contains('border:1px solid #00a6b0;') | Should -BeTrue
    }

    It 'drops the window outline when asked' {
        (New-TestDisc @{ WindowBorder = $false }).Menu | Should -Match '#stage\{[^}]*border:0;'
    }
}

Describe 'Which buttons the menu gets' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    # Each option on its own, and the two extremes. Not all thirty-two: the menu
    # reads the list one entry at a time, so a combination proves nothing that the
    # entries do not.
    It 'carries exactly the buttons it was given: <Name>' -ForEach @(
        @{ Name = 'play and exit only';  Buttons = @('Play','Exit') }
        @{ Name = 'install only';        Buttons = @('Install') }
        @{ Name = 'everything';          Buttons = @('Play','Install','Manual','Extras','Exit') }
        @{ Name = 'exit alone';          Buttons = @('Exit') }
    ) {
        $d = New-TestDisc @{ Buttons = $Buttons; ManualPath = $script:Manual; ExtrasPath = $script:ExtrasDir }
        $m = [regex]::Match($d.Menu, 'var BTNS=\[([^\]]*)\]')
        $m.Success | Should -BeTrue
        $got = @([regex]::Matches($m.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        ($got -join ',') | Should -Be ($Buttons -join ',')
    }

    It 'survives being given no buttons at all' {
        # Nothing ticked is a disc with a menu and no way out of it, which is the
        # user's business - but it must still build rather than throw.
        { New-TestDisc @{ Buttons = @() } } | Should -Not -Throw
    }
}

Describe 'Turning the menu off' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    BeforeAll { $script:NoMenu = New-TestDisc @{ Menu = $false } }

    It 'writes no menu' {
        Test-Path $script:NoMenu.Hta | Should -BeFalse
    }

    It 'leaves autorun.inf with nothing to launch' {
        (Get-Content -Raw $script:NoMenu.Inf) | Should -Not -Match '(?im)^\s*shellexecute\s*='
    }

    It 'still labels the disc and gives it its icon' {
        # The icon and label are the point of the disc even with no menu.
        $inf = Get-Content -Raw $script:NoMenu.Inf
        $inf | Should -Match '(?im)^\s*label\s*='
        $inf | Should -Match '(?im)^\s*icon\s*='
    }
}

Describe 'The background' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    It 'composes one at menu size from any picture' {
        $d = New-TestDisc
        $bg = Join-Path $d.Stage 'AUTORUN\bg.png'
        Test-Path $bg | Should -BeTrue
        $img = [System.Drawing.Image]::FromFile($bg)
        $w = $img.Width; $h = $img.Height; $img.Dispose()
        $w | Should -Be 760
        $h | Should -Be 480
    }

    It 'uses a ready-made one untouched when told to' {
        $d = New-TestDisc @{ BgPath = $script:BgReady; BgAsIs = $true }
        $bg = Join-Path $d.Stage 'AUTORUN\bg.png'
        (Get-Item $bg).Length | Should -Be (Get-Item $script:BgReady).Length
    }
}

Describe 'Rebuilding over a disc that is already there' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    BeforeAll {
        $script:Again = New-TestDisc @{ MusicFile = $script:Music; ManualPath = $script:Manual
                                      Buttons = @('Play','Install','Manual','Exit') }
    }

    It 'builds a second time over the first' {
        # The COM handles the ISO writer used to keep alive made this fail with
        # "something is still using it" until the folder was released.
        { New-TestDisc @{ OutDir = $script:Again.Out } } | Should -Not -Throw
    }

    It 'does not leave two copies of the installer behind' {
        $null = New-TestDisc @{ OutDir = $script:Again.Out }
        @(Get-ChildItem $script:Again.Stage -Filter 'setup_*.exe' -File).Count | Should -Be 1
    }

    It 'keeps assets that were picked from inside the disc folder' {
        # Reopening a built disc points the icon at a file inside the staging
        # folder, and the rebuild wipes that folder before writing it again.
        $icon = Join-Path $script:Again.Stage 'OptionDisc.ico'
        Test-Path $icon | Should -BeTrue
        $d = New-TestDisc @{ OutDir = $script:Again.Out; IconPath = $icon; IconIsIco = $true }
        Test-Path (Join-Path $d.Stage 'OptionDisc.ico') | Should -BeTrue
    }
}

Describe 'A manual and extras belonging to one game, not to the disc' -Tag 'Build' -Skip:(-not $script:CanBuild) {

    # On a disc with two games there is no such thing as "the manual". Before
    # this, there was: one manual for the whole disc, and its button appeared on
    # every game's screen - so on a two-game disc one of them opened the other
    # game's manual.

    BeforeAll {
        $b = $script:Box
        foreach ($n in 'first','second') {
            $d = Join-Path $b "multi\$n"
            New-Item -ItemType Directory -Force -Path $d | Out-Null
            $fs = [IO.File]::Create((Join-Path $d "setup_${n}_1.0.exe")); $fs.SetLength(2MB); $fs.Close()
        }
        $script:ManOne = Join-Path $b 'first_manual.pdf'
        Set-Content -LiteralPath $script:ManOne -Value 'one' -Encoding ASCII
        $script:ManTwo = Join-Path $b 'second_manual.pdf'
        Set-Content -LiteralPath $script:ManTwo -Value 'two' -Encoding ASCII
        $script:ExtOne = Join-Path $b 'first_extras'
        New-Item -ItemType Directory -Force -Path $script:ExtOne | Out-Null
        Set-Content -LiteralPath (Join-Path $script:ExtOne 'artbook.txt') -Value 'a' -Encoding ASCII
        $script:ExtTwo = Join-Path $b 'second_extras'
        New-Item -ItemType Directory -Force -Path $script:ExtTwo | Out-Null
        Set-Content -LiteralPath (Join-Path $script:ExtTwo 'soundtrack.txt') -Value 's' -Encoding ASCII

        $g1 = Get-GameInfo (Join-Path $b 'multi\first')
        $g1.ManualPath = $script:ManOne; $g1.ExtrasPath = $script:ExtOne
        $g2 = Get-GameInfo (Join-Path $b 'multi\second')
        $g2.ManualPath = $script:ManTwo; $g2.ExtrasPath = $script:ExtTwo

        $script:Two = New-TestDisc @{
            Games   = @($g1, $g2)
            Buttons = @('Play','Install','Manual','Extras','Exit')
        }
    }

    It 'gives each game its own Extras folder, beside its installer' {
        Test-Path (Join-Path $script:Two.Stage 'Games\01 - first\Extras\artbook.txt')     | Should -BeTrue
        Test-Path (Join-Path $script:Two.Stage 'Games\02 - second\Extras\soundtrack.txt') | Should -BeTrue
    }

    It 'puts each manual with the game it belongs to' {
        Test-Path (Join-Path $script:Two.Stage 'Games\01 - first\Extras\first_manual.pdf')   | Should -BeTrue
        Test-Path (Join-Path $script:Two.Stage 'Games\02 - second\Extras\second_manual.pdf') | Should -BeTrue
    }

    It 'does not mix one game''s extras into the other''s' {
        Test-Path (Join-Path $script:Two.Stage 'Games\01 - first\Extras\soundtrack.txt') | Should -BeFalse
        Test-Path (Join-Path $script:Two.Stage 'Games\02 - second\Extras\artbook.txt')   | Should -BeFalse
    }

    It 'tells the menu a different manual for each game' {
        $paths = @([regex]::Matches($script:Two.Menu, 'man:"([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
        @($paths | Where-Object { $_ }).Count | Should -Be 2
        ($paths[0]) | Should -Not -Be ($paths[1])
    }

    It 'points every menu path at something that is really there' {
        foreach ($m in [regex]::Matches($script:Two.Menu, '(man|ext|s):"([^"]+)"')) {
            $rel = $m.Groups[2].Value -replace '\\\\','\'
            Test-Path (Join-Path $script:Two.Stage $rel) | Should -BeTrue -Because "the menu points at $rel"
        }
    }

    It 'falls back to the disc-wide manual for a game that has none of its own' {
        # An entry with nothing of its own leaves man empty, and the menu then
        # uses the disc's - which is how every disc built before this behaved.
        $g1 = Get-GameInfo (Join-Path $script:Box 'multi\first')
        $g2 = Get-GameInfo (Join-Path $script:Box 'multi\second')
        $g1.ManualPath = $script:ManOne
        $d = New-TestDisc @{ Games=@($g1,$g2); ManualPath=$script:Manual
                             Buttons=@('Play','Install','Manual','Exit') }
        $paths = @([regex]::Matches($d.Menu, 'man:"([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
        $paths[0] | Should -Not -BeNullOrEmpty
        $paths[1] | Should -BeNullOrEmpty
        $d.Menu.Contains('var MANUAL="handbook.pdf"') | Should -BeTrue
    }

    It 'keeps a single-game disc flat, exactly as before' {
        # One entry still puts its manual in Extras\ at the root, so a disc built
        # by an older DiscWright and rebuilt by this one does not move.
        $g = Get-GameInfo (Join-Path $script:Box 'multi\first')
        $g.ManualPath = $script:ManOne; $g.ExtrasPath = $script:ExtOne
        $d = New-TestDisc @{ Games=@($g); Buttons=@('Play','Install','Manual','Extras','Exit') }
        Test-Path (Join-Path $d.Stage 'Extras\first_manual.pdf') | Should -BeTrue
        Test-Path (Join-Path $d.Stage 'Extras\artbook.txt')      | Should -BeTrue
        Test-Path (Join-Path $d.Stage 'Games')                   | Should -BeFalse
    }

    It 'remembers each game''s manual and extras in the project file' {
        $back = Import-Project (Join-Path $script:Two.Out 'discproject.json')
        $back.GameEntries[0].Manual | Should -Be $script:ManOne
        $back.GameEntries[1].Manual | Should -Be $script:ManTwo
        $back.GameEntries[0].Extras | Should -Be $script:ExtOne
        $back.GameEntries[1].Extras | Should -Be $script:ExtTwo
    }
}
