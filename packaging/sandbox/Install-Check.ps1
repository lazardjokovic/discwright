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
param(
    # Add VBScript to this sandbox before installing, so the run tests the
    # other half of the installer's choice. The host passes it through.
    [switch]$WithVBScript
)

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
# Smart App Control refuses to run an unsigned binary at all. That is a fact
# about the image this is running on, not about the installer, and calling it a
# failure is a lie that stops a release: it happened on 0.9.0, on 0.9.1 and
# again on 0.10.0, while 0.9.2 passed only because that sandbox came up with the
# policy in evaluation mode instead. The same build, the same installer, three
# fails and a pass.
#
# Matched on the wording Windows actually produced, which is recorded here
# rather than guessed at:
#   This command cannot be run due to the error: An Application Control policy
#   has blocked this file.
function Test-PolicyBlocked([string]$message) {
    if (-not $message) { return $false }
    return [bool]($message -match 'Application Control policy' -or
                  $message -match 'blocked by your administrator')
}

# Set when the installer never ran. Everything downstream then reports SKIP
# rather than FAIL, because none of it was tested either way.
$script:Blocked = $false

function Check([string]$what, [bool]$ok, [string]$detail = '', [switch]$Always) {
    if ($script:Blocked -and -not $Always) {
        Say ("SKIP  {0}  [not tested: the installer never ran]" -f $what)
        return $false
    }
    Say (("{0}  {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what) + $(if ($detail) { "  [$detail]" } else { '' }))
    return $ok
}

# Windows Sandbox always runs as this account, which is how the script knows
# whether it is in one. It is also what decides, at the very end, whether it
# is allowed to shut the machine down.
$script:InSandbox = ($env:USERNAME -eq 'WDAGUtilityAccount')
Say "DiscWright installer check, $(if ($script:InSandbox) { 'inside Windows Sandbox' } else { "on $env:COMPUTERNAME" })"
Say "windows : $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"
$sac = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
Say "smart app control here : $(switch ($sac) { 0 {'off'} 1 {'ON'} 2 {'evaluation'} default {'not reported'} })"
Say ""

$setup = @(Get-ChildItem 'C:\dw\in' -Filter '*-setup.exe' | Select-Object -First 1)[0]
Say "installer : $($setup.Name), $([math]::Round($setup.Length / 1MB, 2)) MB"
Say "sha256    : $((Get-FileHash $setup.FullName -Algorithm SHA256).Hash)"

if ($WithVBScript) {
    # The sandbox image has no VBScript, which is what makes it useful. Adding
    # it back tests the other half of the installer's choice, on a machine that
    # really has it rather than on a promise that it would behave.
    Say "adding VBScript to this sandbox (a Feature on Demand, so this downloads)..."
    $add = & dism.exe /Online /Add-Capability /CapabilityName:VBSCRIPT~~~~ 2>&1
    Say "   dism: $((($add | Select-String 'error|complete|Error') -join ' ').Trim())"
}

# Worked out here, before anything asks about it. The installer chooses its
# launcher from this, so every check about the shortcut needs it, and reading
# it further down made the first answer compare against an unset variable.
$vbsHere = Test-Path 'C:\Windows\System32\vbscript.dll'
Say "vbscript  : $(if ($vbsHere) { 'on this image' } else { 'not on this image' })"
Say ""

# 1. Install, silently, with no prompt and no admin.
try {
    $p = Start-Process $setup.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru -Wait
    $null = Check "the installer runs and exits cleanly" ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
} catch {
    if (Test-PolicyBlocked $_.Exception.Message) {
        $script:Blocked = $true
        Say "BLOCKED  this image will not run an unsigned installer"
        Say "         Smart App Control is $(if ($sac -eq 1) { 'ON' } else { 'enforcing' }) here, so the file never"
        Say "         started. Nothing below was tested and none of it is known to be"
        Say "         broken. Run the installer somewhere the policy allows it, such as"
        Say "         the Windows test VM, before believing anything about this build."
    } else {
        $null = Check "the installer runs and exits cleanly" $false $_.Exception.Message
    }
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

    # The Print artwork and Burn to disc buttons dot-source these when pressed.
    # Left out of the installer they are dead ends on an installed copy while
    # working perfectly from a checkout, which is the shape of the defect that
    # shipped in 0.8.0 and had to be fixed in 0.8.1.
    $printMod = Join-Path $dir 'print\DiscWright.Print.ps1'
    $burnMod  = Join-Path $dir 'burn\DiscWright.Burn.ps1'
    $null = Check "the print module is installed, or Print artwork is a dead button" `
        (Test-Path $printMod) $printMod
    $null = Check "the burn module is installed, or Burn to disc is a dead button" `
        (Test-Path $burnMod) $burnMod
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
$null = Check -Always "mshta and JScript are on this image, which every disc's menu needs" `
    ((Test-Path 'C:\Windows\System32\mshta.exe') -and (Test-Path 'C:\Windows\System32\jscript.dll') `
     -and (Test-Path 'C:\Windows\System32\scrrun.dll'))

# 4. Does the thing that was installed actually start? Both ways, and they are
#    different questions: through the shortcut, which is how a person starts it,
#    and directly, which says whether the app itself is fine when the launcher
#    chain is not there. Closed afterwards, since a window nobody closes would
#    hold the sandbox open until the timeout.
function Start-And-Watch([string]$exe, [string[]]$argv, [int]$seconds = 25) {
    $before = @(Get-Process powershell -ErrorAction SilentlyContinue).Id
    # -ArgumentList refuses an empty array, and a shortcut is started with no
    # arguments at all, so the two cases are separate calls rather than one with
    # a sometimes-empty list.
    if ($argv -and $argv.Count) { $null = Start-Process $exe -ArgumentList $argv -PassThru }
    else                        { $null = Start-Process $exe -PassThru }
    Start-Sleep -Seconds $seconds
    $new = @(Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $before })
    $window = @($new | Where-Object { $_.MainWindowTitle -like 'DiscWright*' })
    foreach ($p in $new) { try { $p.Kill() } catch {} }
    Start-Sleep -Seconds 2
    return $window.Count -gt 0
}

if ($start) {
    # The shortcut itself, not a guess at what it points at. Testing wscript and
    # the .vbs regardless was how this check went on failing after the installer
    # had been fixed to stop using them.
    $viaShortcut = Start-And-Watch $start.FullName @()
    $null = Check "the Start menu shortcut opens the app" $viaShortcut `
        $(if (-not $viaShortcut -and -not $vbsHere) { 'and this image has no VBScript' })
}
if ($dir -and (Test-Path (Join-Path $dir 'DiscWright.ps1'))) {
    $viaScript = Start-And-Watch 'powershell.exe' @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $dir 'DiscWright.ps1')`"")
    $null = Check "the app itself opens when started without any launcher" $viaScript
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
# Closes the sandbox, which is what lets the host know it is finished. Only in a
# sandbox: run anywhere else, this used to shut that machine down instead, which
# is exactly what it did to the test VM the one time it was borrowed.
if ($script:InSandbox) {
    shutdown /s /t 0
} else {
    Say "not in a sandbox, so this machine is left running."
}
