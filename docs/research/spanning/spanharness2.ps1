<#
    Does a RAR-packed GOG installer ask for its next volume, or does it stop
    silently?

    Why this exists separately from spanharness.ps1: GOG ships two packaging
    formats and they behave nothing alike when a part is missing.

      idska32...zlb   Inno Setup's own disk slices. A missing slice raises Inno's
                      SelectDisk dialog, the install resumes, and the Inno log
                      records: Asking user for new disk containing "<file>".
                      The Witcher and Alan Wake are built this way.

      Rar!\x1a\x07    A RAR multi-volume set unpacked by unrar.dll from GOG's
                      install script. Inno's spanning code is not involved at
                      all, so there is no dialog and no log line.
                      Dead Space is built this way.

    The first Dead Space run reported success and installed 18% of the game. That
    was not a harness bug - the harness believed the Inno log, and the Inno log
    was telling the truth about the only three files Inno itself installs. The
    game payload is not an Inno file entry.

    So this harness does NOT trust "Installation process succeeded". It counts
    what landed on disk and compares it against the archive's own table of
    contents, which 7-Zip can read without installing anything:
      4297 files / 10,295,918,859 bytes for Dead Space.

    /SILENT rather than /VERYSILENT: message boxes already showed in the previous
    run (four of them, blocking for 39 minutes), so those are not the question.
    What /VERYSILENT could still have hidden is a WIZARD PAGE, which is how a
    custom "insert the next disc" prompt would most likely be built. /SILENT
    keeps the progress window and any page Setup wants to show.

    Must run ELEVATED. Everything lands in C:\dwspan and is deleted at the end.
    Run spancleanup.ps1 AND spanshortcuts.ps1 afterwards.
#>

$ErrorActionPreference = 'Continue'
$Game     = 'Dead Space'
$Src      = "F:\DWdemo\$Game"
$Base     = 'C:\dwspan'
$Install  = Join-Path $Base 'install'
$Result   = Join-Path $Base 'result.txt'
$InnoLog  = Join-Path $Base 'inno.log'

# The archive's own table of contents - the only honest definition of "complete".
$ExpectFiles = 4297
$ExpectBytes = 10295918859

function W([string]$s) { Write-Host $s; Add-Content -LiteralPath $Result -Value $s -Encoding UTF8 }

if (Test-Path -LiteralPath $Base) { Remove-Item -LiteralPath $Base -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $Base, $Install | Out-Null
Set-Content -LiteralPath $Result -Value ("SPAN HARNESS 2 (RAR)  " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
W ("elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

$exe  = Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.exe' } | Select-Object -First 1
$bins = @(Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.bin' } | Sort-Object Name)
if (-not $exe -or $bins.Count -lt 2) { W "need an installer with at least two parts in $Src"; return }

$head = [IO.File]::ReadAllBytes($bins[0].FullName)[0..6]
$tag  = -join ($head | ForEach-Object { [char]$_ })
$fmt  = if ($tag.StartsWith('Rar!')) { 'RAR multi-volume' }
        elseif ($tag -eq 'idska32') { 'Inno disk slice' }
        else { "unknown ($tag)" }
W ""
W "payload format : $fmt"

W ""
W "staging $($bins.Count) discs from $Src"
$discOf = @{}
for ($i = 0; $i -lt $bins.Count; $i++) {
    $d = Join-Path $Base ("disc" + ($i + 1))
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    cmd /c mklink /H "`"$(Join-Path $d $bins[$i].Name)`"" "`"$($bins[$i].FullName)`"" | Out-Null
    $discOf[$bins[$i].Name] = $d
    if ($i -eq 0) { cmd /c mklink /H "`"$(Join-Path $d $exe.Name)`"" "`"$($exe.FullName)`"" | Out-Null }
    W ("  disc {0}: {1}  ({2:N2} GB){3}" -f ($i+1), $bins[$i].Name, ($bins[$i].Length/1GB), $(if($i -eq 0){"  + $($exe.Name)"}else{''}))
}
$disc1 = Join-Path $Base 'disc1'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);' -Name M -Namespace W2 -ErrorAction SilentlyContinue
$root = [System.Windows.Automation.AutomationElement]::RootElement

function Click-At($el) {
    $r = $el.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
    [W2.M]::mouse_event(0x02,0,0,0,0); [W2.M]::mouse_event(0x04,0,0,0,0)
    Start-Sleep -Milliseconds 300
}

W ""
W "launching: $($exe.Name) /SILENT /DIR=$Install"
Start-Process -FilePath (Join-Path $disc1 $exe.Name) `
    -ArgumentList @('/SILENT', "/DIR=`"$Install`"", "/LOG=`"$InnoLog`"", '/NORESTART') | Out-Null

# Every window Setup shows is recorded, not just ones matching a guessed title.
# The whole point is that we do not know what a RAR volume prompt looks like, or
# whether one exists - so nothing is filtered out on the way in.
$seen     = @{}
$prompts  = 0
$deadline = (Get-Date).AddMinutes(30)
$grace    = (Get-Date).AddSeconds(90)

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $alive = @(Get-Process | Where-Object { $_.ProcessName -like 'setup_*' })
    if ($alive.Count -eq 0 -and (Get-Date) -gt $grace) { W ""; W "no installer processes left"; break }

    $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $wins.Count; $i++) {
        $w = $wins.Item($i)
        try {
            if ((Get-Process -Id $w.Current.ProcessId -ErrorAction Stop).ProcessName -notlike 'setup_*') { continue }
        } catch { continue }

        $title = $w.Current.Name
        $kids  = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
        $texts = @($kids | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
        $key   = $title + '|' + (($texts -join ' ') -replace '\s+',' ')
        if ($seen.ContainsKey($key)) { continue }      # the progress window's text changes constantly
        $seen[$key] = $true

        # A window offering a path box is a disc prompt whatever it calls itself.
        $paths    = @($texts | Where-Object { $_ -match '^[A-Z]:\\' })
        $isPrompt = ($title -match 'next part|\.BIN|disk|volume') -or ($paths.Count -gt 0 -and $title -notmatch 'Setup$')

        W ""
        W ("WINDOW '$title'" + $(if ($isPrompt) { '   <-- looks like a disc prompt' } else { '' }))
        W ("  text : " + (($texts -join ' | ') -replace '\s+',' '))

        if ($isPrompt) {
            $prompts++
            $want = ''
            if (Test-Path -LiteralPath $InnoLog) {
                $l = @(Get-Content -LiteralPath $InnoLog -ErrorAction SilentlyContinue | Where-Object { $_ -match 'Asking user for new disk containing "([^"]+)"' })
                if ($l.Count) { $null = $l[-1] -match 'containing "([^"]+)"'; $want = $Matches[1] }
            }
            if (-not $want) { foreach ($t in $texts) { if ($t -match '(setup_[^\s"\\/]+\.bin)') { $want = $Matches[1]; break } } }
            W ("  wants : " + $(if ($want) { $want } else { '(could not tell)' }))
            W ("  default path offered : " + $(if ($paths.Count) { $paths[0] } else { '(none)' }))

            $target = if ($want -and $discOf.ContainsKey($want)) { $discOf[$want] } else { '' }
            if ($target) {
                W ("  answering with : $target")
                $edit = @($kids | Where-Object { $_.Current.Name -match '^[A-Z]:\\' })
                if ($edit.Count) { Click-At $edit[0]; [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 150 }
                [System.Windows.Forms.SendKeys]::SendWait(($target -replace '([+^%~()\[\]{}])','{$1}')); Start-Sleep -Milliseconds 300
            } else { W "  cannot tell which folder it wants - clicking OK as-is" }
        }

        $ok = @($kids | Where-Object { $_.Current.Name -in @('OK','&Yes','Yes') })
        if ($ok.Count) { Click-At $ok[0]; W "  clicked $($ok[0].Current.Name)" }
        Start-Sleep -Seconds 2
    }
}

# --- verdict, measured against the archive rather than the log -----------------
$files = @(Get-ChildItem -LiteralPath $Install -Recurse -File -ErrorAction SilentlyContinue)
$n  = $files.Count
$sz = ($files | Measure-Object Length -Sum).Sum
$logOk = $false
if (Test-Path -LiteralPath $InnoLog) { $logOk = @(Get-Content -LiteralPath $InnoLog | Where-Object { $_ -match 'Installation process succeeded' }).Count -gt 0 }

W ""
W "=== ANSWERS ==="
W ("payload format         : $fmt")
W ("disc prompts seen      : $prompts")
W ("installed              : $n files, {0:N2} GB" -f ($sz/1GB))
W ("archive says complete  : $ExpectFiles files, {0:N2} GB" -f ($ExpectBytes/1GB))
W ("Inno log claims success: $logOk")
$complete = ($n -ge $ExpectFiles)
W ("ACTUALLY COMPLETE      : $complete   ({0:P1} of files)" -f $(if ($ExpectFiles) { $n / $ExpectFiles } else { 0 }))
if ($logOk -and -not $complete) {
    W ""
    W "*** SILENT PARTIAL INSTALL ***"
    W "    Setup reported success, registered the game, and wrote $n of $ExpectFiles files."
}

W ""
W "=== last 25 lines of the Inno log ==="
if (Test-Path -LiteralPath $InnoLog) { Get-Content -LiteralPath $InnoLog | Select-Object -Last 25 | ForEach-Object { W "  $_" } }

Get-Process | Where-Object { $_.ProcessName -like 'setup_*' } | ForEach-Object { try { Stop-Process -Id $_.Id -Force } catch {} }
W ""
W "cleaning up $Install"
Remove-Item -LiteralPath $Install -Recurse -Force -ErrorAction SilentlyContinue
W "DONE - now run spancleanup.ps1 and spanshortcuts.ps1"
