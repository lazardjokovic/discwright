<#
    The control run. Same installer, same switches, ALL THREE VOLUMES PRESENT in
    one folder.

    Why it is needed: every Dead Space run so far had a volume missing, so the
    "Installation failed with code: -3" error is only correlated with the missing
    volume. Nobody has watched this installer succeed on this machine. If it
    completes here - 4297 files - then the -3 is caused by the missing volume and
    nothing else: the environment, the switches and the hardlinked staging are
    held identical, and the only thing that changed is whether volumes 2 and 3
    were beside volume 1.

    If it FAILS here too, then every earlier conclusion about RAR spanning is
    void, because the failure was never about spanning at all.

    /VERYSILENT deliberately: it is the one mode that needed no human. The
    interactive run stalled on GOG's EULA-accept screen, whose checkbox and
    Install button expose no name to UI Automation (three unnamed Panes), and a
    person had to finish it by hand. That is fine for observing behaviour but
    useless as a control, which has to be reproducible.

    Expect this to write ~9.6 GB to C:\dwspan and to take a few minutes.
    Must run ELEVATED. Afterwards run spancleanup.ps1 AND spanshortcuts.ps1 -
    a SUCCESSFUL install registers the game, so cleanup matters more here, not
    less.
#>

$ErrorActionPreference = 'Continue'
$Game    = 'Dead Space'
$Src     = "F:\DWdemo\$Game"
$Base    = 'C:\dwspan'
$Install = Join-Path $Base 'install'
$Result  = Join-Path $Base 'result.txt'
$InnoLog = Join-Path $Base 'inno.log'

$ExpectFiles = 4297
$ExpectBytes = 10295918859

function W([string]$s) { Write-Host $s; Add-Content -LiteralPath $Result -Value $s -Encoding UTF8 }

if (Test-Path -LiteralPath $Base) { Remove-Item -LiteralPath $Base -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $Base, $Install | Out-Null
Set-Content -LiteralPath $Result -Value ("SPAN CONTROL (all volumes present)  " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
W ("elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

$exe  = Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.exe' } | Select-Object -First 1
$bins = @(Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.bin' } | Sort-Object Name)
if (-not $exe -or $bins.Count -lt 2) { W "need an installer with at least two parts in $Src"; return }

$fs = [IO.File]::Open($bins[0].FullName, 'Open', 'Read', 'ReadWrite')
$head = New-Object byte[] 8
[void]$fs.Read($head, 0, 8)
$fs.Close()
$tag = -join ($head | ForEach-Object { [char]$_ })
$fmt = if ($tag.StartsWith('Rar!')) { 'RAR multi-volume' } elseif ($tag.StartsWith('idska32')) { 'Inno disk slice' } else { 'unknown' }
W ""
W "payload format : $fmt"

# One folder, everything in it. Hardlinks, so this costs no disk and no time.
$disc1 = Join-Path $Base 'disc1'
New-Item -ItemType Directory -Force -Path $disc1 | Out-Null
W ""
W "staging ALL parts into one folder (this is the control)"
cmd /c mklink /H "`"$(Join-Path $disc1 $exe.Name)`"" "`"$($exe.FullName)`"" | Out-Null
W ("  " + $exe.Name)
foreach ($b in $bins) {
    cmd /c mklink /H "`"$(Join-Path $disc1 $b.Name)`"" "`"$($b.FullName)`"" | Out-Null
    W ("  {0}  ({1:N2} GB)" -f $b.Name, ($b.Length/1GB))
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);' -Name M -Namespace WC -ErrorAction SilentlyContinue
$root = [System.Windows.Automation.AutomationElement]::RootElement

W ""
W "launching: $($exe.Name) /VERYSILENT /DIR=$Install"
Start-Process -FilePath (Join-Path $disc1 $exe.Name) `
    -ArgumentList @('/VERYSILENT', "/DIR=`"$Install`"", "/LOG=`"$InnoLog`"", '/NORESTART') | Out-Null

# The four redistributable message boxes block the install until clicked, so they
# still have to be dismissed - but every window is recorded first, because a disc
# prompt appearing HERE would mean the staging is wrong.
$seen     = @{}
$dialogs  = 0
$deadline = (Get-Date).AddMinutes(30)
$grace    = (Get-Date).AddSeconds(120)

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
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $dialogs++
        $blurb = (($texts -join ' | ') -replace '\s+',' ')
        if ($blurb.Length -gt 300) { $blurb = $blurb.Substring(0,300) + '...' }
        W ""
        W "WINDOW '$title'"
        W "  text : $blurb"
        $ok = @($kids | Where-Object { $_.Current.Name -eq 'OK' })
        if ($ok.Count) {
            $r = $ok[0].Current.BoundingRectangle
            if ($r.Width -gt 0) {
                [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
                [WC.M]::mouse_event(0x02,0,0,0,0); [WC.M]::mouse_event(0x04,0,0,0,0)
                W "  clicked OK"
            }
        }
        Start-Sleep -Seconds 2
    }
}

$files = @(Get-ChildItem -LiteralPath $Install -Recurse -File -ErrorAction SilentlyContinue)
$n  = $files.Count
$sz = ($files | Measure-Object Length -Sum).Sum
$logOk = $false
if (Test-Path -LiteralPath $InnoLog) { $logOk = @(Get-Content -LiteralPath $InnoLog | Where-Object { $_ -match 'Installation process succeeded' }).Count -gt 0 }
$failed = $false
if (Test-Path -LiteralPath $InnoLog) { $failed = @(Get-Content -LiteralPath $InnoLog | Where-Object { $_ -match 'failed with code' }).Count -gt 0 }

W ""
W "=== CONTROL RESULT ==="
W ("payload format         : $fmt")
W ("windows shown          : $dialogs")
W ("installed              : $n files, {0:N2} GB" -f ($sz/1GB))
W ("archive says complete  : $ExpectFiles files, {0:N2} GB" -f ($ExpectBytes/1GB))
W ("Inno log claims success: $logOk")
W ("saw a 'failed with code': $failed")
$complete = ($n -ge $ExpectFiles)
W ("ACTUALLY COMPLETE      : $complete   ({0:P1} of files)" -f $(if ($ExpectFiles) { $n / $ExpectFiles } else { 0 }))
W ""
if ($complete) {
    W "=> CONTROL PASSED. This installer completes when its volumes are present,"
    W "   so the -3 failure in the earlier runs was caused by the missing volume."
} else {
    W "=> CONTROL FAILED. The installer does not complete even with every volume"
    W "   present, so the earlier runs prove nothing about spanning. Every"
    W "   conclusion drawn from them has to be withdrawn."
}

W ""
W "=== last 20 lines of the Inno log ==="
if (Test-Path -LiteralPath $InnoLog) { Get-Content -LiteralPath $InnoLog | Select-Object -Last 20 | ForEach-Object { W "  $_" } }

Get-Process | Where-Object { $_.ProcessName -like 'setup_*' } | ForEach-Object { try { Stop-Process -Id $_.Id -Force } catch {} }
W ""
W "cleaning up $Install"
Remove-Item -LiteralPath $Install -Recurse -Force -ErrorAction SilentlyContinue
W "DONE - now run spancleanup.ps1 and spanshortcuts.ps1"
