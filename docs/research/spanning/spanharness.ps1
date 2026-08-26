<#
    Does a GOG installer resume when its next .bin is on another "disc"?

    Stages one game's installer parts into separate folders - one per pretend
    disc - runs it, and answers the disk-change prompt each time by pointing it
    at whichever folder holds the file it just asked for.

    Three questions, all answered by the same run:

      1. Does pointing it at another folder actually resume the install, or does
         it only re-prompt?
      2. Does the path it offers by default already work, or is Browse required?
      3. In what ORDER does it ask for slices, and does it ever ask twice for one
         it has already been given? Inno's docs say a retry under solid
         compression seeks back to the start of the stream - "the user would have
         to re-insert disk 1" - and our own probe saw it jump straight to slice 2
         at startup. So ascending order cannot be assumed; it has to be recorded.

    Must run ELEVATED: the installer self-elevates, and a non-elevated process
    cannot drive an elevated window.

    Everything is logged to result.txt. Nothing is installed anywhere but the
    throwaway folder below, and it is deleted at the end.
#>

$ErrorActionPreference = 'Continue'
$Game    = 'Dead Space'                        # 3 parts: 3.91 + 3.91 + 0.29 GB
# Deliberately a game that is NOT installed. A test install rewrites the GOG
# registry key for that product to point at the throwaway folder - run this
# against Alan Wake or Hollow Knight and cleanup would then delete the real
# install's entry along with the fake one.
$Src     = "F:\DWdemo\$Game"
# On C: deliberately. F:\DWdemo is a junction to C:, so the .bin files physically
# live on C: - and a hardlink has to sit on the same volume as its target.
$Base    = 'C:\dwspan'
$Install = Join-Path $Base 'install'
$Result  = Join-Path $Base 'result.txt'
$InnoLog = Join-Path $Base 'inno.log'

function W([string]$s) { Write-Host $s; Add-Content -LiteralPath $Result -Value $s -Encoding UTF8 }

if (Test-Path $Base) { Remove-Item $Base -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $Base, $Install | Out-Null
Set-Content -LiteralPath $Result -Value ("SPAN HARNESS  " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
W ("elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

# --- stage one pretend disc per installer file -------------------------------
$exe   = Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.exe' } | Select-Object -First 1
$bins  = @(Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.bin' } | Sort-Object Name)
if (-not $exe -or $bins.Count -lt 2) { W "need an installer with at least two parts in $Src"; return }

W ""
W "staging $($bins.Count) discs from $Src"
$discOf = @{}          # file name -> folder that holds it
for ($i = 0; $i -lt $bins.Count; $i++) {
    $d = Join-Path $Base ("disc" + ($i + 1))
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    # Hardlink rather than copy: same volume, so 9.5 GB costs nothing and takes
    # no time. The installer cannot tell the difference.
    cmd /c mklink /H "`"$(Join-Path $d $bins[$i].Name)`"" "`"$($bins[$i].FullName)`"" | Out-Null
    $discOf[$bins[$i].Name] = $d
    if ($i -eq 0) { cmd /c mklink /H "`"$(Join-Path $d $exe.Name)`"" "`"$($exe.FullName)`"" | Out-Null }
    W ("  disc {0}: {1}  ({2:N2} GB){3}" -f ($i+1), $bins[$i].Name, ($bins[$i].Length/1GB), $(if($i -eq 0){"  + $($exe.Name)"}else{''}))
}
$disc1 = Join-Path $Base 'disc1'

# --- run it -------------------------------------------------------------------
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$root = [System.Windows.Automation.AutomationElement]::RootElement

W ""
W "launching: $($exe.Name) /VERYSILENT /DIR=$Install"
$proc = Start-Process -FilePath (Join-Path $disc1 $exe.Name) `
        -ArgumentList @('/VERYSILENT', "/DIR=`"$Install`"", "/LOG=`"$InnoLog`"", '/NORESTART') -PassThru

$asked   = @()         # ordered list of every slice it asked for
$handled = 0
$deadline = (Get-Date).AddMinutes(45)

function Get-Dialog {
    $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $wins.Count; $i++) {
        $w = $wins.Item($i)
        try {
            $pn = (Get-Process -Id $w.Current.ProcessId -ErrorAction Stop).ProcessName
            if ($pn -notlike 'setup_*') { continue }
            if ($w.Current.Name -match 'next part|\.BIN|disk|Error') { return $w }
        } catch {}
    }
    return $null
}
function Click-At($el) {
    $r = $el.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
    Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);' -Name M -Namespace W2 -ErrorAction SilentlyContinue
    [W2.M]::mouse_event(0x02,0,0,0,0); [W2.M]::mouse_event(0x04,0,0,0,0)
    Start-Sleep -Milliseconds 300
}

# Inno's setup.exe unpacks a .tmp copy of itself and hands over, so the process
# Start-Process returned exits within seconds while the install carries on. Watching
# it means declaring victory early - and this harness then deletes the install
# folder out from under a running installer, which is how the first Dead Space run
# reported "0 prompts" for an install that had barely started.
$graceUntil = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $alive = @(Get-Process | Where-Object { $_.ProcessName -like 'setup_*' })
    if ($alive.Count -eq 0 -and (Get-Date) -gt $graceUntil) { W ""; W "no installer processes left - finished"; break }

    $dlg = Get-Dialog
    if (-not $dlg) { continue }

    # Anything that is not a disc prompt gets dismissed and noted. Dead Space raises
    # four of these for redistributables it cannot find under /VERYSILENT, and each
    # one blocks the install until somebody clicks OK.
    if ($dlg.Current.Name -notmatch 'next part|\.BIN') {
        $body = @($dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
                  ForEach-Object { $_.Current.Name } | Where-Object { $_ -and $_ -notin @('OK','Cancel','Yes','No') })
        W ""
        W ("SIDE DIALOG '$($dlg.Current.Name)' : " + (($body -join ' ') -replace '\s+',' '))
        $btn = @($dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
                 Where-Object { $_.Current.Name -eq 'OK' })
        if ($btn.Count) { Click-At $btn[0]; W "  dismissed" } else { W "  no OK button - leaving it" }
        Start-Sleep -Seconds 2
        continue
    }

    # The log names the file it wants, which the dialog text does not.
    $want = ''
    if (Test-Path $InnoLog) {
        $lines = @(Get-Content -LiteralPath $InnoLog -ErrorAction SilentlyContinue | Where-Object { $_ -match 'Asking user for new disk containing "([^"]+)"' })
        if ($lines.Count) { $null = $lines[-1] -match 'containing "([^"]+)"'; $want = $Matches[1] }
    }
    $title = $dlg.Current.Name
    W ""
    W "PROMPT #$($handled+1)  title='$title'  wants='$want'"
    $asked += $want

    $target = $(if ($want -and $discOf.ContainsKey($want)) { $discOf[$want] } else { '' })
    if (-not $target) { W "  cannot tell which folder holds '$want' - stopping"; break }

    # What does it offer by default? That decides whether a real disc swap needs
    # Browse or just OK.
    $kids = @($dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
    $shown = @($kids | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^[A-Z]:\\' })
    W ("  default path offered : " + $(if ($shown.Count) { $shown[0] } else { '(none shown)' }))
    W ("  answering with       : $target")

    # Type the folder into the path box, then OK.
    $edit = @($kids | Where-Object { $_.Current.Name -match '^[A-Z]:\\' })
    if ($edit.Count) { Click-At $edit[0]; [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 150 }
    [System.Windows.Forms.SendKeys]::SendWait(($target -replace '([+^%~()\[\]{}])','{$1}')); Start-Sleep -Milliseconds 300
    $ok = @($kids | Where-Object { $_.Current.Name -eq 'OK' })
    if ($ok.Count) { Click-At $ok[0] } else { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}') }
    $handled++
    Start-Sleep -Seconds 3
}

# --- what happened -------------------------------------------------------------
W ""
W "=== ANSWERS ==="
W ("prompts handled     : $handled")
W ("slices asked for    : " + $(if ($asked.Count) { ($asked | ForEach-Object { if ($_ -match '-(\d+)\.bin$') { $Matches[1] } else { $_ } }) -join ' -> ' } else { '(none)' }))
$dupes = @($asked | Group-Object | Where-Object { $_.Count -gt 1 })
W ("asked twice for one : " + $(if ($dupes.Count) { ($dupes | ForEach-Object { $_.Name }) -join ', ' } else { 'no' }))
$logOk = $false
if (Test-Path $InnoLog) { $logOk = @(Get-Content -LiteralPath $InnoLog | Where-Object { $_ -match 'Installation process succeeded' }).Count -gt 0 }
W ("installer said it succeeded : " + $logOk)
$n = @(Get-ChildItem -LiteralPath $Install -Recurse -File -ErrorAction SilentlyContinue).Count
$sz = (Get-ChildItem -LiteralPath $Install -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
W ("installed files     : $n  ({0:N2} GB)" -f ($sz/1GB))
# Files on disk alone proves nothing - the first run wrote 796 of them and was
# nowhere near done. The log line is what says the install actually finished.
W ("=> resumed across discs: " + $(if ($logOk -and $handled -gt 0) { 'YES' }
                                   elseif ($logOk) { 'N/A - never needed another slice' }
                                   else { 'INCONCLUSIVE - install did not complete' }))

W ""
W "=== last 40 lines of the Inno log ==="
if (Test-Path $InnoLog) { Get-Content -LiteralPath $InnoLog | Select-Object -Last 40 | ForEach-Object { W "  $_" } }

Get-Process | Where-Object { $_.ProcessName -like 'setup_*' } | ForEach-Object { try { Stop-Process -Id $_.Id -Force } catch {} }
W ""
W "cleaning up $Install"
Remove-Item $Install -Recurse -Force -ErrorAction SilentlyContinue
W "DONE"
