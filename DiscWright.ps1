<#
  DiscWright
  Turns a GOG offline-installer folder into a burnable "retro game disc" image:
  custom drive icon + label, and an optional autorun splash menu (background, music,
  Play/Install/Manual/Extras/Exit buttons).

  Runs on Smart App Control-locked PCs: launched via signed powershell.exe, uses
  in-process Add-Type (allowed) and the signed mshta.exe to run the generated menu.
  Reuses the same build pipeline proven on the Dead Space disc.

  NOTE: keep this file pure ASCII - PS 5.1 mis-parses em-dashes / ellipses.
#>

# =================== STARTUP ENVIRONMENT GUARD ===================
# Runs before Add-Type on purpose: the things it checks are what make Add-Type and
# the window itself fail, and both failures look like "the app is just broken".
#
#   PS 5.1  Historically because New-Iso compiled its helper with /unsafe, which
#           needed "Add-Type -CompilerParameters" - dropped in PowerShell 6, so the
#           app ran fine until BUILD ISO and died at the last step. That is no
#           longer true: the helper compiles with a plain Add-Type now. The check
#           stays because nothing has been tested on 7, not because it is known to
#           fail. Somebody should try it and either lift this or write down why not.
#   STA     WinForms needs a single-threaded apartment. powershell.exe -File is STA
#           by default, but -MTA and several hosting paths are not, and there the
#           dialogs and file pickers misbehave rather than fail outright.

# No WinForms yet, so MessageBox is not available. WScript.Shell is present in every
# PowerShell on Windows and still gives a real modal window, which is what the rule
# about errors getting a window actually asks for.
function Show-StartupError([string]$msg) {
    try { [void](New-Object -ComObject WScript.Shell).Popup($msg,0,'DiscWright',0x10) }
    catch { Write-Host $msg }
}

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Show-StartupError (
        "DiscWright needs Windows PowerShell 5.1, but this is PowerShell $($PSVersionTable.PSVersion).`r`n`r`n" +
        "Building the ISO uses a compiler option that PowerShell 6 and later removed, so the build would " +
        "fail at the very last step.`r`n`r`n" +
        "Start DiscWright with its shortcut, or with 'Run DiscWright.cmd'.")
    exit 1
}

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    Show-StartupError (
        "DiscWright has to run in single-threaded apartment (STA) mode, and this session is " +
        "$([Threading.Thread]::CurrentThread.GetApartmentState()).`r`n`r`n" +
        "The file pickers and dialogs do not work reliably otherwise.`r`n`r`n" +
        "Start DiscWright with its shortcut, or with 'Run DiscWright.cmd' - both pass the -STA switch.")
    exit 1
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$PROJECT_FILE = 'discproject.json'

# Kept in step with the git tag by a CI check that runs when a tag is pushed, so
# this cannot quietly drift a release behind. Shown in the title bar and the log,
# and written into every project file - a bug report that comes with a project
# file then says for itself which version built the disc.
$APP_VERSION  = '0.4.1'

# =================== SMALL HELPERS ===================

function Test-SamePath([string]$a,[string]$b) {
    if ([string]::IsNullOrWhiteSpace($a) -or [string]::IsNullOrWhiteSpace($b)) { return $false }
    try { return ([IO.Path]::GetFullPath($a).TrimEnd('\') -ieq [IO.Path]::GetFullPath($b).TrimEnd('\')) } catch { return $false }
}

# $child is $parent itself, or lives underneath it
function Test-SubPath([string]$child,[string]$parent) {
    if ([string]::IsNullOrWhiteSpace($child) -or [string]::IsNullOrWhiteSpace($parent)) { return $false }
    try {
        $c=[IO.Path]::GetFullPath($child).TrimEnd('\'); $p=[IO.Path]::GetFullPath($parent).TrimEnd('\')
        return ($c -ieq $p) -or $c.StartsWith($p+'\',[StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}

# Files copied off a mounted ISO or any read-only media keep the ReadOnly attribute.
# Overwriting one later fails - and GDI+ reports that as "a generic error occurred
# in GDI+" rather than access denied, which is impossible to diagnose from the message.
function Clear-ReadOnly([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) { return }
    try {
        $item = Get-Item -LiteralPath $path -Force
        if ($item.PSIsContainer) {
            Get-ChildItem -Recurse -Force -File $path -EA SilentlyContinue |
                Where-Object { $_.IsReadOnly } | ForEach-Object { $_.IsReadOnly = $false }
        } elseif ($item.IsReadOnly) { $item.IsReadOnly = $false }
    } catch {}
}

# Write to temp then move: never leaves a half-written image if encoding fails, and
# sidesteps a read-only or transiently locked destination.
function Save-PngAtomic($bmp,[string]$outPng) {
    $tmp = [IO.Path]::Combine([IO.Path]::GetTempPath(), ([Guid]::NewGuid().ToString('N')+'.png'))
    $bmp.Save($tmp,[System.Drawing.Imaging.ImageFormat]::Png)
    Clear-ReadOnly $outPng
    Move-Item -LiteralPath $tmp -Destination $outPng -Force
}

# Sub-gigabyte payloads read badly in GB - a CD-sized game becomes "0.39 GB", and a
# few megabytes of extras rounds to "0.00 GB", which looks like nothing at all.
# Switch units instead of printing a meaningless zero.
# Minutes and seconds until it runs past an hour, which a 100 GB BD-R XL can.
#
# Floor, not [int]: casting a double to [int] in PowerShell ROUNDS rather than
# truncating, so [int]1.58 is 2 and 95 seconds displayed as "02:35" - a clock that
# reads a minute ahead of itself. A stopwatch counts up, it does not round.
function Format-Elapsed([TimeSpan]$t) {
    if ($t.TotalHours -ge 1) { return '{0:00}:{1:00}:{2:00}' -f [int][math]::Floor($t.TotalHours), $t.Minutes, $t.Seconds }
    return '{0:00}:{1:00}' -f [int][math]::Floor($t.TotalMinutes), $t.Seconds
}

function Format-Size([double]$bytes) {
    if ($bytes -ge 1GB) { return ('{0:N2} GB' -f ($bytes/1GB)) }
    if ($bytes -ge 1MB) { return ('{0:N0} MB' -f ($bytes/1MB)) }
    if ($bytes -ge 1KB) { return ('{0:N0} KB' -f ($bytes/1KB)) }
    return ('{0:N0} bytes' -f $bytes)
}

# For text that lands in HTML markup (title, button captions).
function ConvertTo-HtmlText([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    # Harmless in HTML, where a newline is just whitespace - stripped anyway so that
    # every writer in the build treats them the same way. A rule with an exception
    # is a rule somebody has to remember.
    $s = Remove-ControlChars $s
    if ([string]::IsNullOrEmpty($s)) { return '' }
    return ($s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;' -replace '"','&quot;')
}

# For text that lands inside a JS string literal. HTML entities are NOT decoded
# inside <script>, so these must be backslash-escaped, not entity-escaped.
function ConvertTo-JsString([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    # Control characters first, and by removal rather than by escaping. A newline
    # inside a JS string literal is not a character to encode, it is the end of the
    # literal - JScript treats it as an unterminated string and refuses the whole
    # file, so one bad name takes the entire menu with it. Nothing in a game name
    # needs them.
    $s = Remove-ControlChars $s
    if ([string]::IsNullOrEmpty($s)) { return '' }
    return ($s -replace '\\','\\' -replace '"','\"' -replace '<','\x3c' -replace '>','\x3e')
}

# =================== BUILD PIPELINE ===================

function Get-GameInfo([string]$folder) {
    # Kind and ParentIndex describe an entry's place on the disc rather than
    # anything read out of the folder, so they start as "a game of its own" and
    # the interface changes them afterwards. ParentIndex is an index into the
    # disc's own list of entries, -1 meaning none - names are not stable enough
    # to point with, since two GOG folders can yield the same ProductName.
    # ManualPath and ExtrasPath are this entry's OWN. A disc can also carry a
    # manual and an Extras folder for the whole disc; an entry that has none of
    # its own falls back to those, which is what keeps a single-game disc, and
    # every project written before this, behaving exactly as it did.
    $info = @{ Ok=$false; SetupExe=$null; Files=@(); GameName=$null; Msg=''; TotalBytes=0; Folder=$null
               Warning=''; MissingParts=@(); Kind='Game'; ParentIndex=-1
               ManualPath=$null; ExtrasPath=$null }
    if (-not (Test-Path $folder)) { $info.Msg='Folder not found.'; return $info }
    $exes = @(Get-ChildItem $folder -Filter 'setup_*.exe' -File -ErrorAction SilentlyContinue |
              Sort-Object Length -Descending)
    if ($exes.Count -eq 0) {
        # Two folders in a project look alike and sit next to each other: the GOG
        # download, and the output folder DiscWright writes to. The second holds a
        # disc\ subfolder with the installer one level down, so picking it lands
        # here - and the generic message sends people looking for a problem with
        # their download instead of at which folder they picked. Name it instead.
        $inner = Join-Path $folder 'disc'
        $innerExe = @()
        if (Test-Path $inner) {
            $innerExe = @(Get-ChildItem $inner -Filter 'setup_*.exe' -File -ErrorAction SilentlyContinue)
        }
        if ($innerExe.Count -gt 0) {
            $info.Msg = 'This looks like a disc DiscWright built, not a GOG download. Step 1 wants the folder you downloaded from GOG; this one is where the ISO gets written.'
        } else {
            $info.Msg = 'No GOG "setup_*.exe" found in this folder.'
        }
        return $info
    }
    $exe = $exes[0]
    Set-InstallerFacts $info $exe
    if ($info.MissingParts.Count -eq 0 -and $exes.Count -gt 1) {
        # Several installers in one folder is normal (base game plus DLC). Say which
        # one was picked, so a wrong guess is visible before the disc is burned.
        $info.Warning = "$($exes.Count) installers in this folder - using the largest, $($exe.Name)."
    }
    return $info
}

# Everything true of an installer once one has been chosen: its parts, whether the
# download finished, its name and its size. Shared, because an add-on is described
# exactly the same way - the only difference is how the .exe was picked.
function Set-InstallerFacts([hashtable]$info,[System.IO.FileInfo]$exe) {
    # Parts belong to ONE installer and are named "<installer>-1.bin", "-2.bin"...
    # Taking every setup_*.bin in the folder swept in the parts of a DLC or of a
    # second game stored alongside, and wrote them to the disc as if they belonged
    # to this installer. Match on the chosen exe's own name instead - compared as
    # plain strings, never wildcards, because GOG names are full of brackets and
    # dots that -like and -match would read as syntax.
    $stem = $exe.BaseName + '-'
    $bins = @(Get-ChildItem $exe.DirectoryName -Filter '*.bin' -File -ErrorAction SilentlyContinue |
              Where-Object { $_.Name.StartsWith($stem,[StringComparison]::OrdinalIgnoreCase) } |
              Sort-Object Name)

    # A download that stopped early leaves a gap in the numbering. Building it
    # produces a clean-looking ISO that only fails when someone runs the installer
    # off the burned disc - the worst possible place to discover it, because the
    # disc is already spent. Games with no parts at all are normal; only a gap in
    # an existing sequence is suspicious.
    $nums = @()
    foreach ($b in $bins) { if ($b.BaseName -match '-(\d+)$') { $nums += [int]$Matches[1] } }
    if ($nums.Count -gt 0) {
        $nums = @($nums | Sort-Object -Unique)
        $info.MissingParts = @(1..($nums[-1]) | Where-Object { $nums -notcontains $_ })
    }
    if ($info.MissingParts.Count -gt 0) {
        $info.Warning = 'INCOMPLETE: installer part(s) ' + (($info.MissingParts | ForEach-Object { "-$_" }) -join ', ') +
                        ' are missing - the download looks unfinished.'
    }

    if ($info.Kind -eq 'AddOn') {
        $name = Get-AddOnName $exe.Name
    } else {
        $name = $exe.VersionInfo.ProductName
        if ([string]::IsNullOrWhiteSpace($name)) { $name = ($exe.BaseName -replace '^setup_','' -replace '_',' ' -replace '\s*\d.*$','') }
        # Inno pads VersionInfo strings with trailing spaces - trim or they leak into
        # folder names ("...Edition                    Disc").
        $name = $name.Trim()
    }
    $info.Ok=$true; $info.SetupExe=$exe; $info.Files=@($exe)+@($bins); $info.GameName=$name
    $info.Folder = $exe.DirectoryName
    $info.TotalBytes = ($info.Files | Measure-Object Length -Sum).Sum
    $plural = if ($info.Files.Count -eq 1) { 'file' } else { 'files' }
    $info.Msg = "Detected: $name  ($($info.Files.Count) $plural, $(Format-Size $info.TotalBytes))"
}

# An add-on is named from its filename, never from the installer's ProductName.
# Every GOG patch reports the ProductName of the game it patches - all four
# Hollow Knight patches call themselves "Hollow Knight" - so a disc built that way
# would show four identical buttons and no way to tell which was which. The
# filename is the only thing that distinguishes them.
function Get-AddOnName([string]$fileName) {
    $raw = [IO.Path]::GetFileNameWithoutExtension($fileName)
    $n = $raw
    # patch_<game>_<from>_to_<to>: the version being moved TO is what tells two
    # patches apart, and it belongs at the front where the button will not clip it.
    if     ($n -match '^patch_.+_to_(.+)$') { $n = 'Update ' + $Matches[1] }
    elseif ($n -match '^setup_(.+)$')       { $n = $Matches[1] }
    $n = (($n -replace '_',' ') -replace '\s+',' ').Trim()
    # A filename that is nothing but underscores leaves an empty label, and a
    # button with no text is a button nobody can identify. Fall back rather than
    # produce one.
    if ([string]::IsNullOrWhiteSpace($n)) { $n = $raw.Trim() }
    if ([string]::IsNullOrWhiteSpace($n)) { $n = 'Add-on' }
    return $n
}

# An add-on is picked as a single file rather than a folder: a patch or a DLC
# usually sits in the same folder as the game it belongs to, so pointing at the
# folder would just find the game again. Any .exe is accepted here - the
# setup_*.exe rule exists to identify a GOG game folder, and an add-on's identity
# comes from the list it is added to, not from its filename. That is what lets a
# mod or an overhaul, which is never named setup_*, go on the disc at all.
function Get-AddOnInfo([string]$exePath) {
    $info = @{ Ok=$false; SetupExe=$null; Files=@(); GameName=$null; Msg=''; TotalBytes=0; Folder=$null
               Warning=''; MissingParts=@(); Kind='AddOn'; ParentIndex=-1 }
    if (-not (Test-Path $exePath -PathType Leaf)) { $info.Msg='File not found.'; return $info }
    $exe = Get-Item -LiteralPath $exePath
    if ($exe.Extension -ne '.exe') {
        $info.Msg = "An add-on has to be an installer (.exe). '$($exe.Name)' is not one - loose files belong in step 5, Extra content."
        return $info
    }
    Set-InstallerFacts $info $exe
    return $info
}

# Every disc size DiscWright knows about, smallest first (usable capacities, GiB).
# One table rather than a ladder of magic numbers, because two features now read
# the same figures: the advice line under the installer list, and the splitter
# that packs a set.
#
# An 80-minute CD-R is 360,000 sectors of 2048 bytes = 0.686 GiB; 0.68 leaves a
# little room for the filesystem. Worth having as its own tier rather than
# rounding up to DVD: the sub-700 MB back catalogue is exactly the audience for
# a tool about authentic period discs, and telling someone to put a 1998 game
# on a DVD gets the whole premise wrong.
function Get-MediaTiers {
    return @(
        @{ Key='CD';   Gib=0.68; Name='CD-R 700 MB';                Short='CD-R 700 MB';    RecText='fits CD-R 700 MB' },
        @{ Key='DVD5'; Gib=4.37; Name='DVD5 4.7 GB';                Short='DVD5 4.7 GB';    RecText='fits DVD5 4.7 GB (single layer)' },
        @{ Key='DVD9'; Gib=7.95; Name='DVD9 8.5 GB (dual layer)';   Short='DVD9 8.5 GB';    RecText='needs DVD9 8.5 GB (dual layer)' },
        @{ Key='BD25'; Gib=23.3; Name='BD-R 25 GB';                 Short='BD-R 25 GB';     RecText='too big for DVD - needs BD-R 25 GB' },
        @{ Key='BD50'; Gib=46.6; Name='BD-R DL 50 GB (dual layer)'; Short='BD-R DL 50 GB';  RecText='needs BD-R DL 50 GB (dual layer)' },
        @{ Key='BDXL'; Gib=93.0; Name='BD-R XL 100 GB';             Short='BD-R XL 100 GB'; RecText='needs BD-R XL 100 GB' }
    )
}

# The first entry in the target-disc list: carry on doing what DiscWright has
# always done, which is recommend a size and build exactly one disc.
function Get-MediaAutoText { return 'Fit on one disc (recommended)' }

# What one row of the dropdown reads, given what the planner made of that tier.
# The list is where the question "which disc should I use" gets asked, so it is
# where the answer belongs. No games on the form means no plan and no annotation.
function Get-MediaOptionText([hashtable]$tier, $plan) {
    if (-not $plan) { return [string]$tier.Name }
    if (-not $plan.Ok) { return ("{0}  -  will not fit" -f $tier.Name) }
    $n = @($plan.Discs).Count
    return ("{0}  -  {1} disc{2}" -f $tier.Name, $n, $(if ($n -eq 1) { '' } else { 's' }))
}

# The dropdown shows names; everything else works in keys. Rows carry an
# annotation once there is something to plan, so a row is matched on the tier
# name it starts with rather than on the whole string.
function Get-MediaKeyFromName([string]$name) {
    $n = "$name".Trim()
    foreach ($t in Get-MediaTiers) {
        if ($n -eq $t.Name -or $n.StartsWith($t.Name + '  ')) { return $t.Key }
    }
    return ''
}
function Get-MediaNameFromKey([string]$key) {
    foreach ($t in Get-MediaTiers) { if ($t.Key -ieq $key) { return $t.Name } }
    return (Get-MediaAutoText)
}
# The same medium, minus the "(dual layer)" note. The dropdown has a row to
# itself and can afford the full name; a sentence sharing one line with the
# payload total and a game's title cannot.
function Get-MediaShortFromKey([string]$key) {
    foreach ($t in Get-MediaTiers) { if ($t.Key -ieq $key) { return $t.Short } }
    return (Get-MediaAutoText)
}

# Usable bytes for one disc of the named medium, or 0 for a key nothing recognises.
function Get-MediaCapacity([string]$key) {
    foreach ($t in Get-MediaTiers) { if ($t.Key -ieq $key) { return [double]$t.Gib * 1GB } }
    return [double]0
}

# Recommend the smallest media that fits the payload.
function Get-MediaRec([double]$bytes) {
    $gib = $bytes/1GB
    foreach ($t in Get-MediaTiers) {
        if ($gib -le $t.Gib) { return @{ Fit=$true; Key=$t.Key; Text=$t.RecText } }
    }
    return @{ Fit=$false; Key=''; Text=("too big for one disc ({0:N1} GB) - split it across a set" -f $gib) }
}

# Total bytes of a mixed list of files and folders.
function Get-ItemsSize($items) {
    $sum = [double]0
    foreach ($i in @($items)) {
        if ([string]::IsNullOrWhiteSpace($i) -or -not (Test-Path $i)) { continue }
        if (Test-Path $i -PathType Container) {
            $s = (Get-ChildItem -Recurse -File -Force $i -EA SilentlyContinue | Measure-Object Length -Sum).Sum
            if ($s) { $sum += $s }
        } else { $sum += (Get-Item -LiteralPath $i).Length }
    }
    return $sum
}

# Packs a set in the order the entries already sit in on the form.
#
# Deliberately next-fit rather than any cleverer packing: the list on the form is
# the order the user arranged, and the menu numbers follow it. A packer that
# reordered games to save a disc would produce a set whose disc 2 holds the game
# they put first. Saving one disc is worth less than a set that matches what is
# on screen.
#
# $sizes is one figure per installer, in list order. $overhead is what every disc
# carries no matter what else is on it - menu, icon, background, music.
# $firstDiscExtra is the disc-wide manual and extras, which ride on disc 1 alone
# rather than being copied onto every disc in the set.
#
# Refuses, rather than guessing, when a single installer is larger than a whole
# disc. Spreading one game's parts across several discs is a different problem
# with a different answer, and quietly producing a set that cannot install the
# game would be worse than saying so.
function Split-DiscSet([double[]]$sizes, [double]$capacity, [double]$overhead, [double]$firstDiscExtra) {
    $usable = $capacity - $overhead
    if ($usable -le 0) { return @{ Ok=$false; Reason='capacity'; TooBig=-1; Room=[double]0 } }
    if ($firstDiscExtra -gt $usable) { return @{ Ok=$false; Reason='extras'; TooBig=-1; Room=$usable } }

    $discs = @()
    $cur   = @()
    $used  = [double]0
    $room  = $usable - $firstDiscExtra
    for ($i = 0; $i -lt @($sizes).Count; $i++) {
        $sz = [double]$sizes[$i]
        if ($sz -gt $usable) { return @{ Ok=$false; Reason='entry'; TooBig=$i; Room=$usable } }
        # Disc 1 gives up room to the disc-wide extras, so the first entry can be
        # too big for disc 1 and still fit every disc after it. Let disc 1 carry
        # the extras on their own rather than refuse a set that packs fine.
        if ($cur.Count -eq 0 -and $sz -gt $room) { $discs += ,@(); $room = $usable }
        if ($cur.Count -gt 0 -and ($used + $sz) -gt $room) {
            $discs += ,@($cur)
            $cur = @(); $used = [double]0; $room = $usable
        }
        $cur  += $i
        $used += $sz
    }
    if ($cur.Count -gt 0 -or $discs.Count -eq 0) { $discs += ,@($cur) }
    return @{ Ok=$true; Reason=''; TooBig=-1; Room=$usable; Discs=$discs }
}

# What every disc in a set carries no matter which games land on it: the icon at
# the root and again inside AUTORUN, the composed background, the menu itself and
# autorun.inf. Counted twice for the icon and the background because each ships
# in two places, or is recomposed into a PNG that can come out larger than the
# JPG it was made from. Erring high costs a little headroom; erring low produces
# a disc that will not burn.
function Get-DiscOverheadBytes([hashtable]$s) {
    $b  = [double]2MB
    $b += 2 * (Get-ItemsSize @($s.IconPath))
    $b += 2 * (Get-ItemsSize @($s.BgPath))
    $b += (Get-ItemsSize @($s.MusicFile))
    return $b
}

# Works out how many discs the set needs and what goes on each, in entry indices.
# Packs by group rather than by entry so a game and its add-ons stay together.
function Get-DiscPlan([array]$entries, [string]$mediaKey, [double]$overhead, [double]$firstDiscExtra) {
    $cap = Get-MediaCapacity $mediaKey
    if ($cap -le 0) { return @{ Ok=$false; Reason='media'; Discs=@(); Capacity=[double]0; TooBigName=''; Room=[double]0 } }

    $groups = Get-EntryGroups $entries
    $sizes  = @()
    foreach ($g in $groups) {
        $t = [double]0
        foreach ($i in $g) { $t += [double]$entries[$i].TotalBytes }
        $sizes += $t
    }

    $r = Split-DiscSet ([double[]]$sizes) $cap $overhead $firstDiscExtra
    if (-not $r.Ok) {
        # Name the game rather than the group index. "Group 2 is too big" is a
        # sentence about the packer; the user needs the sentence about the disc.
        $nm = ''; $nb = [double]0
        if ($r.Reason -eq 'entry' -and $r.TooBig -ge 0) {
            $nm = [string]$entries[$groups[$r.TooBig][0]].GameName
            $nb = [double]$sizes[$r.TooBig]
        }
        return @{ Ok=$false; Reason=$r.Reason; Discs=@(); Capacity=$cap; TooBigName=$nm; TooBigBytes=$nb; Room=$r.Room }
    }

    $discs = @()
    foreach ($d in $r.Discs) {
        $idx = @()
        foreach ($gi in $d) { foreach ($e in $groups[$gi]) { $idx += $e } }
        $discs += ,@($idx)
    }
    return @{ Ok=$true; Reason=''; Discs=$discs; Capacity=$cap; TooBigName=''; TooBigBytes=[double]0; Room=$r.Room }
}

# How the plan reads on the line under the installer list, and in the dialog that
# refuses a build. One sentence, no jargon: the number of discs is the answer to
# the only question being asked.
function Get-DiscPlanText([hashtable]$plan, [string]$mediaKey) {
    $name = Get-MediaShortFromKey $mediaKey
    if (-not $plan.Ok) {
        switch ($plan.Reason) {
            # The game's OWN size, not the total on the form. "Alan Wake alone is
            # bigger than a DVD5" sat on a line beginning "2 games (8.94 GB)", and
            # read as though the 8.94 was what would not fit. Naming the figure
            # that is actually too big settles it in the same breath.
            'entry'  { return ("{0} is {1:N2} GB, too big for a {2}" -f $plan.TooBigName, ([double]$plan.TooBigBytes/1GB), $name) }
            'extras' { return ("the extra content alone is bigger than a $name") }
            'media'  { return ("no such disc") }
            default  { return ("cannot be split onto $name") }
        }
    }
    $n = @($plan.Discs).Count
    if ($n -le 1) { return "1 disc, $name" }
    return "$n discs of $name"
}

# What This PC calls each disc in a set. A set of one keeps the plain label:
# a disc with no set around it should not announce itself as "D1".
function Get-DiscSetLabel([string]$label, [int]$n, [int]$of) {
    $base = "$label".Trim()
    if ($of -le 1) { return $base }
    return ("{0} D{1}" -f $base, $n)
}

# The ISO9660 volume identifier is 16 characters, and New-Iso folds anything that
# is not alphanumeric to an underscore. Truncating the finished label at 16 would
# cut the disc number off the end of a long name and hand every disc in the set
# the same volume id, so the number is reserved first and the name takes what is
# left over.
function Get-VolumeLabel([string]$label, [int]$n, [int]$of) {
    $sfx  = if ($of -le 1) { '' } else { "_D$n" }
    $base = (("$label" -replace '[^A-Za-z0-9_]','_')).Trim('_')
    $max  = 16 - $sfx.Length
    if ($max -lt 1) { $max = 1 }
    if ($base.Length -gt $max) { $base = $base.Substring(0, $max) }
    if ([string]::IsNullOrEmpty($base)) { $base = 'DISC' }
    return ($base + $sfx)
}

# Every disc used to ship its icon as "disc.ico". Explorer caches icon bitmaps
# keyed by path, so "E:\disc.ico" was the SAME cache key for every disc that ever
# passed through that drive letter - insert a new disc and Explorer would happily
# redraw the previous disc's icon without re-reading the file. Naming the icon
# after the disc gives each one its own key.
# Strips accents down to the plain letter underneath: a Polish z-acute becomes a
# plain z, a German U-umlaut becomes a plain U. Works by splitting each character
# into its base letter plus its combining marks, then dropping the marks.
# Alphabets with no Latin equivalent - Cyrillic, Greek, CJK - come back untouched,
# which is exactly what the callers test for.
function ConvertTo-AsciiFold([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    $out = New-Object System.Text.StringBuilder
    foreach ($c in $s.Normalize([Text.NormalizationForm]::FormD).ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$out.Append($c)
        }
    }
    return $out.ToString().Normalize([Text.NormalizationForm]::FormC)
}

function Get-DiscIconName([string]$label) {
    if ([string]::IsNullOrWhiteSpace($label)) { return 'disc.ico' }
    # Fold before stripping, or an accented letter is deleted outright rather than
    # reduced: an accented letter used to vanish outright, so the Polish spelling of
    # "Wiedzmin" produced "Wiedmin". Folding first gives back "Wiedzmin".
    $n = ((ConvertTo-AsciiFold $label) -replace '[^A-Za-z0-9]','')
    if ($n.Length -gt 40) { $n = $n.Substring(0,40) }
    if ([string]::IsNullOrWhiteSpace($n)) {
        # A label with no Latin letters or digits at all fell back to the constant
        # "disc.ico" - which is exactly the shared per-path cache key this naming
        # rule exists to break. Hash the label instead, so two Cyrillic or CJK discs
        # in the same drive letter still get different icon filenames.
        $md5 = [System.Security.Cryptography.MD5]::Create()
        try { $h = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($label)) } finally { $md5.Dispose() }
        $n = 'disc' + ((($h[0..3]) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    return ($n + '.ico')
}

# Names the build pipeline owns at the disc root - extra content may not use them.
# 'disc.ico' stays reserved even when unused, so extra content cannot collide with
# discs built before the icon was named after the game.
function Test-ReservedDiscName([string]$name,[string]$iconName='disc.ico') {
    if ($name -like 'setup_*') { return $true }
    return (@('autorun.inf','disc.ico',$iconName,'AUTORUN','Extras','Games',$PROJECT_FILE) -contains $name)
}

# Folder name for one game on a multi-game disc. Numbered, so the order in the
# menu and the order in the disc's own listing agree when someone browses it by
# hand. Folded to plain ASCII for the same reason the disc label is - a disc that
# is legible in every file manager is worth more than an exact title.
function Get-GameFolderName([int]$index,[string]$name) {
    $n = (ConvertTo-AsciiFold ([string]$name)) -replace '[^A-Za-z0-9 _\-\.]',' '
    $n = ($n -replace '\s+',' ').Trim(' ','.')
    if ($n.Length -gt 48) { $n = $n.Substring(0,48).Trim() }
    if ([string]::IsNullOrWhiteSpace($n)) { $n = 'Game' }
    return ('{0:D2} - {1}' -f $index, $n)
}

# Where an entry's installer sits on the finished disc, relative to the disc root.
# Empty string means the disc root itself.
#
# The staging copy and the menu both need this and they MUST agree. They used to
# work it out separately, a few hundred lines apart, from the same rule written
# twice - and the failure mode is silent: the files land in one place, the menu
# points at another, and the disc burns with an Install button greyed out for a
# reason nothing on screen explains. One function now, called by both.
#
# A disc with a single entry keeps the original flat layout, installer at the
# root, exactly as every disc built before multi-game existed. Two or more and
# every entry moves into Games\, add-ons included: their .bin parts are named
# after their own installer, but a flat root full of a dozen setup files is
# unreadable, and the numbering is what makes the disc browsable by hand.
function Get-DiscEntryFolder([array]$entries,[int]$index) {
    if ($entries.Count -le 1) { return '' }
    return (Join-Path 'Games' (Get-GameFolderName ($index+1) $entries[$index].GameName))
}

function Get-DiscEntrySetup([array]$entries,[int]$index) {
    $rel = Get-DiscEntryFolder $entries $index
    $name = $entries[$index].SetupExe.Name
    if ([string]::IsNullOrEmpty($rel)) { return $name }
    return (Join-Path $rel $name)
}

# Where an entry's own manual and extras live on the disc. Beside its installer,
# so a game and everything belonging to it sit together and can be found by hand
# without the menu. A disc holding one entry keeps the flat Extras\ at the root,
# which is where every disc built before this put them.
function Get-DiscEntryExtras([array]$entries,[int]$index) {
    $rel = Get-DiscEntryFolder $entries $index
    if ([string]::IsNullOrEmpty($rel)) { return 'Extras' }
    return (Join-Path $rel 'Extras')
}

# The menu's view of the disc: games in order, each carrying the add-ons that
# point at it. An add-on whose parent is missing, or which points at another
# add-on, is promoted to a game of its own rather than dropped - a disc that
# shows an unexpected entry is recoverable, one that silently omits an installer
# the user paid for and burned is not.
function Get-MenuGames([array]$entries) {
    $out = @()
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $e = $entries[$i]
        $p = [int]$e.ParentIndex
        $isAddOn = ($e.Kind -eq 'AddOn') -and $p -ge 0 -and $p -lt $entries.Count -and
                   $p -ne $i -and $entries[$p].Kind -ne 'AddOn'
        if ($isAddOn) { continue }
        $addOns = @()
        for ($j = 0; $j -lt $entries.Count; $j++) {
            $a = $entries[$j]
            if ($j -eq $i -or $a.Kind -ne 'AddOn') { continue }
            if ([int]$a.ParentIndex -ne $i) { continue }
            $addOns += @{ Name=$a.GameName; Setup=(Get-DiscEntrySetup $entries $j) }
        }
        # An entry's own manual and extras, as paths on the finished disc. Empty
        # means it has none of its own and the menu should fall back to the
        # disc-wide ones.
        $man = ''; $ext = ''
        if ($e.ManualPath -or $e.ExtrasPath) {
            $ext = Get-DiscEntryExtras $entries $i
            if ($e.ManualPath) { $man = Join-Path $ext ([IO.Path]::GetFileName([string]$e.ManualPath)) }
        }
        $out += @{ Name=$e.GameName; MatchName=$e.GameName
                   Setup=(Get-DiscEntrySetup $entries $i); AddOns=@($addOns)
                   Manual=$man; Extras=$ext }
    }
    return ,@($out)
}

# A game and everything filed under it travel together when a set is packed.
# Splitting them would strand an add-on on a disc whose ParentIndex points at a
# game that is not on it, and the menu would show the patch as a game of its own.
# Uses the same test for "is this really an add-on" as the menu does, so an
# orphan - an add-on whose parent was removed - forms its own group, exactly as
# it becomes its own entry in the menu.
function Get-EntryGroups([array]$entries) {
    $groups = @()
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $e = $entries[$i]
        $p = [int]$e.ParentIndex
        $isAddOn = ($e.Kind -eq 'AddOn') -and $p -ge 0 -and $p -lt $entries.Count -and
                   $p -ne $i -and $entries[$p].Kind -ne 'AddOn'
        if ($isAddOn) { continue }
        $g = @($i)
        for ($j = 0; $j -lt $entries.Count; $j++) {
            $a = $entries[$j]
            if ($j -eq $i -or $a.Kind -ne 'AddOn') { continue }
            if ([int]$a.ParentIndex -ne $i) { continue }
            $g += $j
        }
        $groups += ,@($g)
    }
    return ,@($groups)
}

# One disc's slice of the list, renumbered so the disc stands on its own. Both
# the folder names and the menu key off an entry's position, and ParentIndex has
# to point inside the slice - left pointing at the full list it would either miss
# or, worse, land on a different game.
function Get-DiscEntries([array]$entries, [int[]]$indices) {
    $map = @{}
    for ($k = 0; $k -lt @($indices).Count; $k++) { $map[[int]$indices[$k]] = $k }
    $out = @()
    foreach ($i in @($indices)) {
        $src  = $entries[[int]$i]
        $copy = @{}
        foreach ($key in $src.Keys) { $copy[$key] = $src[$key] }
        $p = [int]$src.ParentIndex
        $copy.ParentIndex = $(if ($map.ContainsKey($p)) { $map[$p] } else { -1 })
        $out += $copy
    }
    return ,@($out)
}

# Taking an entry out renumbers every entry after it, and parents are stored as
# positions. Removing the second of four therefore silently re-points an add-on
# that belonged to the fourth at the third - a disc that builds cleanly with the
# DLC filed under the wrong game. So the shift is applied here, in one place,
# rather than left to whoever calls Remove.
#
# An add-on whose own parent is the entry being removed becomes a game of its
# own. The alternative is deleting it too, which throws away an installer the
# user chose, without asking.
function Remove-GameEntry([array]$entries,[int]$index) {
    if ($index -lt 0 -or $index -ge $entries.Count) { return ,@($entries) }
    $kept = @()
    for ($i = 0; $i -lt $entries.Count; $i++) {
        if ($i -eq $index) { continue }
        $e = $entries[$i]
        $p = [int]$e.ParentIndex
        if ($p -eq $index)   { $e.Kind = 'Game'; $e.ParentIndex = -1 }
        elseif ($p -gt $index) { $e.ParentIndex = $p - 1 }
        $kept += $e
    }
    return ,@($kept)
}

# Reordering entries is deliberately not here. The order decides the numbering
# of the Games\ folders and the order of the chooser, so it does matter - but
# nobody has asked for it, and the two buttons it needs do not fit beside the
# list without pushing the window past a 1080p screen. Remove and re-add is the
# workaround until it earns the space.

function Test-IconInput([string]$path) {
    $r = @{ Ok=$false; IsIco=$false; W=0; H=0; Msg='' }
    if (-not (Test-Path $path)) { $r.Msg='File not found.'; return $r }
    $ext = [IO.Path]::GetExtension($path).ToLower()
    try {
        if ($ext -eq '.ico') {
            $ic = New-Object System.Drawing.Icon($path)
            $r.IsIco=$true; $r.W=$ic.Width; $r.H=$ic.Height; $ic.Dispose()
            $r.Ok=$true; $r.Msg="Valid .ico ($($r.W)x$($r.H) default frame). Will be used as-is."
        } else {
            $img=[System.Drawing.Image]::FromFile($path); $r.W=$img.Width; $r.H=$img.Height; $img.Dispose()
            if ($r.W -lt 64 -or $r.H -lt 64) { $r.Msg="Image is only $($r.W)x$($r.H) - too small (min 64, 256+ recommended)."; return $r }
            $r.Ok=$true
            $sq = ($r.W -eq $r.H)
            $r.Msg = "Image $($r.W)x$($r.H)" + $(if(-not $sq){" (not square - will be cropped to a square icon)"}else{" - good."}) + $(if($r.W -lt 256){" [under 256px: may look soft]"}else{""})
        }
    } catch { $r.Msg="Not a readable image: $($_.Exception.Message)" }
    return $r
}

# The icon is validated the moment it is picked; the background never was, so a
# corrupt or unsupported image sailed through the whole form and only failed at
# build time as "A generic error occurred in GDI+" - a message that tells the user
# nothing about which file is at fault.
function Test-BgInput([string]$path) {
    $r = @{ Ok=$false; W=0; H=0; Msg='' }
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) { $r.Msg='File not found.'; return $r }
    try {
        $img=[System.Drawing.Image]::FromFile($path); $r.W=$img.Width; $r.H=$img.Height; $img.Dispose()
        if ($r.W -lt 200 -or $r.H -lt 150) {
            $r.Msg="The image is only $($r.W)x$($r.H). The menu is 760x480, so this would be stretched past recognition."
            return $r
        }
        $r.Ok=$true
    } catch {
        # Deliberately NOT surfacing the GDI+ text: it reports "Out of memory" for
        # any file it cannot decode, which sends people hunting a memory problem
        # they do not have. Say what is actually wrong instead.
        $r.Msg = "Windows cannot read that file as an image. It may be corrupt, still downloading, " +
                 "or a format GDI+ does not support (WebP and HEIC are the usual culprits). " +
                 "PNG, JPG and BMP always work."
    }
    return $r
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w=$bmp.Width;$h=$bmp.Height
    $rect=New-Object System.Drawing.Rectangle(0,0,$w,$h)
    $data=$bmp.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride=$data.Stride; $buf=New-Object byte[] ($stride*$h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0,$buf,0,$buf.Length); $bmp.UnlockBits($data)
    $ms=New-Object System.IO.MemoryStream; $bw=New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40);$bw.Write([int]$w);$bw.Write([int]($h*2));$bw.Write([int16]1);$bw.Write([int16]32)
    $bw.Write([int]0);$bw.Write([int]0);$bw.Write([int]0);$bw.Write([int]0);$bw.Write([int]0);$bw.Write([int]0)
    for($y=$h-1;$y -ge 0;$y--){ $bw.Write($buf,$y*$stride,$w*4) }
    $maskRow=[int]([math]::Floor(($w+31)/32))*4; $bw.Write((New-Object byte[] ($maskRow*$h)),0,($maskRow*$h))
    $bw.Flush(); return $ms.ToArray()
}

function Convert-ToIco([string]$imgPath, [string]$outIco) {
    $src=[System.Drawing.Image]::FromFile($imgPath)
    # square master (center-crop)
    $side=[math]::Min($src.Width,$src.Height)
    $master=New-Object System.Drawing.Bitmap($side,$side,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[System.Drawing.Graphics]::FromImage($master); $g.InterpolationMode='HighQualityBicubic'
    $g.DrawImage($src,(New-Object System.Drawing.Rectangle(0,0,$side,$side)),
        (New-Object System.Drawing.Rectangle([int](($src.Width-$side)/2),[int](($src.Height-$side)/2),$side,$side)),[System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $src.Dispose()
    $sizes=@(16,24,32,48,64,128,256); $entries=@()
    foreach($s in $sizes){
        $b=New-Object System.Drawing.Bitmap($s,$s,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g2=[System.Drawing.Graphics]::FromImage($b); $g2.InterpolationMode='HighQualityBicubic'; $g2.PixelOffsetMode='HighQuality'
        $g2.DrawImage($master,0,0,$s,$s); $g2.Dispose()
        if ($s -ge 256) {
            # Vista onwards, the 256px frame of an .ico is stored as a PNG file
            # rather than a raw DIB. A 32-bit DIB at that size is a quarter of a
            # megabyte on its own and some shells render it poorly. The directory
            # entry already writes 0 for a 256px dimension, which is the same
            # convention, so only the payload changes.
            $msPng = New-Object System.IO.MemoryStream
            $b.Save($msPng,[System.Drawing.Imaging.ImageFormat]::Png)
            $entries += @{ w=$s; data=$msPng.ToArray() }
            $msPng.Dispose()
        } else {
            $entries += @{ w=$s; data=(Get-DibBytes $b) }
        }
        $b.Dispose()
    }
    $master.Dispose()
    $fs=New-Object System.IO.MemoryStream; $w=New-Object System.IO.BinaryWriter($fs)
    $w.Write([int16]0);$w.Write([int16]1);$w.Write([int16]$entries.Count); $off=6+16*$entries.Count
    foreach($e in $entries){ $wb=if($e.w -ge 256){0}else{$e.w}
        $w.Write([byte]$wb);$w.Write([byte]$wb);$w.Write([byte]0);$w.Write([byte]0)
        $w.Write([int16]1);$w.Write([int16]32);$w.Write([int]$e.data.Length);$w.Write([int]$off); $off+=$e.data.Length }
    foreach($e in $entries){ $w.Write($e.data,0,$e.data.Length) }
    $w.Flush(); Clear-ReadOnly $outIco; [System.IO.File]::WriteAllBytes($outIco,$fs.ToArray())
}

# $unit matters: Font sizes in POINTS by default, so on a 300 dpi bitmap a "size"
# meant as pixels comes out ~4x too big. Callers working in pixels must say so.
function New-TitleFont([single]$size,[System.Drawing.GraphicsUnit]$unit=[System.Drawing.GraphicsUnit]::Point) {
    try { return New-Object System.Drawing.Font("Bahnschrift SemiBold",$size,[System.Drawing.FontStyle]::Bold,$unit) }
    catch { return New-Object System.Drawing.Font("Segoe UI",$size,[System.Drawing.FontStyle]::Bold,$unit) }
}

# $panelSide = 'Right' (default) or 'Left' - which edge the button column sits on.
# Pick the side OPPOSITE the focal point of the artwork, or the buttons cover it.
function New-Background([string]$imgPath,[string]$title,[string]$outPng,[string]$panelSide='Right',[bool]$divider=$false,[bool]$showTitle=$false) {
    $W=760;$H=480;$PW=290
    $left = ($panelSide -ieq 'Left')
    $px   = if($left){0}else{$W-$PW}      # panel x
    $dx   = if($left){$PW}else{$W-$PW}    # divider x

    $img=[System.Drawing.Image]::FromFile($imgPath)
    $bmp=New-Object System.Drawing.Bitmap($W,$H)
    $g=[System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode='HighQualityBicubic';$g.SmoothingMode='AntiAlias';$g.TextRenderingHint='ClearTypeGridFit'
    $scale=[math]::Max($W/$img.Width,$H/$img.Height); $sw=[int]($img.Width*$scale);$sh=[int]($img.Height*$scale)
    $g.DrawImage($img,[int](($W-$sw)/2),[int](($H-$sh)/2),$sw,$sh); $img.Dispose()
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70,0,0,0))),0,0,$W,$H)

    # darkest under the buttons, fading toward the divider
    $rect=New-Object System.Drawing.Rectangle($px,0,$PW,$H)
    $cNear=[System.Drawing.Color]::FromArgb(225,2,5,7); $cFar=[System.Drawing.Color]::FromArgb(120,3,8,10)
    $c1 = if($left){$cNear}else{$cFar}; $c2 = if($left){$cFar}else{$cNear}
    $grad=New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,$c1,$c2,0.0)
    $g.FillRectangle($grad,$rect)
    # The divider reads as a hard line drawn across the artwork; off by default.
    if ($divider) { $g.DrawLine((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200,0,190,200),2)),$dx,0,$dx,$H) }

    # Title on the artwork is OFF by default: cover art usually carries the game's
    # own logo already, and a second title drawn over it just fights the artwork.
    if ($showTitle -and -not [string]::IsNullOrWhiteSpace($title)) {
        # title goes on the artwork side, shrunk to fit
        $tx = if($left){$PW+40}else{27}
        $maxW = $W-$PW-54
        $size=30.0; $f=New-TitleFont $size
        while ($size -gt 12 -and $g.MeasureString($title,$f).Width -gt $maxW) {
            $f.Dispose(); $size -= 1.5; $f=New-TitleFont $size
        }
        $g.DrawString($title,$f,(New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180,0,0,0))),($tx+2),29)
        $g.DrawString($title,$f,(New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235,235,245,248))),$tx,27)
        $f.Dispose()
    }
    $g.Dispose(); Save-PngAtomic $bmp $outPng; $bmp.Dispose()
}

# Control characters, gone. Everything a build writes goes into a line-based or a
# quoted format - autorun.inf is one directive per line, the menu's JScript puts
# names inside string literals - so a stray CR or LF does not corrupt the text, it
# ends the line and starts a new one.
#
# In autorun.inf that means a label carrying a newline writes further directives:
#
#     label=My Game
#     open=Extras\payload.exe        <- came from inside the "label"
#
# Windows 7 and later will not silently run an open= from optical media; it offers
# it in the AutoPlay prompt. But the wording of that prompt and what it points at
# would both be chosen by whoever wrote the project file, on a disc the person
# burning it believed was theirs. Project files do get passed around - this repo's
# own README suggests attaching one to a bug report.
#
# In the menu's JScript a newline is an unterminated string literal, so the whole
# HTA fails to parse and the disc opens to nothing.
#
# This is not a restriction on what may be typed. The label box is single line and
# the UI cannot produce these. It is a filter on what a FILE may carry: assigning
# to a single-line TextBox does not strip them, which is exactly the path a loaded
# project takes to reach the build.
function Remove-ControlChars([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return $s }
    return ($s -replace '[\x00-\x1F\x7F]','')
}

# What a label will actually look like once it has been through autorun.inf.
# AutoRun reads the file in the system ANSI codepage - it has no Unicode mode at
# all - so this is a property of Windows, not something the tool can fix. Returns
# the surviving text plus whether anything was lost, so callers can warn rather
# than silently ship a drive called "???????".
function Get-AutorunLabelPreview([string]$label) {
    $enc  = [System.Text.Encoding]::Default
    $seen = $enc.GetString($enc.GetBytes($label))
    return @{ Text=$seen; Lossy=($seen -ne $label); Codepage=$enc.WebName }
}

function New-AutorunInf([string]$label,[string]$iconName,[bool]$menu,[string]$out) {
    # Stripped here rather than only at the call site: this function is what turns
    # text into directives, so it is the last place that can be sure.
    $label    = Remove-ControlChars $label
    $iconName = Remove-ControlChars $iconName
    $lines = @('[autorun]')
    if ($menu) { $lines += 'shellexecute=AUTORUN\menu.hta' }
    $lines += "icon=$iconName"
    $lines += "label=$label"
    if ($menu) { $lines += 'action=Run ' + $label }
    $lines += @('','[Content]','MusicFiles=false','PictureFiles=false','VideoFiles=false')
    # ANSI, not ASCII. ASCII turned every accented character into a literal "?", so
    # "Uber Alles" reached Explorer as "?ber Alles". The ANSI codepage carries the
    # Latin-1 accents and the typographic dashes GOG titles are full of, and best-fit
    # maps most of what it cannot hold - a Polish z-acute arrives as a plain z
    # rather than as a question mark.
    # Not UTF8: PowerShell 5.1 writes a BOM, which AutoRun does not understand.
    Clear-ReadOnly $out; Set-Content -LiteralPath $out -Value ($lines -join "`r`n") -Encoding Default
}

function New-MenuHta([hashtable]$cfg,[string]$out) {
    # cfg: GameName (the disc's label), Games (array), Buttons (ordered array),
    #      MusicFile, ManualFile, PanelSide, IconName, WindowBorder, ButtonStyle
    #
    # Each entry of Games is @{ Name; MatchName; Setup; AddOns=@(@{Name;Setup}) },
    # built by Get-MenuGames. Setup paths are relative to the disc root.
    #
    # The panel used to be baked here as fixed HTML, one button per ticked option.
    # It cannot be any more: the button list now depends on which screen the menu
    # is showing, and that is only known while it runs. So the panel is rendered
    # from data by the menu itself, and what this function emits is the data.
    $panelLeft = if($cfg.PanelSide -ieq 'Left'){20}else{490}

    $menuGames = @($cfg.Games)
    # A caller that still speaks the old single-game contract keeps working, so
    # Preview and anything else calling this directly did not all have to change
    # in the same commit.
    if ($menuGames.Count -eq 0 -and $cfg.SetupExe) {
        $mn = if ($cfg.MatchName) { $cfg.MatchName } else { $cfg.GameName }
        $menuGames = @(@{ Name=$cfg.GameName; MatchName=$mn; Setup=$cfg.SetupExe; AddOns=@() })
    }

    $gamesJs = '[' + (($menuGames | ForEach-Object {
        $addJs = '[' + (($_.AddOns | ForEach-Object {
            '{n:"' + (ConvertTo-JsString $_.Name) + '",s:"' + (ConvertTo-JsString $_.Setup) + '"}'
        }) -join ',') + ']'
        $mn = if ($_.MatchName) { $_.MatchName } else { $_.Name }
        '{n:"' + (ConvertTo-JsString $_.Name) + '",m:"' + (ConvertTo-JsString $mn) +
        '",s:"' + (ConvertTo-JsString $_.Setup) +
        '",man:"' + (ConvertTo-JsString ([string]$_.Manual)) +
        '",ext:"' + (ConvertTo-JsString ([string]$_.Extras)) + '",a:' + $addJs + '}'
    }) -join ',') + ']'

    $btnsJs = '[' + ((@($cfg.Buttons) | ForEach-Object { '"' + (ConvertTo-JsString $_) + '"' }) -join ',') + ']'
    # <bgsound> no longer plays MP3 in current MSHTML - it silently does nothing.
    # The menu renders in quirks mode (no doctype), so switching to a standards
    # document mode for <audio> would break the button box model. Use the Windows
    # Media Player control instead, which works in this document mode.
    $musicJs = ''
    if ($cfg.MusicFile) { $musicJs = ConvertTo-JsString $cfg.MusicFile }
    $tpl = @'
<html>
<head>
<hta:application id="app" applicationname="%%APPNAME%%" border="none" caption="no"
  showintaskbar="yes" singleinstance="yes" sysmenu="no" scroll="no" selection="no"
  contextmenu="no" innerborder="no" maximizebutton="no" minimizebutton="no"
  icon="%%ICONFILE%%" />
<title>%%TITLE%%</title>
<style>
  html,body{margin:0;padding:0;width:760px;height:480px;overflow:hidden;background:#04080a;font-family:'Bahnschrift','Segoe UI',Arial,sans-serif;}
  #stage{position:absolute;left:0;top:0;width:760px;height:480px;background:#04080a url('bg.png') no-repeat 0 0;%%STAGEBORDER%%}
  .panel{position:absolute;left:%%PANELLEFT%%px;top:20px;width:250px;}
  /* Game names are user data and some are long. nowrap+hidden keeps one that got
     past the length clip from growing the 46px button and throwing the panel's
     vertical centering out. */
  .btn{display:block;width:250px;height:46px;margin:0 0 12px 0;line-height:46px;color:#e6ebef;text-decoration:none;
    font-size:15px;font-weight:600;letter-spacing:2px;text-transform:uppercase;cursor:pointer;background:#0a1519;
    white-space:nowrap;overflow:hidden;
    %%BTNBORDER%%padding-left:16px;}
  .btn:hover{background:#12242b;border-color:#00bec8;color:#fff;}
  .btn.play{border-left-color:#35c46a;} .btn.play:hover{border-color:#66e090;}
  .btn.install{border-left-color:#ff781e;} .btn.install:hover{border-color:#ff9a4d;}
  .btn.exit{border-left-color:#a03434;} .btn.exit:hover{border-color:#c86464;}
  #x{position:absolute;right:8px;top:8px;width:28px;height:26px;line-height:26px;text-align:center;color:#e6ebef;
    font-family:'Segoe UI',Arial;font-weight:bold;background:#0a1519;border:1px solid #7a2c2c;cursor:pointer;}
  #x:hover{color:#fff;border-color:#c86464;}
  #mute{position:absolute;right:44px;top:8px;width:28px;height:26px;line-height:26px;text-align:center;color:#e6ebef;
    font-family:'Segoe UI',Arial;font-size:15px;background:#0a1519;border:1px solid #16545a;cursor:pointer;display:none;}
  #mute:hover{color:#fff;border-color:#00bec8;}
  /* The caption above the buttons: which game this screen is for, and which disc
     of the set you are holding. Same nowrap+ellipsis rule as the buttons - game
     names are user data and a long one must not push the panel around. */
  #cap{width:250px;margin:0 0 10px 0;font-family:'Segoe UI',Arial;}
  #cap .capn{display:block;font-size:17px;font-weight:600;letter-spacing:1px;text-transform:uppercase;
    color:#dfe9ee;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
  #cap .capd{display:block;font-size:12px;letter-spacing:2px;color:#8fa5ad;margin-top:3px;}
  #status{position:absolute;left:%%PANELLEFT%%px;bottom:18px;width:250px;text-align:center;display:none;
    color:#9fb3ba;font-size:12px;line-height:17px;}
</style>
<script language="JScript">
  var fso=new ActiveXObject("Scripting.FileSystemObject");
  var shell=new ActiveXObject("Shell.Application");
  var root="";
  var GAMES=%%GAMES%%;       // [{n:name, m:registry match, s:setup, man:manual, ext:extras, a:[{n,s}]}]
  var BTNS=%%BTNS%%;         // which of Play/Install/Manual/Extras/Exit the disc was built with
  var MANUAL="%%MANUAL%%"; var MUSIC="%%MUSIC%%";
  var PREVIEW=%%PREVIEW%%;   // true only for the app's Preview - see refreshButtons
  var DISCNUM=%%DISCNUM%%, DISCOF=%%DISCOF%%;   // 1 and 1 unless this disc is part of a set
  // Which screen is showing: -1 is the game chooser, otherwise an index into GAMES.
  // A disc holding one game has nothing to choose, so it opens on that game and
  // never shows a Back button.
  var cur = (GAMES.length==1) ? 0 : -1;
  // Non-null while the "which one?" screen is up, for a game whose single installer
  // put more than one launchable thing on the machine. It sits on top of whatever
  // screen was showing rather than replacing it, so Back returns there.
  var tasks = null;
  var player=null, musicOn=false;
  // Where this .hta actually lives. document.URL is always an absolute file: URL,
  // unlike app.commandLine - AutoRun can launch the menu with a path relative to the
  // disc root, which has no drive letter and used to leave root pointing at nonsense.
  function htaPath(){
    var u="";
    try{ u=String(document.URL); }catch(e){ u=""; }
    if(u.length){
      var p=u.replace(/^file:/i,"").replace(/\//g,"\\");
      try{ p=decodeURIComponent(p); }catch(e){}
      // HTAs report file://D:\AUTORUN\menu.hta (two slashes, backslashes already),
      // not the textbook file:///D:/... - so decide by what follows the slashes.
      var lead=p.match(/^\\+/);
      if(lead){
        var n=lead[0].length, rest=p.substring(n);
        if(/^[A-Za-z]:/.test(rest)) p=rest;          // drive letter: drop them all
        else if(n>=3) p="\\\\"+rest;                 // UNC: normalise to two
      }
      return p;
    }
    try{ var cl=app.commandLine; var m=cl.match(/([A-Za-z]:\\[^"]*\.hta)/i);
      if(!m) m=cl.match(/(\\\\[^"]*\.hta)/i);
      if(m) return m[1]; }catch(e){}
    return "";
  }
  function init(){
    try{ window.resizeTo(760,480);
      var dw=760-document.body.clientWidth, dh=480-document.body.clientHeight;
      if(dw<0)dw=0; if(dh<0)dh=0; var ow=760+dw, oh=480+dh;
      window.resizeTo(ow,oh); window.moveTo(Math.max(0,(screen.availWidth-ow)/2),Math.max(0,(screen.availHeight-oh)/2));
    }catch(e){}
    root=fso.GetParentFolderName(fso.GetParentFolderName(htaPath()));
    document.onmousedown=startDrag; document.onmousemove=doDrag; document.onmouseup=endDrag;
    show();   // renders the panel, which calls setupHover and refreshButtons itself
    initMusic();
  }
  function initMusic(){
    var mb=document.getElementById("mute");
    if(!MUSIC||!root){ return; }
    var p=fso.BuildPath(root,"AUTORUN\\"+MUSIC);
    if(!fso.FileExists(p)){ return; }
    try{
      player=new ActiveXObject("WMPlayer.OCX");
      player.settings.setMode("loop",true);
      player.settings.volume=55;
      player.URL=p;
      player.controls.play();
      musicOn=true;
      if(mb){ mb.style.display="block"; mb.onclick=toggleMusic; }
    }catch(e){ player=null; }   // Media Player absent - menu still works, just silent
  }
  function toggleMusic(){
    if(!player) return;
    try{
      if(musicOn){ player.controls.pause(); musicOn=false; } else { player.controls.play(); musicOn=true; }
      var mb=document.getElementById("mute");
      if(mb){ mb.style.color = musicOn?"#e6ebef":"#5d6c72"; mb.style.borderColor = musicOn?"#16545a":"#39464a"; }
    }catch(e){}
  }
  var _drag=false,_ox=0,_oy=0;
  function startDrag(){ var e=window.event; var t=e.srcElement; if((t.className||"").indexOf("btn")>=0||t.id=="x")return;
    _drag=true; _ox=e.screenX-window.screenLeft; _oy=e.screenY-window.screenTop; }
  function doDrag(){ if(!_drag)return; var e=window.event; window.moveTo(e.screenX-_ox,e.screenY-_oy); }
  function endDrag(){ _drag=false; }
  function setupHover(){ var a=document.getElementsByTagName("A");
    for(var i=0;i<a.length;i++){ if((a[i].className||"").indexOf("btn")>=0){
      try{ a[i].accent = a[i].currentStyle.borderLeftColor; }catch(e){ a[i].accent = "#00bec8"; }
      a[i].onmouseover=function(){ if(this.btnOff) return; this.style.backgroundColor="#14282f"; this.style.color="#fff";
        this.style.borderTopColor="#00bec8"; this.style.borderRightColor="#00bec8"; this.style.borderBottomColor="#00bec8"; };
      a[i].onmouseout=function(){ if(this.btnOff) return; this.style.backgroundColor="#0a1519"; this.style.color="#e6ebef";
        this.style.borderTopColor="#16545a"; this.style.borderRightColor="#16545a"; this.style.borderBottomColor="#16545a"; }; } }
    var xb=document.getElementById("x"); if(xb){ xb.onmouseover=function(){ this.style.color="#fff"; this.style.borderColor="#c86464"; };
      xb.onmouseout=function(){ this.style.color="#e6ebef"; this.style.borderColor="#7a2c2c"; }; } }
  function openItem(p,isFolder){ try{
    if(!root){ alert("Could not work out the disc folder.\nRun the menu from the disc root."); return; }
    var f=fso.BuildPath(root,p);
    if(isFolder){ if(fso.FolderExists(f)) shell.ShellExecute(f); else alert("Not found:\n"+f); }
    else { if(fso.FileExists(f)) shell.ShellExecute(f); else alert("Not found:\n"+f); } }catch(e){alert(e.message);} }
  // Grey a button out rather than let it fail. Inline styles, not a CSS class:
  // this document runs in quirks mode, where compound selectors like .btn.off
  // are unreliable.
  function setEnabled(id,ok,why){
    var e=document.getElementById(id); if(!e) return;
    e.btnOff = !ok;
    e.title = ok ? "" : why;
    e.style.backgroundColor = "#0a1519";
    if(ok){ e.style.color="#e6ebef"; e.style.cursor="pointer"; e.style.borderLeftColor=e.accent; }
    else  { e.style.color="#5d6c72"; e.style.cursor="default"; e.style.borderLeftColor="#39464a"; }
  }
  function off(id){ var e=document.getElementById(id); return (e && e.btnOff); }
  function has(b){ for(var i=0;i<BTNS.length;i++){ if(BTNS[i]==b) return true; } return false; }
  // A game's own manual and extras if it has them, otherwise the disc's. The
  // fallback is what keeps a one-game disc, and every disc built before games
  // could have their own, behaving exactly as it did.
  function manualOf(g){ if(g.man) return g.man; return MANUAL ? ("Extras\\"+MANUAL) : ""; }
  function extrasOf(g){ if(g.ext) return g.ext; return "Extras"; }
  function esc(s){ return String(s).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;"); }
  // 250px of 15px uppercase with 2px letter-spacing runs out at about twenty
  // characters. Clipping here rather than letting CSS do it keeps the ellipsis
  // visible instead of shearing a letter in half; the full name is on the tooltip.
  function clip(s){ s=String(s); return (s.length>20) ? s.substring(0,19)+"..." : s; }
  function btnHtml(id,cls,label,fn,tip){
    return '<a id="'+id+'" class="btn '+cls+'" onclick="'+fn+'" title="'+esc(tip||"")+'">'+
           esc(clip(label)).replace(/ /g,"&nbsp;")+'</a>';
  }
  // The panel is centred on however many buttons this screen happens to have, so
  // a three-game chooser and a six-button game screen both sit in the middle.
  // "Disc 2 of 3", or nothing at all on a disc that is not part of a set.
  function discLine(){ return DISCOF>1 ? '<span class="capd">Disc '+DISCNUM+' of '+DISCOF+'</span>' : ''; }
  // Game names arrive already escaped for a JS string literal, which turns "<"
  // into < - so they cannot open a tag when they land in innerHTML here.
  function capFor(name){
    var d=discLine();
    if(!name && !d) return "";
    return (name ? '<span class="capn">'+name+'</span>' : '') + d;
  }
  function setPanel(h,cap){
    var p=document.getElementById("pan");
    p.innerHTML=(cap ? '<div id="cap">'+cap+'</div>' : '')+h;
    var c=document.getElementById("cap");
    // Measured rather than assumed: the caption is one line or two depending on
    // whether this disc belongs to a set.
    var capH=c ? c.offsetHeight+10 : 0;
    var a=p.getElementsByTagName("A"), n=a.length;
    // 46px buttons with 12px between them fill a 480px window at eight, and a
    // game with four add-ons already needs eight. Rather than let the ninth draw
    // off the bottom edge where nothing can reach it, the buttons shrink to fit.
    var bh=46, gap=12, avail=440-capH;
    if(n*bh+(n-1)*gap > avail){
      gap = 8;
      bh = Math.floor((avail-(n-1)*gap)/n);
      if(bh > 46) bh = 46;
      if(bh < 22) bh = 22;
      for(var i=0;i<n;i++){
        a[i].style.height=bh+"px"; a[i].style.lineHeight=bh+"px"; a[i].style.marginBottom=gap+"px";
      }
    }
    var block=capH+n*bh+(n-1)*gap, t=Math.floor((480-block)/2);
    if(t<10) t=10;
    p.style.top=t+"px";
    setupHover();
    refreshButtons();
  }
  function renderChooser(){
    var h="";
    for(var i=0;i<GAMES.length;i++){ h+=btnHtml("btn_game_"+i,"play",GAMES[i].n,"pick("+i+")",GAMES[i].n); }
    if(has("Exit")) h+=btnHtml("btn_Exit","exit","Exit","doExit()","");
    // The chooser lists the games itself, so it needs only the disc line.
    setPanel(h,capFor(""));
  }
  function renderGame(){
    var g=GAMES[cur], h="";
    if(has("Play"))    h+=btnHtml("btn_Play","play","Play","doPlay()","");
    if(has("Install")){
      h+=btnHtml("btn_Install","install","Install","doInstall()","Install "+g.n);
      // Add-ons sit directly under Install in the same colour, because that is
      // what they are - another installer, for this game.
      for(var i=0;i<g.a.length;i++){
        h+=btnHtml("btn_addon_"+i,"install",g.a[i].n,"doAddOn("+i+")","Install "+g.a[i].n);
      }
    }
    if(has("Manual"))  h+=btnHtml("btn_Manual","","Game Manual","doManual()","");
    if(has("Extras"))  h+=btnHtml("btn_Extras","","Extras","doExtras()","");
    if(GAMES.length>1) h+=btnHtml("btn_Back","","Back","goBack()","Back to the game list");
    if(has("Exit"))    h+=btnHtml("btn_Exit","exit","Exit","doExit()","");
    setPanel(h,capFor(g.n));
  }
  function show(){ if(tasks) renderTasks(); else if(cur<0) renderChooser(); else renderGame(); }
  function pick(i){ cur=i; show(); }
  function goBack(){ tasks=null; cur=-1; show(); }
  function refreshButtons(){
    // The launch chooser's buttons are built already enabled and none of the ids
    // below exist while it is up.
    if(!root || tasks) return;
    // In preview the game files are not beside the menu, so the existence checks
    // would grey out buttons that will be fine on the real disc. Show them live.
    if(cur<0){
      for(var i=0;i<GAMES.length;i++){
        setEnabled("btn_game_"+i, (PREVIEW || fso.FileExists(fso.BuildPath(root,GAMES[i].s))),
                   "This game's installer is not on the disc.");
      }
      return;
    }
    var g=GAMES[cur];
    setEnabled("btn_Install", (PREVIEW || (g.s!="" && fso.FileExists(fso.BuildPath(root,g.s)))),
               "The installer is not next to this menu.");
    // Registry-based, so it tells the truth in preview as well. Worked out once
    // and used by both the add-ons and Play below.
    var parentOn = (findGame(g.m)!=null);
    // An add-on - a patch, a piece of DLC, a mod - is applied ON TOP of the game
    // it belongs to and needs that game installed first. A GOG patch in
    // particular refuses outright, and the error comes from GOG's installer
    // several clicks later rather than from the menu, which is a poor place to
    // discover the rule. Being on the disc is necessary but not sufficient.
    for(var j=0;j<g.a.length;j++){
      var here = fso.FileExists(fso.BuildPath(root,g.a[j].s));
      setEnabled("btn_addon_"+j, (PREVIEW || (here && parentOn)),
                 here ? (g.n+" is not installed yet - use Install first.")
                      : "This add-on's installer is not on the disc.");
    }
    var gman = manualOf(g), gext = extrasOf(g);
    setEnabled("btn_Manual", (PREVIEW || (gman!="" && fso.FileExists(fso.BuildPath(root,gman)))),
               "There is no manual for " + g.n + " on this disc.");
    setEnabled("btn_Extras", (PREVIEW || fso.FolderExists(fso.BuildPath(root,gext))),
               "There is nothing extra for " + g.n + " on this disc.");
    setEnabled("btn_Play", parentOn,
               g.n+" is not installed yet - use Install first.");
    if(PREVIEW){
      var ids=["btn_Install","btn_Manual","btn_Extras"];
      for(var k=0;k<g.a.length;k++){ ids[ids.length]="btn_addon_"+k; }
      for(var m=0;m<ids.length;m++){ var e=document.getElementById(ids[m]);
        if(e) e.title="Preview only - this works on the built disc"; }
    }
  }
  function previewStop(what){
    alert("This is a preview of the menu.\n\n"+what+" works on the built disc, but the game files are not in the preview folder.");
  }
  function setStatus(msg){
    var s=document.getElementById("status");
    if(s){ s.innerHTML=msg; s.style.display = msg ? "block" : "none"; }
  }
  // ShellExecute returns the moment it hands off, long before the installer draws
  // anything - and a 1 GB setup read off optical media can take minutes. Show that
  // something is happening, and block a second click while it loads.
  function launchWithStatus(id,msg,path,isFolder){
    setStatus(msg);
    setEnabled(id,false,"Starting...");
    window.setTimeout(function(){          // let the window repaint before we block
      openItem(path,isFolder);
      window.setTimeout(function(){ setStatus(""); refreshButtons(); }, 45000);
    }, 60);
  }
  function doInstall(){ if(off("btn_Install")) return; if(PREVIEW){ previewStop("Install"); return; }
    launchWithStatus("btn_Install","Starting the installer...<br>Reading from disc can take a minute.",GAMES[cur].s,false); }
  function doAddOn(i){ var id="btn_addon_"+i; if(off(id)) return;
    var a=GAMES[cur].a[i];
    if(PREVIEW){ previewStop(a.n); return; }
    launchWithStatus(id,"Starting "+esc(a.n)+"...<br>Reading from disc can take a minute.",a.s,false); }
  function doManual(){ if(off("btn_Manual")) return; if(PREVIEW){ previewStop("Game Manual"); return; }
    launchWithStatus("btn_Manual","Opening the manual...",manualOf(GAMES[cur]),false); }
  function doExtras(){ if(off("btn_Extras")) return; if(PREVIEW){ previewStop("Extras"); return; }
    openItem(extrasOf(GAMES[cur]),true); }
  function doExit(){ window.close(); }
  function regGet(reg,h,sub,val){ try{ var i=reg.Methods_.Item("GetStringValue").InParameters.SpawnInstance_();
    i.hDefKey=h;i.sSubKeyName=sub;i.sValueName=val; var o=reg.ExecMethod_("GetStringValue",i); if(o.ReturnValue==0)return o.sValue; }catch(e){} return null; }
  // GOG's registry gameName drops or changes punctuation ("The Witcher Enhanced Edition
  // Director's Cut" vs "The Witcher - Enhanced Edition"), so compare on letters+digits only.
  function norm(s){ return String(s).toLowerCase().replace(/[^a-z0-9]+/g,""); }
  function nameHit(nm,match){ var a=norm(nm), b=norm(match);
    if(!a||!b) return false;
    if(a.indexOf(b)>=0) return true;
    if(a.length>=6 && b.indexOf(a)>=0) return true;   // registry name shorter than ours
    return false; }
  // Takes the name to match rather than reading a global: on a multi-game disc
  // each game asks this about itself, and Play has to light up for the ones that
  // are installed while staying grey for the ones that are not.
  // Returns {exe,dir} for an installed copy, or null. The folder matters as much as
  // the executable: it is where GOG's own goggame-<id>.info sits, and that file is
  // the only place a bundle admits to containing more than one game.
  function findGame(match){ try{ var HKLM=0x80000002; var svc=GetObject("winmgmts:\\\\.\\root\\default"); var reg=svc.Get("StdRegProv");
    var bases=["SOFTWARE\\WOW6432Node\\GOG.com\\Games","SOFTWARE\\GOG.com\\Games"];
    for(var b=0;b<bases.length;b++){ var mi=reg.Methods_.Item("EnumKey").InParameters.SpawnInstance_(); mi.hDefKey=HKLM; mi.sSubKeyName=bases[b];
      var mo=reg.ExecMethod_("EnumKey",mi); if(mo.ReturnValue!=0||mo.sNames==null)continue; var ids=new VBArray(mo.sNames).toArray();
      for(var k=0;k<ids.length;k++){ var sub=bases[b]+"\\"+ids[k]; var nm=regGet(reg,HKLM,sub,"gameName");
        if(nm && nameHit(nm,match)){ var p=regGet(reg,HKLM,sub,"path"); var exe=regGet(reg,HKLM,sub,"exe");
          if(exe && fso.FileExists(exe)) return {exe:exe, dir:(p||fso.GetParentFolderName(exe))};
          var ef=regGet(reg,HKLM,sub,"exeFile");
          if(p&&ef&&fso.FileExists(fso.BuildPath(p,ef))) return {exe:fso.BuildPath(p,ef), dir:p}; } } } }catch(e){} return null; }

  // One string out of a JSON object, unescaped. Picked apart with a regular
  // expression rather than JSON.parse, which the quirks-mode engine an HTA gets
  // does not have.
  function jsonStr(block,key){
    var m=block.match(new RegExp('"'+key+'"\\s*:\\s*"((?:[^"\\\\]|\\\\.)*)"'));
    if(!m) return "";
    return m[1].replace(/\\\\/g,"\\").replace(/\\"/g,'"').replace(/\\\//g,"/");
  }
  // Real .info files contain both "System\\witcher.exe" and "System//witcher.exe".
  function relPath(s){ return String(s).replace(/\//g,"\\").replace(/\\+/g,"\\"); }

  // GOG writes a goggame-<id>.info beside every installed game, listing its play
  // tasks. This exists because a bundle that ships two games in ONE installer -
  // Star Wars: Empire at War Gold Pack, which is what turned this up - registers a
  // single gameName and a single exe. The registry can therefore only ever point at
  // the base game, and the expansion sitting in the next folder is unreachable. The
  // .info file lists both.
  //
  // Every {...} in that file is a play task: the rest is scalars and arrays of
  // strings, so brace pairs containing no braces find exactly the tasks and nothing
  // else. Only the ones that start the game count - "document" is the manual and the
  // readme, which this menu already has its own buttons for, and a hidden task is the
  // raw exe GOG keeps behind its own launcher.
  function playTasks(dir){
    var out=[];
    try{
      if(!dir || !fso.FolderExists(dir)) return out;
      var info=null, en=new Enumerator(fso.GetFolder(dir).Files);
      for(;!en.atEnd();en.moveNext()){ if(/^goggame-\d+\.info$/i.test(en.item().Name)){ info=en.item().Path; break; } }
      if(!info) return out;
      var st=fso.OpenTextFile(info,1), txt=st.ReadAll(); st.Close();
      var re=/\{[^{}]*\}/g, m;
      while((m=re.exec(txt))!=null){
        var b=m[0];
        if(!/"type"\s*:\s*"FileTask"/.test(b)) continue;
        if(/"isHidden"\s*:\s*true/.test(b)) continue;
        var cat=jsonStr(b,"category");
        if(cat!="game" && cat!="launcher") continue;
        var rel=jsonStr(b,"path"); if(!rel) continue;
        var full=fso.BuildPath(dir,relPath(rel));
        if(!fso.FileExists(full)) continue;
        var wd=jsonStr(b,"workingDir");
        out[out.length]={ n:(jsonStr(b,"name")||fso.GetFileName(full)), p:full,
                          a:jsonStr(b,"arguments"),
                          w:(wd ? fso.BuildPath(dir,relPath(wd)) : fso.GetParentFolderName(full)) };
      }
    }catch(ex){}
    return out;
  }
  function launchExe(p,args,wd){
    try{ shell.ShellExecute(p,args||"",wd||fso.GetParentFolderName(p),"open",1); window.close(); }
    catch(ex){ alert(ex.message); }
  }
  function renderTasks(){
    var h="";
    for(var i=0;i<tasks.length;i++){ h+=btnHtml("btn_task_"+i,"play",tasks[i].n,"playTask("+i+")",tasks[i].n); }
    h+=btnHtml("btn_TaskBack","","Back","tasksBack()","Back to "+GAMES[cur].n);
    if(has("Exit")) h+=btnHtml("btn_Exit","exit","Exit","doExit()","");
    setPanel(h,capFor(GAMES[cur].n));
  }
  function playTask(i){ var t=tasks[i]; launchExe(t.p,t.a,t.w); }
  function tasksBack(){ tasks=null; show(); }
  function doPlay(){ if(off("btn_Play")) return;
    var g=GAMES[cur];
    var hit=findGame(g.m);
    if(!hit){ alert(g.n+" isn't installed yet.\n\nUse INSTALL first, then PLAY."); return; }
    // Two or more launch targets means a bundle, so ask which - the same way the
    // disc asks which game when it holds more than one. One target, or an install
    // with no .info file at all, behaves exactly as it always has.
    var t=playTasks(hit.dir);
    if(t.length>1){ tasks=t; show(); return; }
    launchExe(hit.exe,"",fso.GetParentFolderName(hit.exe)); }
  // Re-check when the menu regains focus, so Play lights up after the installer finishes.
  window.onfocus = refreshButtons;
  document.onkeydown=function(){ if(window.event.keyCode==27) window.close(); };
</script>
</head>
<body onload="init()">
  <div id="stage">
    <div id="x" onclick="doExit()">X</div>
    <div id="mute" title="Music on / off">&#9835;</div>
    <div id="status"></div>
    <div class="panel" id="pan"></div>
  </div>
</body>
</html>
'@
    # Quirks-mode box model: width includes padding+border, so dropping the 1px
    # outline does not change the button footprint.
    $stageBorder = if ($cfg.WindowBorder) { 'border:1px solid #00a6b0;' } else { 'border:0;' }
    $btnBorder   = if ($cfg.ButtonStyle -ieq 'Minimal') { 'border:0;border-left:5px solid #00bec8;' }
                   else { 'border:1px solid #16545a;border-left:5px solid #00bec8;' }
    $previewFlag = if ($cfg.Preview) { 'true' } else { 'false' }
    # singleinstance="yes" keys off applicationname, so a shared name means a second
    # menu never opens - Windows just refocuses whatever is already running. A stale
    # Preview window would silently stand in for the disc's own menu.
    $appName = 'DiscMenu_' + (($cfg.GameName -replace '[^A-Za-z0-9]','') )
    if ([string]::IsNullOrWhiteSpace($appName)) { $appName = 'DiscMenu' }
    if ($cfg.Preview) { $appName += '_Preview' }
    # The taskbar icon is cached by path too, so it follows the disc's icon name.
    $iconFile = if ($cfg.IconName) { $cfg.IconName } else { 'disc.ico' }
    $html = $tpl.Replace('%%APPNAME%%',$appName).
                 Replace('%%ICONFILE%%',(ConvertTo-HtmlText $iconFile)).
                 Replace('%%PREVIEW%%',$previewFlag).
                 Replace('%%STAGEBORDER%%',$stageBorder).
                 Replace('%%BTNBORDER%%',$btnBorder).
                 Replace('%%TITLE%%',(ConvertTo-HtmlText $cfg.GameName)).
                 Replace('%%PANELLEFT%%',"$panelLeft").
                 Replace('%%DISCNUM%%',"$([int]$(if($cfg.DiscNum){$cfg.DiscNum}else{1}))").
                 Replace('%%DISCOF%%', "$([int]$(if($cfg.DiscOf){$cfg.DiscOf}else{1}))").
                 Replace('%%GAMES%%',$gamesJs).
                 Replace('%%BTNS%%',$btnsJs).
                 Replace('%%MANUAL%%',(ConvertTo-JsString $cfg.ManualFile)).
                 Replace('%%MUSIC%%',$musicJs)
    Clear-ReadOnly $out; Set-Content -LiteralPath $out -Value $html -Encoding ASCII
}

function New-Iso([string]$stageDir,[string]$isoPath,[string]$volLabel,[scriptblock]$progress=$null) {
    if (-not ([System.Management.Automation.PSTypeName]'ISOFile').Type) {
        $code=@'
public class ISOFile {
  // Progress is called every ReportEvery blocks with (blocksWritten, totalBlocks).
  //
  // It is not only there to draw a bar. The whole build runs on the interface
  // thread, and this loop is the longest thing on it by far - without something
  // pumping the message queue from inside it, Windows greys the window out and
  // labels it "Not Responding" for the length of the write. On a full-size game
  // that is minutes of looking exactly like a crash.
  //
  // IStream.Read wants somewhere to put the byte count. This used to take the
  // address of a local with an unsafe pointer, which meant compiling with
  // /unsafe. Four unmanaged bytes do the same job, and that matters twice over:
  //
  //   Smart App Control - enforced by default on a clean Windows 11 - blocks an
  //   assembly that combines unsafe pointers with a delegate invoked inside the
  //   copy loop. That is the shape of a shellcode loader, so the heuristic is
  //   fair; measured here, the pointer-plus-callback build was refused every
  //   time and this one is accepted every time. Without the change, adding
  //   progress reporting would have stopped DiscWright writing ISOs at all on
  //   any machine with SAC on.
  //
  //   And /unsafe needed Add-Type -CompilerParameters, which PowerShell 6
  //   removed. Dropping it removes the one known reason this cannot run on
  //   PowerShell 7 - untested there, but no longer ruled out by this.
  public static void Create(string Path, object Stream, int BlockSize, int TotalBlocks,
                            System.Action<int,int> Progress, int ReportEvery) {
    byte[] buf=new byte[BlockSize];
    System.IntPtr ptr=System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
    System.IO.FileStream o=System.IO.File.OpenWrite(Path);
    System.Runtime.InteropServices.ComTypes.IStream i=Stream as System.Runtime.InteropServices.ComTypes.IStream;
    try {
      int total=TotalBlocks, done=0;
      if(o!=null && i!=null){
        while(TotalBlocks-->0){
          i.Read(buf,BlockSize,ptr);
          int bytes=System.Runtime.InteropServices.Marshal.ReadInt32(ptr);
          o.Write(buf,0,bytes);
          done++;
          if(Progress!=null && ReportEvery>0 && done%ReportEvery==0) Progress(done,total);
        }
        o.Flush(); o.Close();
        if(Progress!=null) Progress(total,total);
      }
    } finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
  }
}
'@
        Add-Type -TypeDefinition $code
    }
    # A mounted ISO cannot be overwritten, and "used by another process" on its own
    # does not tell the user that the drive they opened in This PC is the cause.
    if (Test-Path $isoPath) {
        try { Remove-Item -LiteralPath $isoPath -Force -ErrorAction Stop }
        catch {
            throw ("The existing ISO cannot be replaced - something is still using it:" +
                   "`r`n`r`n$isoPath`r`n`r`n" +
                   "If you mounted it to check the disc, eject it first: right-click the drive in This PC and choose Eject. " +
                   "Then build again.`r`n`r`nWindows said: $($_.Exception.Message)")
        }
    }
    # Every one of these COM objects has to be released by hand. AddTree opens a
    # handle on each staged file and CreateResultImage holds them until the image
    # is released, so leaving it to the garbage collector means DiscWright is still
    # locking its own disc folder when the build returns. Rebuilding then failed
    # with "something is still using it", pointing the blame at Explorer or a
    # mounted ISO when the process holding the files was DiscWright itself.
    $fsi=$null; $rootItem=$null; $img=$null; $stream=$null
    try {
        $fsi=New-Object -ComObject IMAPI2FS.MsftFileSystemImage

        # Size the virtual medium to the actual payload. The old fixed 12.5M blocks
        # (~25 GB) silently capped builds well below the BD-R DL / XL sizes the UI
        # recommends.
        $payload = (Get-ChildItem -Recurse -File -Force $stageDir | Measure-Object Length -Sum).Sum
        $blocks  = [long][math]::Ceiling(($payload * 1.05) / 2048) + 65536
        if ($blocks -lt 12500000) { $blocks = 12500000 }
        if ($blocks -gt 2147483000) { $blocks = 2147483000 }
        $fsi.FreeMediaBlocks=[int]$blocks

        # UDF only. GOG part filenames run past 64 chars, which Joliet cannot hold,
        # and the .bin parts sit at the ISO9660 4 GiB ceiling.
        $fsi.FileSystemsToCreate=4
        try { $fsi.UDFRevision=0x250 } catch {}
        $vn = ($volLabel -replace '[^A-Za-z0-9_]','_'); if($vn.Length -gt 16){$vn=$vn.Substring(0,16)}
        $fsi.VolumeName=$vn
        # Bind Root once. Reading it creates a fresh wrapper every time, and a
        # wrapper nobody kept a reference to is a wrapper nobody can release.
        $rootItem=$fsi.Root
        $rootItem.AddTree($stageDir,$false)
        $img=$fsi.CreateResultImage()
        $stream=$img.ImageStream

        # Report about 200 times over the whole write, whatever its size. A fixed
        # block interval would fire a handful of times on a CD and tens of
        # thousands on a BD-R XL, and every call crosses back into PowerShell and
        # pumps the message queue, which is not free.
        $every = [int][math]::Max(256, [math]::Floor($img.TotalBlocks / 200))
        $cb = $null
        if ($progress) { $cb = [Action[int,int]]$progress }
        [ISOFile]::Create($isoPath,$stream,$img.BlockSize,$img.TotalBlocks,$cb,$every)
    }
    finally {
        foreach ($o in @($stream,$img,$rootItem,$fsi)) {
            if ($o) { try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o) } catch {} }
        }
        # Releasing drops our references; the handles close when the runtime runs
        # the finalisers. Waiting for that here is what makes an immediate rebuild
        # work instead of failing on a lock that clears a few seconds later.
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    }
}

# =================== PROJECT SAVE / REOPEN ===================

function Save-Project([hashtable]$s,[string]$outDir) {
    $games = @($s.Games)
    $o = [ordered]@{
        # Version is the schema of this file. AppVersion is the DiscWright that
        # wrote it - two different things, deliberately two different keys. This
        # merge is why: the schema went to 2 for the games list while the app was
        # independently at 0.2.0, and one field could not have said both.
        # Version 5 adds MediaKey - which disc the set was planned for. Absent in
        # anything older, which reads back as the automatic setting and so builds
        # the single disc those projects always described.
        Version      = 5
        AppVersion   = $APP_VERSION
        SavedUtc     = (Get-Date).ToUniversalTime().ToString('s')
        # Version 1 knew about exactly one game and stored it here. Both keys are
        # still written, pointing at the first game, so a project saved by this
        # build still opens in 0.1.x instead of failing with no explanation.
        SourceFolder = $(if ($games.Count) { $games[0].Folder }    else { $null })
        GameName     = $(if ($games.Count) { $games[0].GameName } else { $null })
        # Version 3 adds Kind and Parent. Parent is an index into this same array
        # rather than a name, because two GOG folders can report the same
        # ProductName and a name would then bind an add-on to the wrong game.
        # A version 2 file has neither key, and reads back as all-games-no-add-ons,
        # which is exactly what a version 2 disc was.
        # Setup is the full path to the chosen installer, not just its folder. An
        # add-on shares a folder with the game it belongs to - a patch sits beside
        # the setup it patches - so re-detecting from the folder alone would find
        # the game again and quietly turn the add-on into a second copy of it.
        # GameName is stored because it can be edited, and the edit has to survive.
        Games        = @($games | ForEach-Object {
                            [ordered]@{ Folder=$_.Folder; GameName=$_.GameName
                                        Setup=$(if($_.SetupExe){$_.SetupExe.FullName}else{$null})
                                        Kind=$(if($_.Kind -eq 'AddOn'){'AddOn'}else{'Game'})
                                        Parent=[int]$_.ParentIndex
                                        Manual=$_.ManualPath; Extras=$_.ExtrasPath } })
        Label        = $s.Label
        IconPath     = $s.IconPath
        IconIsIco    = [bool]$s.IconIsIco
        Menu         = [bool]$s.Menu
        BgPath       = $s.BgPath
        BgAsIs       = [bool]$s.BgAsIs
        PanelSide    = $s.PanelSide
        Divider      = [bool]$s.Divider
        ShowTitle    = [bool]$s.ShowTitle
        TitleText    = $s.TitleText
        WindowBorder = [bool]$s.WindowBorder
        ButtonStyle  = $s.ButtonStyle
        MusicFile    = $s.MusicFile
        Buttons      = @($s.Buttons)
        ManualPath   = $s.ManualPath
        ExtrasPath   = $s.ExtrasPath
        ExtraItems   = @($s.ExtraItems)
        MediaKey     = [string]$s.MediaKey
        ExtrasEveryDisc = [bool]$s.ExtrasEveryDisc
        OutDir       = $outDir
    }
    $o | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outDir $PROJECT_FILE) -Encoding UTF8
}

function Import-Project([string]$jsonPath) {
    try {
        $j = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
        # v1 stored one game in SourceFolder; v2 added a Games array; v3 gave each
        # entry a Kind and a Parent. Read any of them and always hand back the same
        # list of entries, so nothing downstream has to know which version it came
        # from. An older file simply yields entries that are all plain games.
        $entries = @()
        if ($j.PSObject.Properties.Name -contains 'Games' -and $j.Games) {
            foreach ($g in @($j.Games)) {
                if (-not $g -or -not $g.Folder) { continue }
                $kind = 'Game'; $parent = -1; $setup = $null; $nm = $null; $man = $null; $ext = $null
                if ($g.PSObject.Properties.Name -contains 'Kind' -and $g.Kind -eq 'AddOn') { $kind = 'AddOn' }
                if ($g.PSObject.Properties.Name -contains 'Parent') { $parent = [int]$g.Parent }
                if ($g.PSObject.Properties.Name -contains 'Setup')  { $setup = [string]$g.Setup }
                if ($g.PSObject.Properties.Name -contains 'GameName') { $nm = [string]$g.GameName }
                # Version 4. Absent in anything older, which reads back as an entry
                # with none of its own - and the menu then falls back to the
                # disc-wide manual and Extras, exactly as those discs behaved.
                if ($g.PSObject.Properties.Name -contains 'Manual') { $man = [string]$g.Manual }
                if ($g.PSObject.Properties.Name -contains 'Extras') { $ext = [string]$g.Extras }
                $entries += ,@{ Folder=[string]$g.Folder; Kind=$kind; ParentIndex=$parent; Setup=$setup; Name=$nm
                                Manual=$man; Extras=$ext }
            }
        }
        if ($entries.Count -eq 0 -and $j.SourceFolder) {
            $entries = @(@{ Folder=[string]$j.SourceFolder; Kind='Game'; ParentIndex=-1
                            Setup=$null; Name=[string]$j.GameName })
        }
        $folders = @($entries | ForEach-Object { $_.Folder })
        return @{
            SourceFolder=$j.SourceFolder; GameFolders=$folders; GameEntries=@($entries)
            Label=$j.Label; IconPath=$j.IconPath; IconIsIco=[bool]$j.IconIsIco
            Menu=[bool]$j.Menu; BgPath=$j.BgPath; BgAsIs=[bool]$j.BgAsIs
            PanelSide=$(if($j.PanelSide){$j.PanelSide}else{'Right'})
            Divider=[bool]$j.Divider; ShowTitle=[bool]$j.ShowTitle; TitleText=[string]$j.TitleText
            WindowBorder=[bool]$j.WindowBorder
            ButtonStyle=$(if($j.ButtonStyle){$j.ButtonStyle}else{'Bordered'})
            MusicFile=$j.MusicFile; Buttons=@($j.Buttons); ManualPath=$j.ManualPath; ExtrasPath=$j.ExtrasPath
            # Version 5. Absent in anything older, which is the automatic setting -
            # and the automatic setting is what those projects always described.
            MediaKey=$(if ($j.PSObject.Properties.Name -contains 'MediaKey') { [string]$j.MediaKey } else { '' })
            ExtrasEveryDisc=$(if ($j.PSObject.Properties.Name -contains 'ExtrasEveryDisc') { [bool]$j.ExtrasEveryDisc } else { $false })
            ExtraItems=@($j.ExtraItems); OutDir=$j.OutDir; Origin='project file'
        }
    } catch { return $null }
}

# Reconstruct settings from a built disc folder that has no project file
# (anything authored before manifests existed, or hand-edited on disc).
function Import-DiscFolder([string]$discDir) {
    $inf = Join-Path $discDir 'autorun.inf'
    if (-not (Test-Path $inf)) { return $null }
    $text = Get-Content -Raw -LiteralPath $inf
    $label = ''; if ($text -match '(?im)^\s*label\s*=\s*(.+?)\s*$') { $label = $Matches[1] }
    $icon  = 'disc.ico'; if ($text -match '(?im)^\s*icon\s*=\s*(.+?)\s*$') { $icon = $Matches[1] }
    $menu  = ($text -match '(?im)^\s*shellexecute\s*=')

    # A disc folder carries no record of which installer was an add-on, so
    # everything comes back as a plain game. Only reached when the disc has no
    # project file beside it, which every build since 0.1.0 writes.
    $r = @{ SourceFolder=$discDir; GameFolders=@($discDir)
            GameEntries=@(@{ Folder=$discDir; Kind='Game'; ParentIndex=-1 })
            Label=$label; IconPath=(Join-Path $discDir $icon); IconIsIco=$true
            Menu=$menu; BgPath=$null; BgAsIs=$true; PanelSide='Right'; MusicFile=$null
            Buttons=@(); ManualPath=$null; ExtrasPath=$null; ExtraItems=@()
            Divider=$false; ShowTitle=$false; TitleText=''
            WindowBorder=$true; ButtonStyle='Bordered'
            OutDir=(Split-Path $discDir -Parent); Origin='disc folder' }

    # Anything at the disc root the pipeline does not generate is user content.
    $r.ExtraItems = @(Get-ChildItem -Force $discDir | Where-Object { -not (Test-ReservedDiscName $_.Name $icon) } | ForEach-Object { $_.FullName })

    $hta = Join-Path $discDir 'AUTORUN\menu.hta'
    if ($menu -and (Test-Path $hta)) {
        $h = Get-Content -Raw -LiteralPath $hta
        # Menus now carry their button list as data (var BTNS=[...]) because the
        # panel is rendered while the menu runs, so there are no <a> tags in the
        # file to scrape. Discs built before that still have them, and this is the
        # path for a disc with no project file beside it - which is exactly the old
        # disc that needs the old reader. Try the data first, fall back to scraping.
        $known = @('Play','Install','Manual','Extras','Exit')
        $found = @()
        if ($h -match 'var BTNS=\[([^\]]*)\]') {
            foreach ($m in [regex]::Matches($Matches[1],'"([^"]+)"')) {
                $k = $m.Groups[1].Value
                if ($known -contains $k -and $found -notcontains $k) { $found += $k }
            }
        } else {
            $map = @{ doPlay='Play'; doInstall='Install'; doManual='Manual'; doExtras='Extras'; doExit='Exit' }
            foreach ($m in [regex]::Matches($h,'class="btn[^"]*"\s+onclick="(do\w+)\(\)"')) {
                $k=$m.Groups[1].Value; if ($map.ContainsKey($k) -and ($found -notcontains $map[$k])) { $found += $map[$k] }
            }
        }
        $r.Buttons = $found
        if ($h -match '\.panel\{position:absolute;left:(\d+)px') { $r.PanelSide = if([int]$Matches[1] -lt 300){'Left'}else{'Right'} }
        if ($h -match '#stage\{[^}]*border:0;') { $r.WindowBorder = $false }
        if ($h -match '(?m)^\s*border:0;border-left:5px') { $r.ButtonStyle = 'Minimal' }
        # menus built before the WMP switch used <bgsound>; accept either form
        $mn = $null
        if ($h -match 'var MUSIC="([^"]*)"') { $mn = $Matches[1] }
        elseif ($h -match '<bgsound src="([^"]+)"') { $mn = $Matches[1] }
        if ($mn) { $mf = Join-Path $discDir ('AUTORUN\'+$mn); if (Test-Path $mf) { $r.MusicFile=$mf } }
        if ($h -match 'var MANUAL="([^"]*)"') {
            $mn=$Matches[1]
            if ($mn) { $mp = Join-Path $discDir ('Extras\'+$mn); if (Test-Path $mp) { $r.ManualPath=$mp } }
        }
        $bg = Join-Path $discDir 'AUTORUN\bg.png'
        if (Test-Path $bg) { $r.BgPath=$bg; $r.BgAsIs=$true }
    }
    $ex = Join-Path $discDir 'Extras'
    if (Test-Path $ex) {
        $others = @(Get-ChildItem -Recurse -File $ex | Where-Object { -not (Test-SamePath $_.FullName $r.ManualPath) })
        if ($others.Count -gt 0) { $r.ExtrasPath = $ex }
    }
    if (-not (Test-Path $r.IconPath)) { $r.IconPath=$null }
    return $r
}

# =================== BUILD ORCHESTRATION ===================

# The file this build will write. One function rather than the same expression in
# two places, because the BUILD button promises to replace a file and this names
# that file - if the two ever drifted apart the button would be lying and nothing
# would say so. Returns '' when there is not enough yet to name one.
function Get-IsoPath([string]$outDir, [string]$label) {
    if ([string]::IsNullOrWhiteSpace($outDir)) { return '' }
    $name = ($label -replace '[^A-Za-z0-9_\- ]','_').Trim()
    if ([string]::IsNullOrWhiteSpace($name)) { return '' }
    return (Join-Path $outDir ($name + '.iso'))
}

function Invoke-Build([hashtable]$s, [scriptblock]$log, [scriptblock]$progress=$null) {
    $stage = Join-Path $s.OutDir 'disc'
    $tmpKeep = $null

    $games = @($s.Games)
    # Rebuilding a disc folder in place: the payload already lives in the stage,
    # so do NOT wipe it. Only ever true for a single game - several games live in
    # Games\ subfolders, so their source folder is never the stage itself.
    $inPlace = ($games.Count -eq 1) -and (Test-SamePath $games[0].Folder $stage)

    if ($inPlace) {
        & $log "Rebuilding in place - installer files left untouched."
        New-Item -ItemType Directory -Force -Path $stage,"$stage\AUTORUN" | Out-Null
    } else {
        # Any asset the user picked from inside the old stage would be destroyed
        # by the wipe - copy it out to temp first and repoint at the copy.
        if (Test-Path $stage) {
            foreach ($k in @('IconPath','BgPath','MusicFile','ManualPath')) {
                $p = $s[$k]
                if ($p -and (Test-Path $p) -and (Test-SubPath $p $stage)) {
                    if (-not $tmpKeep) { $tmpKeep = Join-Path ([IO.Path]::GetTempPath()) ('discwright_'+[Guid]::NewGuid().ToString('N').Substring(0,8)); New-Item -ItemType Directory -Force -Path $tmpKeep | Out-Null }
                    $n = Join-Path $tmpKeep ([IO.Path]::GetFileName($p)); Copy-Item -LiteralPath $p -Destination $n -Force
                    $s[$k] = $n; & $log "  preserved $([IO.Path]::GetFileName($p))"
                }
            }
            if ($s.ExtrasPath -and (Test-Path $s.ExtrasPath) -and (Test-SubPath $s.ExtrasPath $stage)) {
                if (-not $tmpKeep) { $tmpKeep = Join-Path ([IO.Path]::GetTempPath()) ('discwright_'+[Guid]::NewGuid().ToString('N').Substring(0,8)); New-Item -ItemType Directory -Force -Path $tmpKeep | Out-Null }
                $ne = Join-Path $tmpKeep 'Extras'; New-Item -ItemType Directory -Force -Path $ne | Out-Null
                Copy-Item "$($s.ExtrasPath)\*" $ne -Recurse -Force -EA SilentlyContinue
                $s.ExtrasPath = $ne; & $log "  preserved Extras folder"
            }
            $kept = @()
            foreach ($it in @($s.ExtraItems)) {
                if ($it -and (Test-Path $it) -and (Test-SubPath $it $stage)) {
                    if (-not $tmpKeep) { $tmpKeep = Join-Path ([IO.Path]::GetTempPath()) ('discwright_'+[Guid]::NewGuid().ToString('N').Substring(0,8)); New-Item -ItemType Directory -Force -Path $tmpKeep | Out-Null }
                    $nm = [IO.Path]::GetFileName($it.TrimEnd('\')); $np = Join-Path $tmpKeep $nm
                    if (Test-Path $it -PathType Container) { New-Item -ItemType Directory -Force -Path $np | Out-Null; Copy-Item "$it\*" $np -Recurse -Force -EA SilentlyContinue }
                    else { Copy-Item -LiteralPath $it -Destination $np -Force }
                    $kept += $np; & $log "  preserved $nm"
                } else { $kept += $it }
            }
            $s.ExtraItems = $kept
        }
        & $log "Preparing staging folder..."
        # Mounting the ISO to check it and then rebuilding is the obvious workflow,
        # and it leaves a handle on the folder. Raw exception text here reads as a
        # crash; say which folder and what to do about it.
        if (Test-Path $stage) {
            try { Remove-Item $stage -Recurse -Force -ErrorAction Stop }
            catch {
                throw ("The old disc folder cannot be replaced - something is still using it:" +
                       "`r`n`r`n$stage`r`n`r`n" +
                       "Close any Explorer window showing that folder, eject the ISO if you have it mounted, " +
                       "then build again.`r`n`r`nWindows said: $($_.Exception.Message)")
            }
        }
        New-Item -ItemType Directory -Force -Path $stage,"$stage\AUTORUN" | Out-Null

        # Where each entry goes is Get-DiscEntryFolder's decision, not this loop's -
        # the menu asks the same function the same question later.
        $stageRoot = [IO.Path]::GetPathRoot($stage)
        for ($gi = 0; $gi -lt $games.Count; $gi++) {
            $g = $games[$gi]
            $rel = Get-DiscEntryFolder $games $gi
            if ([string]::IsNullOrEmpty($rel)) { $destDir = $stage }
            else {
                $destDir = Join-Path $stage $rel
                New-Item -ItemType Directory -Force -Path $destDir | Out-Null
                $what = if ($g.Kind -eq 'AddOn') { 'add-on' } else { 'game' }
                & $log "  $what : $($g.GameName) -> $(Split-Path $destDir -Leaf)"
            }
            # Hardlinks are per-volume, so this is decided per game: two games on
            # different drives can still have one linked and the other copied.
            $sameVol = ([IO.Path]::GetPathRoot($g.SetupExe.FullName) -eq $stageRoot)
            foreach ($f in $g.Files) {
                $dest = Join-Path $destDir $f.Name
                if ($sameVol) { try { New-Item -ItemType HardLink -Path $dest -Target $f.FullName -EA Stop | Out-Null; & $log "  linked $($f.Name)" }
                                catch { Copy-Item $f.FullName $dest; & $log "  copied $($f.Name)" } }
                else { & $log "  copying $($f.Name) ..."; Copy-Item $f.FullName $dest }
            }

            # This entry's own manual and extras, beside its installer, so a game
            # and everything belonging to it sit together on the disc. Without
            # this the Manual button on every game's screen opened whichever
            # manual the disc happened to carry - on a two-game disc, the wrong
            # one for one of them.
            if ($g.ManualPath -or $g.ExtrasPath) {
                $gx = Join-Path $stage (Get-DiscEntryExtras $games $gi)
                New-Item -ItemType Directory -Force -Path $gx | Out-Null
                if ($g.ExtrasPath -and (Test-Path $g.ExtrasPath)) {
                    Copy-Item "$($g.ExtrasPath)\*" $gx -Recurse -Force -EA SilentlyContinue
                    Clear-ReadOnly $gx
                    & $log "    extras for $($g.GameName)"
                }
                if ($g.ManualPath -and (Test-Path $g.ManualPath)) {
                    $md = Join-Path $gx ([IO.Path]::GetFileName($g.ManualPath))
                    Clear-ReadOnly $md; Copy-Item -LiteralPath $g.ManualPath -Destination $md -Force; Clear-ReadOnly $md
                    & $log "    manual for $($g.GameName): $([IO.Path]::GetFileName($g.ManualPath))"
                }
            }
        }
    }

    # icon - named after the disc so Explorer's per-path icon cache cannot serve a
    # previous disc's bitmap for the same drive letter.
    $icoName = Get-DiscIconName $s.Label
    $icoOut = Join-Path $stage $icoName
    if ($s.IconIsIco) {
        if (Test-SamePath $s.IconPath $icoOut) { & $log "Icon already in place." }
        else { Clear-ReadOnly $icoOut; Copy-Item $s.IconPath $icoOut -Force; Clear-ReadOnly $icoOut; & $log "Icon copied (.ico used as-is)." }
    }
    else { & $log "Building multi-size icon from image..."; Convert-ToIco $s.IconPath $icoOut }
    & $log "Disc icon: $icoName"
    # Rebuilding a disc whose label (or the icon naming rule) changed would
    # otherwise leave the previous icon behind as dead weight on the disc. Runs
    # AFTER the copy above, because the old icon is often the copy's source.
    foreach ($stale in @(Get-ChildItem $stage -Filter '*.ico' -File -EA SilentlyContinue)) {
        if ($stale.Name -ne $icoName) {
            Clear-ReadOnly $stale.FullName; Remove-Item -LiteralPath $stale.FullName -Force -EA SilentlyContinue
            & $log "  removed old icon: $($stale.Name)"
        }
    }

    # menu + background + music
    if ($s.Menu) {
        $bgOut = Join-Path $stage 'AUTORUN\bg.png'
        if ($s.BgAsIs) {
            if (Test-SamePath $s.BgPath $bgOut) { & $log "Background kept as-is (already on disc)." }
            else { Clear-ReadOnly $bgOut; Copy-Item $s.BgPath $bgOut -Force; Clear-ReadOnly $bgOut; & $log "Background copied as-is (no compositing)." }
        } else {
            & $log "Composing menu background ($($s.PanelSide) panel)..."
            # An empty title box means "use the disc label" - the box is only for
            # overriding it, so a blank one must not paint an empty title.
            $bgTitle = if ([string]::IsNullOrWhiteSpace($s.TitleText)) { $s.Label } else { $s.TitleText }
            if ($s.ShowTitle) { & $log "  title on artwork: $bgTitle" }
            New-Background $s.BgPath $bgTitle $bgOut $s.PanelSide ([bool]$s.Divider) ([bool]$s.ShowTitle)
        }

        $manualName=''
        if ($s.Buttons -contains 'Manual' -and $s.ManualPath) {
            New-Item -ItemType Directory -Force -Path "$stage\Extras" | Out-Null
            $manualName = [IO.Path]::GetFileName($s.ManualPath)
            $mDest = "$stage\Extras\$manualName"
            if (-not (Test-SamePath $s.ManualPath $mDest)) { Clear-ReadOnly $mDest; Copy-Item $s.ManualPath $mDest -Force; Clear-ReadOnly $mDest }
        }
        if ($s.Buttons -contains 'Extras' -and $s.ExtrasPath -and (Test-Path $s.ExtrasPath)) {
            New-Item -ItemType Directory -Force -Path "$stage\Extras" | Out-Null
            if (-not (Test-SamePath $s.ExtrasPath "$stage\Extras")) {
                Copy-Item "$($s.ExtrasPath)\*" "$stage\Extras\" -Recurse -Force -EA SilentlyContinue; Clear-ReadOnly "$stage\Extras"
            } else { & $log "Extras already in place." }
        }
        $musicName=''
        if ($s.MusicFile -and (Test-Path $s.MusicFile)) {
            $musicName = 'music'+[IO.Path]::GetExtension($s.MusicFile)
            $muDest = "$stage\AUTORUN\$musicName"
            if (-not (Test-SamePath $s.MusicFile $muDest)) { Clear-ReadOnly $muDest; Copy-Item $s.MusicFile $muDest -Force; Clear-ReadOnly $muDest }
        }
        # The hta:application icon= attribute resolves next to the .hta, not at the
        # disc root, so the icon needs a copy inside AUTORUN. Without it the menu
        # window shows the generic mshta icon in the taskbar.
        $icoMenu = Join-Path $stage "AUTORUN\$icoName"
        if (-not (Test-SamePath $icoOut $icoMenu)) {
            Clear-ReadOnly $icoMenu; Copy-Item $icoOut $icoMenu -Force; Clear-ReadOnly $icoMenu
        }
        foreach ($stale in @(Get-ChildItem (Join-Path $stage 'AUTORUN') -Filter '*.ico' -File -EA SilentlyContinue)) {
            if ($stale.Name -ne $icoName) {
                Clear-ReadOnly $stale.FullName; Remove-Item -LiteralPath $stale.FullName -Force -EA SilentlyContinue
            }
        }
        & $log "Generating autorun menu (menu.hta)..."
        $menuGames = Get-MenuGames $games
        if ($menuGames.Count -gt 1) { & $log "  chooser: $($menuGames.Count) games" }
        foreach ($mgm in $menuGames) {
            if ($mgm.AddOns.Count) { & $log "  $($mgm.Name): $($mgm.AddOns.Count) add-on installer(s)" }
        }
        New-MenuHta @{ GameName=$s.Label; Games=$menuGames; Buttons=$s.Buttons;
                       MusicFile=$musicName; ManualFile=$manualName; PanelSide=$s.PanelSide; IconName=$icoName
                       WindowBorder=[bool]$s.WindowBorder; ButtonStyle=$s.ButtonStyle
                       DiscNum=$(if($s.DiscNum){[int]$s.DiscNum}else{1}); DiscOf=$(if($s.DiscOf){[int]$s.DiscOf}else{1}) } (Join-Path $stage 'AUTORUN\menu.hta')
    }

    # extra content - copied to the disc root, keeping its own name
    if (@($s.ExtraItems).Count) {
        & $log "Adding extra content..."
        foreach ($it in @($s.ExtraItems)) {
            if ([string]::IsNullOrWhiteSpace($it)) { continue }
            if (-not (Test-Path $it)) { & $log "  MISSING, skipped: $it"; continue }
            $nm = [IO.Path]::GetFileName($it.TrimEnd('\'))
            if (Test-ReservedDiscName $nm $icoName) { & $log "  SKIPPED (reserved disc name): $nm"; continue }
            $dest = Join-Path $stage $nm
            if (Test-SamePath $it $dest) { & $log "  already in place: $nm"; continue }
            if (Test-Path $it -PathType Container) {
                New-Item -ItemType Directory -Force -Path $dest | Out-Null
                Copy-Item "$it\*" $dest -Recurse -Force -EA SilentlyContinue; Clear-ReadOnly $dest
                & $log "  folder: $nm"
            } else {
                Clear-ReadOnly $dest; Copy-Item -LiteralPath $it -Destination $dest -Force; Clear-ReadOnly $dest
                & $log "  file:   $nm"
            }
        }
    }

    & $log "Writing autorun.inf..."
    New-AutorunInf $s.Label $icoName $s.Menu (Join-Path $stage 'autorun.inf')

    & $log "Building ISO (UDF, this can take ~30-60s for a full game)..."
    $iso = Get-IsoPath $s.OutDir $s.Label
    # A disc in a set gets its volume id worked out with the disc number reserved,
    # so a long name cannot truncate away the one part that tells two discs apart.
    $vol = $(if ($s.VolumeLabel) { [string]$s.VolumeLabel } else { Get-VolumeLabel $s.Label 1 1 })
    New-Iso $stage $iso $vol $progress
    & $log ("DONE.  ISO: {0}  ({1:N2} GB)" -f $iso, ((Get-Item $iso).Length/1GB))

    # One project file describes the whole set, so the caller saves it once after
    # the last disc rather than each disc overwriting it with its own slice.
    if (-not $s.SkipProject) {
        Save-Project $s $s.OutDir
        & $log "Saved $PROJECT_FILE - use 'Open existing disc project' to edit and rebuild."
    }
    if ($tmpKeep -and (Test-Path $tmpKeep)) { Remove-Item $tmpKeep -Recurse -Force -EA SilentlyContinue }
    return $iso
}

# Builds every disc the plan calls for, one after another, and returns the ISOs.
# A plan of one disc goes straight through to Invoke-Build unchanged - a single
# disc must come out byte for byte the way it always has, label and all.
function Invoke-BuildSet([hashtable]$s, [scriptblock]$log, [scriptblock]$progress=$null, [scriptblock]$onDisc=$null) {
    $plan = $s.Plan
    # Returned as a plain array, not the ",@()" wrapper used for values that get
    # assigned straight across: every caller here wraps the result in @(), and a
    # wrapper on top of that nests the list one level deeper than anyone reads.
    if (-not $plan -or -not $plan.Ok -or @($plan.Discs).Count -le 1) {
        return @(Invoke-Build $s $log $progress)
    }

    $all  = @($s.Games)
    $n    = @($plan.Discs).Count
    $isos = @()
    for ($d = 0; $d -lt $n; $d++) {
        $num = $d + 1
        if ($onDisc) { & $onDisc $num $n }
        & $log ''
        & $log "===== Disc $num of $n ====="
        $ds = @{}
        foreach ($k in $s.Keys) { $ds[$k] = $s[$k] }
        $ds.Games       = Get-DiscEntries $all @($plan.Discs[$d])
        $ds.Label       = Get-DiscSetLabel $s.Label $num $n
        $ds.VolumeLabel = Get-VolumeLabel  $s.Label $num $n
        $ds.SkipProject = $true
        $ds.DiscNum     = $num
        $ds.DiscOf      = $n
        # The disc-wide manual, extras and loose files ride on disc 1 alone unless
        # the form says otherwise: copying them onto every disc multiplies bonus
        # content by the size of the set - the wrong default for a 2 GB making-of
        # and the right one for a 3 MB manual, hence the choice.
        # A game carries its OWN manual and extras with it, wherever it lands.
        if ($num -ne 1 -and -not $s.ExtrasEveryDisc) { $ds.ManualPath = $null; $ds.ExtrasPath = $null; $ds.ExtraItems = @() }
        $isos += (Invoke-Build $ds $log $progress)
    }

    Save-Project $s $s.OutDir
    & $log ''
    & $log "Saved $PROJECT_FILE - use 'Open existing disc project' to edit and rebuild the set."
    return @($isos)
}

# =================== UI ===================
# LabelSeededFrom records the name DiscWright typed into the disc label itself, so
# it can tell its own guess from something the user wrote. LastGameBrowse is the
# folder the game picker should reopen on, and LastFileBrowse the same for the icon,
# background, music and manual pickers. Both deliberately outlive New disc and
# removing every entry: where your GOG downloads and artwork live does not change
# when you start a second disc.
$state = @{ Games=@(); IconPath=$null; IconIsIco=$false; BgPath=$null; MusicFile=$null; ManualPath=$null; ExtrasPath=$null; ExtraItems=@()
            LabelSeededFrom=$null; LastGameBrowse=$null; LastFileBrowse=$null }

# One game per disc is still the common case, so the disc layout, the UI and the
# menu are unchanged for it. Only sizing, the build's copy step and the menu need
# to think in lists, and they go through these two.
# The leading comma is load-bearing. PowerShell unrolls a single-element array on
# return, so "return @(...)" with one game handed back the bare hashtable instead
# of a list. Callers then read .Count on a hashtable - which is its number of KEYS,
# nine - and the media line announced "9 games (0 bytes)" for one game. Get-FirstGame
# was worse: $g[0] indexed the hashtable by the key 0, found nothing, and returned
# null, so the preview menu lost the game entirely. Wrapping in an outer array means
# the unroll gives back the inner one.
function Get-Games { return ,@($state.Games | Where-Object { $_ -and $_.Ok }) }

# Do NOT write @(Get-Games) here. Get-Games already hands back an array, and wrapping
# it again gives a one-element array whose element is that array - so $g[0] came back
# as every game at once. Plain assignment is what preserves it.
function Get-FirstGame {
    $g = Get-Games
    if ($g.Count -gt 0) { return $g[0] }
    return $null
}

$form=New-Object System.Windows.Forms.Form
# The version belongs where it can be read off a screenshot without being asked
# for, since that is how it arrives in a bug report.
$form.Text="DiscWright $APP_VERSION"
# 1054 is what the controls need. Opening at that height regardless is how a
# window ends up with its Build button under the taskbar: 1080p leaves 1032
# usable, and a 768px laptop far less. Open at whatever fits and let AutoScroll
# cover the difference - the alternative is a window that cannot be used at all
# on the screen it opened on.
$formWanted = 992
$formUsable = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Height
$form.Size=New-Object System.Drawing.Size(700,([Math]::Min($formWanted,$formUsable)))
$form.MinimumSize=New-Object System.Drawing.Size(700,420)
$form.StartPosition='CenterScreen'
$form.Font=New-Object System.Drawing.Font('Segoe UI',9)
$form.AutoScroll=$true

function AddLabel($t,$x,$y,$w){ $l=New-Object System.Windows.Forms.Label; $l.Text=$t; $l.Location=New-Object System.Drawing.Point($x,$y); $l.Size=New-Object System.Drawing.Size($w,20); $form.Controls.Add($l); return $l }
function AddText($x,$y,$w){ $t=New-Object System.Windows.Forms.TextBox; $t.Location=New-Object System.Drawing.Point($x,$y); $t.Size=New-Object System.Drawing.Size($w,24); $form.Controls.Add($t); return $t }
function AddBtn($t,$x,$y,$w){ $b=New-Object System.Windows.Forms.Button; $b.Text=$t; $b.Location=New-Object System.Drawing.Point($x,$y); $b.Size=New-Object System.Drawing.Size($w,24); $form.Controls.Add($b); return $b }

# A FolderBrowserDialog with no SelectedPath opens wherever Windows last left it,
# process-wide. After a build that is the output folder, so Browse for the game
# folder in step 1 reopened on the disc it had just written - and the two folders
# are named alike enough that picking the wrong one looks like the app losing the
# game rather than a misclick. Each Browse now starts where its own box points.
function New-FolderDialog([string]$description,[string]$startAt) {
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($description) { $d.Description = $description }
    if ($startAt -and (Test-Path $startAt -PathType Container)) { $d.SelectedPath = $startAt }
    return $d
}

# Where the game picker opens when nothing on the form points anywhere yet: a fresh
# start, or straight after New disc emptied the list.
#
# It does not simply reopen where it was last time. FolderBrowserDialog is the old
# SHBrowseForFolder tree, and it does NOT restore the previous folder between
# openings - opening it once and cancelling teaches it nothing. So the first pick of
# a session landed wherever the shell felt like, which on a machine with a redirected
# Desktop is several expansions away from anything useful.
#
# GOG Galaxy's own download folder is the best guess there is: DiscWright exists to
# read GOG offline installers, and that is where Galaxy puts them unless told
# otherwise. If it is not there, fall back to nothing rather than to a wrong guess.

# Whether the disc label sitting in the box is one DiscWright typed itself, and may
# therefore be taken back when the last entry goes.
#
# A named predicate rather than an inline condition because it is the entire rule,
# and the click handler that applies it cannot be reached from a test: getting a
# game onto the form means driving the folder picker, whose tree is invisible to UI
# Automation. This way the rule is tested even though the wiring is not.
#
# Compared against what was SEEDED, never against the entry being removed. A user
# who types a label that happens to match the game's name still owns it - they typed
# it, so it survives.
function Test-LabelIsSeeded([string]$current, [string]$seeded) {
    if ([string]::IsNullOrEmpty($seeded)) { return $false }
    return ($current -eq $seeded)
}

function Get-DefaultGameBrowseFolder {
    foreach ($p in @(
        (Join-Path ${env:ProgramFiles(x86)} 'GOG Galaxy\Games\Offline Installers'),
        (Join-Path $env:ProgramFiles        'GOG Galaxy\Games\Offline Installers')
    )) {
        if ($p -and (Test-Path $p -PathType Container)) { return $p }
    }
    return ''
}

# The folder a path points into, whether the path is a folder or a file inside one.
# Returns '' for anything that is not there any more, so a stale box does not aim a
# dialog at a folder that has been deleted.
function Get-ExistingFolderOf([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return '' }
    $p = $path.Trim()
    if (Test-Path $p -PathType Container) { return $p }
    $dir = Split-Path $p -Parent
    if ($dir -and (Test-Path $dir -PathType Container)) { return $dir }
    return ''
}

# Where a FILE picker should open. Same problem the game picker had, and the same
# answer: an OpenFileDialog with no InitialDirectory opens wherever the shell last
# left it, which on a machine with a redirected Desktop is somebody's OneDrive - so
# the icon, background, music and manual pickers all started there.
#
# Asked in order:
#   1. the folder the picker's own box already points at, so reopening a picker
#      lands where its current value came from;
#   2. wherever a file was last picked from - artwork, a manual and a soundtrack
#      for one disc usually live near each other, and this outlives New disc for
#      the same reason the game picker's memory does;
#   3. the first game's folder, because GOG's extras are downloaded beside the
#      installer they belong to.
# If none of those exist, no aim is better than a wrong one: a SelectedPath that
# does not exist is ignored silently, which is indistinguishable from not setting
# it, only harder to notice later.
function Get-MediaBrowseFallback {
    $p = Get-ExistingFolderOf $state.LastFileBrowse
    if ($p) { return $p }
    $first = Get-FirstGame
    if ($first -and $first.Folder) { return (Get-ExistingFolderOf ([string]$first.Folder)) }
    return ''
}

function New-FileDialog([string]$title, [string]$filter, [string]$current) {
    $d = New-Object System.Windows.Forms.OpenFileDialog
    if ($title)  { $d.Title  = $title }
    if ($filter) { $d.Filter = $filter }

    $start = Get-ExistingFolderOf $current
    if (-not $start) { $start = Get-MediaBrowseFallback }
    if ($start) { $d.InitialDirectory = $start }
    return $d
}

# Remembers where a file was just taken from, so the next picker opens there.
function Set-LastFileBrowse([string]$picked) {
    $dir = Get-ExistingFolderOf (Split-Path $picked -Parent)
    if ($dir) { $state.LastFileBrowse = $dir }
}

# --- new / reopen / inspect row ---
# Four buttons in the width that held three, so they are narrower than they were.
# Each is still wider than its own longest label at 9pt with room to spare; the
# window test that no two controls overlap is what guards the arithmetic.
$btnNew      = AddBtn 'New disc' 15 8 110
$btnOpenProj = AddBtn 'Open existing disc...' 135 8 185
$btnOpenDisc = AddBtn 'Show disc folder' 330 8 160
$btnPreview  = AddBtn 'Preview menu' 500 8 155

# Step 1 was one text box holding one folder. It is a list now, because a disc
# can carry several games and their add-ons, and the one thing the old box could
# not show is the thing that matters most here: which entry belongs to which.
# Everything below moved down 58px to make room.
AddLabel '1)  Installers on this disc (a game is a GOG folder; an add-on is one file):' 15 46 520 | Out-Null
$lvGames=New-Object System.Windows.Forms.ListView
$lvGames.Location=New-Object System.Drawing.Point(15,68)
$lvGames.Size=New-Object System.Drawing.Size(520,108)
$lvGames.View='Details'; $lvGames.FullRowSelect=$true; $lvGames.MultiSelect=$false
$lvGames.HideSelection=$false; $lvGames.HeaderStyle='Nonclickable'; $lvGames.ShowItemToolTips=$true
[void]$lvGames.Columns.Add('#',28)
[void]$lvGames.Columns.Add('Name',215)
[void]$lvGames.Columns.Add('Type',70)
[void]$lvGames.Columns.Add('Belongs to',180)
$form.Controls.Add($lvGames)
$btnFolder  =AddBtn 'Add game...' 545 68  110
$btnAddOn   =AddBtn 'Add-on...'   545 96  110
$btnGameEdit=AddBtn 'Change...'   545 124 110
$btnGameDel =AddBtn 'Remove'      545 152 110
$lblGame=AddLabel '' 15 180 655; $lblGame.ForeColor=[System.Drawing.Color]::DimGray
# A game's title is user data and a refusal quotes it in full, so no width is
# wide enough for every case. AutoEllipsis ends a line that will not fit with
# "..." instead of cutting a word in half, and the tooltip carries the rest.
$lblGame.AutoSize=$false; $lblGame.AutoEllipsis=$true

AddLabel '2)  Disc label (shown in This PC):' 15 210 300 | Out-Null
$txtLabel=AddText 15 232 300

# Which disc you are actually going to burn. Until now DiscWright only ever
# recommended a size; a payload past the biggest disc was simply refused. Saying
# which medium you own turns that into a set: the games are packed onto as many
# discs as it takes, in the order they sit in the list.
AddLabel 'Target disc:' 330 210 200 | Out-Null
$cmbMedia=New-Object System.Windows.Forms.ComboBox
$cmbMedia.DropDownStyle='DropDownList'
$cmbMedia.Location=New-Object System.Drawing.Point(330,232)
$cmbMedia.Size=New-Object System.Drawing.Size(240,24)
[void]$cmbMedia.Items.Add((Get-MediaAutoText))
foreach ($t in Get-MediaTiers) { [void]$cmbMedia.Items.Add($t.Name) }
$cmbMedia.SelectedIndex=0
$form.Controls.Add($cmbMedia)

AddLabel '3)  Disc icon (.ico, or .png/.jpg to auto-convert):' 15 268 520 | Out-Null
$txtIcon=AddText 15 290 520
$btnIcon=AddBtn 'Browse...' 545 290 110
$lblIcon=AddLabel '' 15 318 500; $lblIcon.ForeColor=[System.Drawing.Color]::DimGray
# Below the Browse button, not beside it: at y=290 this 44px-tall preview sat on
# top of a 24px-tall button in the same 110px column, and whichever Windows drew
# last won. Nothing lines up on this row at x=560, and 318+44 clears the step 4
# group box at y=372.
$picIcon=New-Object System.Windows.Forms.PictureBox; $picIcon.Location=New-Object System.Drawing.Point(560,318); $picIcon.Size=New-Object System.Drawing.Size(44,44); $picIcon.SizeMode='Zoom'; $picIcon.BorderStyle='FixedSingle'; $form.Controls.Add($picIcon)

$grp=New-Object System.Windows.Forms.GroupBox; $grp.Text='4)  Autorun menu'; $grp.Location=New-Object System.Drawing.Point(15,372); $grp.Size=New-Object System.Drawing.Size(645,286); $form.Controls.Add($grp)
$chkMenu=New-Object System.Windows.Forms.CheckBox; $chkMenu.Text='Run splash menu when disc is inserted'; $chkMenu.Location=New-Object System.Drawing.Point(15,24); $chkMenu.Size=New-Object System.Drawing.Size(400,22); $chkMenu.Checked=$true; $grp.Controls.Add($chkMenu)

$lblBg=New-Object System.Windows.Forms.Label; $lblBg.Text='Background image:'; $lblBg.Location=New-Object System.Drawing.Point(15,54); $lblBg.Size=New-Object System.Drawing.Size(130,20); $grp.Controls.Add($lblBg)
$txtBg=New-Object System.Windows.Forms.TextBox; $txtBg.Location=New-Object System.Drawing.Point(150,52); $txtBg.Size=New-Object System.Drawing.Size(350,24); $grp.Controls.Add($txtBg)
$btnBg=New-Object System.Windows.Forms.Button; $btnBg.Text='Browse...'; $btnBg.Location=New-Object System.Drawing.Point(510,51); $btnBg.Size=New-Object System.Drawing.Size(110,24); $grp.Controls.Add($btnBg)

$chkBgAsIs=New-Object System.Windows.Forms.CheckBox; $chkBgAsIs.Text='Use as-is (already 760x480, no overlay)'; $chkBgAsIs.Location=New-Object System.Drawing.Point(150,80); $chkBgAsIs.Size=New-Object System.Drawing.Size(250,22); $grp.Controls.Add($chkBgAsIs)
$lblSide=New-Object System.Windows.Forms.Label; $lblSide.Text='Buttons:'; $lblSide.Location=New-Object System.Drawing.Point(410,82); $lblSide.Size=New-Object System.Drawing.Size(55,20); $grp.Controls.Add($lblSide)
$cmbSide=New-Object System.Windows.Forms.ComboBox; $cmbSide.DropDownStyle='DropDownList'; $cmbSide.Location=New-Object System.Drawing.Point(468,79); $cmbSide.Size=New-Object System.Drawing.Size(100,24); [void]$cmbSide.Items.AddRange(@('Right','Left')); $cmbSide.SelectedIndex=0; $grp.Controls.Add($cmbSide)

$lblTitle=New-Object System.Windows.Forms.Label; $lblTitle.Text='Title on artwork:'; $lblTitle.Location=New-Object System.Drawing.Point(15,110); $lblTitle.Size=New-Object System.Drawing.Size(130,20); $grp.Controls.Add($lblTitle)
$chkTitle=New-Object System.Windows.Forms.CheckBox; $chkTitle.Text='Show title'; $chkTitle.Location=New-Object System.Drawing.Point(150,108); $chkTitle.Size=New-Object System.Drawing.Size(145,22); $grp.Controls.Add($chkTitle)
$txtTitle=New-Object System.Windows.Forms.TextBox; $txtTitle.Location=New-Object System.Drawing.Point(300,106); $txtTitle.Size=New-Object System.Drawing.Size(320,24); $txtTitle.Enabled=$false; $grp.Controls.Add($txtTitle)

$chkMusic=New-Object System.Windows.Forms.CheckBox; $chkMusic.Text='Background music'; $chkMusic.Location=New-Object System.Drawing.Point(15,136); $chkMusic.Size=New-Object System.Drawing.Size(130,22); $grp.Controls.Add($chkMusic)
$txtMusic=New-Object System.Windows.Forms.TextBox; $txtMusic.Location=New-Object System.Drawing.Point(150,134); $txtMusic.Size=New-Object System.Drawing.Size(350,24); $txtMusic.Enabled=$false; $grp.Controls.Add($txtMusic)
$btnMusic=New-Object System.Windows.Forms.Button; $btnMusic.Text='Browse...'; $btnMusic.Location=New-Object System.Drawing.Point(510,133); $btnMusic.Size=New-Object System.Drawing.Size(110,24); $btnMusic.Enabled=$false; $grp.Controls.Add($btnMusic)

$lblBtns=New-Object System.Windows.Forms.Label; $lblBtns.Text='Menu buttons:'; $lblBtns.Location=New-Object System.Drawing.Point(15,164); $lblBtns.Size=New-Object System.Drawing.Size(120,20); $grp.Controls.Add($lblBtns)
$cbPlay =New-Object System.Windows.Forms.CheckBox; $cbPlay.Text='Play'; $cbPlay.Checked=$true; $cbPlay.Location=New-Object System.Drawing.Point(150,162); $cbPlay.Size=New-Object System.Drawing.Size(70,22); $grp.Controls.Add($cbPlay)
$cbInst =New-Object System.Windows.Forms.CheckBox; $cbInst.Text='Install'; $cbInst.Checked=$true; $cbInst.Location=New-Object System.Drawing.Point(225,162); $cbInst.Size=New-Object System.Drawing.Size(75,22); $grp.Controls.Add($cbInst)
$cbMan  =New-Object System.Windows.Forms.CheckBox; $cbMan.Text='Manual'; $cbMan.Location=New-Object System.Drawing.Point(305,162); $cbMan.Size=New-Object System.Drawing.Size(80,22); $grp.Controls.Add($cbMan)
$cbExtra=New-Object System.Windows.Forms.CheckBox; $cbExtra.Text='Extras'; $cbExtra.Location=New-Object System.Drawing.Point(390,162); $cbExtra.Size=New-Object System.Drawing.Size(75,22); $grp.Controls.Add($cbExtra)
$cbExit =New-Object System.Windows.Forms.CheckBox; $cbExit.Text='Exit'; $cbExit.Checked=$true; $cbExit.Location=New-Object System.Drawing.Point(465,162); $cbExit.Size=New-Object System.Drawing.Size(70,22); $grp.Controls.Add($cbExit)

$lblMan=New-Object System.Windows.Forms.Label; $lblMan.Text='Manual file:'; $lblMan.Location=New-Object System.Drawing.Point(15,192); $lblMan.Size=New-Object System.Drawing.Size(130,20); $grp.Controls.Add($lblMan)
$txtMan=New-Object System.Windows.Forms.TextBox; $txtMan.Location=New-Object System.Drawing.Point(150,190); $txtMan.Size=New-Object System.Drawing.Size(350,24); $txtMan.Enabled=$false; $grp.Controls.Add($txtMan)
$btnMan=New-Object System.Windows.Forms.Button; $btnMan.Text='Browse...'; $btnMan.Location=New-Object System.Drawing.Point(510,189); $btnMan.Size=New-Object System.Drawing.Size(110,24); $btnMan.Enabled=$false; $grp.Controls.Add($btnMan)

$lblEx=New-Object System.Windows.Forms.Label; $lblEx.Text='Extras folder:'; $lblEx.Location=New-Object System.Drawing.Point(15,220); $lblEx.Size=New-Object System.Drawing.Size(130,20); $grp.Controls.Add($lblEx)
$txtEx=New-Object System.Windows.Forms.TextBox; $txtEx.Location=New-Object System.Drawing.Point(150,218); $txtEx.Size=New-Object System.Drawing.Size(350,24); $txtEx.Enabled=$false; $grp.Controls.Add($txtEx)
$btnEx=New-Object System.Windows.Forms.Button; $btnEx.Text='Browse...'; $btnEx.Location=New-Object System.Drawing.Point(510,217); $btnEx.Size=New-Object System.Drawing.Size(110,24); $btnEx.Enabled=$false; $grp.Controls.Add($btnEx)

$lblStyle=New-Object System.Windows.Forms.Label; $lblStyle.Text='Look:'; $lblStyle.Location=New-Object System.Drawing.Point(15,250); $lblStyle.Size=New-Object System.Drawing.Size(130,20); $grp.Controls.Add($lblStyle)
$chkDivider=New-Object System.Windows.Forms.CheckBox; $chkDivider.Text='Divider line'; $chkDivider.Location=New-Object System.Drawing.Point(150,248); $chkDivider.Size=New-Object System.Drawing.Size(100,22); $grp.Controls.Add($chkDivider)
$chkWinBorder=New-Object System.Windows.Forms.CheckBox; $chkWinBorder.Text='Window border'; $chkWinBorder.Checked=$true; $chkWinBorder.Location=New-Object System.Drawing.Point(255,248); $chkWinBorder.Size=New-Object System.Drawing.Size(115,22); $grp.Controls.Add($chkWinBorder)
$lblBtnStyle=New-Object System.Windows.Forms.Label; $lblBtnStyle.Text='Buttons:'; $lblBtnStyle.Location=New-Object System.Drawing.Point(380,250); $lblBtnStyle.Size=New-Object System.Drawing.Size(55,20); $grp.Controls.Add($lblBtnStyle)
$cmbBtnStyle=New-Object System.Windows.Forms.ComboBox; $cmbBtnStyle.DropDownStyle='DropDownList'; $cmbBtnStyle.Location=New-Object System.Drawing.Point(438,247); $cmbBtnStyle.Size=New-Object System.Drawing.Size(130,24); [void]$cmbBtnStyle.Items.AddRange(@('Minimal','Bordered')); $cmbBtnStyle.SelectedIndex=0; $grp.Controls.Add($cmbBtnStyle)

$grpX=New-Object System.Windows.Forms.GroupBox; $grpX.Text='5)  Extra content (copied to the disc root as-is)'; $grpX.Location=New-Object System.Drawing.Point(15,668); $grpX.Size=New-Object System.Drawing.Size(645,124); $form.Controls.Add($grpX)
$lstExtra=New-Object System.Windows.Forms.ListBox; $lstExtra.Location=New-Object System.Drawing.Point(15,22); $lstExtra.Size=New-Object System.Drawing.Size(480,72); $lstExtra.SelectionMode='MultiExtended'; $lstExtra.HorizontalScrollbar=$true; $grpX.Controls.Add($lstExtra)
# The disc-wide manual, Extras folder and loose files ride on disc 1 of a set,
# because copying a 2 GB extras folder onto five discs costs ten. That is the
# right default and the wrong answer for a 3 MB PDF, so it is a choice. A game's
# OWN manual and extras always travel with that game and are not affected.
$chkXAll=New-Object System.Windows.Forms.CheckBox; $chkXAll.Text='Also put the manual and extras on every disc of a set'; $chkXAll.Location=New-Object System.Drawing.Point(15,98); $chkXAll.Size=New-Object System.Drawing.Size(400,22); $grpX.Controls.Add($chkXAll)
$btnXFile=New-Object System.Windows.Forms.Button; $btnXFile.Text='Add files...'; $btnXFile.Location=New-Object System.Drawing.Point(505,22); $btnXFile.Size=New-Object System.Drawing.Size(120,24); $grpX.Controls.Add($btnXFile)
$btnXDir =New-Object System.Windows.Forms.Button; $btnXDir.Text='Add folder...'; $btnXDir.Location=New-Object System.Drawing.Point(505,52); $btnXDir.Size=New-Object System.Drawing.Size(120,24); $grpX.Controls.Add($btnXDir)
$btnXDel =New-Object System.Windows.Forms.Button; $btnXDel.Text='Remove'; $btnXDel.Location=New-Object System.Drawing.Point(505,82); $btnXDel.Size=New-Object System.Drawing.Size(120,24); $grpX.Controls.Add($btnXDel)

AddLabel '6)  Output folder (ISO + disc staging go here):' 15 800 520 | Out-Null
$txtOut=AddText 15 822 520
$btnOut=AddBtn 'Browse...' 545 822 110

$btnBuild=New-Object System.Windows.Forms.Button; $btnBuild.Text='BUILD ISO'; $btnBuild.Location=New-Object System.Drawing.Point(15,856); $btnBuild.Size=New-Object System.Drawing.Size(150,30); $btnBuild.BackColor=[System.Drawing.Color]::FromArgb(0,150,160); $btnBuild.ForeColor=[System.Drawing.Color]::White; $form.Controls.Add($btnBuild)
$txtLog=New-Object System.Windows.Forms.TextBox; $txtLog.Multiline=$true; $txtLog.ScrollBars='Vertical'; $txtLog.ReadOnly=$true; $txtLog.Location=New-Object System.Drawing.Point(180,856); $txtLog.Size=New-Object System.Drawing.Size(480,78); $form.Controls.Add($txtLog)

# Both sit in space the layout already had, under the BUILD button and beside the
# log, so nothing else has to move. Hidden until a build starts - an idle window
# looks exactly as it did before.
$pbBuild=New-Object System.Windows.Forms.ProgressBar; $pbBuild.Location=New-Object System.Drawing.Point(15,894); $pbBuild.Size=New-Object System.Drawing.Size(150,14); $pbBuild.Minimum=0; $pbBuild.Maximum=1000; $pbBuild.Visible=$false; $form.Controls.Add($pbBuild)
$lblElapsed=New-Object System.Windows.Forms.Label; $lblElapsed.Location=New-Object System.Drawing.Point(15,914); $lblElapsed.Size=New-Object System.Drawing.Size(160,20); $lblElapsed.ForeColor=[System.Drawing.Color]::DimGray; $form.Controls.Add($lblElapsed)

$buildWatch = New-Object System.Diagnostics.Stopwatch
# Prefixed onto the elapsed line while a set builds. A set runs the progress bar
# from 0 to 100 once per disc, and with nothing saying which disc that was, the
# second pass reads as the first one having restarted.
$script:BuildDiscTag = ''

$log = { param($m)
    $txtLog.AppendText($m + "`r`n")
    if ($buildWatch.IsRunning) {
        $lblElapsed.Text = $(if ($script:BuildDiscTag) { $script:BuildDiscTag + '  ' + (Format-Elapsed $buildWatch.Elapsed) }
                             else                     { 'Elapsed  ' + (Format-Elapsed $buildWatch.Elapsed) })
    }
    [System.Windows.Forms.Application]::DoEvents()
}

# Handed to Invoke-Build, which passes it down to the ISO writer. Called about 200
# times over a write of any size. DoEvents is the load-bearing line: the build owns
# the interface thread, and this is the only thing keeping the window painting
# while the longest step runs.
$buildProgress = { param($done,$total)
    if ($total -gt 0) {
        $v = [int](1000.0 * $done / $total); if ($v -gt 1000) { $v = 1000 }
        $pbBuild.Value = $v
        # The label has 160px before it runs under the log box, so the disc tag
        # replaces the word ISO rather than being added in front of it: a set
        # shows "D1/2  45%  0:32" in the same width "ISO  45%   0:32" needed.
        $lblElapsed.Text = $(if ($script:BuildDiscTag) { '{0}  {1}%   {2}' -f $script:BuildDiscTag, [int]($v/10), (Format-Elapsed $buildWatch.Elapsed) }
                             else                     { 'ISO  {0}%   {1}'  -f [int]($v/10), (Format-Elapsed $buildWatch.Elapsed) })
    }
    [System.Windows.Forms.Application]::DoEvents()
}

# ---- shared bits ----
# Everything that will land on the disc, not just the installer - artbooks and
# soundtracks can push a build past a disc tier. The media label and the build's
# own capacity check both read this, so they cannot disagree about the size and
# promise a disc the build then refuses.
function Get-PayloadBytes {
    $games = Get-Games
    if ($games.Count -eq 0) { return @{ Installer=[double]0; Extra=[double]0; Total=[double]0 } }
    # Summed by hand, not with Measure-Object. Get-GameInfo returns a hashtable, and
    # Measure-Object -Property looks for a real PROPERTY - a hashtable key is not one,
    # so it found nothing and reported a total of zero for every disc. $g.TotalBytes
    # works because member access on a hashtable does read keys; Measure-Object does
    # not go through that path.
    $installer = [double]0
    foreach ($g in $games) { $installer += [double]$g.TotalBytes }
    # Split out rather than summed in one go: the music rides on every disc of a
    # set because every disc has its own menu, while the manual, the extras and
    # the loose files ride on disc 1 alone. Adding them together would charge the
    # wrong disc for one of them.
    $side = @()
    $side += @($state.ExtraItems)
    if ($cbMan.Checked   -and $state.ManualPath) { $side += $state.ManualPath }
    if ($cbExtra.Checked -and $state.ExtrasPath) { $side += $state.ExtrasPath }
    $discExtra = [double](Get-ItemsSize $side)
    $music     = [double]0
    if ($chkMusic.Checked -and $state.MusicFile) { $music = [double](Get-ItemsSize @($state.MusicFile)) }
    return @{ Installer=$installer; Extra=($discExtra + $music); DiscExtra=$discExtra; Music=$music
              Total=($installer + $discExtra + $music) }
}

# The overhead figure and the disc-1 figure the planner needs, read off the form.
# Takes an already-measured payload when the caller has one: Get-PayloadBytes
# walks the extras folder, and the advice line runs on every keystroke in the
# disc label. Measuring the same folder twice per character is not free.
function Get-PlanInputs([hashtable]$payload=$null) {
    $p = $(if ($payload) { $payload } else { Get-PayloadBytes })
    $o = Get-DiscOverheadBytes @{ IconPath=$state.IconPath; BgPath=$state.BgPath
                                  MusicFile=$(if($chkMusic.Checked){$state.MusicFile}else{$null}) }
    # On every disc they are overhead, like the menu. On disc 1 alone they are a
    # one-off charge against the first disc's room. Same bytes, different bill.
    if ($chkXAll.Checked) { return @{ Overhead=($o + $p.DiscExtra); FirstDiscExtra=[double]0; Payload=$p } }
    return @{ Overhead=$o; FirstDiscExtra=$p.DiscExtra; Payload=$p }
}

# The medium the form is currently pointed at, '' for the automatic setting.
function Get-SelectedMediaKey { return (Get-MediaKeyFromName ([string]$cmbMedia.SelectedItem)) }

# Rebuilding the rows re-enters through SelectedIndexChanged, and the handler
# calls straight back here. One flag rather than unhooking the event: unhooking
# and rehooking a WinForms handler around every list refresh is the kind of thing
# that eventually leaves it unhooked.
$script:MediaRowsBusy = $false

# Re-annotate the dropdown for what is on the form now. Selection is restored by
# KEY, because the text it was selected by has just changed underneath it.
function Update-MediaOptions {
    if ($script:MediaRowsBusy) { return }
    $games = Get-Games
    $pi    = $(if ($games.Count) { Get-PlanInputs } else { $null })
    $rows  = @((Get-MediaAutoText))
    foreach ($t in Get-MediaTiers) {
        $plan = $null
        if ($games.Count) { $plan = Get-DiscPlan $games $t.Key $pi.Overhead $pi.FirstDiscExtra }
        $rows += (Get-MediaOptionText $t $plan)
    }
    # Only touch the control when something actually changed. Rewriting the list
    # on every keystroke would close the dropdown under a user reading it.
    $now = @($cmbMedia.Items | ForEach-Object { [string]$_ })
    if (($now -join '|') -eq ($rows -join '|')) { return }

    $key = Get-SelectedMediaKey
    $script:MediaRowsBusy = $true
    try {
        $cmbMedia.BeginUpdate()
        $cmbMedia.Items.Clear()
        [void]$cmbMedia.Items.AddRange([object[]]$rows)
        $idx = 0
        if ($key) {
            for ($i = 1; $i -lt $rows.Count; $i++) {
                if ((Get-MediaKeyFromName $rows[$i]) -eq $key) { $idx = $i; break }
            }
        }
        $cmbMedia.SelectedIndex = $idx
    } finally {
        $cmbMedia.EndUpdate()
        $script:MediaRowsBusy = $false
    }
}

# Point the dropdown at a medium by key. Used when a project is reopened: the row
# text depends on what is on the form, so the key is the only stable handle.
function Select-MediaKey([string]$key) {
    for ($i = 0; $i -lt $cmbMedia.Items.Count; $i++) {
        if ((Get-MediaKeyFromName ([string]$cmbMedia.Items[$i])) -eq $key) { $cmbMedia.SelectedIndex = $i; return }
    }
    $cmbMedia.SelectedIndex = 0
}

# The plan for what is on the form right now, or $null when no target is chosen.
function Get-CurrentPlan([hashtable]$payload=$null) {
    $key = Get-SelectedMediaKey
    if (-not $key) { return $null }
    $pi = Get-PlanInputs $payload
    return (Get-DiscPlan (Get-Games) $key $pi.Overhead $pi.FirstDiscExtra)
}

# When a set is planned, say on the form where the disc-wide content lands. Only
# the surprising case is annotated: unticked means disc 1 alone and nothing else
# on the form says so, while ticked is spelled out on the checkbox itself.
#
# $script: on every control, deliberately. The entry dialog declares its own
# $lblMan for "Its own manual:", and PowerShell's dynamic scoping would hand
# that one to this function whenever it is reached from inside that dialog.
function Update-DiscWideLabels($plan) {
    if (-not $script:lblMan -or -not $script:lblEx -or -not $script:grpX) { return }
    $isSet = ($plan -and $plan.Ok -and @($plan.Discs).Count -gt 1)
    $note  = ($isSet -and -not $script:chkXAll.Checked)
    $man  = $(if ($note) { 'Manual file (disc 1):' }   else { 'Manual file:' })
    $ext  = $(if ($note) { 'Extras folder (disc 1):' } else { 'Extras folder:' })
    $cap  = $(if ($note) { '5)  Extra content (copied to disc 1 of the set)' }
             else        { '5)  Extra content (copied to the disc root as-is)' })
    if ($script:lblMan.Text -ne $man) { $script:lblMan.Text = $man }
    if ($script:lblEx.Text  -ne $ext) { $script:lblEx.Text  = $ext }
    if ($script:grpX.Text   -ne $cap) { $script:grpX.Text   = $cap }
}

function Update-MediaLabel {
    $games = Get-Games
    Update-MediaOptions
    if ($games.Count -eq 0) {
        # Removing the last entry has to clear this line, not leave it alone.
        # Returning early left the previous disc's summary sitting under an empty
        # list - still green, still naming a size and a disc type that nothing on
        # the form accounted for any more.
        $lblGame.Text = ''
        $lblGame.ForeColor = [System.Drawing.Color]::DimGray
        Update-DiscWideLabels $null
        return
    }
    $g = $games[0]
    $p = Get-PayloadBytes
    $m = Get-MediaRec $p.Total
    # One entry keeps its own detection line. Several get a summary instead - the
    # per-entry detail would not fit, and the total is what decides the disc.
    #
    # Games and add-ons are counted separately. Calling the lot "5 games" when
    # four of them are patches for the fifth describes a disc that is not the one
    # about to be built, and the count is the only place the difference shows
    # before the menu is generated.
    $nAdd  = @($games | Where-Object { $_.Kind -eq 'AddOn' }).Count
    $nGame = $games.Count - $nAdd
    $txt = if ($games.Count -eq 1) { $g.Msg }
           else {
               $part = "$nGame game$(if($nGame -ne 1){'s'})"
               if ($nAdd -gt 0) { $part += " + $nAdd add-on$(if($nAdd -ne 1){'s'})" }
               "$part ($(Format-Size $p.Installer))"
           }
    if ($p.Extra -gt 0) { $txt += " + $(Format-Size $p.Extra) extra" }
    $key  = Get-SelectedMediaKey
    if ($key) {
        # A target disc turns the advice line into a plan: how many discs, of
        # what. The recommendation is no longer the interesting number once the
        # user has told DiscWright which discs are actually in the drawer.
        $pi   = Get-PlanInputs $p
        $plan = Get-DiscPlan $games $key $pi.Overhead $pi.FirstDiscExtra
        $lblGame.Text = "$txt   ->   $(Get-DiscPlanText $plan $key)"
        $lblGame.ForeColor = if($plan.Ok){[System.Drawing.Color]::Green}else{[System.Drawing.Color]::DarkOrange}
        Update-DiscWideLabels $plan
    } else {
        $lblGame.Text = "$txt   ->   Disc: $($m.Text)"
        $lblGame.ForeColor = if($m.Fit){[System.Drawing.Color]::Green}else{[System.Drawing.Color]::DarkOrange}
        Update-DiscWideLabels $null
    }
    Set-StatusTip $lblGame.Text
    # A missing installer part outranks the media advice - it is the thing that
    # makes the disc useless, so it takes the label and the colour.
    # With several games the label can only carry one warning, so take the first -
    # but prefer a missing installer part over a "picked the largest installer"
    # note, since only the first one makes the disc useless.
    $warned = @($games | Where-Object { $_.MissingParts.Count -gt 0 })
    if ($warned.Count -eq 0) { $warned = @($games | Where-Object { $_.Warning }) }
    if ($warned.Count -gt 0) {
        $w = $warned[0]
        $lblGame.Text = if ($games.Count -eq 1) { $w.Warning } else { "$($w.GameName): $($w.Warning)" }
        $lblGame.ForeColor = if($w.MissingParts.Count -gt 0){[System.Drawing.Color]::Firebrick}else{[System.Drawing.Color]::DarkOrange}
        Set-StatusTip $lblGame.Text
    }
}

# The advice line is clipped when it will not fit, so the whole of it has to be
# reachable somewhere. Guarded because the logic tests exercise Update-MediaLabel
# with stand-in controls and no tooltip provider.
function Set-StatusTip([string]$text) {
    if ($tips -and $lblGame -is [System.Windows.Forms.Control]) { $tips.SetToolTip($lblGame, $text) }
}

# The log box is for build progress. A click that cannot do its job needs an
# answer the user cannot miss, so those get a dialog.
function Show-Warn([string]$msg,[string]$title='DiscWright') {
    [void][System.Windows.Forms.MessageBox]::Show($msg,$title,
        [System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
}

function Show-Confirm([string]$msg,[string]$title='DiscWright') {
    return ([System.Windows.Forms.MessageBox]::Show($msg,$title,
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning) -eq [System.Windows.Forms.DialogResult]::Yes)
}

# A build that refuses to start is an error, and errors get a window - the log line
# alone is easy to miss, which makes the BUILD button look like it did nothing.
function Deny-Build([string]$logMsg,[string]$dlgMsg) { & $log "ERROR: $logMsg"; Show-Warn $dlgMsg }

$tips = New-Object System.Windows.Forms.ToolTip

# True when building now would overwrite the ISO this disc writes.
#
# It used to ask "is there ANY .iso in here", which is a different question and got
# a misleading answer: build one disc labelled ALAN WAKE and the next THE WITCHER
# into the same folder, and the button still read REBUILD ISO with a tooltip
# offering to "replace the ISO already in the output folder" - then it wrote a
# second file and left the first one untouched.
#
# The staging folder deliberately does not count. It is rebuilt from scratch by
# every build and holds nothing that is not also in the ISO, so warning about
# replacing it would be warning about nothing.
#
# DiscWright still never deletes an ISO it did not write. Two discs built into one
# folder means two files, which is correct - it cannot tell its own leftovers from
# something you put there. The button just has to stop claiming otherwise.
function Test-AlreadyBuilt([string]$outDir, [string]$label) {
    if ([string]::IsNullOrWhiteSpace($outDir) -or -not (Test-Path $outDir)) { return $false }
    $iso = Get-IsoPath $outDir $label
    if (-not $iso) { return $false }
    return (Test-Path -LiteralPath $iso -PathType Leaf)
}

# Whether anything on the form differs from how it opens, which is what decides
# if "New disc" has anything to do.
#
# The output folder is deliberately NOT part of this. New disc keeps it - if you
# are making three discs in an evening they all go to the same place, and clearing
# it is the one field you would have to retype every time. Counting it as dirty
# would leave the button enabled straight after a reset, so clicking it again
# would do nothing visible and look broken.
function Test-FormDirty {
    if (@($state.Games).Count) { return $true }
    foreach ($box in $txtLabel,$txtIcon,$txtBg,$txtMusic,$txtMan,$txtEx,$txtTitle) {
        if ($box.Text.Trim()) { return $true }
    }
    if ($lstExtra.Items.Count) { return $true }
    # Anything switched away from its opening position, in either direction.
    if (-not $chkMenu.Checked -or -not $chkWinBorder.Checked) { return $true }
    if ($chkBgAsIs.Checked -or $chkTitle.Checked -or $chkMusic.Checked -or $chkDivider.Checked) { return $true }
    if ($cmbSide.SelectedIndex -ne 0 -or $cmbBtnStyle.SelectedIndex -ne 0) { return $true }
    if ($cmbMedia.SelectedIndex -ne 0) { return $true }
    if ($chkXAll.Checked) { return $true }
    if (-not $cbPlay.Checked -or -not $cbInst.Checked -or -not $cbExit.Checked) { return $true }
    if ($cbMan.Checked -or $cbExtra.Checked) { return $true }
    return $false
}

# Grey out the two inspect buttons when there is nothing for them to open, and
# make the build button say what it will actually do.
function Update-ActionButtons {
    $t = $txtOut.Text.Trim()
    $hasOut  = $t -and (Test-Path $t)
    # Preview renders current settings, so it needs no build - only a menu and a background.
    $canPreview = $chkMenu.Checked -and $state.BgPath -and (Test-Path $state.BgPath)
    $isDirty = Test-FormDirty
    $btnOpenDisc.Enabled = [bool]$hasOut
    $btnPreview.Enabled  = [bool]$canPreview
    $btnNew.Enabled      = [bool]$isDirty
    $tips.SetToolTip($btnNew, $(if($isDirty){'Clear everything and start a new disc. The output folder is kept.'}else{'Nothing to clear - this is already a new disc'}))
    $tips.SetToolTip($btnOpenDisc, $(if($hasOut){'Open the disc staging folder in Explorer'}else{'Set an output folder first (step 6)'}))
    $tips.SetToolTip($btnPreview,  $(if($canPreview){'Show the menu using the current settings - no rebuild needed'}else{'Turn the menu on and choose a background first (step 4)'}))
    $tips.SetToolTip($btnOpenProj, 'Load a disc you already built, to edit and rebuild it')

    # The button names the file it is about to write, so REBUILD always means "this
    # exact file is going to be overwritten" rather than "there is something in that
    # folder somewhere".
    $isoNow = Get-IsoPath $t $txtLabel.Text.Trim()
    if (Test-AlreadyBuilt $t $txtLabel.Text.Trim()) {
        $btnBuild.Text = 'REBUILD ISO'
        $tips.SetToolTip($btnBuild, "Replace $(Split-Path $isoNow -Leaf) and rebuild the disc folder")
    } else {
        $btnBuild.Text = 'BUILD ISO'
        $tips.SetToolTip($btnBuild, $(if($isoNow){"Write $(Split-Path $isoNow -Leaf) and the disc folder"}else{'Build the disc folder and ISO'}))
    }

    # Divider and panel side are painted into bg.png by New-Background, which is
    # skipped when the background is used as-is - so they cannot do anything then.
    $composites = $chkMenu.Checked -and (-not $chkBgAsIs.Checked)
    $chkDivider.Enabled = $composites
    $cmbSide.Enabled    = $composites
    # Two different reasons these go grey - say which one actually applies, or the
    # tooltip sends the user off to untick "Use as-is" when the menu is just off.
    $asIsWhy = 'Baked into bg.png - to change it, pick the original artwork in step 4 and untick "Use as-is"'
    $menuWhy = 'The autorun menu is switched off - tick it at the top of step 4'
    $whyOff  = if (-not $chkMenu.Checked) { $menuWhy } else { $asIsWhy }
    $tips.SetToolTip($chkDivider, $(if($composites){'Draw a line where the button panel meets the artwork'}else{$whyOff}))
    $tips.SetToolTip($cmbSide,    $(if($composites){'Which side the button panel sits on'}else{$whyOff}))

    # The title is painted into bg.png too, so it follows the same rule - and the
    # text box only means anything once the title is switched on.
    $chkTitle.Enabled = $composites
    $lblTitle.Enabled = $composites
    $txtTitle.Enabled = $composites -and $chkTitle.Checked
    $tips.SetToolTip($chkTitle, $(if($composites){'Draw the title over the artwork. Off by default - most cover art already has the logo on it.'}else{$whyOff}))
    $tips.SetToolTip($txtTitle, $(if(-not $composites){$whyOff}
                                  elseif(-not $chkTitle.Checked){'Tick "Show title" first'}
                                  else{'Leave blank to use the disc label from step 2'}))

    # Nothing disc-wide to carry means nothing for this to decide about.
    $hasWide = ($cbMan.Checked -and $state.ManualPath) -or ($cbExtra.Checked -and $state.ExtrasPath) -or ($lstExtra.Items.Count -gt 0)
    $chkXAll.Enabled = [bool]$hasWide
    $tips.SetToolTip($chkXAll, $(if(-not $hasWide){'Add a manual, an Extras folder or some extra content first'}
                                 else{'Off: they go on disc 1 of a set. On: every disc carries its own copy, which costs their size per disc. A game''s own manual and extras always travel with that game either way.'}))
}

# Renders the CURRENT settings into a temp folder so look changes can be checked
# without a rebuild. Install/Manual/Extras are inert there - the game files are not
# in the preview folder - but Play still works, since it finds the game via the registry.
function New-PreviewMenu {
    if (-not $chkMenu.Checked) { Show-Warn "The autorun menu is switched off in step 4, so there is nothing to preview."; return $null }
    if (-not $state.BgPath -or -not (Test-Path $state.BgPath)) { Show-Warn "Choose a background image first (step 4)."; return $null }

    $btns=@(); if($cbPlay.Checked){$btns+='Play'}; if($cbInst.Checked){$btns+='Install'}
    if($cbMan.Checked){$btns+='Manual'}; if($cbExtra.Checked){$btns+='Extras'}; if($cbExit.Checked){$btns+='Exit'}
    if ($btns.Count -eq 0) { Show-Warn "Tick at least one menu button first (step 4)."; return $null }

    $prev = Join-Path ([IO.Path]::GetTempPath()) 'DiscWrightPreview'
    if (Test-Path $prev) { Remove-Item $prev -Recurse -Force -EA SilentlyContinue }
    New-Item -ItemType Directory -Force -Path "$prev\AUTORUN" | Out-Null

    $title = $txtLabel.Text.Trim()
    $first = Get-FirstGame
    if (-not $title -and $first) { $title = $first.GameName }
    if (-not $title) { $title = 'Preview' }
    # A custom title overrides the label; blank falls back to it.
    $bgTitle = $txtTitle.Text.Trim(); if (-not $bgTitle) { $bgTitle = $title }

    $bgOut = Join-Path $prev 'AUTORUN\bg.png'
    if ($chkBgAsIs.Checked) { Copy-Item $state.BgPath $bgOut -Force }
    else { New-Background $state.BgPath $bgTitle $bgOut ([string]$cmbSide.SelectedItem) ([bool]$chkDivider.Checked) ([bool]$chkTitle.Checked) }

    $musicName=''
    if ($chkMusic.Checked -and $state.MusicFile -and (Test-Path $state.MusicFile)) {
        $musicName = 'music'+[IO.Path]::GetExtension($state.MusicFile)
        Copy-Item $state.MusicFile "$prev\AUTORUN\$musicName" -Force
    }

    # The preview shows the chooser and the add-on buttons exactly as the disc
    # will, which is the point of previewing before a build that takes minutes.
    # With nothing picked yet there is still a menu to look at, so invent one
    # entry rather than render an empty panel.
    $prevGames = Get-MenuGames (Get-Games)
    if ($prevGames.Count -eq 0) {
        $prevGames = @(@{ Name=$title; MatchName=$title; Setup='setup.exe'; AddOns=@() })
    }
    # Stage the icon too, so the preview's taskbar icon matches the built disc.
    $prevIco = Get-DiscIconName $title
    if ($state.IconPath -and (Test-Path $state.IconPath) -and $state.IconIsIco) {
        Copy-Item $state.IconPath (Join-Path $prev "AUTORUN\$prevIco") -Force -EA SilentlyContinue
    } elseif ($state.IconPath -and (Test-Path $state.IconPath)) {
        try { Convert-ToIco $state.IconPath (Join-Path $prev "AUTORUN\$prevIco") } catch {}
    }
    New-MenuHta @{ GameName=$title; Games=$prevGames; Buttons=$btns
                   MusicFile=$musicName; ManualFile=''; PanelSide=[string]$cmbSide.SelectedItem; IconName=$prevIco
                   WindowBorder=$chkWinBorder.Checked; ButtonStyle=[string]$cmbBtnStyle.SelectedItem
                   Preview=$true } "$prev\AUTORUN\menu.hta"
    return "$prev\AUTORUN\menu.hta"
}

# Replace the whole list. Used when a project is opened; the entries carry the
# Kind and Parent read out of the file, so an add-on comes back as an add-on.
function Set-GameEntries([array]$entries) {
    $found = @()
    foreach ($e in @($entries)) {
        if (-not $e -or [string]::IsNullOrWhiteSpace($e.Folder)) { continue }
        # An add-on is identified by its installer, not its folder - the folder is
        # usually the parent game's. Fall back to the folder for anything written
        # before Setup was recorded.
        if ($e.Kind -eq 'AddOn' -and $e.Setup -and (Test-Path $e.Setup -PathType Leaf)) {
            $g = Get-AddOnInfo $e.Setup
        } else {
            $g = Get-GameInfo $e.Folder
        }
        if ($e.Kind -eq 'AddOn') { $g.Kind = 'AddOn'; $g.ParentIndex = [int]$e.ParentIndex }
        # A name the user edited is theirs, not something to re-derive on open.
        if (-not [string]::IsNullOrWhiteSpace($e.Name)) { $g.GameName = [string]$e.Name }
        # Only carried forward if the file is still where it was. A manual that
        # has been moved or deleted since the disc was built must not silently
        # become a menu button pointing at nothing.
        if ($e.Manual -and (Test-Path $e.Manual)) { $g.ManualPath = [string]$e.Manual }
        if ($e.Extras -and (Test-Path $e.Extras)) { $g.ExtrasPath = [string]$e.Extras }
        $found += ,$g
    }
    $state.Games = @($found)
    Update-GameList
    $bad = @($found | Where-Object { -not $_.Ok })
    if ($found.Count -and $bad.Count -eq 0) { Update-MediaLabel }
    elseif ($bad.Count) {
        $lblGame.Text = $bad[0].Msg
        $lblGame.ForeColor = [System.Drawing.Color]::Firebrick
    }
    # Comma for the same reason as Get-Games: one entry would otherwise come back
    # as a bare hashtable and the caller's $found[0] would hand back null.
    return ,@($found)
}

function Set-GameFolders([string[]]$paths) {
    $entries = @(@($paths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                 ForEach-Object { @{ Folder=$_; Kind='Game'; ParentIndex=-1 } })
    # Assign first, then return with the leading comma. `return (Set-GameEntries ...)`
    # looks equivalent and is not: returning a one-element array unrolls it, so a
    # single game came back as a bare hashtable and Set-GameFolder's $found[0]
    # handed its caller $null. Same trap as Get-Games and Get-FirstGame.
    $found = Set-GameEntries $entries
    return ,@($found)
}

# Callers that still deal in one folder - reopening a v1 project - go through
# here and get the single result back, as before.
function Set-GameFolder([string]$path) {
    $found = Set-GameFolders @($path)
    if ($found.Count -gt 0) { return $found[0] }
    return (Get-GameInfo $path)
}

# Add one folder to whatever is already on the disc. Unlike Set-GameFolders this
# rejects a folder with no installer in it rather than parking a broken row in
# the list: the list is a picture of the disc, and there is no disc entry for a
# folder that has nothing to install.
function Add-GameFolder([string]$path) {
    $g = Get-GameInfo $path
    if (-not $g.Ok) {
        $lblGame.Text = $g.Msg
        $lblGame.ForeColor = [System.Drawing.Color]::Firebrick
        return $null
    }
    # Compared on the installer, not the folder. A patch added as an add-on lives
    # in the same folder as the game it patches, so a folder comparison would
    # refuse to re-add the game after it had been removed from the list.
    foreach ($e in @($state.Games)) {
        if ($e.SetupExe -and (Test-SamePath $e.SetupExe.FullName $g.SetupExe.FullName)) {
            $lblGame.Text = "$($g.GameName) is already on this disc."
            $lblGame.ForeColor = [System.Drawing.Color]::DarkOrange
            return $null
        }
    }
    $state.Games = @(@($state.Games) + $g)
    Update-GameList
    Update-MediaLabel
    return $g
}

# Add-on installers, one or several at a time. Picked as files rather than a
# folder because a patch or a DLC usually sits in the same folder as the game it
# belongs to - pointing at the folder would just find the game again.
function Add-AddOnFiles([string[]]$paths,[int]$parentIndex) {
    $added = 0; $refused = @()
    foreach ($p in @($paths)) {
        $a = Get-AddOnInfo $p
        if (-not $a.Ok) { $refused += $a.Msg; continue }
        $dup = $false
        foreach ($e in @($state.Games)) {
            if ($e.SetupExe -and (Test-SamePath $e.SetupExe.FullName $a.SetupExe.FullName)) { $dup = $true; break }
        }
        if ($dup) { $refused += "$($a.SetupExe.Name) is already on this disc."; continue }
        $a.ParentIndex = $parentIndex
        $state.Games = @(@($state.Games) + $a)
        $added++
    }
    Update-GameList
    Update-MediaLabel
    if ($refused.Count) {
        $lblGame.Text = $(if ($refused.Count -eq 1) { $refused[0] } else { "$($refused.Count) files were not added - $($refused[0])" })
        $lblGame.ForeColor = [System.Drawing.Color]::DarkOrange
    }
    return $added
}

# Which game an add-on belongs to. Not asked when the answer cannot vary: with a
# single game on the disc there is nothing to choose.
function Show-ParentPickerDialog([string]$what) {
    $games = @($state.Games)
    $candidates = @()
    for ($i = 0; $i -lt $games.Count; $i++) {
        if ($games[$i].Kind -eq 'AddOn') { continue }
        $candidates += ,@{ Index=$i; Name=$games[$i].GameName }
    }
    if ($candidates.Count -eq 0) { return -1 }
    if ($candidates.Count -eq 1) { return [int]$candidates[0].Index }

    $dlg=New-Object System.Windows.Forms.Form
    $dlg.Text='Which game?'; $dlg.Font=$form.Font
    $dlg.FormBorderStyle='FixedDialog'; $dlg.MaximizeBox=$false; $dlg.MinimizeBox=$false
    $dlg.StartPosition='CenterParent'; $dlg.ShowInTaskbar=$false
    $dlg.ClientSize=New-Object System.Drawing.Size(420,132)

    $l=New-Object System.Windows.Forms.Label
    $l.Text="Which game does $what belong to?"
    $l.Location=New-Object System.Drawing.Point(15,15); $l.Size=New-Object System.Drawing.Size(390,36)
    $dlg.Controls.Add($l)

    $cmb=New-Object System.Windows.Forms.ComboBox
    $cmb.DropDownStyle='DropDownList'
    $cmb.Location=New-Object System.Drawing.Point(15,58); $cmb.Size=New-Object System.Drawing.Size(390,24)
    foreach ($c in $candidates) { [void]$cmb.Items.Add($c.Name) }
    $dlg.Controls.Add($cmb)

    $ok=New-Object System.Windows.Forms.Button; $ok.Text='OK'; $ok.Enabled=$false
    $ok.Location=New-Object System.Drawing.Point(235,96); $ok.Size=New-Object System.Drawing.Size(80,26)
    $ok.DialogResult='OK'; $dlg.Controls.Add($ok)
    $cancel=New-Object System.Windows.Forms.Button; $cancel.Text='Cancel'
    $cancel.Location=New-Object System.Drawing.Point(325,96); $cancel.Size=New-Object System.Drawing.Size(80,26)
    $cancel.DialogResult='Cancel'; $dlg.Controls.Add($cancel)
    $dlg.AcceptButton=$ok; $dlg.CancelButton=$cancel
    $cmb.Add_SelectedIndexChanged({ $ok.Enabled = ($cmb.SelectedIndex -ge 0) }.GetNewClosure())

    $r = $dlg.ShowDialog($form)
    $picked = -1
    if ($r -eq [System.Windows.Forms.DialogResult]::OK -and $cmb.SelectedIndex -ge 0) {
        $picked = [int]$candidates[$cmb.SelectedIndex].Index
    }
    $dlg.Dispose()
    return $picked
}

# Redraw the list from $state.Games. Called after anything that changes it, so
# the list never disagrees with what will be built.
function Update-GameList {
    $sel = -1
    if ($lvGames.SelectedIndices.Count) { $sel = $lvGames.SelectedIndices[0] }
    $games = @($state.Games)
    $lvGames.BeginUpdate()
    $lvGames.Items.Clear()
    for ($i = 0; $i -lt $games.Count; $i++) {
        $g = $games[$i]
        $name = if ($g.Ok -and $g.GameName) { $g.GameName } else { '(no installer found)' }
        $it = New-Object System.Windows.Forms.ListViewItem([string]($i+1))
        [void]$it.SubItems.Add($name)
        [void]$it.SubItems.Add($(if ($g.Kind -eq 'AddOn') { 'Add-on' } else { 'Game' }))
        $owner = ''
        if ($g.Kind -eq 'AddOn') {
            $p = [int]$g.ParentIndex
            $owner = if ($p -ge 0 -and $p -lt $games.Count) { $games[$p].GameName }
                     else { '(parent is gone - will be its own entry)' }
        }
        [void]$it.SubItems.Add($owner)
        if (-not $g.Ok) { $it.ForeColor = [System.Drawing.Color]::Firebrick }
        $it.ToolTipText = [string]$g.Folder
        [void]$lvGames.Items.Add($it)
    }
    $lvGames.EndUpdate()
    if ($sel -ge 0 -and $sel -lt $lvGames.Items.Count) { $lvGames.Items[$sel].Selected = $true }
    Update-GameButtons
}

# Nothing that cannot be done right now stays clickable.
function Update-GameButtons {
    $sel = ($lvGames.SelectedIndices.Count -gt 0)
    $btnGameDel.Enabled  = $sel
    # Change... offers "make this an add-on of ___", which needs something to be
    # an add-on of. With one entry on the disc there is no such thing.
    $btnGameEdit.Enabled = $sel -and (@($state.Games).Count -gt 1)
    # An add-on has to belong to a game, so there is nothing to add one to until
    # a game is on the disc.
    $btnAddOn.Enabled    = (@($state.Games | Where-Object { $_ -and $_.Kind -ne 'AddOn' }).Count -gt 0)
}

# Asks whether an entry is a game or an add-on, and of what. A window rather
# than two more controls on the main form: step 1 is already the tallest part of
# it, and this is answered once per installer, not adjusted while working.
function Show-EntryKindDialog([int]$index) {
    $games = @($state.Games)
    if ($index -lt 0 -or $index -ge $games.Count) { return $false }
    $me = $games[$index]

    # Only a plain game can be a parent: nothing can parent itself, and an add-on
    # of an add-on has nowhere sensible to appear in the menu.
    $candidates = @()
    for ($i = 0; $i -lt $games.Count; $i++) {
        if ($i -eq $index -or $games[$i].Kind -eq 'AddOn') { continue }
        $candidates += ,@{ Index=$i; Name=$games[$i].GameName }
    }

    $dlg=New-Object System.Windows.Forms.Form
    $dlg.Text='Entry on the disc'; $dlg.Font=$form.Font
    $dlg.FormBorderStyle='FixedDialog'; $dlg.MaximizeBox=$false; $dlg.MinimizeBox=$false
    $dlg.StartPosition='CenterParent'; $dlg.ShowInTaskbar=$false
    $dlg.ClientSize=New-Object System.Drawing.Size(480,292)

    $l=New-Object System.Windows.Forms.Label
    $l.Text='How should this installer appear on the disc?'
    $l.Location=New-Object System.Drawing.Point(15,15); $l.Size=New-Object System.Drawing.Size(450,20)
    $dlg.Controls.Add($l)

    # Editable, because the derived name is a guess. A GOG patch reports the
    # ProductName of the game it patches, so its name comes from the filename and
    # reads like a filename. This is also the name of its folder on the disc.
    $lblName=New-Object System.Windows.Forms.Label
    $lblName.Text='Name on the menu:'
    $lblName.Location=New-Object System.Drawing.Point(20,48); $lblName.Size=New-Object System.Drawing.Size(115,20)
    $dlg.Controls.Add($lblName)
    $txtName=New-Object System.Windows.Forms.TextBox
    $txtName.Location=New-Object System.Drawing.Point(140,46); $txtName.Size=New-Object System.Drawing.Size(320,24)
    $txtName.Text=[string]$me.GameName
    $dlg.Controls.Add($txtName)

    $rbGame=New-Object System.Windows.Forms.RadioButton
    $rbGame.Text='A game of its own'
    $rbGame.Location=New-Object System.Drawing.Point(20,85); $rbGame.Size=New-Object System.Drawing.Size(440,22)
    $dlg.Controls.Add($rbGame)

    $rbAdd=New-Object System.Windows.Forms.RadioButton
    $rbAdd.Text='An add-on (DLC, expansion, patch, mod) for:'
    $rbAdd.Location=New-Object System.Drawing.Point(20,113); $rbAdd.Size=New-Object System.Drawing.Size(440,22)
    $dlg.Controls.Add($rbAdd)

    $cmb=New-Object System.Windows.Forms.ComboBox
    $cmb.DropDownStyle='DropDownList'
    $cmb.Location=New-Object System.Drawing.Point(40,139); $cmb.Size=New-Object System.Drawing.Size(420,24)
    foreach ($c in $candidates) { [void]$cmb.Items.Add($c.Name) }
    $dlg.Controls.Add($cmb)

    # This entry's own manual and extras. Blank means it has none, and the menu
    # falls back to the disc-wide ones from step 4 - which is what every disc
    # built before this did for every game on it.
    $lblMan=New-Object System.Windows.Forms.Label
    $lblMan.Text='Its own manual:'
    $lblMan.Location=New-Object System.Drawing.Point(20,175); $lblMan.Size=New-Object System.Drawing.Size(115,20)
    $dlg.Controls.Add($lblMan)
    $txtMan=New-Object System.Windows.Forms.TextBox
    $txtMan.Location=New-Object System.Drawing.Point(140,173); $txtMan.Size=New-Object System.Drawing.Size(200,24)
    $txtMan.Text=[string]$me.ManualPath
    $dlg.Controls.Add($txtMan)
    $btnMan=New-Object System.Windows.Forms.Button; $btnMan.Text='Browse...'
    $btnMan.Location=New-Object System.Drawing.Point(348,172); $btnMan.Size=New-Object System.Drawing.Size(100,24)
    $dlg.Controls.Add($btnMan)

    $lblExt=New-Object System.Windows.Forms.Label
    $lblExt.Text='Its own extras:'
    $lblExt.Location=New-Object System.Drawing.Point(20,205); $lblExt.Size=New-Object System.Drawing.Size(115,20)
    $dlg.Controls.Add($lblExt)
    $txtExt=New-Object System.Windows.Forms.TextBox
    $txtExt.Location=New-Object System.Drawing.Point(140,203); $txtExt.Size=New-Object System.Drawing.Size(200,24)
    $txtExt.Text=[string]$me.ExtrasPath
    $dlg.Controls.Add($txtExt)
    $btnExt=New-Object System.Windows.Forms.Button; $btnExt.Text='Browse...'
    $btnExt.Location=New-Object System.Drawing.Point(348,202); $btnExt.Size=New-Object System.Drawing.Size(100,24)
    $dlg.Controls.Add($btnExt)

    $btnMan.Add_Click({
        # This entry's own folder before the shared fallback, for the same reason as
        # its extras below.
        $cur = $txtMan.Text.Trim()
        if (-not $cur) { $cur = [string]$me.Folder }
        $d=New-FileDialog "Pick the manual for this game" 'Manuals|*.pdf;*.txt;*.htm;*.html;*.rtf;*.doc;*.docx|All files|*.*' $cur
        if ($txtMan.Text -and (Test-Path $txtMan.Text)) { $d.FileName = $txtMan.Text }
        if ($d.ShowDialog() -eq 'OK') { Set-LastFileBrowse $d.FileName; $txtMan.Text = $d.FileName }
        $d.Dispose()
    }.GetNewClosure())
    $btnExt.Add_Click({
        # This entry's own folder before the shared fallback: a game's extras were
        # downloaded beside that game, not beside whichever one was touched last.
        $start = $txtExt.Text.Trim()
        if (-not $start) { $start = Get-ExistingFolderOf ([string]$me.Folder) }
        if (-not $start) { $start = Get-MediaBrowseFallback }
        $d=New-FolderDialog 'Pick a folder of extras for this game' $start
        if ($d.ShowDialog() -eq 'OK') { $txtExt.Text = $d.SelectedPath }
    }.GetNewClosure())

    $note=New-Object System.Windows.Forms.Label
    $note.ForeColor=[System.Drawing.Color]::DimGray
    $note.Location=New-Object System.Drawing.Point(20,233); $note.Size=New-Object System.Drawing.Size(440,20)
    $dlg.Controls.Add($note)

    $ok=New-Object System.Windows.Forms.Button; $ok.Text='OK'
    $ok.Location=New-Object System.Drawing.Point(295,258); $ok.Size=New-Object System.Drawing.Size(80,26)
    $ok.DialogResult='OK'; $dlg.Controls.Add($ok)
    $cancel=New-Object System.Windows.Forms.Button; $cancel.Text='Cancel'
    $cancel.Location=New-Object System.Drawing.Point(385,258); $cancel.Size=New-Object System.Drawing.Size(80,26)
    $cancel.DialogResult='Cancel'; $dlg.Controls.Add($cancel)
    $dlg.AcceptButton=$ok; $dlg.CancelButton=$cancel

    # The parent list is dead until "add-on" is chosen, and OK is dead until a
    # parent is actually picked - so the dialog cannot produce an add-on of
    # nothing, which Get-MenuGames would then quietly promote back to a game.
    $sync = {
        $cmb.Enabled = $rbAdd.Checked
        $ok.Enabled  = ((-not $rbAdd.Checked) -or ($cmb.SelectedIndex -ge 0)) -and
                       (-not [string]::IsNullOrWhiteSpace($txtName.Text))
    }.GetNewClosure()
    $rbGame.Add_CheckedChanged($sync); $rbAdd.Add_CheckedChanged($sync)
    $cmb.Add_SelectedIndexChanged($sync); $txtName.Add_TextChanged($sync)

    if ($candidates.Count -eq 0) {
        $rbAdd.Enabled = $false
        $note.Text = 'Add another game first - an add-on has to belong to one.'
    }
    if ($me.Kind -eq 'AddOn') {
        $rbAdd.Checked = $true
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            if ([int]$candidates[$i].Index -eq [int]$me.ParentIndex) { $cmb.SelectedIndex = $i }
        }
    } else { $rbGame.Checked = $true }
    & $sync

    $r = $dlg.ShowDialog($form)
    $changed = $false
    if ($r -eq [System.Windows.Forms.DialogResult]::OK) {
        if ($rbAdd.Checked -and $cmb.SelectedIndex -ge 0) {
            $me.Kind = 'AddOn'; $me.ParentIndex = [int]$candidates[$cmb.SelectedIndex].Index
        } else {
            $me.Kind = 'Game'; $me.ParentIndex = -1
        }
        $n = $txtName.Text.Trim()
        if ($n) { $me.GameName = $n }
        # Emptying a box clears it, which is how an entry goes back to using the
        # disc-wide manual or extras. A path that no longer exists is refused
        # rather than stored, so a build cannot be set up to point at nothing.
        $mp = $txtMan.Text.Trim()
        $me.ManualPath = $(if ($mp -and (Test-Path $mp -PathType Leaf)) { $mp } else { $null })
        $xp = $txtExt.Text.Trim()
        $me.ExtrasPath = $(if ($xp -and (Test-Path $xp -PathType Container)) { $xp } else { $null })
        $changed = $true
    }
    $dlg.Dispose()
    return $changed
}

function Add-ExtraItem([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) { return }
    $full = (Resolve-Path -LiteralPath $path).Path
    $nm = [IO.Path]::GetFileName($full.TrimEnd('\'))
    if (Test-ReservedDiscName $nm (Get-DiscIconName $txtLabel.Text.Trim())) { & $log "Cannot add '$nm' - that name is reserved for the disc's own files."; return }
    foreach ($e in $lstExtra.Items) { if (Test-SamePath $e $full) { return } }
    [void]$lstExtra.Items.Add($full)
    $state.ExtraItems = @($lstExtra.Items)
    Update-MediaLabel
    # A ListBox raises nothing when its Items collection changes, so the two places
    # that change it say so themselves - otherwise a disc holding nothing but extra
    # content would leave "New disc" greyed out with plenty to clear.
    Update-ActionButtons
}

function Set-IconFile([string]$path) {
    $txtIcon.Text=$path; $state.IconPath=$path
    $r=Test-IconInput $path; $state.IconIsIco=$r.IsIco
    $lblIcon.Text=$r.Msg; $lblIcon.ForeColor= if($r.Ok){[System.Drawing.Color]::Green}else{[System.Drawing.Color]::Firebrick}
    # Load through a MemoryStream: Image.FromFile keeps the file open, which would
    # block wiping the staging folder when the icon is picked from inside it.
    if ($picIcon.Image) { $old=$picIcon.Image; $picIcon.Image=$null; $old.Dispose() }
    try { $ms=New-Object System.IO.MemoryStream(,[System.IO.File]::ReadAllBytes($path)); $picIcon.Image=[System.Drawing.Image]::FromStream($ms) } catch { $picIcon.Image=$null }
}

# A background that is already exactly 760x480 is almost certainly one we composed
# earlier - re-compositing would double the darkening and stamp the title twice.
function Test-ComposedBg([string]$path) {
    try { $i=[System.Drawing.Image]::FromFile($path); $ok=($i.Width -eq 760 -and $i.Height -eq 480); $i.Dispose(); return $ok } catch { return $false }
}

function Set-BgFile([string]$path) {
    # Refuse an unusable image here rather than let it fail during the build. Silent
    # during a project load - reopening an old disc whose artwork has since moved
    # should not throw a dialog at the user before they have done anything.
    $chk = Test-BgInput $path
    if (-not $chk.Ok) {
        if (-not $state.Loading) { Show-Warn ("This background cannot be used:`r`n`r`n$path`r`n`r`n$($chk.Msg)") }
        $txtBg.Text=''; $state.BgPath=$null
        Update-ActionButtons
        return
    }
    $txtBg.Text=$path; $state.BgPath=$path
    # Set BOTH ways: picking a fresh source must clear a previously-ticked as-is,
    # or the divider and panel side stay greyed out with no way to reach them.
    $chkBgAsIs.Checked = [bool](Test-ComposedBg $path)
    Update-ActionButtons
}

# Put every control back where it opens. The counterpart to Open-Project: the app
# has always had a way to load a disc and no way to start a fresh one short of
# closing and reopening it, which is a real gap once you are making more than one
# disc in a sitting.
#
# The values here are the control defaults from the UI section above, repeated
# rather than read back from the controls - there is nowhere else to read them
# from, since a WinForms control does not remember what it was constructed with.
# If a default changes up there and not here, the window test that a reset form
# matches a freshly opened one is what says so.
#
# Nothing on disk is touched. A disc already built stays built; this clears the
# form, not the output.
function Reset-Form {
    $state.Loading = $true   # same reason as Open-Project: no dialogs mid-reset

    $null = Set-GameEntries @()
    $lblGame.Text = ''; $lblGame.ForeColor = [System.Drawing.Color]::DimGray
    $cmbMedia.SelectedIndex = 0
    $chkXAll.Checked = $false

    # LastGameBrowse and LastFileBrowse are deliberately NOT reset - see where
    # $state is declared.
    $txtLabel.Clear(); $state.LabelSeededFrom = $null

    $txtIcon.Clear(); $state.IconPath = $null; $state.IconIsIco = $false
    $lblIcon.Text = ''; $lblIcon.ForeColor = [System.Drawing.Color]::DimGray
    # Disposed the same way Set-IconFile does it - the preview holds the bitmap
    # open, and a reset that leaked one per disc would add up over an evening.
    if ($picIcon.Image) { $old = $picIcon.Image; $picIcon.Image = $null; $old.Dispose() }

    $chkMenu.Checked = $true
    $txtBg.Clear(); $state.BgPath = $null
    $chkBgAsIs.Checked = $false
    $cmbSide.SelectedIndex = 0
    $chkTitle.Checked = $false; $txtTitle.Clear()
    $chkMusic.Checked = $false; $txtMusic.Clear(); $state.MusicFile = $null
    $cbPlay.Checked = $true; $cbInst.Checked = $true; $cbExit.Checked = $true
    $cbMan.Checked = $false; $cbExtra.Checked = $false
    $txtMan.Clear(); $state.ManualPath = $null
    $txtEx.Clear();  $state.ExtrasPath = $null
    $chkDivider.Checked = $false
    $chkWinBorder.Checked = $true
    $cmbBtnStyle.SelectedIndex = 0

    $lstExtra.Items.Clear(); $state.ExtraItems = @()

    $state.Loading = $false
    $txtLog.Clear()
    # Same first line a fresh window writes, so a pasted log still says which
    # version and which PowerShell produced it.
    & $log "DiscWright $APP_VERSION  -  Windows PowerShell $($PSVersionTable.PSVersion)"
    if ($txtOut.Text.Trim()) { & $log "New disc. The output folder was kept." }
    else { & $log "New disc." }
    Update-ActionButtons
}

function Open-Project([string]$folder) {
    $txtLog.Clear()
    $state.Loading = $true   # keep control handlers from popping dialogs mid-load
    # accept either the output folder or the disc folder itself
    $disc = $folder
    $proj = Join-Path $folder $PROJECT_FILE
    if (-not (Test-Path (Join-Path $disc 'autorun.inf'))) { $disc = Join-Path $folder 'disc' }
    if (-not (Test-Path $proj) -and (Test-Path (Join-Path (Split-Path $folder -Parent) $PROJECT_FILE))) { $proj = Join-Path (Split-Path $folder -Parent) $PROJECT_FILE }

    $p = $null
    if (Test-Path $proj) { $p = Import-Project $proj }
    if (-not $p -and (Test-Path (Join-Path $disc 'autorun.inf'))) { $p = Import-DiscFolder $disc }
    if (-not $p) {
        $state.Loading = $false
        & $log "ERROR: no $PROJECT_FILE and no disc\autorun.inf under that folder."
        Show-Warn "That folder does not look like a disc project.`r`n`r`nExpected either a $PROJECT_FILE, or a 'disc' folder containing autorun.inf, in:`r`n`r`n$folder"
        return
    }

    & $log "Loaded from $($p.Origin)."

    # Import-Project has already folded v1's single SourceFolder, v2's Games array
    # and v3's Kind/Parent into one list of entries, so only that list is read here.
    $entries = @(@($p.GameEntries) | Where-Object { $_ -and $_.Folder })
    if ($entries.Count -eq 0 -and $p.SourceFolder) {
        $entries = @(@{ Folder=[string]$p.SourceFolder; Kind='Game'; ParentIndex=-1 })
    }
    $gone = @($entries | Where-Object { -not (Test-Path $_.Folder) })
    if ($entries.Count -eq 0 -or $gone.Count -eq $entries.Count) {
        & $log "  original GOG folder is gone - using the disc folder itself (rebuild in place)."
        $entries = @(@{ Folder=$disc; Kind='Game'; ParentIndex=-1 })
    } elseif ($gone.Count -gt 0) {
        & $log "  $($gone.Count) of $($entries.Count) game folders are gone - leaving those out."
        # Not a Where-Object filter: parents are stored as positions, so dropping
        # an entry renumbers everything after it and an add-on would end up
        # attached to whichever game slid into the gap. Remove-GameEntry is the
        # one place that knows to renumber, so it does the dropping. Backwards,
        # because each removal shifts the indices still to be checked.
        for ($i = $entries.Count - 1; $i -ge 0; $i--) {
            # No @() - see the note on the Remove button's handler.
            if (-not (Test-Path $entries[$i].Folder)) { $entries = Remove-GameEntry $entries $i }
        }
    }
    foreach ($g in (Set-GameEntries $entries)) { if (-not $g.Ok) { & $log "  WARNING: $($g.Msg)" } }

    # A label out of a project file is the user's, whatever it says - so no seed
    # marker. Removing the last game must not take it away.
    #
    # Cleaned on the way in as well as on the way out, so the box shows the label
    # that will actually be built. The writers strip control characters regardless;
    # doing it here too means a poisoned project file cannot present one thing in
    # the window and put another on the disc.
    $txtLabel.Text = Remove-ControlChars $p.Label; $state.LabelSeededFrom = $null
    # A project written before target discs existed has no MediaKey, and a key
    # this build does not recognise is not worth guessing at either: both fall
    # back to the automatic setting rather than silently planning a set.
    #
    # ContainsKey, not PSObject.Properties: Import-Project hands back a HASHTABLE.
    # A hashtable's keys are not PSObject properties, so the property test read
    # false for every project ever saved and the target disc was silently dropped
    # on reopen. Found by hand-testing the round trip, which no test covered.
    $mk = $(if ($p.ContainsKey('MediaKey')) { [string]$p.MediaKey } else { '' })
    if ((Get-MediaCapacity $mk) -le 0) { $mk = '' }
    if ($p.IconPath -and (Test-Path $p.IconPath)) { Set-IconFile $p.IconPath } else { & $log "  icon missing - pick one again." }

    $chkMenu.Checked = $p.Menu
    if ($p.BgPath -and (Test-Path $p.BgPath)) { Set-BgFile $p.BgPath } else { $txtBg.Text=''; $state.BgPath=$null }
    $chkBgAsIs.Checked = [bool]$p.BgAsIs
    $cmbSide.SelectedItem = $(if($p.PanelSide -ieq 'Left'){'Left'}else{'Right'})
    $chkDivider.Checked   = [bool]$p.Divider
    $chkTitle.Checked     = [bool]$p.ShowTitle
    $txtTitle.Text        = [string]$p.TitleText
    $chkWinBorder.Checked = [bool]$p.WindowBorder
    $cmbBtnStyle.SelectedItem = $(if($p.ButtonStyle -ieq 'Minimal'){'Minimal'}else{'Bordered'})

    $hasMusic = ($p.MusicFile -and (Test-Path $p.MusicFile))
    $chkMusic.Checked = $hasMusic
    if ($hasMusic) { $txtMusic.Text=$p.MusicFile; $state.MusicFile=$p.MusicFile } else { $txtMusic.Text=''; $state.MusicFile=$null }

    $b = @($p.Buttons)
    $cbPlay.Checked  = ($b -contains 'Play')
    $cbInst.Checked  = ($b -contains 'Install')
    $cbMan.Checked   = ($b -contains 'Manual')
    $cbExtra.Checked = ($b -contains 'Extras')
    $cbExit.Checked  = ($b -contains 'Exit')

    if ($p.ManualPath -and (Test-Path $p.ManualPath)) { $txtMan.Text=$p.ManualPath; $state.ManualPath=$p.ManualPath } else { $txtMan.Text=''; $state.ManualPath=$null }
    if ($p.ExtrasPath -and (Test-Path $p.ExtrasPath)) { $txtEx.Text=$p.ExtrasPath; $state.ExtrasPath=$p.ExtrasPath } else { $txtEx.Text=''; $state.ExtrasPath=$null }

    $lstExtra.Items.Clear(); $state.ExtraItems=@()
    foreach ($it in @($p.ExtraItems)) {
        if ($it -and (Test-Path $it)) { Add-ExtraItem $it }
        elseif ($it) { & $log "  extra content missing, dropped: $it" }
    }

    $out = $p.OutDir; if (-not $out -or -not (Test-Path $out)) { $out = Split-Path $disc -Parent }
    $txtOut.Text = $out

    $chkXAll.Checked = $(if ($p.ContainsKey('ExtrasEveryDisc')) { [bool]$p.ExtrasEveryDisc } else { $false })
    # Last, not beside the label: the rows are annotated from the entries, so the
    # list this has to find its medium in does not exist until they are loaded.
    Update-MediaOptions
    Select-MediaKey $mk
    Update-MediaLabel

    $state.Loading = $false
    Update-ActionButtons

    if ($chkMenu.Checked -and $state.BgPath -and $chkBgAsIs.Checked -and (Test-ComposedBg $state.BgPath)) {
        & $log "NOTE: the background is the already-composed bg.png, so Divider and Buttons-side are greyed out."
        & $log "      To change them, pick the ORIGINAL artwork in step 4 and untick 'Use as-is'."
    }
    & $log "Ready. Edit anything above, then BUILD ISO to rewrite the disc + image."
    if (Test-SamePath $src $disc) { & $log "In-place mode: the game files already sit in the staging folder and will not be re-copied." }
}

# ---- events ----
$btnNew.Add_Click({
    # Asked before, not undone after: everything on the form goes, and there is no
    # undo. The button is greyed out when there is nothing to lose, so reaching
    # this dialog always means something would actually be discarded.
    $msg = "Clear everything and start a new disc?" + "`r`n`r`n" +
           "The installer list, disc label, icon, menu settings and extra content are all reset." + "`r`n`r`n" +
           "Nothing on disk is touched - a disc you have already built stays where it is, and the output folder is kept."
    if (-not (Show-Confirm $msg 'New disc')) { return }
    Reset-Form
})
$btnOpenProj.Add_Click({
    $d = New-FolderDialog 'Pick the disc output folder (the one holding disc\ and the .iso)' $txtOut.Text.Trim()
    if ($d.ShowDialog() -eq 'OK') { Open-Project $d.SelectedPath }
})
$btnOpenDisc.Add_Click({
    $t=$txtOut.Text.Trim()
    if (-not $t) {
        Show-Warn "No output folder is set yet.`r`n`r`nPick one in step 6, or use 'Open existing disc...' to load a disc you already built."
        return
    }
    if (-not (Test-Path $t)) { Show-Warn "That output folder does not exist yet:`r`n`r`n$t"; return }
    $d = Join-Path $t 'disc'
    if (Test-Path $d) { Start-Process explorer.exe $d; return }
    # Do not quietly open something else than what the button promises.
    Show-Warn "Nothing has been built here yet - there is no 'disc' staging folder in:`r`n`r`n$t`r`n`r`nOpening the output folder instead."
    Start-Process explorer.exe $t
})
$btnPreview.Add_Click({
    $h = New-PreviewMenu
    if (-not $h) { return }
    & $log 'Preview built from the current settings (Install/Manual do nothing in preview).'
    Start-Process mshta.exe "`"$h`""
})
$txtOut.Add_TextChanged({ Update-ActionButtons })
$btnFolder.Add_Click({
    # Open where the last one came from: several games on a disc usually come
    # from sibling folders in the same download directory.
    # Beside the last game added, else wherever a game was last picked from - which
    # survives New disc and survives removing every entry, because where the GOG
    # downloads live does not change when you start a second disc. Only when neither
    # exists does it fall back to guessing.
    $start = ''
    $last = @($state.Games) | Select-Object -Last 1
    if     ($last -and $last.Folder)  { $start = Split-Path $last.Folder -Parent }
    elseif ($state.LastGameBrowse)    { $start = $state.LastGameBrowse }
    else                              { $start = Get-DefaultGameBrowseFolder }
    $d = New-FolderDialog 'Pick a folder you downloaded from GOG (it holds setup_*.exe)' $start
    if ($d.ShowDialog() -eq 'OK') {
        $state.LastGameBrowse = Split-Path $d.SelectedPath -Parent
        $g = Add-GameFolder $d.SelectedPath
        # Label and output folder are seeded from the first game only. Changing
        # them on the second would rename a disc the user had already named.
        if ($g -and @($state.Games).Count -eq 1) {
            if ([string]::IsNullOrWhiteSpace($txtLabel.Text)) { $txtLabel.Text=$g.GameName; $state.LabelSeededFrom=$g.GameName }
            if ([string]::IsNullOrWhiteSpace($txtOut.Text)) { $txtOut.Text=Join-Path ([Environment]::GetFolderPath('Desktop')) ((($g.GameName -replace '[^A-Za-z0-9_\- ]','_').Trim())+' Disc') }
        }
        Update-ActionButtons
    }
})
$btnAddOn.Add_Click({
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Title = 'Pick one or more add-on installers - DLC, an expansion, a patch, a mod'
    # Any .exe, not just setup_*.exe. GOG patches are named patch_*, and a mod
    # installer is named whatever its author chose - refusing those is what kept
    # the two things people actually own off the disc.
    $d.Filter = 'Installers (*.exe)|*.exe|All files|*.*'
    $d.Multiselect = $true
    $first = Get-FirstGame
    if ($first -and $first.Folder -and (Test-Path $first.Folder)) { $d.InitialDirectory = $first.Folder }
    if ($d.ShowDialog() -eq 'OK') {
        $names = @($d.FileNames | ForEach-Object { [IO.Path]::GetFileName($_) })
        $what = if ($names.Count -eq 1) { "`"$($names[0])`"" } else { "these $($names.Count) installers" }
        $parent = Show-ParentPickerDialog $what
        if ($parent -ge 0) {
            $n = Add-AddOnFiles @($d.FileNames) $parent
            if ($n) { & $log "Added $n add-on installer(s) to $($state.Games[$parent].GameName)." }
            Update-ActionButtons
        }
    }
    $d.Dispose()
})
$btnGameDel.Add_Click({
    if (-not $lvGames.SelectedIndices.Count) { return }
    # NOT @(Remove-GameEntry ...). It already returns a list, and wrapping it
    # again gives a one-element array holding that list - so removing one of five
    # entries left a single row whose name was all four remaining names run
    # together. Same trap as Get-Games and Get-FirstGame.
    $state.Games = Remove-GameEntry @($state.Games) $lvGames.SelectedIndices[0]
    # Take back the label DiscWright typed, but only that one. Adding a game to an
    # empty form seeds the label from its name; removing that game used to leave
    # the name behind, and because seeding only fires into an EMPTY box, the next
    # game added never replaced it - so a disc built after swapping the game out
    # carried the previous game's name. Compared against what was seeded rather
    # than against the game just removed, so a label the user typed survives even
    # if they typed the game's own name.
    if (@($state.Games).Count -eq 0 -and (Test-LabelIsSeeded $txtLabel.Text $state.LabelSeededFrom)) {
        $txtLabel.Clear(); $state.LabelSeededFrom = $null
    }
    Update-GameList; Update-MediaLabel; Update-ActionButtons
})
$btnGameEdit.Add_Click({
    if (-not $lvGames.SelectedIndices.Count) { return }
    if (Show-EntryKindDialog $lvGames.SelectedIndices[0]) {
        Update-GameList; Update-MediaLabel; Update-ActionButtons
    }
})
$lvGames.Add_SelectedIndexChanged({ Update-GameButtons })
$lvGames.Add_DoubleClick({ if ($btnGameEdit.Enabled) { $btnGameEdit.PerformClick() } })
$btnIcon.Add_Click({
    $d=New-FileDialog 'Pick the disc icon' 'Images|*.ico;*.png;*.jpg;*.jpeg;*.bmp' $txtIcon.Text
    if ($d.ShowDialog() -eq 'OK') { Set-LastFileBrowse $d.FileName; Set-IconFile $d.FileName }
})
$btnBg.Add_Click({ $d=New-FileDialog 'Pick the menu background' 'Images|*.png;*.jpg;*.jpeg;*.bmp' $txtBg.Text; if($d.ShowDialog() -eq 'OK'){ Set-LastFileBrowse $d.FileName; Set-BgFile $d.FileName } })
$btnMusic.Add_Click({ $d=New-FileDialog 'Pick the background music' 'Audio|*.mp3;*.wav;*.wma' $txtMusic.Text; if($d.ShowDialog() -eq 'OK'){ Set-LastFileBrowse $d.FileName; $txtMusic.Text=$d.FileName; $state.MusicFile=$d.FileName } })
# Manuals are usually PDF but plenty ship as text or HTML, and the menu just
# shell-executes whatever it is - nothing in the pipeline needs it to be a PDF.
$btnMan.Add_Click({ $d=New-FileDialog 'Pick the manual for this disc' 'Manuals|*.pdf;*.txt;*.htm;*.html;*.rtf;*.doc;*.docx|All files|*.*' $txtMan.Text; if($d.ShowDialog() -eq 'OK'){ Set-LastFileBrowse $d.FileName; $txtMan.Text=$d.FileName; $state.ManualPath=$d.FileName; Update-MediaLabel } })
$btnEx.Add_Click({
    # Folder pickers READ the shared fallback but never write it. Only a file pick
    # updates it, so choosing an extras folder cannot quietly move where the icon
    # picker opens next.
    $start = $txtEx.Text.Trim(); if (-not $start) { $start = Get-MediaBrowseFallback }
    $d=New-FolderDialog 'Pick a folder of extras to put on the disc' $start
    if($d.ShowDialog() -eq 'OK'){ $txtEx.Text=$d.SelectedPath; $state.ExtrasPath=$d.SelectedPath; Update-MediaLabel } })
$btnOut.Add_Click({ $d=New-FolderDialog 'Pick where the disc folder and the ISO get written' $txtOut.Text.Trim(); if($d.ShowDialog() -eq 'OK'){ $txtOut.Text=$d.SelectedPath } })
$btnXFile.Add_Click({
    $d=New-FileDialog 'Pick any files to put on the disc' 'All files|*.*' ''
    $d.Multiselect=$true
    if($d.ShowDialog() -eq 'OK'){ Set-LastFileBrowse $d.FileNames[0]; foreach($fn in $d.FileNames){ Add-ExtraItem $fn } }
})
$btnXDir.Add_Click({
    $first = Get-FirstGame
    $d = New-FolderDialog 'Pick a folder to put on the disc' $(if ($first) { [string]$first.Folder } else { '' })
    if($d.ShowDialog() -eq 'OK'){ Add-ExtraItem $d.SelectedPath }
})
$btnXDel.Add_Click({
    foreach($i in @($lstExtra.SelectedIndices | Sort-Object -Descending)){ $lstExtra.Items.RemoveAt($i) }
    $state.ExtraItems = @($lstExtra.Items); Update-MediaLabel; Update-ActionButtons
})
$chkMusic.Add_CheckedChanged({ $txtMusic.Enabled=$chkMusic.Checked; $btnMusic.Enabled=$chkMusic.Checked; Update-MediaLabel })
$cbMan.Add_CheckedChanged({ $txtMan.Enabled=$cbMan.Checked; $btnMan.Enabled=$cbMan.Checked; Update-MediaLabel })
$cbExtra.Add_CheckedChanged({ $txtEx.Enabled=$cbExtra.Checked; $btnEx.Enabled=$cbExtra.Checked; Update-MediaLabel })
$chkBgAsIs.Add_CheckedChanged({
    # Unticking this on a background we already composed would re-darken it and
    # stamp the title a second time, while the old divider stays painted in.
    if (-not $state.Loading -and -not $chkBgAsIs.Checked -and $state.BgPath -and (Test-ComposedBg $state.BgPath)) {
        Show-Warn ("This background is already 760x480, so it is most likely one this app composed earlier." +
                   "`r`n`r`nRecompositing it would darken it a second time, draw the title twice, and any divider line already painted into it would stay." +
                   "`r`n`r`nPick the original artwork in step 4 instead, then the divider and panel side will work.")
    }
    Update-ActionButtons
})
$chkTitle.Add_CheckedChanged({
    # Ticking it with an empty box: seed the disc label so there is something to
    # see and edit, rather than an empty field the user has to guess at.
    if ($chkTitle.Checked -and -not $txtTitle.Text.Trim()) { $txtTitle.Text = $txtLabel.Text.Trim() }
    Update-ActionButtons
})
$chkMenu.Add_CheckedChanged({
    foreach($c in @($lblBg,$txtBg,$btnBg,$chkBgAsIs,$lblSide,$cmbSide,$lblTitle,$chkTitle,$txtTitle,$chkMusic,$lblBtns,$cbPlay,$cbInst,$cbMan,$cbExtra,$cbExit,$lblStyle,$chkDivider,$chkWinBorder,$lblBtnStyle,$cmbBtnStyle)){ $c.Enabled=$chkMenu.Checked }
    Update-ActionButtons   # re-applies the as-is rules on top
})

$btnBuild.Add_Click({
    $txtLog.Clear()
    if ((Get-Games).Count -eq 0) { Deny-Build 'no valid GOG game folder.' 'Pick a valid GOG game folder first (step 1).'; return }
    if ([string]::IsNullOrWhiteSpace($txtLabel.Text)) { Deny-Build 'no disc label.' 'Enter a disc label (step 2). It is the name This PC shows for the disc.'; return }
    if (-not $state.IconPath) { Deny-Build 'no disc icon.' 'Choose a disc icon (step 3).'; return }
    if ([string]::IsNullOrWhiteSpace($txtOut.Text)) { Deny-Build 'no output folder.' 'Choose an output folder (step 6). The disc folder and the ISO are written there.'; return }
    if ($chkMenu.Checked -and -not $state.BgPath) { Deny-Build 'menu is on but no background chosen.' 'The autorun menu is switched on, so it needs a background image (step 4).'; return }

    # AutoRun has no Unicode mode, so a label Windows cannot encode reaches Explorer
    # as question marks. Nothing the tool can do about that, but shipping a disc
    # called "???????" without warning is not acceptable either - show exactly what
    # the drive will say and let the user rename it first.
    $lblPrev = Get-AutorunLabelPreview $txtLabel.Text.Trim()
    if ($lblPrev.Lossy) {
        if (-not (Show-Confirm ("Windows cannot show this disc label in full." +
                                "`r`n`r`nYou typed : $($txtLabel.Text.Trim())" +
                                "`r`n This PC will show : $($lblPrev.Text)" +
                                "`r`n`r`nAutoRun reads the disc label in the $($lblPrev.Codepage) codepage and has no Unicode mode, " +
                                "so characters outside it cannot be stored on the disc at all." +
                                "`r`n`r`nUse the second spelling, or go back to step 2 and rename the disc." +
                                "`r`n`r`nBuild anyway?"))) {
            & $log 'Cancelled - disc label cannot be encoded.'; return
        }
        & $log "WARNING: label will appear as '$($lblPrev.Text)' in This PC."
    }

    # An unfinished download burns a disc that fails only at install time. Confirm
    # rather than block outright: the part numbering is a convention, not a promise,
    # so a false positive must not be able to stop a legitimate build.
    $incomplete = @(Get-Games | Where-Object { $_.MissingParts.Count -gt 0 })
    if ($incomplete.Count -gt 0) {
        # $gm is captured before the inner loop: the nested ForEach-Object rebinds
        # $_ to the part number, so the outer game would be unreachable otherwise.
        $miss = ($incomplete | ForEach-Object {
                    $gm = $_
                    $gm.MissingParts | ForEach-Object { "$($gm.SetupExe.BaseName)-$_.bin" }
                 }) -join "`r`n"
        if (-not (Show-Confirm ("This GOG download looks incomplete. These installer parts are missing:`r`n`r`n$miss" +
                                "`r`n`r`nA disc built from it will look fine but fail when the installer runs, " +
                                "and by then the disc is spent.`r`n`r`nRe-download the game from GOG first." +
                                "`r`n`r`nBuild anyway?"))) {
            & $log 'Cancelled - installer parts are missing.'; return
        }
        & $log 'WARNING: building with missing installer parts, at your request.'
    }

    $outDir = $txtOut.Text.Trim()
    $pay    = Get-PayloadBytes

    # The media label has always worked out that a payload is too big for any disc,
    # but nothing acted on it - the build ran to completion and handed back an ISO
    # that could never be burned.
    $mediaKey  = Get-SelectedMediaKey
    $plan      = Get-CurrentPlan $pay
    $discCount = $(if ($plan -and $plan.Ok) { @($plan.Discs).Count } else { 1 })

    if ($plan -and -not $plan.Ok) {
        # A chosen medium that cannot hold the set is a different failure from a
        # payload past the biggest disc there is, and it needs its own sentence.
        $mName = Get-MediaNameFromKey $mediaKey
        $why   = switch ($plan.Reason) {
            'entry'  { ("{0} on its own is {1:N2} GB, and a {2} holds {3:N2} GB." -f $plan.TooBigName,
                        ([double]$plan.TooBigBytes/1GB), $mName, ($plan.Room/1GB)) +
                       "`r`n`r`nSpreading one game's installer across several discs is not something DiscWright can do yet. Choose a larger disc in step 2, or leave that game off this set." }
            'extras' { "The extra content in step 5 is bigger than one $mName on its own, before any game is added.`r`n`r`nRemove some of it, or choose a larger disc." }
            default  { "This set cannot be planned for a $mName." }
        }
        Deny-Build ("cannot plan a set of $mName - $($plan.Reason).") $why
        return
    }

    $rec = Get-MediaRec $pay.Total
    if (-not $plan -and -not $rec.Fit) {
        Deny-Build ("payload is {0:N1} GB - too big for any single disc." -f ($pay.Total/1GB)) (
            ("This disc would be {0:N1} GB, which is bigger than any single disc DiscWright can write." -f ($pay.Total/1GB)) +
            "`r`n`r`nThe largest supported medium is BD-R XL at 100 GB." +
            "`r`n`r`nPick the disc you are going to burn in step 2 and DiscWright will split the games across a set.")
        return
    }

    # Staging copies the payload, then the ISO is written beside it, so the output
    # volume needs room for both. Installer files are hardlinked rather than copied
    # when the source sits on the same NTFS volume, which costs nothing - so only
    # count them when that shortcut is not available.
    try {
        $outRoot = [IO.Path]::GetPathRoot($outDir)
        if ($outRoot) {
            $di       = New-Object System.IO.DriveInfo($outRoot)
            $free     = [double]$di.AvailableFreeSpace
            # Hardlinking needs every installer on the same volume as the stage.
            # With games spread across drives, some get copied, so budget for the lot.
            $srcRoots = @(Get-Games | ForEach-Object { [IO.Path]::GetPathRoot($_.SetupExe.FullName) } | Sort-Object -Unique)
            $linkable = ($di.DriveFormat -ieq 'NTFS') -and ($srcRoots.Count -eq 1) -and ($srcRoots[0] -ieq $outRoot)
            # Assign the branch, do not inline it: "(if ...)" parses as a command
            # invocation and only blows up at run time, which would hide here.
            $stageCost = if ($linkable) { $pay.Extra } else { $pay.Total }
            $needed    = ($stageCost + $pay.Total) * 1.02
            if ($free -gt 0 -and $needed -gt $free) {
                Deny-Build ("needs {0:N1} GB, only {1:N1} GB free on {2}" -f ($needed/1GB), ($free/1GB), $outRoot) (
                    ("Not enough free space on {0}" -f $outRoot.TrimEnd('\')) +
                    ("`r`n`r`nThis build needs about {0:N1} GB and there is {1:N1} GB free." -f ($needed/1GB), ($free/1GB)) +
                    $(if ($linkable) { "`r`n`r`nThe installer files will be hardlinked rather than copied, so they are already left out of that figure." }
                      else { "`r`n`r`nThe installer is on a different drive, so it has to be copied into the staging folder as well as written into the ISO." }) +
                    "`r`n`r`nFree up space, or choose an output folder on another drive.")
                return
            }
        }
    } catch {
        # A UNC or otherwise unmeasurable path is no reason to refuse the build.
        & $log 'NOTE: could not check free space on the output volume - building anyway.'
    }

    # A rebuild replaces work that already exists - say exactly what goes.
    $stage      = Join-Path $outDir 'disc'
    # Every ISO this build is about to write, so the confirmation can name them
    # rather than describe "the ISO" and then quietly replace three files.
    $setLabels  = @(1..$discCount | ForEach-Object { Get-DiscSetLabel $txtLabel.Text.Trim() $_ $discCount })
    $setIsos    = @($setLabels | ForEach-Object { Get-IsoPath $outDir $_ })
    $isoPath    = $setIsos[0]
    $existing   = @($setLabels | Where-Object { Test-AlreadyBuilt $outDir $_ })
    $isoExists  = ($existing.Count -gt 0)
    $stageExists = Test-Path $stage
    # Ask before destroying anything - but only about what actually goes. The ISO
    # named by THIS label, and the staging folder. A differently named ISO sitting
    # in the same folder is not touched and must not be described as if it were.
    if ($isoExists -or $stageExists) {
        # Rebuilding in place only applies to a single game whose folder IS the
        # stage. Several games always live in Games\ subfolders, never at the root.
        $bGames  = Get-Games
        $inPlace = ($bGames.Count -eq 1) -and (Test-SamePath $bGames[0].Folder $stage)
        $isoName = if ($isoPath) { Split-Path $isoPath -Leaf } else { 'the ISO' }
        $isoList = ($setIsos | ForEach-Object { '  ' + (Split-Path $_ -Leaf) }) -join "`r`n"
        if ($discCount -gt 1) {
            $msg = "Build a set of $discCount discs in:`r`n$outDir`r`n`r`nThese will be written:`r`n$isoList"
            if ($isoExists) { $msg += "`r`n`r`n$($existing.Count) of them already exist and will be replaced." }
            $msg += "`r`n`r`nAny other ISO already in this folder is left alone."
        } elseif ($isoExists) {
            $msg = "Rebuild the disc in:`r`n$outDir`r`n`r`n$isoName already exists and will be replaced."
        } else {
            $msg = "Build the disc in:`r`n$outDir`r`n`r`n$isoName will be written. Any other ISO already in this folder is left alone."
        }
        if ($stageExists -and -not $inPlace) {
            $msg += "`r`n`r`nThe 'disc' folder will be rebuilt from scratch. Anything you added to it by hand that is not listed under Extra content will be lost."
        } elseif ($inPlace) {
            $msg += "`r`n`r`nThe game files already in the disc folder will be left untouched."
        }
        $msg += "`r`n`r`nContinue?"
        if (-not (Show-Confirm $msg)) { & $log 'Cancelled - nothing was changed.'; return }
    }

    New-Item -ItemType Directory -Force -Path $txtOut.Text | Out-Null
    $buttons=@(); if($cbPlay.Checked){$buttons+='Play'}; if($cbInst.Checked){$buttons+='Install'}; if($cbMan.Checked){$buttons+='Manual'}; if($cbExtra.Checked){$buttons+='Extras'}; if($cbExit.Checked){$buttons+='Exit'}
    $s=@{ Games=(Get-Games); Label=$txtLabel.Text.Trim(); IconPath=$state.IconPath; IconIsIco=$state.IconIsIco;
         Menu=$chkMenu.Checked; BgPath=$state.BgPath; BgAsIs=$chkBgAsIs.Checked; PanelSide=[string]$cmbSide.SelectedItem;
         Divider=$chkDivider.Checked; ShowTitle=$chkTitle.Checked; TitleText=$txtTitle.Text.Trim();
         WindowBorder=$chkWinBorder.Checked; ButtonStyle=[string]$cmbBtnStyle.SelectedItem;
         MusicFile=$(if($chkMusic.Checked){$state.MusicFile}else{$null});
         Buttons=$buttons; ManualPath=$(if($cbMan.Checked){$state.ManualPath}else{$null}); ExtrasPath=$(if($cbExtra.Checked){$state.ExtrasPath}else{$null});
         ExtraItems=@($lstExtra.Items); OutDir=$txtOut.Text.Trim(); MediaKey=$mediaKey; Plan=$plan
         ExtrasEveryDisc=$chkXAll.Checked }
    $btnBuild.Enabled=$false
    $script:BuildDiscTag = ''
    $pbBuild.Value=0; $pbBuild.Visible=$true
    $buildWatch.Restart()
    $lblElapsed.Text='Starting...'
    try {
        $onDisc = { param($num,$of)
            $script:BuildDiscTag = "D$num/$of"
            $pbBuild.Value = 0
            [System.Windows.Forms.Application]::DoEvents()
        }
        $isos = @(Invoke-BuildSet $s $log $buildProgress $onDisc)
        $buildWatch.Stop()
        $took = Format-Elapsed $buildWatch.Elapsed
        $script:BuildDiscTag = ''
        $lblElapsed.Text = $(if ($isos.Count -gt 1) { "$($isos.Count) discs in $took" } else { "Done in $took" })
        & $log "Total time: $took"
        $what = $(if ($isos.Count -gt 1) { "$($isos.Count) discs built in $took" } else { "Build complete in $took" })
        [System.Windows.Forms.MessageBox]::Show(($what + "`n`n" + ($isos -join "`n")),"DiscWright") | Out-Null
    }
    catch {
        $buildWatch.Stop(); $script:BuildDiscTag = ''; $lblElapsed.Text='Failed'
        & $log ('ERROR: '+$_.Exception.Message); Show-Warn ("The build failed:`r`n`r`n" + $_.Exception.Message)
    }
    finally {
        if ($buildWatch.IsRunning) { $buildWatch.Stop() }
        $pbBuild.Visible=$false
        $btnBuild.Enabled=$true; Update-ActionButtons
    }
})

# DiscWright.vbs starts this process with STARTUPINFO wShowWindow = SW_HIDE so no
# console ever flashes. WinForms applies that state to the first top-level window,
# which would leave the form invisible - so show it explicitly once it exists.
Add-Type -Namespace GDA -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
'@
# "New disc" greys itself out when there is nothing to clear, so every edit that
# can make the form dirty has to refresh it. Most already run through
# Update-ActionButtons for their own reasons - the background, the output folder,
# the installer list - but the plain fields and toggles did not, because until now
# nothing depended on them. Wired in one loop rather than appended to fifteen
# separate handlers, which is where one would eventually get missed.
foreach ($c in @($txtLabel,$txtIcon,$txtMusic,$txtMan,$txtEx,$txtTitle)) {
    $c.Add_TextChanged({ Update-ActionButtons })
}
foreach ($c in @($chkBgAsIs,$chkMusic,$chkDivider,$chkWinBorder,$cbPlay,$cbInst,$cbMan,$cbExtra,$cbExit)) {
    $c.Add_CheckedChanged({ Update-ActionButtons })
}
# This one moves bytes between "disc 1 only" and "every disc", so it changes the
# plan as well as the buttons.
$chkXAll.Add_CheckedChanged({ Update-MediaLabel; Update-ActionButtons })
foreach ($c in @($cmbSide,$cmbBtnStyle)) {
    $c.Add_SelectedIndexChanged({ Update-ActionButtons })
}
# The target disc changes the answer on the advice line, not only the buttons.
$cmbMedia.Add_SelectedIndexChanged({ Update-MediaLabel; Update-ActionButtons })

Update-ActionButtons   # start greyed out - nothing to open yet
Update-GameList        # and the same for Change.../Remove, with an empty list

# First line in the log box, so it is already in the screenshot when someone
# posts one, and already in the copied text when someone pastes the log.
& $log "DiscWright $APP_VERSION  -  Windows PowerShell $($PSVersionTable.PSVersion)"

$form.Add_Shown({
    [void][GDA.Win]::ShowWindow($form.Handle,5)      # SW_SHOW
    [void][GDA.Win]::SetForegroundWindow($form.Handle)
    $form.Activate()
})

[void]$form.ShowDialog()



