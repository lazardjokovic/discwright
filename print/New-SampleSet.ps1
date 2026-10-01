<#
  Render every combination the format tables describe, into a folder outside
  the repository, so they can be looked at and printed.

  WHY A FOLDER OUTSIDE THE REPOSITORY

  These are build outputs, not source. They are large, they are binary, and
  they are regenerated from the tables every time this runs, so git should
  never see them. They also need to be somewhere a person can open without
  going through a checkout.

  WHY EVERY COMBINATION RATHER THAN ONE NICE ONE

  Because a renderer that has only ever run for a standard DVD case will have
  something wrong with it for a Blu-ray case, and nobody will find out until
  somebody prints one. Rendering the whole table on every run is cheap and
  turns an unknown into a file that either looks right or does not.

      .\New-SampleSet.ps1
      .\New-SampleSet.ps1 -OutRoot D:\somewhere\else
#>
[CmdletBinding()]
param(
    # Deliberately not the Desktop and not anything inside OneDrive: these are
    # regenerated files and should not be synced anywhere.
    [string]$OutRoot = (Join-Path $env:USERPROFILE 'DiscWright-Lab'),
    # A real discproject.json, to see the artwork with a real title, real game
    # names and the cover art the person actually chose. Placeholder text hides
    # the problems that only turn up with a real picture in the panel.
    [string]$ProjectPath,
    [switch]$KeepExisting
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DiscWright.Print.ps1')

# A PDF carries its real size, which is what makes it printable, but nothing
# previews it at a glance. The page image is already inside the file, so it
# comes back out beside it as a JPEG for browsing.
function Save-Preview([string]$pdf) {
    $bytes = [IO.File]::ReadAllBytes($pdf)
    $start = -1
    for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
        if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xD8) { $start = $i; break }
    }
    $end = -1
    for ($i = $bytes.Length - 2; $i -gt $start; $i--) {
        if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xD9) { $end = $i + 1; break }
    }
    if ($start -lt 0 -or $end -lt 0) { return $null }
    $jpg = [IO.Path]::ChangeExtension($pdf, '.preview.jpg')
    [IO.File]::WriteAllBytes($jpg, $bytes[$start..$end])
    return $jpg
}

$folders = @{
    Samples     = Join-Path $OutRoot 'samples'
    Wraps       = Join-Path $OutRoot 'combinations\wraps'
    Faces       = Join-Path $OutRoot 'combinations\disc-faces'
    Calibration = Join-Path $OutRoot 'combinations\calibration'
}

if ((Test-Path $OutRoot) -and -not $KeepExisting) {
    # Everything here is regenerated, so a stale file from a renderer that has
    # since changed is worse than no file at all.
    Get-ChildItem -LiteralPath $OutRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.pdf', '.png', '.jpg', '.txt' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}
foreach ($f in $folders.Values) { New-Item -ItemType Directory -Force -Path $f | Out-Null }

$made = New-Object System.Collections.ArrayList
function Add-Made([string]$kind, [string]$path, [string]$note) {
    if (-not $path) { return }
    $null = $made.Add([pscustomobject]@{
        Kind = $kind
        File = $path.Substring($OutRoot.Length).TrimStart('\')
        Size = '{0:N0} KB' -f ((Get-Item $path).Length / 1KB)
        Note = $note
    })
}

Write-Output "Rendering into $OutRoot"

# ---- every case, on every page size: the wrap, and the sheet that measures it
foreach ($case in $script:CaseFormats) {
    foreach ($page in 'a4', 'letter') {
        $wrap = Join-Path $folders.Wraps "wrap-$($case.Key)-$page.pdf"
        try {
            $null = New-CaseWrap -OutPdf $wrap -Case $case.Key -Page $page `
                                 -Title 'Gothic' -Subtitle 'GOG edition, 2001' `
                                 -Contents @('Gothic', 'Gothic II: Gold Edition', 'Manuals and soundtrack')
            $null = Save-Preview $wrap
            Add-Made 'wrap' $wrap "$($case.Name), $page"
        } catch {
            # A combination that cannot work should say so here rather than at
            # the printer. A CD insert on Letter is a real example.
            Write-Output "  skipped $($case.Key) on $page : $($_.Exception.Message.Split([char]10)[0])"
        }

        $calib = Join-Path $folders.Calibration "calibration-$($case.Key)-$page.pdf"
        try {
            $null = New-CalibrationSheet -OutPdf $calib -Case $case.Key -Page $page
            $null = Save-Preview $calib
            Add-Made 'calibration' $calib "$($case.Name), $page"
        } catch {
            Write-Output "  skipped calibration $($case.Key) on $page : $($_.Exception.Message.Split([char]10)[0])"
        }
    }
}

# ---- every disc, and a long title on each, since that is where type breaks
foreach ($disc in $script:DiscFormats) {
    $short = Join-Path $folders.Faces "face-$($disc.Key)-short-title.png"
    $null = New-DiscFace -OutPng $short -Disc $disc.Key -Title 'Gothic' -Subtitle 'GOG edition'
    Add-Made 'disc face' $short "$($disc.Name), short title"

    $long = Join-Path $folders.Faces "face-$($disc.Key)-long-title.png"
    $null = New-DiscFace -OutPng $long -Disc $disc.Key -Accent '#5A1E1E' `
                         -Title 'The Chronicles of Riddick: Escape from Butcher Bay' `
                         -Subtitle 'Two games, one disc'
    Add-Made 'disc face' $long "$($disc.Name), long title"
}

# ---- the pair worth actually printing first, kept together
$sampleWrap = Join-Path $folders.Samples 'gothic-wrap-dvd-a4.pdf'
$null = New-CaseWrap -OutPdf $sampleWrap -Title 'Gothic' -Subtitle 'GOG edition, 2001' `
                     -Contents @('Gothic', 'Gothic II: Gold Edition', 'Manuals and soundtrack')
$null = Save-Preview $sampleWrap
Add-Made 'sample' $sampleWrap 'standard DVD case, A4, with crop marks'

$sampleFace = Join-Path $folders.Samples 'gothic-disc-face-hub.png'
$null = New-DiscFace -OutPng $sampleFace -Title 'Gothic' -Subtitle 'GOG edition'
Add-Made 'sample' $sampleFace 'hub-printable disc, for Epson Photo+'

$sampleCalib = Join-Path $folders.Samples 'calibration-dvd-a4.pdf'
$null = New-CalibrationSheet -OutPdf $sampleCalib
$null = Save-Preview $sampleCalib
Add-Made 'sample' $sampleCalib 'print this one first, on plain paper'

# ---- a real disc, if one was named
if ($ProjectPath) {
    $fromProject = Join-Path $folders.Samples 'from-a-real-project'
    New-Item -ItemType Directory -Force -Path $fromProject | Out-Null
    try {
        $art = New-ArtworkForProject -ProjectPath $ProjectPath -OutDir $fromProject
        $null = Save-Preview $art.Wrap
        $cover = if ($art.UsedCover) { 'with its own cover art' } else { 'no cover art found' }
        Add-Made 'project' $art.Wrap "$($art.Title), $cover"
        Add-Made 'project' $art.DiscFace "$($art.Title), hub-printable"
    } catch {
        Write-Output "  could not read $ProjectPath : $($_.Exception.Message.Split([char]10)[0])"
    }
}

# ---- what is here, so the folder explains itself
$readme = Join-Path $OutRoot 'README.txt'
$lines = New-Object System.Collections.ArrayList
$null = $lines.Add('DiscWright print output')
$null = $lines.Add('=======================')
$null = $lines.Add('')
$null = $lines.Add("Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm') by print\New-SampleSet.ps1.")
$null = $lines.Add('Everything here is regenerated on every run. Nothing here is source,')
$null = $lines.Add('and nothing here is synced anywhere, so edits will be lost.')
$null = $lines.Add('')
$null = $lines.Add('samples\      the three worth printing first')
$null = $lines.Add('combinations\ every case and every disc the format table knows')
$null = $lines.Add('')
$null = $lines.Add('Print PDFs at 100%, with scaling or "fit to page" turned OFF.')
$null = $lines.Add('The .preview.jpg beside each PDF is only for looking at: it has no')
$null = $lines.Add('physical size and must not be printed.')
$null = $lines.Add('')
$null = $lines.Add('Disc faces are PNGs for Epson Photo+ or Canon Easy-PhotoPrint, which')
$null = $lines.Add('set the diameters and the tray alignment themselves.')
$null = $lines.Add('')
$null = $lines.Add('FILES')
$null = $lines.Add('')
foreach ($m in $made) {
    $null = $lines.Add(('  {0,-12} {1,-46} {2,9}  {3}' -f $m.Kind, $m.File, $m.Size, $m.Note))
}
Set-Content -LiteralPath $readme -Value $lines -Encoding UTF8

Write-Output ''
Write-Output ("{0} files written" -f $made.Count)
$made | Group-Object Kind | ForEach-Object { Write-Output ("  {0,-12} {1}" -f $_.Name, $_.Count) }
Write-Output ''
Write-Output "Open: $OutRoot"
