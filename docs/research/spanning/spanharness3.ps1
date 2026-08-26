<#
    The interactive version. Drives the installer's wizard the way a person
    would, with volumes 2 and 3 sitting on other "discs".

    Why a third harness:

      spanharness.ps1   /VERYSILENT. Works, but a custom WIZARD PAGE would be
                        hidden by it, so it cannot rule out a volume prompt.
      spanharness2.ps1  /SILENT, to keep wizard pages visible. GOG's installer
                        refuses to run that way - it stops on its custom Welcome
                        page with "Failed to proceed to next wizard page;
                        aborting." and installs nothing.

    So the only run that can see a wizard-page prompt is a real interactive one.

    The question it answers: when a RAR-packed GOG installer reaches the end of
    volume 1 and volume 2 is not beside it, does it ASK, or does it stop and
    report success? Under /VERYSILENT it did the latter - 792 of 4297 files, no
    prompt, "Installation process succeeded", game registered as installed. If
    that repeats here, with every wizard page visible, it is the real behaviour
    and not an artifact of silent mode.

    TWO THINGS THIS INSTALLER DOES THAT BROKE THE NAIVE VERSION:

    1. Every control is a bare Pane to UI Automation - no Button type, no
       patterns. So elements are found by NAME and clicked by COORDINATE.
    2. Clicking Install raises a MODAL EULA window on top of the main one. A
       coordinate click aimed at the main window then lands on whatever is
       actually at those pixels - which is how the last run opened a "Save
       as .txt" file dialog and wedged itself.

    Hence: only ever act on the FOREGROUND window, and decide what to click from
    an explicit rule per window rather than a global list of hopeful button
    names. Anything not in the rules is logged and left alone.

    Success is measured against the archive's own table of contents (7-Zip can
    read it without installing anything), never against the Inno log. The Inno
    log tells the truth about the three files Inno itself installs; the game
    payload is unpacked by unrar.dll from GOG's script and Inno knows nothing
    about it.

    Must run ELEVATED, and the desktop must be left alone. Afterwards run
    spancleanup.ps1 AND spanshortcuts.ps1.
#>

$ErrorActionPreference = 'Continue'
$Game    = 'Dead Space'
$Src     = "F:\DWdemo\$Game"
$Base    = 'C:\dwspan'
$Install = Join-Path $Base 'install'
$Result  = Join-Path $Base 'result.txt'
$InnoLog = Join-Path $Base 'inno.log'
$Tree    = Join-Path $Base 'windows.txt'

$ExpectFiles = 4297
$ExpectBytes = 10295918859

function W([string]$s) { Write-Host $s; Add-Content -LiteralPath $Result -Value $s -Encoding UTF8 }
function T([string]$s) { Add-Content -LiteralPath $Tree -Value $s -Encoding UTF8 }

if (Test-Path -LiteralPath $Base) { Remove-Item -LiteralPath $Base -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $Base, $Install | Out-Null
Set-Content -LiteralPath $Result -Value ("SPAN HARNESS 3 (interactive)  " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
Set-Content -LiteralPath $Tree -Value "window trees seen during the run" -Encoding UTF8
W ("elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

$exe  = Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.exe' } | Select-Object -First 1
$bins = @(Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.bin' } | Sort-Object Name)
if (-not $exe -or $bins.Count -lt 2) { W "need an installer with at least two parts in $Src"; return }

# Eight bytes off the front. NOT ReadAllBytes - these files are ~4 GB and .NET
# refuses a byte array that size, which is how an earlier run reported
# "unknown ( )" for a format it had already identified.
$fs = [IO.File]::Open($bins[0].FullName, 'Open', 'Read', 'ReadWrite')
$head = New-Object byte[] 8
[void]$fs.Read($head, 0, 8)
$fs.Close()
$tag = -join ($head | ForEach-Object { [char]$_ })
$fmt = if ($tag.StartsWith('Rar!')) { 'RAR multi-volume' }
       elseif ($tag.StartsWith('idska32')) { 'Inno disk slice' }
       else { "unknown (" + (($head | ForEach-Object { '{0:x2}' -f $_ }) -join ' ') + ")" }
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
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
'@ -Name M -Namespace W3 -ErrorAction SilentlyContinue

function Click-At($el) {
    $r = $el.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { return $false }
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
    [W3.M]::mouse_event(0x02,0,0,0,0); [W3.M]::mouse_event(0x04,0,0,0,0)
    Start-Sleep -Milliseconds 400
    return $true
}

# What to click, per window. First matching rule wins. A window that matches
# nothing is logged and left alone rather than guessed at.
$rules = @(
    @{ Match = '^Select Setup Language$';                        Click = @('OK') },
    @{ Match = 'MICROSOFT SOFTWARE LICENSE TERMS|^EULA$|License'; Click = @('Close','OK','Accept','I Accept') },
    @{ Match = 'Save As|Save as';                                Click = @('Cancel') },
    @{ Match = 'Setup$';                                         Click = @('Install','Next','Finish','OK') }
)
# Never clicked, whatever a rule says - these open file dialogs or back out.
# Cancel is deliberately NOT here: the Save As rule needs it to dismiss a file
# dialog that should never have opened. No other rule offers Cancel.
$never = @('Save as .txt','Options','Back','&Back')

W ""
W "launching interactively: $($exe.Name) /DIR=$Install"
Start-Process -FilePath (Join-Path $disc1 $exe.Name) `
    -ArgumentList @("/DIR=`"$Install`"", "/LOG=`"$InnoLog`"", '/NORESTART') | Out-Null

$seen     = @{}
$prompts  = 0
$clicks   = 0
$started  = $false
$deadline = (Get-Date).AddMinutes(30)
$grace    = (Get-Date).AddSeconds(120)
$lastKey  = ''
$repeat   = 0

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2

    if (-not $started -and (Test-Path -LiteralPath $InnoLog)) {
        if (Select-String -LiteralPath $InnoLog -Pattern 'Starting the installation process' -Quiet) {
            $started = $true; W ""; W ">>> unpacking has begun at $(Get-Date -Format HH:mm:ss)"
        }
    }

    $alive = @(Get-Process | Where-Object { $_.ProcessName -like 'setup_*' })
    if ($alive.Count -eq 0 -and (Get-Date) -gt $grace) { W ""; W "no installer processes left"; break }

    # ONLY the foreground window. Clicking coordinates on a window that is not on
    # top means clicking whatever modal is covering it.
    $h = [W3.M]::GetForegroundWindow()
    if ($h -eq [IntPtr]::Zero) { continue }
    $w = $null
    try { $w = [System.Windows.Automation.AutomationElement]::FromHandle($h) } catch { continue }
    if (-not $w) { continue }
    try {
        if ((Get-Process -Id $w.Current.ProcessId -ErrorAction Stop).ProcessName -notlike 'setup_*') { continue }
    } catch { continue }

    $title = $w.Current.Name
    $kids  = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
    $texts = @($kids | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
    $key   = $title + '|' + (($texts -join ' ') -replace '\s+',' ')

    # The same window staying put is normal (unpacking). Only act once per state,
    # but keep counting so a genuinely stuck wizard is visible in the log.
    if ($seen.ContainsKey($key)) {
        if ($key -eq $lastKey) { $repeat++ } else { $repeat = 0; $lastKey = $key }
        continue
    }
    $seen[$key] = $true; $lastKey = $key; $repeat = 0

    T ""
    T ("=== '$title' ===")
    foreach ($k in $kids) { T ("    [{0}] '{1}'" -f $k.Current.ControlType.ProgrammaticName.Replace('ControlType.',''), $k.Current.Name) }

    $paths = @($texts | Where-Object { $_ -match '^[A-Z]:\\' })
    $isPrompt = ($title -match 'next part|\.BIN|disk|volume|insert') -or
                (($texts -join ' ') -match 'next part|setup_.*\.bin|insert the|next volume')

    W ""
    W ("WINDOW '$title'" + $(if ($isPrompt) { '   <-- CANDIDATE DISC PROMPT' } else { '' }))
    W ("  text : " + ((($texts | Select-Object -First 20) -join ' | ') -replace '\s+',' ').Substring(0, [Math]::Min(400, ((($texts | Select-Object -First 20) -join ' | ') -replace '\s+',' ').Length)))

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
            if ($edit.Count) { [void](Click-At $edit[0]); [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 150 }
            [System.Windows.Forms.SendKeys]::SendWait(($target -replace '([+^%~()\[\]{}])','{$1}')); Start-Sleep -Milliseconds 300
        }
    }

    $rule = $rules | Where-Object { $title -match $_.Match } | Select-Object -First 1
    if (-not $rule -and $isPrompt) { $rule = @{ Click = @('OK','Continue','Retry') } }
    if ($rule) {
        $hit = $null
        foreach ($name in $rule.Click) {
            $c = @($kids | Where-Object { $_.Current.Name -eq $name -and $never -notcontains $_.Current.Name })
            if ($c.Count) { $hit = $c[0]; break }
        }
        if ($hit) {
            $n = $hit.Current.Name           # read BEFORE clicking; the element may die with the window
            if (Click-At $hit) { $clicks++; W "  clicked '$n'" }
        } else {
            W ("  rule matched but none of [" + ($rule.Click -join ', ') + "] present - left alone")
        }
    } else {
        W "  no rule for this window - left alone"
    }
    Start-Sleep -Seconds 2
}

$files = @(Get-ChildItem -LiteralPath $Install -Recurse -File -ErrorAction SilentlyContinue)
$n  = $files.Count
$sz = ($files | Measure-Object Length -Sum).Sum
$logOk = $false
if (Test-Path -LiteralPath $InnoLog) { $logOk = @(Get-Content -LiteralPath $InnoLog | Where-Object { $_ -match 'Installation process succeeded' }).Count -gt 0 }

W ""
W "=== ANSWERS ==="
W ("payload format         : $fmt")
W ("wizard clicks          : $clicks")
W ("disc prompts seen      : $prompts")
W ("installed              : $n files, {0:N2} GB" -f ($sz/1GB))
W ("archive says complete  : $ExpectFiles files, {0:N2} GB" -f ($ExpectBytes/1GB))
W ("Inno log claims success: $logOk")
$complete = ($n -ge $ExpectFiles)
W ("ACTUALLY COMPLETE      : $complete   ({0:P1} of files)" -f $(if ($ExpectFiles) { $n / $ExpectFiles } else { 0 }))
if ($logOk -and -not $complete) {
    W ""
    W "*** SILENT PARTIAL INSTALL ***"
    W "    Setup reported success and wrote $n of $ExpectFiles files, with every"
    W "    wizard page visible and $prompts prompt(s) shown."
}

W ""
W "=== last 30 lines of the Inno log ==="
if (Test-Path -LiteralPath $InnoLog) { Get-Content -LiteralPath $InnoLog | Select-Object -Last 30 | ForEach-Object { W "  $_" } }

Get-Process | Where-Object { $_.ProcessName -like 'setup_*' } | ForEach-Object { try { Stop-Process -Id $_.Id -Force } catch {} }
W ""
W "cleaning up $Install"
Remove-Item -LiteralPath $Install -Recurse -Force -ErrorAction SilentlyContinue
W "DONE - now run spancleanup.ps1 and spanshortcuts.ps1"
