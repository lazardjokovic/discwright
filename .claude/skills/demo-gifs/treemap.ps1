# How many DOWN presses reach each folder in the demo folder.
#
# The folder dialog's tree is invisible to UI Automation, so a game is picked by
# opening the seeded node and stepping down through its children. That only works
# if the step count is right, and guessing it from an alphabetical sort is a
# guess: the shell sorts its own way and the folder holds junctions, working
# folders and whatever else is lying about.
#
# So ask it. Step 6's Browse seeds from its own box, which makes it a tree that
# can be opened at the demo folder as often as needed without touching anything the
# recording will use.
param(
    [string]$DemoRoot = 'F:\DWdemo',
    [int]$Steps = 12
)
$ErrorActionPreference = 'Stop'
$SC = $PSScriptRoot
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $repo 'tests\ui\UiDriver.psm1') -Force
. "$SC\demolib.ps1"

$app = Start-DiscWright -AppPath (Join-Path $repo 'DiscWright.ps1')
$win = $app.Window
Beat 1.2

$map = @{}
try {
    $box = Get-BoxAfter $win '6)  Output folder*'
    for ($n = 1; $n -le $Steps; $n++) {
        Set-CtlText -Ctl $box -Text $DemoRoot
        Beat 0.3
        Invoke-Ctl -Ctl (Find-BrowseForBox -Win $win -Box $box) -SettleMs 1200
        $dlg = Find-Ctl -Root $win -NameLike 'Browse For Folder' -TimeoutSec 10
        if (-not $dlg) { Write-Host "  $n : no dialog"; continue }
        $null = Set-FolderTreeFocus $dlg
        Send-Keys '{RIGHT}' 350
        for ($i = 0; $i -lt $n; $i++) { Send-Keys '{DOWN}' 130 }
        Invoke-Ctl -Ctl (Find-Ctl -Root $dlg -NameLike 'OK' -TimeoutSec 5) -SettleMs 900
        $got = $box.Current.Name
        $map[$n] = $got
        Write-Host ('  {0,2} -> {1}' -f $n, $got)
    }
}
finally {
    Stop-DiscWright $app
}

$map.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" } |
    Set-Content -LiteralPath (Join-Path $env:TEMP 'discwright-treemap.txt') -Encoding UTF8
Write-Host ('  written to ' + (Join-Path $env:TEMP 'discwright-treemap.txt'))
