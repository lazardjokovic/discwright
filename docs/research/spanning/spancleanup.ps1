<#
    Undoes what the span harness left behind.

    The test install ran to completion, so GOG's installer registered The Witcher
    in HKLM as installed at C:\dwspan\install - a folder the harness then deleted.
    That matters beyond tidiness: DiscWright's own Play button reads exactly these
    keys to decide whether a game is installed, so it would light up for a game
    that is not there and launch a missing exe. It also leaves a dead entry in
    Add/Remove Programs pointing at an uninstaller that no longer exists.

    Removes only the two keys the test created, and only if they still point at
    C:\dwspan. A real install of The Witcher elsewhere is left alone.
#>
$ErrorActionPreference = 'Continue'
"elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Found by where they point, not by product id: the harness can be run against
# any game, and hard-coding one id means the next run leaves its mess behind.
$targets = @()
foreach ($base in 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games','HKLM:\SOFTWARE\GOG.com\Games') {
    if (Test-Path $base) { foreach ($k in Get-ChildItem $base) { $targets += @{ Key=$k.PSPath; Prop='path' } } }
}
foreach ($base in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall') {
    if (Test-Path $base) { foreach ($k in Get-ChildItem $base | Where-Object { $_.PSChildName -match '_is1$' }) { $targets += @{ Key=$k.PSPath; Prop='InstallLocation' } } }
}
$hit = 0
foreach ($t in $targets) {
    if (-not (Test-Path $t.Key)) { continue }
    $val = (Get-ItemProperty -Path $t.Key -Name $t.Prop -ErrorAction SilentlyContinue).$($t.Prop)
    if ($val -notlike 'C:\dwspan*') { continue }   # a real install elsewhere - leave it alone
    $hit++
    try { Remove-Item -Path $t.Key -Recurse -Force -ErrorAction Stop; "removed : $($t.Key)   (was '$val')" }
    catch { "FAILED  : $($t.Key)  -> $($_.Exception.Message)" }
}

if ($hit -eq 0) { "nothing pointed at the test folder - no registry changes needed" }

"`n--- anything left pointing at the test folder? ---"
$left = @()
foreach ($t in $targets) {
    if (-not (Test-Path $t.Key)) { continue }
    $val = (Get-ItemProperty -Path $t.Key -Name $t.Prop -ErrorAction SilentlyContinue).$($t.Prop)
    if ($val -like 'C:\dwspan*') { $left += $t.Key }
}
if ($left.Count) { $left | ForEach-Object { "STILL THERE : $_" } } else { "none - clean" }

# The staged discs are hardlinks, so removing them cannot touch the real GOG
# downloads - only the extra directory entries this test made.
if (Test-Path 'C:\dwspan') {
    Remove-Item 'C:\dwspan' -Recurse -Force -ErrorAction SilentlyContinue
    "`nC:\dwspan removed : " + (-not (Test-Path 'C:\dwspan'))
}
"DONE"
