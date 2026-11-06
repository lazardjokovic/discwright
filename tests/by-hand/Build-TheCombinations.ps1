<#
.SYNOPSIS
    Turns a-disc.pict into the rows the suite runs.

.DESCRIPTION
    The ten axes in a-disc.pict multiply out to 138,240 projects. Nobody tests
    that, and nobody needs to: failures come of one value or two values
    interacting far more often than of six, so what is worth covering is every
    PAIR of values, and PICT works out the shortest set of rows that does.

    The result is COMMITTED, as a-disc.txt, and that is deliberate. Running the
    suite must not need PICT installed, the rows must be the same on every
    machine and in every run, and a row that starts failing must be a change in
    the program rather than a reshuffle in a generator.

    So: change a-disc.pict, run this, read the diff, commit both.

.EXAMPLE
    tests\by-hand\Build-TheCombinations.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$model = Join-Path $here 'a-disc.pict'
$rows = Join-Path $here 'a-disc.txt'

if (-not (Get-Command pict -ErrorAction SilentlyContinue)) {
    throw "PICT is not on the path. winget install --id Microsoft.PICT"
}

Write-Host "working out the shortest set that covers every pair..." -ForegroundColor Cyan

# /r with no number seeds from the model rather than the clock, so the same
# model gives the same rows every time. Without it the set is reshuffled on
# every run and the diff is unreadable.
$out = & pict $model /r:1

if ($LASTEXITCODE -ne 0) { throw "PICT would not read the model." }

$warnings = $out | Select-String 'Warning'
if ($warnings) {
    Write-Warning "PICT had something to say about the model:"
    $warnings | ForEach-Object { Write-Warning "  $_" }
}

$table = $out | Where-Object { $_ -notmatch 'Warning' }

# Written with the repository's own endings, like every other file here.
$table -join "`r`n" | Set-Content -Path $rows -Encoding utf8 -NoNewline
Add-Content -Path $rows -Value "`r`n" -NoNewline

$count = $table.Count - 1
$axes = ($table[0] -split "`t").Count

Write-Host ""
Write-Host "$count rows over $axes axes" -ForegroundColor Green
Write-Host $rows -ForegroundColor DarkGray
Write-Host ""
Write-Host "EveryKindOfDisc.cs runs every one of them. Read the diff before committing:" -ForegroundColor DarkGray
Write-Host "  git diff tests/by-hand/a-disc.txt" -ForegroundColor DarkGray
