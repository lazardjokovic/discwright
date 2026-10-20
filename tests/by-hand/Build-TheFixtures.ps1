<#
.SYNOPSIS
    Makes every folder the test matrix asks for, so a pass can start at once.

.DESCRIPTION
    tests/by-hand/test-matrix.html names shapes of folder rather than
    particular games: a GOG download with a gap in its parts, a folder of files
    with two executables worth running, a folder that holds games rather than
    being one. This builds all of them.

    Nothing here is a real game. The recognisers read names, extensions and
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
    [string] $Into = "$env:USERPROFILE\DiscWright-Lab\fixtures"
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
Lump "$at\TheGame.exe" (40 * $MB)
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
Lump "$at\TheGame.exe" (40 * $MB)
Lump "$at\Config.exe" (8 * $MB)
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

# ================================================================ the tally

$count = (Get-ChildItem $Into -Directory).Count
$bytes = (Get-ChildItem $Into -Recurse -File | Measure-Object -Property Length -Sum).Sum

Write-Host ""
Write-Host "$count folders, $([math]::Round($bytes / 1GB, 2)) GB as the planner will see it" -ForegroundColor Green
Write-Host $Into
