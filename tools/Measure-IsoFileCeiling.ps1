<#
.SYNOPSIS
    Measures the largest file IMAPI will put into an ISO9660/Joliet image.

.DESCRIPTION
    $ISO9660_MAX_FILE in DiscWright.ps1 decides whether the "readable on Windows
    XP and older" box is offered, and whether a build adds the older filesystems
    or falls back to UDF alone. Getting it wrong in the generous direction fails
    a build after the staging copy, which on a big game is several minutes of
    work thrown away.

    It was wrong once, set to the format's own 32-bit ceiling of 4 GiB minus a
    byte, when IMAPI in fact stops at 2 GiB. So the number is measured here
    rather than reasoned about, and this script exists so it can be measured
    again - on another version of Windows, or when a build fails and the ceiling
    is the suspect.

    Nothing is written to disc. CreateResultImage works from the catalogue, so
    the test files are made by setting a length and never writing a byte, and the
    result image is released without being streamed anywhere.

.PARAMETER WorkDir
    Where the empty test files go. Needs to be on a volume with room for one file
    of the largest size probed; NTFS does not zero it, so this is quick.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Measure-IsoFileCeiling.ps1
#>
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'discwright-isolimit'),
    [long]$Low  = 1073741824,      # 1 GiB, expected to pass
    [long]$High = 4294967295       # 4 GiB minus a byte, the format's own ceiling
)
$ErrorActionPreference = 'Stop'

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

function Test-Size([long]$Bytes, [int]$FileSystems = 7) {
    $dir = Join-Path $WorkDir 'stage'
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    # Not $fs for this handle: the filesystem mask below is a number, PowerShell
    # variable names are case-insensitive, and one would quietly become the other.
    $handle = [IO.File]::Create((Join-Path $dir 'part.bin'))
    try { $handle.SetLength($Bytes) } finally { $handle.Dispose() }

    $fsi = $null; $root = $null; $img = $null
    try {
        $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        $fsi.FreeMediaBlocks = 2147483000
        $fsi.FileSystemsToCreate = $FileSystems
        try { $fsi.UDFRevision = 0x250 } catch { }
        $fsi.VolumeName = 'CEILING'
        $root = $fsi.Root
        $root.AddTree($dir, $false)
        $img = $fsi.CreateResultImage()
        return @{ Ok = $true; Msg = '' }
    }
    catch { return @{ Ok = $false; Msg = $_.Exception.Message.Trim() } }
    finally {
        foreach ($o in @($img, $root, $fsi)) {
            if ($o) { try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o) } catch { } }
        }
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    Write-Host 'Which filesystems will take a 3 GiB file:'
    $names = @{ 1 = 'ISO9660'; 3 = 'ISO9660 + Joliet'; 4 = 'UDF'; 5 = 'ISO9660 + UDF'; 7 = 'all three' }
    foreach ($mask in 1, 3, 4, 5, 7) {
        $r = Test-Size 3221225472 $mask
        Write-Host ('  {0,-18} {1}' -f $names[$mask], $(if ($r.Ok) { 'ok' } else { $r.Msg }))
    }

    Write-Host ''
    Write-Host ('Narrowing the ceiling between {0:N0} and {1:N0} bytes:' -f $Low, $High)
    if (-not (Test-Size $Low).Ok)  { throw ("even {0:N0} bytes was refused" -f $Low) }
    if ((Test-Size $High).Ok)      { throw ("{0:N0} bytes was accepted; nothing to narrow" -f $High) }
    $lo = $Low; $hi = $High
    while (($hi - $lo) -gt 1) {
        $mid = $lo + [long](($hi - $lo) / 2)
        if ((Test-Size $mid).Ok) { $lo = $mid } else { $hi = $mid }
        Write-Host ('  ... {0:N0}' -f $lo)
    }

    Write-Host ''
    Write-Host ('  largest accepted: {0:N0} bytes' -f $lo)
    Write-Host ('  first refused:    {0:N0} bytes' -f $hi)
    Write-Host ''
    Write-Host ('  $ISO9660_MAX_FILE should be {0:N0}' -f $lo)
}
finally {
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
}
