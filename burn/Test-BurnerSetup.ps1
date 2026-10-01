<#
  What the drive can do, what is in it, and whether a given ISO would fit.

  Costs no media. A CD-R is written once, so anything the drive will answer for
  free gets asked before anything is written.

      .\Test-BurnerSetup.ps1
      .\Test-BurnerSetup.ps1 -IsoPath D:\discs\gothic.iso
#>
[CmdletBinding()]
param(
    [string]$IsoPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DiscWright.Burn.ps1')

function Write-Section([string]$title) { Write-Output "`n$title`n$('-' * $title.Length)" }

try { $burners = Get-BurnerInfo }
catch { Write-Output "Cannot ask the burning service anything: $($_.Exception.Message)"; exit 1 }

if (-not $burners.Count) { Write-Output 'No disc recorder on this machine.'; exit 1 }

Write-Output "Recorders: $($burners.Count)"

foreach ($b in $burners) {
    Write-Section "$($b.Drive)  $($b.Vendor) $($b.Product)"
    Write-Output "  can write : $(if ($b.Writes.Count) { $b.Writes -join ', ' } else { 'nothing reported' })"
    Write-Output "  disc in it: $($b.MediaName)"
    if ($b.MediaType -ne 0) {
        Write-Output "  blank     : $($b.Blank)"
        Write-Output ("  free      : {0:N0} sectors, {1:N1} MB" -f $b.FreeSectors, ($b.FreeBytes / 1MB))
    }
    if ($b.Speeds.Count) {
        $list = ($b.Speeds | ForEach-Object { "$($_.Multiple)x" }) -join ', '
        Write-Output "  speeds    : $list   (for the disc that is loaded)"
        Write-Output "              burning below the top speed is the usual advice for cheap media"
    }
    Write-Output "  ready     : $($b.Ready)  ($($b.Why))"

    if ($IsoPath -and (Test-Path -LiteralPath $IsoPath)) {
        $iso = Get-Item -LiteralPath $IsoPath
        Write-Section "Would $($iso.Name) fit in $($b.Drive)?"
        if ($b.FreeSectors -le 0) {
            Write-Output '  cannot say: put a blank disc in and run this again'
        } else {
            $fit = Test-IsoFitsMedia -IsoBytes $iso.Length -FreeSectors $b.FreeSectors
            Write-Output ("  ISO       : {0:N1} MB, {1:N0} sectors" -f ($fit.IsoBytes / 1MB), $fit.NeededSectors)
            Write-Output ("  disc      : {0:N1} MB, {1:N0} sectors" -f ($fit.FreeBytes / 1MB), $fit.FreeSectors)
            Write-Output "  fits      : $($fit.Fits)"
            if ($fit.Fits) {
                Write-Output ("  spare     : {0:N1} MB" -f (($fit.SpareSectors * 2048) / 1MB))
            } else {
                Write-Output ("  short by  : {0:N1} MB" -f ((-$fit.SpareSectors * 2048) / 1MB))
            }
        }
    }
}

if (-not $IsoPath) {
    Write-Output "`nPass -IsoPath to also check whether a particular ISO fits what is loaded."
}

Write-Section 'Before burning anything'
Write-Output '  Burning is the one step here that cannot be undone, and a CD-R is'
Write-Output '  written once. Write-IsoToDisc supports -WhatIf: it runs every check'
Write-Output '  above and writes nothing, which is how to rehearse without spending'
Write-Output '  a disc. Afterwards, Test-BurnedDisc hashes every file on the disc'
Write-Output '  against the folder it was built from, because a burn that ends'
Write-Output '  without an error is not the same as a disc holding the right bytes.'
