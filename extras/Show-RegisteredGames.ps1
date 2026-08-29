<#
.SYNOPSIS
    Lists the games GOG has registered on this machine, and optionally answers
    whether the disc menu's Play button would find one of them.

.DESCRIPTION
    A helper for check 4 in docs/MANUAL-CHECKS.md - the one thing no test can
    arrange, which is a real GOG installer registering itself.

    Run it before installing and again afterwards: the game you just installed
    should appear, with ExeThere true. That is the whole check. Everything the
    menu does with these keys afterwards is covered by the suite.

    The menu finds an installed copy by reading gameName out of each key under
    HKLM\SOFTWARE\GOG.com\Games and comparing it to the name the entry carries -
    the registered name, not the one shown, which is why renaming a game for the
    menu does not break Play.

    -Match answers that comparison for a name you type. It does not reimplement
    the comparison: norm and nameHit are lifted out of DiscWright.ps1 and run
    under cscript, the same engine the .hta uses, so this cannot drift away from
    what the menu actually does. A copy pasted in here would agree with itself
    forever and tell you nothing.

    Reads the registry only. Installs nothing, changes nothing, needs no
    elevation.

.PARAMETER Match
    A name to test, as the menu would search for it. Adds a Play column saying
    whether that name finds an installed copy.

.EXAMPLE
    .\extras\Show-RegisteredGames.ps1

    Everything GOG has registered.

.EXAMPLE
    .\extras\Show-RegisteredGames.ps1 -Match 'Alan Wake'

    Whether a disc whose entry matches on 'Alan Wake' would light up Play.
#>
[CmdletBinding()]
param([string]$Match)

$ErrorActionPreference = 'Stop'

# Both hives, because a 32-bit installer on a 64-bit machine writes under
# WOW6432Node and a 64-bit one does not. The menu reads both, in this order.
$bases = @(
    'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games'
    'HKLM:\SOFTWARE\GOG.com\Games'
)

$games = @()
foreach ($base in $bases) {
    if (-not (Test-Path $base)) { continue }
    foreach ($key in Get-ChildItem $base -ErrorAction SilentlyContinue) {
        $p = Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue
        if (-not $p -or -not $p.gameName) { continue }
        # exe is what the menu launches; exeFile beside path is the older shape,
        # and findGame accepts either. A key whose exe has gone is a game that was
        # uninstalled without tidying up, and Play would fail on it.
        $exe = $p.exe
        if (-not $exe -and $p.path -and $p.exeFile) { $exe = Join-Path $p.path $p.exeFile }
        $games += [pscustomobject]@{
            Id        = $key.PSChildName
            GameName  = [string]$p.gameName
            ExeThere  = [bool]($exe -and (Test-Path $exe))
            Path      = [string]$p.path
        }
    }
}

# Sorted once, before anything is emitted or sent to cscript, so the plain
# listing and the -Match listing come back in the same order.
$games = @($games | Sort-Object GameName)

if (-not $games) {
    Write-Warning 'Nothing is registered under HKLM\SOFTWARE\GOG.com\Games. No GOG game is installed on this machine.'
    return
}

if (-not $Match) {
    $games
    return
}

# The menu's own matcher, taken out of the app rather than rewritten here.
$appScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1'
if (-not (Test-Path $appScript)) { throw "Cannot find DiscWright.ps1 beside this script." }

function Get-JsFunction([string]$text, [string]$name) {
    $start = $text.IndexOf("function $name(")
    if ($start -lt 0) { throw "DiscWright.ps1 has no JScript function called $name" }
    $i = $text.IndexOf('{', $start); $depth = 0
    for ($j = $i; $j -lt $text.Length; $j++) {
        if ($text[$j] -eq '{') { $depth++ }
        elseif ($text[$j] -eq '}') { $depth--; if ($depth -eq 0) { return $text.Substring($start, $j - $start + 1) } }
    }
    throw "unbalanced braces in $name"
}

$cscript = @(
    "$env:SystemRoot\System32\cscript.exe"
    (Get-Command cscript.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $cscript) { throw 'cscript.exe not found, so the menu matcher cannot be run.' }

$appText = Get-Content -Raw -LiteralPath $appScript

# One cscript run for the whole list rather than one per game: the names go in on
# stdin as a JSON array so nothing has to be escaped onto a command line.
$js = (Get-JsFunction $appText 'norm') + "`r`n" + (Get-JsFunction $appText 'nameHit') + "`r`n" +
      # Parenthesised: an object literal at the start of a statement is a BLOCK to
      # this engine, and "match": inside one is a label rather than a key.
      'var IN = eval("(" + WScript.StdIn.ReadAll() + ")");' + "`r`n" +
      'for (var i = 0; i < IN.names.length; i++)' + "`r`n" +
      '  WScript.Echo(nameHit(IN.names[i], IN.match) ? "1" : "0");' + "`r`n"

$tmp   = Join-Path ([IO.Path]::GetTempPath()) ('dwmatch_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$jsF   = "$tmp.js"
$dataF = "$tmp.json"
try {
    $payload = @{ match = $Match; names = @($games | ForEach-Object { $_.GameName }) } | ConvertTo-Json -Compress
    Set-Content -LiteralPath $jsF   -Value $js      -Encoding Ascii
    # Ascii, so no BOM reaches eval. ConvertTo-Json escapes anything above 7-bit
    # as \uXXXX, so a name full of trademark symbols survives it intact.
    Set-Content -LiteralPath $dataF -Value $payload -Encoding Ascii
    $hits = @(cmd /c "`"$cscript`" //Nologo //E:JScript `"$jsF`" < `"$dataF`"" 2>&1)
} finally {
    foreach ($f in @($jsF, $dataF)) { if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
}

for ($i = 0; $i -lt $games.Count; $i++) {
    $hit = ($i -lt $hits.Count) -and ("$($hits[$i])".Trim() -eq '1')
    $games[$i] | Add-Member -NotePropertyName 'Play' -NotePropertyValue $(
        if ($hit -and $games[$i].ExeThere) { 'lights up' }
        elseif ($hit) { 'matches, but its exe has gone' }
        else { 'stays grey' }) -PassThru
}
