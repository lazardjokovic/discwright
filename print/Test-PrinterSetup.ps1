<#
  Everything about a printer that can be checked before any paper moves.

  WHY

  A wrap is 273 mm wide and a disc face is 120 mm across. Both are close enough
  to the limits of an A4 page that the printer's own unprintable margins decide
  whether they fit, and a driver that cannot fit a page quietly scales it. That
  is the failure the public guide blames on an "expansion" slider, and it costs
  a sheet of photo paper to discover by printing.

  Windows will say all of it in advance: the sizes offered, the area actually
  printable, whether borderless exists, which trays and media types the driver
  exposes, and at what resolutions. So ask first.

  WHAT IT CANNOT TELL YOU

  Whether 100 mm on the page is 100 mm in your hand. That depends on the
  settings chosen at print time, so the calibration sheet still gets printed on
  one sheet of plain paper. And nothing here touches tray alignment for discs,
  which stays the printer software's job.

  USAGE

      .\Test-PrinterSetup.ps1                 # every printer on the machine
      .\Test-PrinterSetup.ps1 -Printer 'EPSON L8050 Series'
#>
[CmdletBinding()]
param(
    [string]$Printer
)

Add-Type -AssemblyName System.Printing
. (Join-Path $PSScriptRoot 'DiscWright.Print.ps1')

# The print API measures in device independent pixels, 96 to the inch,
# regardless of what the printer does in hardware.
function ConvertFrom-Dip([double]$dip) { return [Math]::Round($dip / 96.0 * 25.4, 1) }

function Get-ImageableArea($queue, [string]$mediaName, [string]$orientation) {
    # Capabilities depend on the ticket: the printable area of a page is not a
    # property of the printer, it is a property of the printer plus the paper
    # plus which way round it goes.
    $ticket = New-Object System.Printing.PrintTicket
    $ticket.PageMediaSize = New-Object System.Printing.PageMediaSize(
        [System.Printing.PageMediaSizeName]::$mediaName)
    $ticket.PageOrientation = [System.Printing.PageOrientation]::$orientation
    try { $caps = $queue.GetPrintCapabilities($ticket) } catch { return $null }
    if (-not $caps.PageImageableArea) { return $null }
    $a = $caps.PageImageableArea
    return @{
        LeftMm   = ConvertFrom-Dip $a.OriginWidth
        TopMm    = ConvertFrom-Dip $a.OriginHeight
        WidthMm  = ConvertFrom-Dip $a.ExtentWidth
        HeightMm = ConvertFrom-Dip $a.ExtentHeight
    }
}

function Write-Section([string]$title) { "`n$title`n$('-' * $title.Length)" }

<#
    Does each piece of artwork fit inside an area the printer will really put
    ink on? Separated from the reporting so it can be tested without a printer,
    because this verdict is the whole point of the script and "it printed
    something" is not a check.
#>
function Get-FitReport {
    param(
        [Parameter(Mandatory)][double]$AreaWidthMm,
        [Parameter(Mandatory)][double]$AreaHeightMm
    )
    $rows = @()
    foreach ($c in $script:CaseFormats) {
        $rows += , [pscustomobject]@{
            Name     = $c.Name
            WidthMm  = [double]$c.WrapWidthMm
            HeightMm = [double]$c.HeightMm
            Fits     = ($c.WrapWidthMm -le $AreaWidthMm) -and ($c.HeightMm -le $AreaHeightMm)
        }
    }
    # A disc face on paper is the full disc, whatever the printable ring inside
    # it turns out to be.
    $rows += , [pscustomobject]@{
        Name     = 'A disc face, printed on paper'
        WidthMm  = 120.0
        HeightMm = 120.0
        Fits     = (120 -le $AreaWidthMm) -and (120 -le $AreaHeightMm)
    }
    return $rows
}

function Test-OnePrinter($queue) {
    Write-Section "PRINTER: $($queue.Name)"
    "  driver     : $($queue.QueueDriver.Name)"
    "  port       : $($queue.QueuePort.Name)"
    "  status     : $($queue.QueueStatus)"
    if ($queue.IsOffline) { "  OFFLINE, so nothing below is worth acting on" }

    $caps = $queue.GetPrintCapabilities()

    Write-Section 'What it offers'
    $sizes = @($caps.PageMediaSizeCapability | ForEach-Object { $_.PageMediaSizeName })
    "  page sizes : $($sizes.Count) offered"
    foreach ($want in 'ISOA4', 'NorthAmericaLetter') {
        $has = $sizes -contains $want
        "  $($want.PadRight(11)): $(if ($has) { 'yes' } else { 'NO' })"
    }
    $bl = @($caps.PageBorderlessCapability)
    "  borderless : $(if ($bl -contains 'Borderless') { 'yes' } elseif ($bl.Count) { $bl -join ', ' } else { 'not reported by the driver' })"
    $res = @($caps.PageResolutionCapability | ForEach-Object { "$($_.X)x$($_.Y)" } | Select-Object -Unique)
    "  resolutions: $(if ($res.Count) { $res -join ', ' } else { 'not reported' })"
    $bins = @($caps.InputBinCapability)
    "  input bins : $(if ($bins.Count) { $bins -join ', ' } else { 'not reported' })"
    $media = @($caps.PageMediaTypeCapability)
    "  media types: $(if ($media.Count) { $media -join ', ' } else { 'not reported' })"

    # The strongly typed properties above come from a fixed list that predates
    # disc printing, so an Epson disc tray cannot appear in them. The raw
    # capabilities document does carry vendor features, so look there too.
    Write-Section 'Vendor features the driver names itself'
    try {
        $stream = $queue.GetPrintCapabilitiesAsXml()
        $text = (New-Object IO.StreamReader($stream)).ReadToEnd()
        $pattern = 'name="([^"]*(?:CD|DVD|Disc|Borderless|Glossy|Photo|Tray|Matte)[^"]*)"'
        $hits = @([regex]::Matches($text, $pattern) |
                  ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if ($hits.Count) { $hits | ForEach-Object { "  $_" } }
        else { '  nothing matching disc, tray, borderless or photo media' }
    } catch {
        "  could not read the capabilities document: $($_.Exception.Message)"
    }

    Write-Section 'Will the artwork fit without being scaled'
    foreach ($page in @(
        @{ Name = 'A4 landscape';     Media = 'ISOA4';              Orientation = 'Landscape' }
        @{ Name = 'Letter landscape'; Media = 'NorthAmericaLetter'; Orientation = 'Landscape' }
        @{ Name = 'A4 portrait';      Media = 'ISOA4';              Orientation = 'Portrait' }
    )) {
        $area = Get-ImageableArea $queue $page.Media $page.Orientation
        if (-not $area) { "  $($page.Name): the driver would not say"; continue }
        "  $($page.Name): prints $($area.WidthMm) x $($area.HeightMm) mm, " +
        "margins $($area.LeftMm) mm left and $($area.TopMm) mm top"
        foreach ($row in (Get-FitReport -AreaWidthMm $area.WidthMm -AreaHeightMm $area.HeightMm)) {
            $note = if ($row.Fits) { 'fits' } else { 'DOES NOT FIT, the driver would scale it' }
            "      $($row.Name.PadRight(34)) $($row.WidthMm) x $($row.HeightMm) mm  $note"
        }
    }

    Write-Section 'Still to do by hand'
    '  Print the calibration sheet on one plain sheet at 100% with no scaling,'
    '  measure its 100 mm ruler, and hold the outlines against a real case.'
    '  Nothing above can establish that, and nothing here touches disc tray'
    '  alignment, which belongs to Epson Photo+ or its equivalent.'
}

$server = New-Object System.Printing.LocalPrintServer
$queues = @($server.GetPrintQueues())

if ($Printer) {
    $queue = $queues | Where-Object { $_.Name -eq $Printer }
    if (-not $queue) {
        "No printer called '$Printer'. This machine has:"
        $queues | ForEach-Object { "  $($_.Name)" }
        exit 1
    }
    Test-OnePrinter $queue
} else {
    if (-not $queues.Count) { 'No printers installed on this machine.'; exit 1 }
    "Printers on this machine: $($queues.Count)"
    foreach ($q in $queues) { Test-OnePrinter $q }
}
