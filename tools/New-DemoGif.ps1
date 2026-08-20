<#
.SYNOPSIS
    Records DiscWright being driven through a scripted sequence and writes an
    animated GIF of it.

.DESCRIPTION
    The demonstrations in the README are the first thing anyone sees, and
    re-recording them by hand after every change is why they go stale. This
    drives the window exactly as the tests in tests/ui do, photographs it as it
    goes, and assembles the frames.

    NOTHING IN THE RECORDING SHOWS WHO MADE IT
    A GIF of this window is a GIF of several text boxes full of paths, and a
    path under a home directory publishes the account name to everyone who reads
    the README. So the fixture is built somewhere with no name in it -
    C:\Users\Public by default - and every path the window displays comes from
    there. Change it with -WorkDir; anything under $env:USERPROFILE is refused.

    ENCODING
    ffmpeg if it is installed, which is worth having: it builds a palette from
    the footage and writes only the rectangles that changed between frames, so
    the same recording comes out several times smaller and looks better. Without
    it, WPF's GifBitmapEncoder is used instead - that writes every frame whole
    and omits both the frame delay and the loop instruction, so those get patched
    into the bytes afterwards. See Write-GifFallback.

    LEAVE THE MACHINE ALONE while it records. It drives the real pointer and the
    real foreground window.

.PARAMETER OutFile
    Where to write the .gif.

.PARAMETER Scenario
    AddAndBuild - open a prepared disc, add an add-on, select it, remove it.
    Tour        - the window sitting still, for a still-ish banner.

.PARAMETER CropHeight
    Keep only the top N pixels of the window. A demonstration of step 1 does not
    need the 700 pixels of settings below it, and every pixel is paid for in
    every frame. 0 keeps the whole window.

.EXAMPLE
    .\tools\New-DemoGif.ps1 -OutFile docs\add-on.gif -CropHeight 260 -Width 620
#>
# Write-Host is this script's output: progress for a person watching it record.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'A recorder whose console output is progress for a person watching. Nothing consumes it as data.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'A failed frame grab, while a window repaints, must not abort a recording.')]
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutFile,
    [ValidateSet('AddAndBuild','Tour')][string]$Scenario = 'AddAndBuild',
    [ValidateRange(2,15)][int]$Fps = 4,
    [ValidateRange(200,1400)][int]$Width = 620,
    [ValidateRange(0,2000)][int]$CropHeight = 0,
    # Somewhere with no person's name in the path. Everything the window shows
    # is built here, so this is what ends up on screen.
    [string]$WorkDir = (Join-Path $env:PUBLIC 'DiscWright Demo'),
    [string]$FfmpegPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'tests\ui\UiDriver.psm1') -Force
Add-Type -AssemblyName System.Drawing, PresentationCore, WindowsBase

if (-not (Test-UiAvailable)) { throw 'no usable desktop to record' }

# The whole point of WorkDir is that it carries no account name. Guard it, or a
# careless -WorkDir puts the thing back that this exists to keep out.
$profileDir = [IO.Path]::GetFullPath($env:USERPROFILE).TrimEnd('\')
$wantedDir  = [IO.Path]::GetFullPath($WorkDir).TrimEnd('\')
if ($wantedDir.StartsWith($profileDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw "WorkDir is inside your home directory, so the recording would show your account name in every path box:`n  $wantedDir`nPick somewhere neutral - the default is $env:PUBLIC."
}

function Find-Ffmpeg {
    param([string]$Explicit)
    if ($Explicit) { if (Test-Path $Explicit) { return $Explicit }; throw "no ffmpeg at $Explicit" }
    $c = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    # winget puts it here and only updates PATH for new shells.
    $guess = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Recurse -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($guess) { return $guess.FullName }
    return $null
}

# --------------------------------------------------------------------------
# Recording - frames go to disk, so a long take does not sit in memory and
# ffmpeg has something to read.
# --------------------------------------------------------------------------

$script:FrameDir = Join-Path ([IO.Path]::GetTempPath()) ('dwframes_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force -Path $script:FrameDir | Out-Null
$script:FrameNo = 0

function Add-Frame {
    param($Win, [int]$CropHeight = 0)
    try {
        $r = $Win.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { return }
        $h = [int]$r.Height
        if ($CropHeight -gt 0 -and $CropHeight -lt $h) { $h = $CropHeight }
        $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, $h)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
        $g.Dispose()
        $script:FrameNo++
        $bmp.Save((Join-Path $script:FrameDir ('f{0:D5}.png' -f $script:FrameNo)),
                  [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    } catch { }
}

function Add-Frames {
    <#  .SYNOPSIS Hold on the current state, so a viewer has time to read it. #>
    param($Win, [double]$Seconds, [int]$Fps, [int]$CropHeight = 0)
    $n = [int][math]::Max(1, [math]::Round($Seconds * $Fps))
    $gap = [int](1000 / $Fps)
    for ($i = 0; $i -lt $n; $i++) {
        Add-Frame -Win $Win -CropHeight $CropHeight
        Start-Sleep -Milliseconds $gap
    }
}

# --------------------------------------------------------------------------
# Encoding
# --------------------------------------------------------------------------

function Write-GifFfmpeg {
    <#
    .SYNOPSIS Two passes: build a palette from the footage, then apply it.

    .DESCRIPTION
        stats_mode=diff weights the palette towards the parts of the frame that
        actually move, which for an interface is the few controls that changed
        rather than the acres of grey around them. diff_mode=rectangle then
        writes only the changed rectangle of each frame, which is where nearly
        all of the size saving comes from.
    #>
    param([string]$Ffmpeg, [string]$FrameDir, [string]$Path, [int]$Fps, [int]$Width)
    $palette = Join-Path $FrameDir 'palette.png'
    $in = Join-Path $FrameDir 'f%05d.png'

    & $Ffmpeg -y -loglevel error -framerate $Fps -i $in `
        -vf "scale=${Width}:-1:flags=lanczos,palettegen=stats_mode=diff:max_colors=192" $palette
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed building the palette (exit $LASTEXITCODE)" }

    & $Ffmpeg -y -loglevel error -framerate $Fps -i $in -i $palette `
        -lavfi "scale=${Width}:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" `
        -loop 0 $Path
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed writing the gif (exit $LASTEXITCODE)" }
}

if (-not ('DwGdi' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DwGdi {
  [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
}
"@
}

function Write-GifFallback {
    <#
    .SYNOPSIS Animated GIF with no ffmpeg, from WPF's encoder plus byte surgery.

    .DESCRIPTION
        GifBitmapEncoder writes every frame and puts a Graphic Control Extension
        in front of each - with a delay of zero, which viewers render as fast as
        they can. It also omits the Netscape application extension, so the
        animation plays once and stops. Both are fixed in the finished bytes:

          - Every Graphic Control Extension starts 21 F9 04, and the two bytes
            after the packed field are the delay in hundredths of a second.
          - The Netscape block goes straight after the global colour table, whose
            size is declared in the logical screen descriptor. Loop count 0 means
            forever.
    #>
    param([string]$FrameDir, [string]$Path, [int]$DelayMs, [int]$Width)

    $files = @(Get-ChildItem $FrameDir -Filter 'f*.png' | Sort-Object Name)
    if ($files.Count -eq 0) { throw 'nothing was recorded' }

    $enc = New-Object System.Windows.Media.Imaging.GifBitmapEncoder
    foreach ($file in $files) {
        $src = [System.Drawing.Image]::FromFile($file.FullName)
        $h = [int]([double]$Width * $src.Height / $src.Width)
        $small = New-Object System.Drawing.Bitmap($Width, $h)
        $g = [System.Drawing.Graphics]::FromImage($small)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($src, 0, 0, $Width, $h)
        $g.Dispose(); $src.Dispose()

        $hbm = $small.GetHbitmap()
        try {
            $bs = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHBitmap(
                $hbm, [IntPtr]::Zero, [System.Windows.Int32Rect]::Empty,
                [System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
            $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bs))
        } finally { [void][DwGdi]::DeleteObject($hbm); $small.Dispose() }
    }
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms); $bytes = $ms.ToArray(); $ms.Dispose()

    $units = [int][math]::Max(2, [math]::Round($DelayMs / 10.0))
    $lo = [byte]($units -band 0xFF); $hi = [byte](($units -shr 8) -band 0xFF)
    for ($i = 0; $i -lt $bytes.Length - 8; $i++) {
        if ($bytes[$i] -eq 0x21 -and $bytes[$i+1] -eq 0xF9 -and $bytes[$i+2] -eq 0x04) {
            $bytes[$i+4] = $lo; $bytes[$i+5] = $hi
        }
    }
    $insertAt = 13
    if ($bytes[10] -band 0x80) { $insertAt += 3 * [math]::Pow(2, ($bytes[10] -band 0x07) + 1) }
    $insertAt = [int]$insertAt
    $netscape = [byte[]]@(0x21,0xFF,0x0B,
        0x4E,0x45,0x54,0x53,0x43,0x41,0x50,0x45,0x32,0x2E,0x30,
        0x03,0x01,0x00,0x00,0x00)
    # Through a stream, not by slicing: $bytes[0..$n] returns Object[] in
    # PowerShell, which every byte consumer downstream rejects.
    $out = New-Object System.IO.MemoryStream
    $out.Write($bytes, 0, $insertAt)
    $out.Write($netscape, 0, $netscape.Length)
    $out.Write($bytes, $insertAt, $bytes.Length - $insertAt)
    [IO.File]::WriteAllBytes($Path, $out.ToArray())
    $out.Dispose()
}

# --------------------------------------------------------------------------
# The fixture, built by DiscWright's own functions so the recording shows the
# real application working on a real disc.
# --------------------------------------------------------------------------

$appPath = Join-Path $root 'DiscWright.ps1'
$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($appPath, [ref]$null, [ref]$errs)
if ($errs -and $errs.Count) { throw "DiscWright.ps1 has $($errs.Count) parse errors" }
foreach ($f in $ast.FindAll({ param($n)
        $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($f.Extent.Text))
}
$script:PROJECT_FILE = 'discproject.json'

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

function New-Installer([string]$dir, [string]$name, [double]$mb) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $fs = [IO.File]::Create((Join-Path $dir $name)); $fs.SetLength([long]($mb * 1MB)); $fs.Close()
    return (Join-Path $dir $name)
}

$gameDir = Join-Path $WorkDir 'GOG Downloads\night_courier'
$null    = New-Installer $gameDir 'setup_night_courier_1.0.exe' 6
$patch   = New-Installer $gameDir 'patch_night_courier_1.0_to_1.2.exe' 2

$bmp = New-Object System.Drawing.Bitmap(1280,720)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(18,26,42)); $g.Dispose()
$art = Join-Path $WorkDir 'cover.png'
$bmp.Save($art, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

$projOut = Join-Path $WorkDir 'Night Courier Disc'
New-Item -ItemType Directory -Force -Path $projOut | Out-Null
$null = Invoke-Build @{
    Games=@((Get-GameInfo $gameDir)); Label='Night Courier'; IconPath=$art; IconIsIco=$false
    Menu=$true; BgPath=$art; BgAsIs=$false; PanelSide='Right'
    Divider=$false; ShowTitle=$false; TitleText=''
    WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
    Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
    ExtraItems=@(); OutDir=$projOut
} { param($m) $null = $m }

$ffmpeg = Find-Ffmpeg -Explicit $FfmpegPath
Write-Host "recording '$Scenario' at $Fps fps"
Write-Host "  fixture : $WorkDir"
Write-Host "  encoder : $(if ($ffmpeg) { 'ffmpeg' } else { 'GifBitmapEncoder (install ffmpeg for smaller files)' })"

$app = $null
try {
    $app = Start-DiscWright -AppPath $appPath
    $win = $app.Window
    Start-Sleep -Milliseconds 800
    Add-Frames -Win $win -CropHeight $CropHeight -Seconds 1.2 -Fps $Fps

    if ($Scenario -eq 'AddAndBuild') {
        Write-Host '  opening the built disc'
        Set-CtlText -Ctl (Get-BoxAfter -Win $win -LabelLike '6)  Output folder*') -Text $projOut
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 0.8 -Fps $Fps
        Invoke-CtlNamed -Root $win -NameLike 'Open existing disc*' | Out-Null
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 0.8 -Fps $Fps
        Complete-FolderDialog -Win $win | Out-Null
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 1.8 -Fps $Fps

        Write-Host '  adding a patch as an add-on'
        Invoke-CtlNamed -Root $win -NameLike 'Add-on*' | Out-Null
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 0.8 -Fps $Fps
        Complete-FileDialog -Win $win -TitleLike 'Pick one or more add-on*' -Files @($patch) | Out-Null
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 2.4 -Fps $Fps

        Write-Host '  selecting it, then removing it'
        Select-ListRow -Win $win -Index 1
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 1.2 -Fps $Fps
        Invoke-CtlNamed -Root $win -NameLike 'Remove' | Out-Null
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 1.8 -Fps $Fps
    }
    else {
        Add-Frames -Win $win -CropHeight $CropHeight -Seconds 3.0 -Fps $Fps
    }
}
finally { Stop-DiscWright $app }

$dir = Split-Path $OutFile -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

if ($ffmpeg) { Write-GifFfmpeg -Ffmpeg $ffmpeg -FrameDir $script:FrameDir -Path $OutFile -Fps $Fps -Width $Width }
else         { Write-GifFallback -FrameDir $script:FrameDir -Path $OutFile -DelayMs ([int](1000/$Fps)) -Width $Width }

Remove-Item $script:FrameDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue

$size = (Get-Item $OutFile).Length
Write-Host ("wrote {0}  ({1} frames, {2:N0} KB)" -f $OutFile, $script:FrameNo, ($size / 1KB))
