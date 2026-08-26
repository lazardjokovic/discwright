<#
    Second half of the cleanup: the shortcuts a completed test install leaves
    outside the registry.

    spancleanup.ps1 removes the registry keys and C:\dwspan, but Inno also writes
    Start Menu and desktop shortcuts pointing into the install folder. Those
    survive and point at a folder that no longer exists.

    Rule, same as the registry pass: a shortcut is removed only if it points at
    C:\dwspan, so a real install of the same game is never touched.

    -LiteralPath EVERYWHERE, not optional. GOG names its Start Menu groups
    "<Game> [GOG.com]", and to PowerShell's path parser "[GOG.com]" is a wildcard
    character class. Remove-Item -Path on such a name matches nothing, deletes
    nothing, and does NOT error - a wildcard matching zero items is not a failure.
    An earlier version of this script reported twelve shortcuts removed and
    removed none of them.
#>
$ErrorActionPreference = 'Continue'
"elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$sh = New-Object -ComObject WScript.Shell
$roots = @(
    'C:\ProgramData\Microsoft\Windows\Start Menu\Programs',
    'C:\Users\Public\Desktop',
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)

$emptied = New-Object System.Collections.Generic.HashSet[string]
$killed = 0
foreach ($r in $roots) {
    if (-not (Test-Path -LiteralPath $r)) { continue }
    foreach ($l in Get-ChildItem -LiteralPath $r -Recurse -Filter *.lnk -ErrorAction SilentlyContinue) {
        $t = ''
        try { $t = $sh.CreateShortcut($l.FullName).TargetPath } catch { continue }
        if ($t -notlike 'C:\dwspan*') { continue }
        try {
            Remove-Item -LiteralPath $l.FullName -Force -ErrorAction Stop
            # Verified, not assumed - see the note above about silent no-ops.
            if (Test-Path -LiteralPath $l.FullName) { "FAILED (still there) : $($l.FullName)" }
            else { $killed++; [void]$emptied.Add($l.DirectoryName); "removed : $($l.FullName)  -> $t" }
        } catch { "FAILED : $($l.FullName) -> $($_.Exception.Message)" }
    }
}
if ($killed -eq 0) { "no shortcuts pointed at the test folder" }

# Only groups this run took a shortcut out of, and only once no .lnk remains in
# them. Inno also drops a Support.url and an empty Documents folder that point at
# gog.com rather than the install, so a group can be pure test residue while not
# being empty - "no shortcuts left" is the right test, not "no files left".
# A blanket sweep over every empty folder under the Start Menu is NOT the same
# thing: it reaches unrelated apps whose groups happen to be empty.
$groups = New-Object System.Collections.Generic.HashSet[string]
foreach ($d in @($emptied)) {
    $cur = $d
    while ($cur -and ($roots -notcontains $cur)) {
        $parent = Split-Path $cur -Parent
        if ($roots -contains $parent) { [void]$groups.Add($cur); break }
        $cur = $parent
    }
}
foreach ($g in @($groups)) {
    if (-not (Test-Path -LiteralPath $g)) { continue }
    $lnks = @(Get-ChildItem -LiteralPath $g -Recurse -Force -Filter *.lnk -ErrorAction SilentlyContinue)
    if ($lnks.Count -ne 0) { "kept (still has $($lnks.Count) shortcut(s)) : $g"; continue }
    $left = @(Get-ChildItem -LiteralPath $g -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    Remove-Item -LiteralPath $g -Force -Recurse -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $g) { "could not remove group : $g" }
    else { "removed group : $g" + $(if ($left.Count) { "  (also took: " + ($left -join ', ') + ")" } else { '' }) }
}

"DONE"
