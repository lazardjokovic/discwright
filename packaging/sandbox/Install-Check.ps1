<#
    Runs INSIDE Windows Sandbox, started by Test-Installer.ps1. Everything it
    writes goes to C:\dw\out, which is the host's folder.

    The questions are the ones the packaging runbook asks by hand, in order:
    does it install without a prompt, is it really there, does the app start
    from what was installed, does it uninstall, and is it gone afterwards.

    It says PASS or FAIL per line and never stops early: a release check that
    answers one question and dies has told you almost nothing.
#>
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'Runs in the sandbox with its console as the only place to see progress; the file it writes is the result.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'Killing a process that has already gone is the intended outcome, and there is nothing to log in a machine about to shut down.')]
param()

$ErrorActionPreference = 'Continue'
$out = 'C:\dw\out'
$lines = New-Object System.Collections.ArrayList

function Say([string]$text) {
    $null = $lines.Add($text)
    Write-Host $text
    # Written as it goes, so a sandbox that dies halfway still reports what it
    # had reached.
    Set-Content -LiteralPath (Join-Path $out 'progress.txt') -Value $lines -Encoding UTF8
}
function Check([string]$what, [bool]$ok, [string]$detail = '') {
    Say (("{0}  {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what) + $(if ($detail) { "  [$detail]" } else { '' }))
    return $ok
}

Say "DiscWright installer check, inside Windows Sandbox"
Say "windows : $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"
$sac = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
Say "smart app control here : $(switch ($sac) { 0 {'off'} 1 {'ON'} 2 {'evaluation'} default {'not reported'} })"
Say ""

$setup = @(Get-ChildItem 'C:\dw\in' -Filter '*-setup.exe' | Select-Object -First 1)[0]
Say "installer : $($setup.Name), $([math]::Round($setup.Length / 1MB, 2)) MB"
Say "sha256    : $((Get-FileHash $setup.FullName -Algorithm SHA256).Hash)"
Say ""

# 1. Install, silently, with no prompt and no admin.
try {
    $p = Start-Process $setup.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru -Wait
    $null = Check "the installer runs and exits cleanly" ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
} catch {
    $null = Check "the installer runs and exits cleanly" $false $_.Exception.Message
}

# 2. What it left behind, as Windows records it.
$entry = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
         Where-Object { $_.DisplayName -like '*DiscWright*' } | Select-Object -First 1
$null = Check "Windows lists it as an installed program" ($null -ne $entry) $(if ($entry) { "$($entry.DisplayName) $($entry.DisplayVersion)" })
$null = Check "it installed for this user, with no administrator" ($null -ne $entry)

$dir = if ($entry) { $entry.InstallLocation } else { $null }
if ($dir -and (Test-Path $dir)) {
    $files = @(Get-ChildItem $dir -Recurse -File)
    Say "installed into : $dir"
    foreach ($f in $files) { Say "   $($f.Name)  $($f.Length) bytes" }
    $null = Check "the app itself is there, as scripts rather than an exe" `
        ((Test-Path (Join-Path $dir 'DiscWright.ps1')) -and (Test-Path (Join-Path $dir 'DiscWright.vbs')))
    $version = (Select-String -Path (Join-Path $dir 'DiscWright.ps1') -Pattern "^\`$APP_VERSION\s*=\s*'([^']+)'" |
                Select-Object -First 1).Matches.Groups[1].Value
    $null = Check "the installed copy names its version" ($version -ne '') $version
} else {
    $null = Check "the app itself is there, as scripts rather than an exe" $false 'no install location'
}

$shortcut = @(Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter '*.lnk' -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -like '*DiscWright*' })
$null = Check "a Start menu shortcut exists" ($shortcut.Count -gt 0) ($shortcut.Name -join ', ')

# What that shortcut actually starts. The installer picks its launcher from what
# the machine has: wscript and the .vbs where VBScript exists, because that is
# the one that opens the app with no console flashing up first, and powershell
# directly where it does not. Pointing at a launcher this machine cannot run is
# how 0.8.0 shipped a Start menu entry that only ever opened an error box.
$start = @($shortcut | Where-Object { $_.Name -notlike 'Uninstall*' })[0]
if ($start) {
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut($start.FullName)
    Say "shortcut target: $($lnk.TargetPath) $($lnk.Arguments)"
    $usesVbs = $lnk.TargetPath -like '*wscript.exe' -or $lnk.Arguments -like '*.vbs*'
    $null = Check "the shortcut's launcher is one this machine can run" `
        ($usesVbs -eq $vbsHere) `
        $(if ($usesVbs) { 'wscript and the .vbs' } else { 'powershell directly' })
}

# 3. The Windows pieces this depends on, because a clean image is where their
#    absence shows. VBScript became a Feature on Demand in Windows 11 24H2 and
#    Microsoft has said it goes away; the disc's menu needs mshta with JScript
#    and Scripting.FileSystemObject, which is a separate question.
Say ""
$vbsHere = Test-Path 'C:\Windows\System32\vbscript.dll'
Say "VBScript on this image: $vbsHere"
$null = Check "mshta and JScript are on this image, which every disc's menu needs" `
    ((Test-Path 'C:\Windows\System32\mshta.exe') -and (Test-Path 'C:\Windows\System32\jscript.dll') `
     -and (Test-Path 'C:\Windows\System32\scrrun.dll'))

# 4. Does the thing that was installed actually start? Both ways, and they are
#    different questions: through the shortcut, which is how a person starts it,
#    and directly, which says whether the app itself is fine when the launcher
#    chain is not there. Closed afterwards, since a window nobody closes would
#    hold the sandbox open until the timeout.
function Start-And-Watch([string]$exe, [string[]]$argv, [int]$seconds = 25) {
    $before = @(Get-Process powershell -ErrorAction SilentlyContinue).Id
    $null = Start-Process $exe -ArgumentList $argv -PassThru
    Start-Sleep -Seconds $seconds
    $new = @(Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $before })
    $window = @($new | Where-Object { $_.MainWindowTitle -like 'DiscWright*' })
    foreach ($p in $new) { try { $p.Kill() } catch {} }
    Start-Sleep -Seconds 2
    return $window.Count -gt 0
}

if ($dir -and (Test-Path (Join-Path $dir 'DiscWright.vbs'))) {
    $viaShortcut = Start-And-Watch 'wscript.exe' @("`"$(Join-Path $dir 'DiscWright.vbs')`"")
    $null = Check "the Start menu's launcher opens the app" $viaShortcut `
        $(if (-not $viaShortcut -and -not $vbsHere) { 'no VBScript on this image, so wscript cannot run the .vbs' })
    $viaScript = Start-And-Watch 'powershell.exe' @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $dir 'DiscWright.ps1')`"")
    $null = Check "the app itself opens when started without that launcher" $viaScript
}

# 5. Uninstall, the way Windows would.
if ($entry -and $entry.UninstallString) {
    $exe = $entry.UninstallString.Trim('"')
    try {
        $p = Start-Process $exe -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru -Wait
        $null = Check "it uninstalls" ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
    } catch {
        $null = Check "it uninstalls" $false $_.Exception.Message
    }
    Start-Sleep -Seconds 3
    $left = @(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
              Where-Object { $_.DisplayName -like '*DiscWright*' })
    $null = Check "Windows no longer lists it" ($left.Count -eq 0)
    $null = Check "its folder is gone" (-not ($dir -and (Test-Path $dir))) $dir
    $stillThere = @(Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter '*.lnk' -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like '*DiscWright*' })
    $null = Check "the Start menu shortcut is gone" ($stillThere.Count -eq 0)
}

Say ""
Say "done $(Get-Date -Format s)"
Set-Content -LiteralPath (Join-Path $out 'result.txt') -Value $lines -Encoding UTF8
Start-Sleep -Seconds 3
# Closes the sandbox, which is what lets the host know it is finished.
shutdown /s /t 0
