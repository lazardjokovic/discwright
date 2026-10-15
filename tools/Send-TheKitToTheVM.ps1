# Puts the testing kit on the test VM.
#
# The kit says 11 GB and occupies 130 MB, because the big files are sparse. So
# the sparse ones are REMADE on the far side rather than copied: sent down a
# PowerShell Direct session they would be 10.3 GB of zeroes, one base64 chunk
# at a time, which is most of an hour to transfer nothing.
#
# Why bother at all: OpeningTheDiscsThatExist reads the kit during an ordinary
# test run, and when the kit is absent it opens nothing and still passes. A
# test that quietly does nothing is worse than no test, so the machine that
# runs it needs the kit on it.

[CmdletBinding()]
param(
    [string] $Vm = 'DiscWright-Win11',
    [string] $User = 'dw',
    [string] $Kit = $(if ($env:DISCWRIGHT_KIT) { $env:DISCWRIGHT_KIT } else { Join-Path $env:USERPROFILE 'DiscWright-Kit' }),

    # Anything bigger than this is taken to be one of the deliberate sparse
    # ones and is remade rather than sent.
    [int] $RemakeOverMB = 64
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Kit)) { throw "No kit at $Kit" }

$password = $env:DISCWRIGHT_VM_PASSWORD
if (-not $password) { throw "DISCWRIGHT_VM_PASSWORD is not set." }

$credential = New-Object System.Management.Automation.PSCredential(
    $User, (ConvertTo-SecureString $password -AsPlainText -Force))

Write-Host "--- gathering the kit ---"

$sending = @()
$remaking = @()

foreach ($file in Get-ChildItem $Kit -Recurse -File -Force) {
    # The junction to the real games is not followed. Those are tens of
    # gigabytes and they live on a drive the VM cannot see anyway.
    $relative = $file.FullName.Substring($Kit.Length).TrimStart('\')
    if ($relative -like '4 REAL GAMES*') { continue }

    if ($file.Length -gt ($RemakeOverMB * 1MB)) {
        $remaking += [PSCustomObject]@{ Path = $relative; Bytes = $file.Length }
    }
    else {
        $sending += [PSCustomObject]@{ Path = $relative; Bytes = $file.Length }
    }
}

Write-Host ("  {0} files to send, {1:N1} MB" -f $sending.Count, (($sending | Measure-Object Bytes -Sum).Sum / 1MB))
Write-Host ("  {0} to remake on the far side, {1:N0} MB of nothing" -f $remaking.Count, (($remaking | Measure-Object Bytes -Sum).Sum / 1MB))

$session = New-PSSession -VMName $Vm -Credential $credential

try {
    $there = Invoke-Command -Session $session -ScriptBlock {
        $kit = Join-Path $env:USERPROFILE 'DiscWright-Kit'
        if (Test-Path $kit) { Remove-Item $kit -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $kit | Out-Null
        $kit
    }

    Write-Host "--- copying to $there ---"

    foreach ($one in $sending) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $Kit $one.Path))
        $as64 = [Convert]::ToBase64String($bytes)

        Invoke-Command -Session $session -ArgumentList $there, $one.Path, $as64 -ScriptBlock {
            param($kit, $relative, $as64)
            $to = Join-Path $kit $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $to) | Out-Null
            [IO.File]::WriteAllBytes($to, [Convert]::FromBase64String($as64))
        }
    }

    Write-Host "--- remaking the sparse ones ---"

    foreach ($one in $remaking) {
        Invoke-Command -Session $session -ArgumentList $there, $one.Path, $one.Bytes -ScriptBlock {
            param($kit, $relative, $bytes)
            $to = Join-Path $kit $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $to) | Out-Null

            $f = [IO.File]::Create($to)
            try { $f.SetLength($bytes) } finally { $f.Dispose() }

            & fsutil sparse setflag $to | Out-Null
            & fsutil sparse setrange $to 0 $bytes | Out-Null
        }
        Write-Host ("  {0,-56} {1:N0} MB" -f (Split-Path $one.Path -Leaf), ($one.Bytes / 1MB))
    }

    # A saved project writes down where its games were, absolutely, and on this
    # machine that path has a name in it. Copied unchanged, every project on the
    # far side points at folders belonging to somebody who does not exist there,
    # which is how a test that reads them came to fail the moment the kit
    # arrived.
    Write-Host "--- pointing the saved projects at this machine ---"

    $repointed = Invoke-Command -Session $session -ArgumentList $there, $Kit -ScriptBlock {
        param($kit, $wasAt)

        # As the paths appear inside JSON, where every backslash is doubled.
        $from = $wasAt.Replace('\', '\\')
        $to = $kit.Replace('\', '\\')
        $done = 0

        foreach ($f in Get-ChildItem $kit -Recurse -Filter 'discproject.json' -File) {
            $bytes = [IO.File]::ReadAllBytes($f.FullName)
            $hadBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
            $text = [Text.Encoding]::UTF8.GetString($bytes)

            if ($text -notlike "*$from*") { continue }

            # Written back with its mark exactly as found: every project file v1
            # wrote begins with one, and reading past it is a thing 2.0 had to
            # be taught.
            [IO.File]::WriteAllBytes(
                $f.FullName,
                (New-Object Text.UTF8Encoding($hadBom)).GetBytes($text.Replace($from, $to)))

            $done++
        }

        return $done
    }

    Write-Host "  $repointed project files now point at the kit on that machine"

    $report = Invoke-Command -Session $session -ArgumentList $there -ScriptBlock {
        param($kit)
        $files = Get-ChildItem $kit -Recurse -File -Force
        [PSCustomObject]@{
            Files = $files.Count
            Apparent = ($files | Measure-Object Length -Sum).Sum
            Discs = (Get-ChildItem (Join-Path $kit '2 DISCS - open these') -Directory -ErrorAction SilentlyContinue).Count
            Sources = (Get-ChildItem (Join-Path $kit '1 SOURCES - add these') -Directory -ErrorAction SilentlyContinue).Count
        }
    }

    Write-Host ""
    Write-Host "--- on $Vm ---"
    Write-Host ("  {0} files, {1:N0} MB as the app sees it" -f $report.Files, ($report.Apparent / 1MB))
    Write-Host ("  {0} source folders, {1} discs to open" -f $report.Sources, $report.Discs)
}
finally {
    Remove-PSSession $session
}
