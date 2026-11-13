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

# ==================================== each game's own manual and extras, 2.0.4
#
# The folders section F's own cases start from. Every manual is a real PDF of
# one page that says which file it is and which game it belongs to, so when a
# menu button opens something, whoever is watching can see at once whether it
# opened the right one. A sparse file of zeros called manual.pdf opens as an
# error in every reader, and that reads as a bug in the menu.

# A PDF a reader really opens, one page, the lines given written on it.
#
# Built by hand rather than by a library, because nothing on a clean Windows
# writes one. The cross reference table holds the byte offset of every object,
# so the text is ASCII and every line ends in a bare line feed, or the offsets
# drift and a strict reader refuses the file.
function Pdf([string] $path, [string[]] $lines) {
    $parent = Split-Path $path -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

    $text = 'BT /F1 22 Tf 60 770 Td 30 TL'
    foreach ($line in $lines) {
        # Brackets would close the PDF string early, and nothing here needs them.
        $plain = ($line -replace '[()]', '')
        $text += " ($plain) '"
    }
    $text += ' ET'

    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
        "<< /Length $($text.Length) >>`nstream`n$text`nendstream",
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
    )

    $out = "%PDF-1.4`n"
    $offsets = @()
    for ($i = 0; $i -lt $objects.Count; $i++) {
        $offsets += $out.Length
        $out += "$($i + 1) 0 obj`n$($objects[$i])`nendobj`n"
    }

    $xref = $out.Length
    $out += "xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n"
    foreach ($at in $offsets) { $out += ("{0:D10} 00000 n `n" -f $at) }
    $out += "trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"

    [System.IO.File]::WriteAllBytes($path, [System.Text.Encoding]::ASCII.GetBytes($out))
}

# F15 to F19  one GOG download with every kind of goodie beside it: two
# manuals by name, a folder of manuals, a zipped manual that is never guessed
# to be one, an artbook with a folder inside it, maps, a readme, a patch the
# installer already covers and a patch past it.
$at = Folder 'own-gog-goodies'
Lump "$at\setup_lantern_keep_1.5_(70300).exe" (300 * $MB)
Lump "$at\setup_lantern_keep_1.5_(70300)-1.bin" (200 * $MB)
Lump "$at\patch_lantern_keep_1.4_(70100)_to_1.5_(70300).exe" (30 * $MB)
Lump "$at\patch_lantern_keep_1.5_(70300)_to_1.6_(70400).exe" (12 * $MB)
Pdf "$at\Lantern Keep Manual.pdf" 'Lantern Keep', 'Lantern Keep Manual.pdf', 'The game''s manual, by its name.'
Pdf "$at\Lantern Keep Game Guide.pdf" 'Lantern Keep', 'Lantern Keep Game Guide.pdf', 'A guide counts as a manual.'
Pdf "$at\Manuals\Manual English.pdf" 'Lantern Keep', 'Manual English.pdf', 'Inside the folder called Manuals.'
Pdf "$at\Manuals\Manual Deutsch.pdf" 'Lantern Keep', 'Manual Deutsch.pdf', 'Inside the folder called Manuals.'
Lump "$at\lantern_keep_manual.zip" (2 * $MB)
Words "$at\README.txt" 'Lantern Keep. This README sits beside the installer.'
1..6 | ForEach-Object { Picture "$at\Artbook\page $_.png" 800 600 'DarkSlateBlue' "Lantern Keep artbook page $_" }
1..3 | ForEach-Object { Picture "$at\Artbook\Sketches\sketch $_.png" 600 600 'DimGray' "Lantern Keep sketch $_" }
1..2 | ForEach-Object { Picture "$at\Maps\map $_.png" 800 800 'DarkGreen' "Lantern Keep map $_" }

# F20, F21  one manual, so Manual opens the file itself, and extras of its
# own, so a lone game takes a folder on the disc.
$at = Folder 'own-one-manual'
Lump "$at\setup_quiet_harbour_2.0_(71000).exe" (250 * $MB)
Pdf "$at\Quiet Harbour manual.pdf" 'Quiet Harbour', 'Quiet Harbour manual.pdf', 'The only manual, so Manual opens this page.'
1..3 | ForEach-Object { Picture "$at\Wallpapers\wallpaper $_.png" 1280 720 'Teal' "Quiet Harbour wallpaper $_" }

# F21  a manual and nothing else, so a lone game keeps the root.
$at = Folder 'own-manual-only'
Lump "$at\setup_cinder_road_1.0_(71500).exe" (250 * $MB)
Pdf "$at\Cinder Road Manual.pdf" 'Cinder Road', 'Cinder Road Manual.pdf', 'A lone game with only a manual keeps the root.'

# F22, F23  a game with nothing beside it, and its goodies kept somewhere
# else, which is how the owner's own test kit is laid out.
$at = Folder 'own-bare-game'
Lump "$at\setup_bare_game_1.0_(75000).exe" (200 * $MB)

$at = Folder 'own-goodies-apart'
Pdf "$at\Bare Game manual.pdf" 'Bare Game', 'Bare Game manual.pdf', 'Added from the list, not from the dialog.'
Pdf "$at\Bare Game reference card.pdf" 'Bare Game', 'Bare Game reference card.pdf', 'A second manual, so Manual opens the folder.'
1..3 | ForEach-Object { Picture "$at\Concept Art\concept $_.png" 800 600 'Maroon' "Bare Game concept $_" }
1..2 | ForEach-Object { Picture "$at\Posters\poster $_.png" 600 900 'Indigo' "Bare Game poster $_" }

# F22  the disc's own manual and extras, chosen in Artwork, music and extras,
# to set beside the games' own.
$at = Folder 'disc-wide'
Pdf "$at\Disc manual.pdf" 'The disc', 'Disc manual.pdf', 'The disc''s own manual, not any one game''s.'
Words "$at\Disc extras\About this disc.txt" 'The disc''s own extras, not any one game''s.'
Picture "$at\Disc extras\poster.png" 600 900 'SaddleBrown' 'The disc''s poster'

# F25  one game too big for a CD with a manual and extras of its own, which
# ride every disc of the set and are counted on every one.
$at = Folder 'own-for-a-set'
Lump "$at\setup_long_winter_1.0_(74000).exe" ([long](1.2 * $GB))
Pdf "$at\Long Winter manual.pdf" 'Long Winter', 'Long Winter manual.pdf', 'On every disc of the set.'
Lump "$at\Making Of\making of.mp4" (150 * $MB)

# F26  three games to add together, each with a manual and an artbook. Each
# carries a patch the dialog judges against that game's own installer:
# North Gate's and Ember Fall's go past theirs, Salt Marsh's installer already
# is 1.2, so its patch arrives unticked even with the first game's answers.
$at = Folder 'own-three-games'
$three = @(
    @{ Name = 'North Gate'; Setup = 'setup_north_gate_1.0_(72000).exe'; Patch = 'patch_north_gate_1.0_(72000)_to_1.1_(72100).exe'; Colour = 'SteelBlue' },
    @{ Name = 'Salt Marsh'; Setup = 'setup_salt_marsh_1.2_(72500).exe'; Patch = 'patch_salt_marsh_1.1_(72400)_to_1.2_(72500).exe'; Colour = 'OliveDrab' },
    @{ Name = 'Ember Fall'; Setup = 'setup_ember_fall_1.0_(73000).exe'; Patch = 'patch_ember_fall_1.0_(73000)_to_1.1_(73100).exe'; Colour = 'Firebrick' }
)
foreach ($game in $three) {
    $in = Join-Path $at $game.Name
    Lump "$in\$($game.Setup)" (150 * $MB)
    Lump "$in\$($game.Patch)" (10 * $MB)
    Pdf "$in\$($game.Name) manual.pdf" $game.Name, "$($game.Name) manual.pdf", 'Added together with two other games.'
    1..2 | ForEach-Object { Picture "$in\Artbook\page $_.png" 800 600 $game.Colour "$($game.Name) artbook page $_" }
}

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
