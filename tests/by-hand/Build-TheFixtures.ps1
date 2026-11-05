<#
.SYNOPSIS
    Makes every folder the test matrix asks for, so a pass can start at once.

.DESCRIPTION
    tests/by-hand/test-matrix.html names shapes of folder rather than
    particular games: a GOG download with a gap in its parts, a folder of files
    with two executables worth running, a folder that holds games rather than
    being one. This builds all of them.

    Almost nothing here is a real game. The recognisers read names, extensions and
    sizes, so that is all these carry, and the big ones are sparse: a file that
    plans as 1.2 GB and costs almost nothing on the disk.

    Run it again whenever you like. It clears what it made last time first, so
    a half finished pass cannot leave a folder that is neither one shape nor
    the other.

.PARAMETER Into
    Where to build them. Never inside the repository and never inside OneDrive.

.EXAMPLE
    tests\by-hand\Build-TheFixtures.ps1
#>

[CmdletBinding()]
param(
    [string] $Into = "$env:USERPROFILE\DiscWright-Lab\fixtures",

    # A real GOG offline installer folder, for the two cases that need a game
    # which genuinely installs. Hollow Knight is the one to reach for: it is the
    # smallest here at 1.8 GB and the only one carrying real patches, which is
    # exactly the add-on shape case 6 asks about.
    [string] $RealGameFrom = "${env:ProgramFiles(x86)}\GOG Galaxy\Games\Offline Installers\hollow_knight",

    # A real program for the folders whose whole point is that the disc runs
    # them. A sparse file of zeros is not a program and will not start, so the
    # case cannot be judged with one.
    #
    # IT HAS TO OPEN A WINDOW RATHER THAN A CONSOLE. cmd.exe was used first
    # because it starts from anywhere and stays up, and a padded one is a
    # terrible stand-in: a terminal is not what anybody's game looks like, and a
    # padded cmd.exe cannot even find its own banner text, so it opens saying
    # 'The system cannot find message text for message number 0x2350'. Every
    # pass then reads as a failure to whoever is watching.
    #
    # Swept the obvious ones on 2026-11-05. Copied out of System32, charmap,
    # notepad, msinfo32, resmon, perfmon, cleanmgr, dfrgui and sndvol all quit
    # the instant they start. dxdiag, osk and wscript survive. wscript is the
    # one to take: it is 180 KB, it opens a plain settings dialog and waits, it
    # does nothing at all while it is up, and it still runs after being padded
    # to 40 MB, which is the part that actually had to be checked. dxdiag can
    # open a modal question about driver signing on a machine that has not run
    # it before, and anything modal stops a by-hand pass dead.
    [string] $RealProgramFrom = "$env:WINDIR\System32\wscript.exe"
)

$ErrorActionPreference = 'Stop'

if ($Into -like '*OneDrive*') {
    throw "Not inside OneDrive. These plan as several gigabytes and a sync client should never see them."
}

Write-Host "building fixtures in $Into" -ForegroundColor Cyan

if (Test-Path $Into) { Remove-Item $Into -Recurse -Force }
New-Item -ItemType Directory -Path $Into -Force | Out-Null

# ---------------------------------------------------------------- the tools

function Folder([string] $name) {
    $path = Join-Path $Into $name
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $path
}

# A file of exactly this many bytes and almost none of it on the disk.
# Nothing here is ever read, only measured, so a 2 GB installer can cost
# nothing at all.
#
# THE ORDER MATTERS AND IT IS NOT OBVIOUS. Marked sparse after SetLength, the
# clusters are already allocated and the flag cannot free them: the file reads
# as sparse, fsutil says its valid data length is zero, and 14.4 GB of
# fixtures still costs 14.4 GB. Measured on the test VM, where the free space
# went down by exactly what the files add up to. Empty first, then the flag,
# then the length, and the whole lot costs a few megabytes.
function Lump([string] $path, [long] $bytes) {
    $parent = Split-Path $path -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

    [System.IO.File]::Create($path).Dispose()

    if ($bytes -ge 1MB) { & fsutil sparse setflag $path | Out-Null }

    $file = [System.IO.File]::OpenWrite($path)
    try {
        $file.SetLength($bytes)
    } finally {
        $file.Dispose()
    }
}

# A program that will really start, padded out to the size of a game.
#
# A Lump is zeros and zeros are not a program. Case 7 ends in "the disc runs
# it" three times over, so the two folders it burns carry a real one.
#
# Same order as Lump and for the same reason: empty, then the flag, then the
# bytes, then the length. The program itself is a few hundred KB and real, the
# tail is sparse, and the whole thing costs almost nothing while planning as 40
# MB. Trailing bytes after a PE are ignored, so it still runs.
function RealProgram([string] $path, [long] $bytes) {
    $parent = Split-Path $path
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    if (-not (Test-Path $RealProgramFrom)) {
        Write-Warning "No real program at $RealProgramFrom, so $path is zeros and will not run."
        Lump $path $bytes
        return
    }
    if (Test-Path $path) { Remove-Item $path -Force }
    $made = [IO.File]::Create($path)
    $made.Close()
    & fsutil sparse setflag $path | Out-Null
    $real = [IO.File]::ReadAllBytes($RealProgramFrom)
    $writing = [IO.File]::Open($path, 'Open', 'Write')
    $writing.Write($real, 0, $real.Length)
    if ($bytes -gt $real.Length) { $writing.SetLength($bytes) }
    $writing.Close()
}
function Words([string] $path, [string] $text) {
    $parent = Split-Path $path -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Set-Content -Path $path -Value $text -Encoding utf8
}

$MB = 1MB
$GB = 1GB

# ============================================================ GOG downloads

# E1  the plain one
$at = Folder 'gog-plain'
Lump "$at\setup_plain_game_1.0_(12345).exe" (300 * $MB)

# E2  an installer and its complete numbered parts
$at = Folder 'gog-with-parts'
Lump "$at\setup_big_game_2.1_(22222).exe" (200 * $MB)
1..3 | ForEach-Object { Lump "$at\setup_big_game_2.1_(22222)-$_.bin" (400 * $MB) }

# E10a  a gap at 3, so the download looks unfinished
$at = Folder 'gog-unfinished'
Lump "$at\setup_cut_short_1.0_(33333).exe" (200 * $MB)
1, 2, 4 | ForEach-Object { Lump "$at\setup_cut_short_1.0_(33333)-$_.bin" (300 * $MB) }

# E10b  a base game beside its DLC: it takes the largest and says which
$at = Folder 'gog-two-installers'
Lump "$at\setup_the_base_game_1.0_(44444).exe" (800 * $MB)
Lump "$at\setup_the_dlc_1.0_(44445).exe" (90 * $MB)

# E2 to E4  something beside it that looks like an add-on
$at = Folder 'gog-with-a-patch'
Lump "$at\setup_patched_game_1.0_(55555).exe" (250 * $MB)
Lump "$at\patch_1.0_to_1.1.exe" (40 * $MB)

# E5  something beside it to keep rather than to install
$at = Folder 'gog-with-a-readme'
Lump "$at\setup_documented_game_1.0_(66666).exe" (250 * $MB)
Words "$at\README.txt" 'Read me before installing.'

# E9  one of each at once
$at = Folder 'gog-with-both'
Lump "$at\setup_everything_1.0_(77777).exe" (250 * $MB)
Lump "$at\patch_1.0_to_1.2.exe" (40 * $MB)
Words "$at\README.txt" 'Read me before installing.'
Lump "$at\soundtrack_flac.exe" (120 * $MB)

# E10  the folder is itself an add-on, not a game. It has to start with
# setup_ or the GOG recogniser never claims it and there is nothing to ask
# about; the add-on words are what make it look like one.
$at = Folder 'gog-that-is-a-patch'
Lump "$at\setup_some_game_soundtrack_1.0_(98888).exe" (160 * $MB)

# ==================================================== folders of game files

# E11  nothing in it can be started
$at = Folder 'files-nothing-to-run'
Lump "$at\data\things.pak" (120 * $MB)
Words "$at\readme.txt" 'A folder of files and nothing to run.'

# E12  exactly one worth running, so nothing is asked
$at = Folder 'files-one-to-run'
RealProgram "$at\TheGame.exe" (40 * $MB)
Lump "$at\data\things.pak" (120 * $MB)

# E13  one worth running, beside four that are never the game
$at = Folder 'files-one-and-noise'
Lump "$at\TheGame.exe" (40 * $MB)
Lump "$at\unins000.exe" (900KB)
Lump "$at\vcredist_x64.exe" (25 * $MB)
Lump "$at\dxsetup.exe" (12 * $MB)
Lump "$at\oalinst.exe" (3 * $MB)
Lump "$at\data\things.pak" (120 * $MB)

# E14  two worth running, so it asks
$at = Folder 'files-two-to-run'
RealProgram "$at\TheGame.exe" (40 * $MB)
RealProgram "$at\Config.exe" (8 * $MB)
Lump "$at\data\things.pak" (120 * $MB)

# E17  nothing at the top, two deep inside
$at = Folder 'files-deep'
Lump "$at\bin\win64\TheGame.exe" (40 * $MB)
Lump "$at\bin\win32\TheGame.exe" (30 * $MB)
Lump "$at\assets\maps\first.map" (60 * $MB)

# ============================================== a folder that holds games

# E20 to E22  two games inside, and loose files that belong to neither
# Both have to be downloads: a folder counts as holding a game when it holds
# a setup_, so a folder of loose files inside one is not one of them.
$at = Folder 'a-folder-of-games'
Lump "$at\First Game\setup_first_game_1.0_(81111).exe" (200 * $MB)
Lump "$at\Second Game\setup_second_game_1.0_(81222).exe" (140 * $MB)
Lump "$at\Second Game\setup_second_game_1.0_(81222)-1.bin" (90 * $MB)
Words "$at\about these.txt" 'Two games, and this file belongs to neither.'
Words "$at\where they came from.txt" 'Downloaded on a Tuesday.'

# ==================================================================== sizes

# G1  comfortably one CD
$at = Folder 'size-fits-a-cd'
Lump "$at\setup_small_game_1.0_(91111).exe" (420 * $MB)

# G4, G5  one file too big for a CD, fits a DVD
$at = Folder 'size-one-too-big-for-a-cd'
Lump "$at\setup_wide_game_1.0_(92222).exe" ([long](1.2 * $GB))

# G6  two files too big for a CD
$at = Folder 'size-two-too-big-for-a-cd'
Lump "$at\setup_wide_twice_1.0_(93333).exe" ([long](1.1 * $GB))
Lump "$at\setup_wide_twice_1.0_(93333)-1.bin" ([long](1.3 * $GB))

# G3  three CDs' worth, nothing individually too big
$at = Folder 'size-three-cds'
1..4 | ForEach-Object { Lump "$at\setup_long_game_1.0_(94444)-$_.bin" (480 * $MB) }
Lump "$at\setup_long_game_1.0_(94444).exe" (200 * $MB)

# G12  one file over the ISO9660 ceiling of 2 GiB
$at = Folder 'size-over-the-iso-ceiling'
Lump "$at\setup_enormous_1.0_(95555).exe" ([long](2.2 * $GB))

# ============================================================ awkward input

# J1  an empty folder
Folder 'odd-empty-folder' | Out-Null

# J2  nothing a recogniser knows
$at = Folder 'odd-nothing-recognised'
Words "$at\notes.txt" 'No executable anywhere.'
Words "$at\more notes.txt" 'Still nothing.'

# J4  a game folder with a name the disc needs for itself
$at = Folder 'odd-reserved-name\AUTORUN'
Lump "$at\TheGame.exe" (20 * $MB)

# J5  a loose setup_ at the top, which reads back as a game
$at = Folder 'odd-loose-setup'
Lump "$at\TheGame.exe" (20 * $MB)
Lump "$at\setup_not_really.exe" (5 * $MB)

# J10  a name far too long for the row it is drawn in
$at = Folder 'odd-long-names'
Lump "$at\setup_a_game_with_an_extremely_long_file_name_that_will_certainly_not_fit_in_the_row_1.0_(96666).exe" (80 * $MB)

# K20  an apostrophe, on the window, in a folder name and on the menu
$at = Folder "Baldur's Gate Test"
Lump "$at\setup_baldurs_gate_1.0_(97777).exe" (150 * $MB)

# ======================================= artwork, a manual, extras, a tune

Add-Type -AssemblyName System.Drawing

function Picture([string] $path, [int] $w, [int] $h, [string] $colour, [string] $caption) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromName($colour))
    $size = [int]([math]::Max(11, [math]::Min($w, $h) / 12))
    $font = New-Object System.Drawing.Font 'Segoe UI', $size
    $g.DrawString($caption, $font, [System.Drawing.Brushes]::White, 14, 14)
    $g.DrawRectangle([System.Drawing.Pens]::White, 5, 5, $w - 11, $h - 11)
    $g.Dispose()
    $font.Dispose()
    $parent = Split-Path $path -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$art = Folder 'artwork'
Picture "$art\icon.png" 256 256 'DarkSlateBlue' 'icon'
Picture "$art\background-760x480.png" 760 480 'DarkSlateGray' 'exactly right'
Picture "$art\background-wide.png" 1920 600 'SaddleBrown' 'far too wide'
Picture "$art\background-tall.png" 600 1400 'DarkOliveGreen' 'far too tall'
Picture "$art\cover.png" 1200 1200 'Maroon' 'cover'
Picture "$art\disc-face.png" 1000 1000 'MidnightBlue' 'disc face'
Words "$art\not-a-picture.png" 'This has a picture name and is a sentence.'

$extras = Folder 'extras'
Words "$extras\How to play.txt" 'Press the keys.'
Words "$extras\Interview.txt" 'A long time ago.'
Picture "$extras\concept\first.png" 800 600 'Indigo' 'concept art'
Words "$extras\concept\notes.txt" 'Early sketches.'

$loose = Folder 'loose-files'
Words "$loose\A note about the disc.txt" 'This belongs to the disc, not to a game.'
Words "$loose\Checksums you might want.txt" 'Nothing in particular.'

# A real tune rather than random bytes. An mp3 that is not really an mp3
# plays nothing, and hearing it is the whole of the music cases.
$music = Folder 'music'
$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue

if ($ffmpeg) {
    & $ffmpeg.Source -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=440:duration=12" -codec:a libmp3lame -q:a 6 "$music\tune.mp3"
    & $ffmpeg.Source -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=330:duration=8" "$music\tune.wav"
    Write-Host "  a real tune, made with ffmpeg" -ForegroundColor DarkGray
} else {
    Write-Warning "ffmpeg is not on the path, so there is no real tune. The music cases need one that really plays."
}

Words "$music\not-a-tune.mp3" 'This has a tune name and is a sentence.'

# ========================================================= a game that is real
#
# Everything above is sparse, because the recognisers only ever measure. Two
# cases cannot live with that. Case 6 step 3 wants an add-on to come alive AFTER
# its game is installed, and the menu decides that by asking the machine whether
# the game is REGISTERED, which no file of zeros will ever be. Case 7 ends in
# "the disc runs it" three times over and wants the same.
#
# A padded real program is not enough either, and October already learnt that:
# Character Map with 1.2 GB on the end starts, quits and registers nothing, so
# the end of the flow was inferred rather than seen.
#
# So this one is real when the machine has something to be real with. Hard links
# rather than copies, so 1.8 GB costs nothing and reads identically. That wants
# the lab and the installers on one volume, and falls back to copying when they
# are not.

$real = Folder 'gog-real'

if (-not (Test-Path $RealGameFrom)) {
    Write-Warning "No real game at $RealGameFrom, so gog-real is empty. Case 6 step 3 and case 7 need one: point -RealGameFrom at a GOG offline installer folder."
} else {
    $took = 0
    $copied = 0
    foreach ($f in Get-ChildItem $RealGameFrom -File | Where-Object { $_.Extension -in '.exe', '.bin' }) {
        $to = Join-Path $real $f.Name
        try {
            New-Item -ItemType HardLink -Path $to -Target $f.FullName -ErrorAction Stop | Out-Null
        } catch {
            Copy-Item $f.FullName $to
            $copied++
        }
        $took++
    }
    $size = (Get-ChildItem $real -File | Measure-Object -Property Length -Sum).Sum
    $how = if ($copied -gt 0) { "$copied copied, the rest hard linked" } else { 'all hard linked, so it costs nothing' }
    Write-Host "  a real game: $took files, $([math]::Round($size / 1GB, 2)) GB, $how" -ForegroundColor DarkGray
}

# ================================================================ the tally

$count = (Get-ChildItem $Into -Directory).Count
$bytes = (Get-ChildItem $Into -Recurse -File | Measure-Object -Property Length -Sum).Sum

Write-Host ""
Write-Host "$count folders, $([math]::Round($bytes / 1GB, 2)) GB as the planner will see it" -ForegroundColor Green
Write-Host $Into
