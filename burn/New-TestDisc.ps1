<#
  Build a small disc worth burning to a real CD-R.

  WHY A PURPOSE-BUILT DISC

  The things a first burn has to settle are whether the bytes survive, whether
  autorun fires from real optical media rather than from a mounted image, and
  whether the menu works off the disc. None of that needs a large game, and a
  small disc burns in minutes instead of most of an hour. Burning a real 8 GB
  game to find out that autorun works would be a poor trade.

  It is built by DiscWright's own functions, not by a fixture that imitates
  them, because a disc built by anything else proves nothing about the app.

      .\New-TestDisc.ps1
      .\New-TestDisc.ps1 -OutDir D:\burn-test -GameMb 40 -LegacyFs
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $env:USERPROFILE 'DiscWright-Lab\burn-test'),
    # Two games at this size each, plus the menu. Small on purpose.
    # Big enough that the drive has to sustain a write for a minute or two,
    # small enough to stay well inside a CD-R. A 50 MB disc would be over
    # before the drive settled.
    [double]$GameMb = 120,
    [string]$Label = 'DISCWRIGHT TEST',
    # Also write ISO9660 and Joliet, so the disc reads on something old. Worth
    # a disc of its own later rather than mixing it into the first one.
    [switch]$LegacyFs
)

$ErrorActionPreference = 'Stop'
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
$script:APP_VERSION = [regex]::Match((Get-Content $appScript -Raw),
    "\`$APP_VERSION\s*=\s*'([^']+)'").Groups[1].Value

$work = Join-Path $OutDir 'src'
foreach ($d in $OutDir, $work) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

function New-Art([string]$path, [int]$w, [int]$h, [int[]]$rgb) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb($rgb[0], $rgb[1], $rgb[2]))
    $font = New-Object System.Drawing.Font('Segoe UI', 36)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('DiscWright burn test', $font, $brush, 40, 40)
    $font.Dispose(); $brush.Dispose(); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

<#
    A real program, so the disc can be told to run it and actually run it.

    The first test disc carried 120 MB of random bytes with an .exe name.
    Windows could not parse a PE header, fell back to assuming MS-DOS, and
    said "Unsupported 16-Bit Application". That proved the menu launches the
    right path from the disc and proved nothing about running anything.

    So this compiles a genuine console program and pads it out afterwards.
    Bytes appended past the end of a PE image are ignored by the loader, which
    is how the file can be both a working executable and large enough to make
    the drive work for its living.
#>
function New-FakeGame([string]$slug, [double]$mb) {
    $dir = Join-Path $work $slug
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $stem = "setup_${slug}_1.0_(90210)"
    $path = Join-Path $dir "$stem.exe"

    $source = @"
using System;
using System.IO;
using System.Windows.Forms;
public static class FakeInstaller {
    [STAThread]
    public static int Main(string[] args) {
        string where = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string nl = Environment.NewLine;
        string note = "DiscWright test installer for $slug" + nl + nl + "It ran from:" + nl + where;
        try {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "discwright-installer-ran.txt"),
                DateTime.Now.ToString("s") + "  |  " + where + nl);
        } catch { }
        MessageBox.Show(note, "DiscWright test installer",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
"@
    Add-Type -TypeDefinition $source -OutputAssembly $path `
             -OutputType ConsoleApplication `
             -ReferencedAssemblies 'System.Windows.Forms', 'System.Drawing'

    $realBytes = (Get-Item -LiteralPath $path).Length
    $target = [long]($mb * 1MB)
    if ($target -gt $realBytes) {
        # Appended, not written over: the PE stays intact and the loader never
        # looks past where the headers say the image ends.
        $fs = [IO.File]::Open($path, [IO.FileMode]::Append)
        try {
            $buf = New-Object byte[] (1MB)
            $rand = New-Object Random 1234
            $left = $target - $realBytes
            while ($left -gt 0) {
                # Random rather than zeroes: a run of zeroes tells us nothing
                # about whether the drive wrote what it was given.
                $rand.NextBytes($buf)
                $take = [int][Math]::Min($buf.Length, $left)
                $fs.Write($buf, 0, $take)
                $left -= $take
            }
        } finally { $fs.Close() }
    }
    return $dir
}

Write-Output "Building a test disc in $OutDir"
$bg   = New-Art (Join-Path $OutDir 'bg.png')  1280 720 @(18, 38, 58)
$icon = New-Art (Join-Path $OutDir 'art.png')  512 512 @(27, 42, 65)

# Two distinct names: 'gothic' and 'gothic2' both come back as Gothic, and a
# menu with two buttons reading the same thing tests nothing about choosing
# between them.
$folders = @((New-FakeGame 'gothic' $GameMb), (New-FakeGame 'arcanum' $GameMb))
$games = @()
foreach ($f in $folders) {
    # Get-GameInfo is the GOG path, which is what these folders imitate.
    # Get-FolderInfo is the newer one for a folder of loose game files, and it
    # would describe these differently.
    $info = Get-GameInfo $f
    if (-not $info -or -not $info.Ok) {
        throw "DiscWright did not recognise $f as a GOG folder: $($info.Msg)"
    }
    $games += , $info
}
Write-Output "  games: $(($games | ForEach-Object { $_.GameName }) -join ', ')"

$settings = @{
    Games = $games; Label = $Label; IconPath = $icon; IconIsIco = $false
    Menu = $true; BgPath = $bg; BgAsIs = $false; PanelSide = 'Right'
    Divider = $false; ShowTitle = $true; TitleText = 'DiscWright burn test'
    WindowBorder = $true; ButtonStyle = 'Minimal'; MusicFile = $null
    Buttons = @('Play', 'Install', 'Exit'); ManualPath = $null; ExtrasPath = $null
    ExtraItems = @(); OutDir = $OutDir; LinuxInfo = $false; LegacyFs = [bool]$LegacyFs
}

$iso = Invoke-Build $settings { param($line) Write-Verbose $line }

$isoItem = Get-Item -LiteralPath $iso
Write-Output ''
Write-Output "ISO    : $($isoItem.FullName)"
Write-Output ("size   : {0:N1} MB" -f ($isoItem.Length / 1MB))
Write-Output "staging: $(Join-Path $OutDir 'disc')"
Write-Output "label  : $Label"
Write-Output ''
if ($isoItem.Length -gt (700MB)) {
    Write-Warning 'That is larger than a CD-R. Lower -GameMb or use a DVD.'
} else {
    Write-Output 'Fits a CD-R. Next:'
    Write-Output "  ..\burn\Test-BurnerSetup.ps1 -IsoPath `"$($isoItem.FullName)`""
}
