<#
.SYNOPSIS
    Records DiscWright being driven through a scripted sequence and writes an
    animated GIF of it.

.DESCRIPTION
    The README's demonstrations are the first thing anyone sees, and re-recording
    them by hand after every change is the reason they go stale. This drives the
    window the same way the tests in tests/ui do, photographing it as it goes,
    and assembles the frames into a GIF.

    No ffmpeg, no install. WPF's GifBitmapEncoder writes a multi-frame GIF but
    leaves out the two things that make it an animation - the per-frame delay and
    the loop instruction - so both are patched into the byte stream afterwards.
    See Write-AnimatedGif for what gets edited and where.

    LEAVE THE MACHINE ALONE while it records. It drives the real pointer and the
    real foreground window.

.PARAMETER OutFile
    Where to write the .gif.

.PARAMETER Scenario
    Which sequence to record.
      AddAndBuild  - open a prepared disc, add an add-on, remove one
      Tour         - just the window, scrolled top to bottom

.PARAMETER Fps
    Frames captured per second. 5 is plenty for showing an interface and keeps
    the file small; a GIF holds 256 colours and every frame costs real bytes.

.PARAMETER Width
    Scale the window down to this width. The window is 700px wide; 560 keeps
    text legible at a fraction of the size.

.EXAMPLE
    .\tools\New-DemoGif.ps1 -OutFile docs\add-on.gif
#>
# Write-Host is this script's output: it reports what it is recording as it goes.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'A recorder whose console output is progress for a person watching it. Nothing consumes it as data.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'Frame capture must not abort a recording because one grab failed while a window was repainting.')]
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutFile,
    [ValidateSet('AddAndBuild','Tour')][string]$Scenario = 'AddAndBuild',
    [ValidateRange(2,15)][int]$Fps = 5,
    [ValidateRange(200,1200)][int]$Width = 560,
    # Keep only the top N pixels of the window. GifBitmapEncoder writes every
    # frame whole, with no differencing between them, so the file size is very
    # nearly (pixels x frames) - and a demonstration of step 1 does not need the
    # 700 pixels of settings underneath it. 0 keeps the whole window.
    [ValidateRange(0,2000)][int]$CropHeight = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'tests\ui\UiDriver.psm1') -Force
Add-Type -AssemblyName System.Drawing, PresentationCore, WindowsBase

if (-not (Test-UiAvailable)) { throw 'no usable desktop to record' }

# --------------------------------------------------------------------------
# Recording
# --------------------------------------------------------------------------

$script:Frames = @()

function Add-Frame {
    <#  .SYNOPSIS One photograph of the window, cropped and scaled down. #>
    param($Win, [int]$TargetWidth, [int]$CropHeight = 0)
    try {
        $r = $Win.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { return }
        $grabH = [int]$r.Height
        if ($CropHeight -gt 0 -and $CropHeight -lt $grabH) { $grabH = $CropHeight }
        $full = New-Object System.Drawing.Bitmap([int]$r.Width, $grabH)
        $g = [System.Drawing.Graphics]::FromImage($full)
        $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $full.Size)
        $g.Dispose()

        $h = [int]([double]$TargetWidth * $full.Height / $full.Width)
        $small = New-Object System.Drawing.Bitmap($TargetWidth, $h)
        $g2 = [System.Drawing.Graphics]::FromImage($small)
        $g2.InterpolationMode = 'HighQualityBicubic'
        $g2.DrawImage($full, 0, 0, $TargetWidth, $h)
        $g2.Dispose()
        $full.Dispose()
        $script:Frames += $small
    } catch { }
}

function Add-Frames {
    <#  .SYNOPSIS Hold on the current state for a moment, so a viewer can read it. #>
    param($Win, [int]$TargetWidth, [double]$Seconds, [int]$Fps, [int]$CropHeight = 0)
    $n = [int][math]::Max(1, [math]::Round($Seconds * $Fps))
    $gap = [int](1000 / $Fps)
    for ($i = 0; $i -lt $n; $i++) {
        Add-Frame -Win $Win -TargetWidth $TargetWidth -CropHeight $CropHeight
        Start-Sleep -Milliseconds $gap
    }
}

# --------------------------------------------------------------------------
# Writing the GIF
# --------------------------------------------------------------------------

function Write-AnimatedGif {
    <#
    .SYNOPSIS
        Write frames as a looping animated GIF.

    .DESCRIPTION
        GifBitmapEncoder happily writes every frame, and writes a Graphic Control
        Extension in front of each one - with a delay of zero, which most viewers
        render as "as fast as possible". It also omits the Netscape application
        extension that says how many times to loop, so the animation plays once.

        Both are fixable in the finished bytes:

          - Every Graphic Control Extension starts 21 F9 04. The two bytes after
            the packed field are the delay in hundredths of a second, so each one
            gets the frame delay written into it.
          - The Netscape block goes immediately after the Global Colour Table,
            whose size is declared in the Logical Screen Descriptor. Its payload
            is a loop count of 0, meaning forever.
    #>
    param([System.Drawing.Bitmap[]]$Frames, [string]$Path, [int]$DelayMs)

    if (-not $Frames -or $Frames.Count -eq 0) { throw 'nothing was recorded' }

    $enc = New-Object System.Windows.Media.Imaging.GifBitmapEncoder
    foreach ($f in $Frames) {
        $h = $f.GetHbitmap()
        try {
            $src = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHBitmap(
                $h, [IntPtr]::Zero, [System.Windows.Int32Rect]::Empty,
                [System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
            $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($src))
        } finally {
            [void][DwGdi]::DeleteObject($h)
        }
    }
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    $bytes = $ms.ToArray()
    $ms.Dispose()

    # --- per-frame delay -------------------------------------------------
    $delayUnits = [int][math]::Max(2, [math]::Round($DelayMs / 10.0))   # hundredths
    $lo = [byte]($delayUnits -band 0xFF)
    $hi = [byte](($delayUnits -shr 8) -band 0xFF)
    $patched = 0
    for ($i = 0; $i -lt $bytes.Length - 8; $i++) {
        if ($bytes[$i] -eq 0x21 -and $bytes[$i+1] -eq 0xF9 -and $bytes[$i+2] -eq 0x04) {
            $bytes[$i+4] = $lo      # delay low byte
            $bytes[$i+5] = $hi      # delay high byte
            $patched++
        }
    }

    # --- loop forever ----------------------------------------------------
    # Header 6 + Logical Screen Descriptor 7 = 13, then the Global Colour Table
    # if bit 7 of byte 10 is set, sized 3 * 2^(N+1) where N is the low 3 bits.
    $insertAt = 13
    if ($bytes[10] -band 0x80) {
        $n = $bytes[10] -band 0x07
        $insertAt += 3 * [math]::Pow(2, $n + 1)
    }
    $insertAt = [int]$insertAt
    $netscape = [byte[]]@(
        0x21, 0xFF, 0x0B,
        0x4E,0x45,0x54,0x53,0x43,0x41,0x50,0x45,0x32,0x2E,0x30,   # NETSCAPE2.0
        0x03, 0x01, 0x00, 0x00,                                    # loop count 0 = forever
        0x00
    )
    # Written through a stream rather than by slicing: $bytes[0..$n] in PowerShell
    # comes back as Object[], not byte[], and every consumer downstream rejects it.
    $outMs = New-Object System.IO.MemoryStream
    $outMs.Write($bytes, 0, $insertAt)
    $outMs.Write($netscape, 0, $netscape.Length)
    $outMs.Write($bytes, $insertAt, $bytes.Length - $insertAt)
    $final = $outMs.ToArray()
    $outMs.Dispose()

    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [IO.File]::WriteAllBytes($Path, $final)
    return [pscustomobject]@{ Frames = $Frames.Count; DelaysPatched = $patched; Bytes = $final.Length }
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

# --------------------------------------------------------------------------
# Scenarios
# --------------------------------------------------------------------------

$appPath = Join-Path $root 'DiscWright.ps1'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('dwgif_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force -Path $sandbox | Out-Null

# The fixture is built by DiscWright's own functions, so the recording shows the
# real application working on a real disc rather than a mock-up.
$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($appPath, [ref]$null, [ref]$errs)
if ($errs -and $errs.Count) { throw "DiscWright.ps1 has $($errs.Count) parse errors" }
foreach ($f in $ast.FindAll({ param($n)
        $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($f.Extent.Text))
}
$script:PROJECT_FILE = 'discproject.json'

function New-Installer([string]$dir, [string]$name, [double]$mb) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $fs = [IO.File]::Create((Join-Path $dir $name)); $fs.SetLength([long]($mb * 1MB)); $fs.Close()
    return (Join-Path $dir $name)
}

$gameDir = Join-Path $sandbox 'src\night_courier'
$null    = New-Installer $gameDir 'setup_night_courier_1.0.exe' 6
$patch   = New-Installer $gameDir 'patch_night_courier_1.0_to_1.2.exe' 2

$bmp = New-Object System.Drawing.Bitmap(1280,720)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(18,26,42)); $g.Dispose()
$art = Join-Path $sandbox 'art.png'
$bmp.Save($art, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

$projOut = Join-Path $sandbox 'built'
New-Item -ItemType Directory -Force -Path $projOut | Out-Null
$null = Invoke-Build @{
    Games=@((Get-GameInfo $gameDir)); Label='Night Courier'; IconPath=$art; IconIsIco=$false
    Menu=$true; BgPath=$art; BgAsIs=$false; PanelSide='Right'
    Divider=$false; ShowTitle=$false; TitleText=''
    WindowBorder=$true; ButtonStyle='Minimal'; MusicFile=$null
    Buttons=@('Play','Install','Exit'); ManualPath=$null; ExtrasPath=$null
    ExtraItems=@(); OutDir=$projOut
} { param($m) $null = $m }

Write-Host "recording '$Scenario' at $Fps fps, $Width px wide"
$app = $null
try {
    $app = Start-DiscWright -AppPath $appPath
    $win = $app.Window
    Start-Sleep -Milliseconds 800

    Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 1.2 -Fps $Fps

    if ($Scenario -eq 'AddAndBuild') {
        Write-Host '  opening the built disc'
        Set-CtlText -Ctl (Get-BoxAfter -Win $win -LabelLike '6)  Output folder*') -Text $projOut
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 0.8 -Fps $Fps

        Invoke-CtlNamed -Root $win -NameLike 'Open existing disc*' | Out-Null
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 0.8 -Fps $Fps
        Complete-FolderDialog -Win $win | Out-Null
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 1.8 -Fps $Fps

        Write-Host '  adding a patch as an add-on'
        Invoke-CtlNamed -Root $win -NameLike 'Add-on*' | Out-Null
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 0.8 -Fps $Fps
        Complete-FileDialog -Win $win -TitleLike 'Pick one or more add-on*' -Files @($patch) | Out-Null
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 2.2 -Fps $Fps

        Write-Host '  selecting and removing it again'
        Select-ListRow -Win $win -Index 1
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 1.0 -Fps $Fps
        Invoke-CtlNamed -Root $win -NameLike 'Remove' | Out-Null
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 1.8 -Fps $Fps
    }
    else {
        Add-Frames -Win $win -TargetWidth $Width -CropHeight $CropHeight -Seconds 3.0 -Fps $Fps
    }
}
finally {
    Stop-DiscWright $app
}

$info = Write-AnimatedGif -Frames $script:Frames -Path $OutFile -DelayMs ([int](1000 / $Fps))
foreach ($f in $script:Frames) { $f.Dispose() }
Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ("wrote {0}  ({1} frames, {2} delays patched, {3:N0} KB)" -f
    $OutFile, $info.Frames, $info.DelaysPatched, ($info.Bytes / 1KB))
