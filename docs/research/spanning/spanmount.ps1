<#
    Does a disc swap actually work, with real ISOs on ONE drive letter?

    This is the test the folder-based harness could not do. Last night's Witcher
    run put each slice in a DIFFERENT folder, so the path in the disc dialog was
    always wrong and the harness typed a new one every time. That told us
    spanning resumes, but not whether a person would have to use Browse.

    On real discs every disc is the SAME drive letter. The dialog offers the
    installer's original folder, never updated - and on real media that stale
    path still resolves, because the letter has not changed, only the medium
    behind it. If that holds, a swap is: change disc, click OK. If it does not,
    it is: change disc, Browse, find the folder, OK - seven times for a
    three-disc game, which is a different product.

    So: build three real UDF ISOs, mount them one at a time on ONE letter, and at
    every prompt click OK WITHOUT touching the path box. Whether the dialog closes
    is the whole measurement.

    UDF only, matching DiscWright's own builder: slice 2 is 4,294,967,294 bytes,
    two bytes under 4 GiB, and ISO9660 cannot hold a file that size. Joliet is out
    too - GOG's part names run past 64 characters.

    The Witcher is used because it is Inno-sliced (idska32). RAR-packed games
    cannot span at all and there is nothing here to test for them.

    Must run ELEVATED (mounting and re-lettering volumes). The desktop must be
    left alone. Afterwards run spancleanup.ps1 AND spanshortcuts.ps1 - a
    completed install registers the game.
#>

$ErrorActionPreference = 'Continue'
$Game    = 'The Witcher'
$Src     = "F:\DWdemo\$Game"
$Base    = 'C:\dwspan'
$IsoDir  = Join-Path $Base 'iso'
$Stage   = Join-Path $Base 'stage'
$Install = Join-Path $Base 'install'
$Result  = Join-Path $Base 'result.txt'
$InnoLog = Join-Path $Base 'inno.log'
$Letter  = 'R'                      # the one drive letter every disc appears on

# Reference from the folder-based run of 2026-08-23, which completed.
$RefFiles = 2685

function W([string]$s) { Write-Host $s; Add-Content -LiteralPath $Result -Value $s -Encoding UTF8 }

if (Test-Path -LiteralPath $Base) { Remove-Item -LiteralPath $Base -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $Base, $IsoDir, $Stage, $Install | Out-Null
Set-Content -LiteralPath $Result -Value ("SPAN MOUNT SWAP  " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
W ("elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

$exe  = Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.exe' } | Select-Object -First 1
$bins = @(Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Name -like 'setup_*.bin' } | Sort-Object Name)
if (-not $exe -or $bins.Count -lt 2) { W "need an installer with at least two parts in $Src"; return }

$fs = [IO.File]::Open($bins[0].FullName, 'Open', 'Read', 'ReadWrite')
$head = New-Object byte[] 8; [void]$fs.Read($head, 0, 8); $fs.Close()
$tag = -join ($head | ForEach-Object { [char]$_ })
if (-not $tag.StartsWith('idska32')) {
    W "first .bin is not an Inno slice (tag '$tag') - this test only applies to idska32 games"
    return
}
W ""
W "payload format : Inno disk slice (idska32)"

# --- the ISO writer, same settings as DiscWright's ---------------------------
if (-not ('ISOFileMin' -as [type])) {
Add-Type -TypeDefinition @'
public class ISOFileMin {
  public static void Create(string Path, object Stream, int BlockSize, int TotalBlocks) {
    byte[] buf = new byte[BlockSize];
    System.IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
    System.IO.FileStream o = System.IO.File.OpenWrite(Path);
    System.Runtime.InteropServices.ComTypes.IStream i = Stream as System.Runtime.InteropServices.ComTypes.IStream;
    try {
      if (o != null && i != null) {
        while (TotalBlocks-- > 0) {
          i.Read(buf, BlockSize, ptr);
          int bytes = System.Runtime.InteropServices.Marshal.ReadInt32(ptr);
          o.Write(buf, 0, bytes);
        }
        o.Flush(); o.Close();
      }
    } finally {
      if (o != null) o.Dispose();
      System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
    }
  }
}
'@
}

function New-Iso([string]$stageDir, [string]$isoPath, [string]$label) {
    $fsi=$null; $rootItem=$null; $img=$null; $stream=$null
    try {
        $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        $payload = (Get-ChildItem -Recurse -File -Force $stageDir | Measure-Object Length -Sum).Sum
        $blocks  = [long][math]::Ceiling(($payload * 1.05) / 2048) + 65536
        if ($blocks -lt 12500000) { $blocks = 12500000 }
        if ($blocks -gt 2147483000) { $blocks = 2147483000 }
        $fsi.FreeMediaBlocks = [int]$blocks
        $fsi.FileSystemsToCreate = 4          # UDF only
        try { $fsi.UDFRevision = 0x250 } catch {}
        $vn = ($label -replace '[^A-Za-z0-9_]','_'); if ($vn.Length -gt 16) { $vn = $vn.Substring(0,16) }
        $fsi.VolumeName = $vn
        $rootItem = $fsi.Root
        $rootItem.AddTree($stageDir, $false)
        $img = $fsi.CreateResultImage()
        $stream = $img.ImageStream
        [ISOFileMin]::Create($isoPath, $stream, $img.BlockSize, $img.TotalBlocks)
    } finally {
        foreach ($o in @($stream,$img,$rootItem,$fsi)) {
            if ($o) { try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o) } catch {} }
        }
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    }
}

# --- build one disc per slice -------------------------------------------------
W ""
W "building $($bins.Count) ISOs (UDF, one slice each)"
$isoOf = @{}      # slice file name -> iso path
$isos  = @()
for ($i = 0; $i -lt $bins.Count; $i++) {
    $n  = $i + 1
    $sd = Join-Path $Stage "disc$n"
    New-Item -ItemType Directory -Force -Path $sd | Out-Null
    cmd /c mklink /H "`"$(Join-Path $sd $bins[$i].Name)`"" "`"$($bins[$i].FullName)`"" | Out-Null
    if ($i -eq 0) { cmd /c mklink /H "`"$(Join-Path $sd $exe.Name)`"" "`"$($exe.FullName)`"" | Out-Null }
    $iso = Join-Path $IsoDir "WITCHER_D$n.iso"
    $t0 = Get-Date
    New-Iso $sd $iso "WITCHER_D$n"
    $isoOf[$bins[$i].Name] = $iso
    $isos += $iso
    W ("  disc {0}: {1}  ({2:N2} GB iso, {3:N0}s)" -f $n, (Split-Path $iso -Leaf), ((Get-Item $iso).Length/1GB), ((Get-Date)-$t0).TotalSeconds)
    Remove-Item -LiteralPath $sd -Recurse -Force -ErrorAction SilentlyContinue
}

# --- mount helpers: always the same drive letter ------------------------------
function Get-MountedLetter([string]$iso) {
    try {
        $di = Get-DiskImage -ImagePath $iso -ErrorAction Stop
        if (-not $di.Attached) { return $null }
        $v = $di | Get-Volume -ErrorAction Stop
        if ($v -and $v.DriveLetter) { return [string]$v.DriveLetter }
    } catch {}
    return $null
}

function Force-Letter([string]$cur, [string]$want) {
    if ($cur -eq $want) { return $true }
    # Win32_Volume can re-letter a mounted image; mountvol is the fallback.
    try {
        $v = Get-CimInstance Win32_Volume -Filter "DriveLetter='$cur`:'" -ErrorAction Stop
        if ($v) {
            Set-CimInstance -InputObject $v -Property @{ DriveLetter = "$want`:" } -ErrorAction Stop
            Start-Sleep -Milliseconds 800
            if (Test-Path -LiteralPath "$want`:\") { return $true }
        }
    } catch {}
    try {
        $v = Get-CimInstance Win32_Volume -Filter "DriveLetter='$cur`:'" -ErrorAction Stop
        if ($v -and $v.DeviceID) {
            cmd /c "mountvol $cur`: /D" | Out-Null
            cmd /c "mountvol $want`: $($v.DeviceID)" | Out-Null
            Start-Sleep -Milliseconds 800
            if (Test-Path -LiteralPath "$want`:\") { return $true }
        }
    } catch {}
    return $false
}

$mounted = $null
function Swap-To([string]$iso) {
    if ($script:mounted -and $script:mounted -ne $iso) {
        try { Dismount-DiskImage -ImagePath $script:mounted -ErrorAction Stop | Out-Null } catch { W "  (dismount complained: $($_.Exception.Message))" }
        Start-Sleep -Milliseconds 800
        $script:mounted = $null
    }
    if ($script:mounted -eq $iso) { return $true }
    try { Mount-DiskImage -ImagePath $iso -ErrorAction Stop | Out-Null } catch { W "  MOUNT FAILED: $($_.Exception.Message)"; return $false }
    Start-Sleep -Seconds 1
    $cur = Get-MountedLetter $iso
    if (-not $cur) { W "  mounted but no drive letter appeared"; return $false }
    if (-not (Force-Letter $cur $Letter)) { W "  could not put it on $Letter`: (it is on $cur`:)"; return $false }
    $script:mounted = $iso
    return $true
}

W ""
W "mounting disc 1 on $Letter`:"
if (-not (Swap-To $isos[0])) { W "cannot mount - stopping"; return }
W ("  $Letter`:\ now holds : " + ((Get-ChildItem -LiteralPath "$Letter`:\" | ForEach-Object { $_.Name }) -join ', '))

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,int e);' -Name M -Namespace WM -ErrorAction SilentlyContinue
$root = [System.Windows.Automation.AutomationElement]::RootElement

function Click-At($el) {
    $r = $el.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { return $false }
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
    [WM.M]::mouse_event(0x02,0,0,0,0); [WM.M]::mouse_event(0x04,0,0,0,0)
    Start-Sleep -Milliseconds 400
    return $true
}
function Get-SetupWindows {
    $out = @()
    $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $wins.Count; $i++) {
        $w = $wins.Item($i)
        try { if ((Get-Process -Id $w.Current.ProcessId -ErrorAction Stop).ProcessName -notlike 'setup_*') { continue } } catch { continue }
        $out += $w
    }
    return $out
}

$srcExeOnDisc = Join-Path "$Letter`:\" $exe.Name
W ""
W "launching from the mounted disc: $srcExeOnDisc"
Start-Process -FilePath $srcExeOnDisc `
    -ArgumentList @('/VERYSILENT', "/DIR=`"$Install`"", "/LOG=`"$InnoLog`"", '/NORESTART') | Out-Null

$prompts     = 0
$plainOkWork = 0
$neededType  = 0
$order       = @()
$deadline    = (Get-Date).AddMinutes(60)
$grace       = (Get-Date).AddSeconds(90)
$seenOther   = @{}

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $alive = @(Get-Process | Where-Object { $_.ProcessName -like 'setup_*' })
    if ($alive.Count -eq 0 -and (Get-Date) -gt $grace) { W ""; W "no installer processes left"; break }

    foreach ($w in Get-SetupWindows) {
        $title = $w.Current.Name
        $hwnd  = $w.Current.NativeWindowHandle
        $kids  = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
        $texts = @($kids | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
        $paths = @($texts | Where-Object { $_ -match '^[A-Z]:\\' })

        $isPrompt = ($title -match 'next part|\.BIN|disk|insert') -or ($paths.Count -gt 0 -and $texts -notcontains 'Options')
        if (-not $isPrompt) {
            $key = $title + '|' + (($texts -join ' ') -replace '\s+',' ')
            if (-not $seenOther.ContainsKey($key)) {
                $seenOther[$key] = $true
                $blurb = (($texts -join ' | ') -replace '\s+',' ')
                if ($blurb.Length -gt 200) { $blurb = $blurb.Substring(0,200) + '...' }
                W ""; W "OTHER WINDOW '$title' : $blurb"
                $ok = @($kids | Where-Object { $_.Current.Name -eq 'OK' })
                if ($ok.Count) { [void](Click-At $ok[0]); W "  dismissed" }
            }
            continue
        }

        # Which slice does it want? The Inno log names it; the dialog does not.
        $want = ''
        if (Test-Path -LiteralPath $InnoLog) {
            $l = @(Get-Content -LiteralPath $InnoLog -ErrorAction SilentlyContinue | Where-Object { $_ -match 'Asking user for new disk containing "([^"]+)"' })
            if ($l.Count) { $null = $l[-1] -match 'containing "([^"]+)"'; $want = $Matches[1] }
        }
        $prompts++
        $shortWant = if ($want -match '-(\d+)\.bin$') { $Matches[1] } else { $want }
        $order += $shortWant

        W ""
        W "PROMPT #$prompts  title='$title'"
        W ("  wants                : " + $(if ($want) { $want } else { '(could not tell)' }))
        W ("  default path offered : " + $(if ($paths.Count) { $paths[0] } else { '(none shown)' }))

        if (-not $want -or -not $isoOf.ContainsKey($want)) { W "  cannot tell which disc holds it - stopping"; break }

        # THE SWAP: same letter, different medium.
        W ("  swapping $Letter`: to " + (Split-Path $isoOf[$want] -Leaf))
        if (-not (Swap-To $isoOf[$want])) { W "  swap failed - stopping"; break }
        W ("  $Letter`:\ now holds : " + ((Get-ChildItem -LiteralPath "$Letter`:\" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) -join ', '))

        # THE MEASUREMENT: click OK and touch nothing else.
        $ok = @($kids | Where-Object { $_.Current.Name -eq 'OK' })
        if (-not $ok.Count) { W "  no OK button found - stopping"; break }
        [void](Click-At $ok[0])
        W "  clicked OK without editing the path"

        # Did that dialog go away?
        $gone = $false
        for ($t = 0; $t -lt 6; $t++) {
            Start-Sleep -Seconds 1
            $still = @(Get-SetupWindows | Where-Object { $_.Current.NativeWindowHandle -eq $hwnd })
            if (-not $still.Count) { $gone = $true; break }
        }
        if ($gone) {
            $plainOkWork++
            W "  ==> ACCEPTED. The stale path resolved to the new disc."
        } else {
            $neededType++
            W "  ==> REJECTED. Still asking, so the path had to be retyped."
            $edit = @($kids | Where-Object { $_.Current.Name -match '^[A-Z]:\\' })
            if ($edit.Count) { [void](Click-At $edit[0]); [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 150 }
            [System.Windows.Forms.SendKeys]::SendWait("$Letter`:\"); Start-Sleep -Milliseconds 300
            $ok2 = @($kids | Where-Object { $_.Current.Name -eq 'OK' })
            if ($ok2.Count) { [void](Click-At $ok2[0]) }
            Start-Sleep -Seconds 2
        }
    }
}

# --- verdict ------------------------------------------------------------------
$files = @(Get-ChildItem -LiteralPath $Install -Recurse -File -ErrorAction SilentlyContinue)
$n  = $files.Count
$sz = ($files | Measure-Object Length -Sum).Sum
$logOk = $false
if (Test-Path -LiteralPath $InnoLog) { $logOk = @(Get-Content -LiteralPath $InnoLog | Where-Object { $_ -match 'Installation process succeeded' }).Count -gt 0 }

W ""
W "=== ANSWERS ==="
W ("prompts                    : $prompts")
W ("slices asked for           : " + $(if ($order.Count) { $order -join ' -> ' } else { '(none)' }))
W ("plain OK accepted          : $plainOkWork")
W ("needed the path retyped    : $neededType")
W ("installed                  : $n files, {0:N2} GB" -f ($sz/1GB))
W ("reference run (folders)    : $RefFiles files, 13.97 GB")
W ("Inno log says succeeded    : $logOk")
W ""
if ($logOk -and $prompts -gt 0 -and $neededType -eq 0) {
    W "=> SWAPPING IS JUST 'CHANGE DISC, CLICK OK'."
    W "   The stale default path resolved every time, because the drive letter"
    W "   never changed. Browse was never needed. $prompts swaps for $($bins.Count) discs."
} elseif ($logOk -and $prompts -gt 0) {
    W "=> SWAPPING WORKS BUT IS CLUMSY."
    W "   $neededType of $prompts prompts would have needed the user to Browse."
} elseif ($prompts -eq 0) {
    W "=> NO PROMPT APPEARED. Either everything fitted, or the staging is wrong."
} else {
    W "=> DID NOT COMPLETE. See the log below."
}

W ""
W "=== last 25 lines of the Inno log ==="
if (Test-Path -LiteralPath $InnoLog) { Get-Content -LiteralPath $InnoLog | Select-Object -Last 25 | ForEach-Object { W "  $_" } }

Get-Process | Where-Object { $_.ProcessName -like 'setup_*' } | ForEach-Object { try { Stop-Process -Id $_.Id -Force } catch {} }
foreach ($i in $isos) { try { Dismount-DiskImage -ImagePath $i -ErrorAction SilentlyContinue | Out-Null } catch {} }
W ""
W "unmounted all discs; cleaning up $Install"
Remove-Item -LiteralPath $Install -Recurse -Force -ErrorAction SilentlyContinue
W "DONE - now run spancleanup.ps1 and spanshortcuts.ps1"
