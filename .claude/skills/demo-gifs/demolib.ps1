# Shared helpers for the demo recordings. Dot-sourced, not a module, because it
# needs UiDriver's own [DwInput] and $script: state and there is no reason to
# give it a second scope of its own.
#
# Everything here exists to make a driven run look like a person using the app:
# the pointer travels to a control instead of teleporting onto it, and dialogs
# are given time to finish painting before anything is typed into them. A
# recording of instant clicks reads as a screenshot slideshow.

function Beat([double]$s = 1.2) { Start-Sleep -Milliseconds ([int]($s * 1000)) }

# Stretches of the recording to drop, by the clock. Every Browse For Folder goes
# in here: its tree shows the OneDrive node and a personal folder, both of which
# carry a real name, and no amount of seeding scrolls them out of view. The
# 0.4.0 GIFs cut the same segment.
function Start-CutLog([string]$Path) {
    $script:CutFile = $Path
    Set-Content -LiteralPath $Path -Value '' -Encoding UTF8
}

function Add-Cut {
    param([datetime]$From, [datetime]$To)
    if (-not $script:CutFile) { return }
    Add-Content -LiteralPath $script:CutFile -Encoding UTF8 `
        -Value ('{0},{1}' -f $From.ToString('o'), $To.ToString('o'))
}

function Move-To {
    <#  .SYNOPSIS Glide the pointer to a screen point, the way a hand would. #>
    param([int]$X, [int]$Y, [int]$Steps = 14, [int]$StepMs = 18)
    Add-Type -AssemblyName System.Windows.Forms
    $p = [System.Windows.Forms.Cursor]::Position
    $x0 = $p.X; $y0 = $p.Y
    for ($i = 1; $i -le $Steps; $i++) {
        $t = $i / [double]$Steps
        # Ease out. Constant speed looks mechanical; a hand slows into the target.
        $e = 1 - [Math]::Pow(1 - $t, 3)
        [void][DwInput]::SetCursorPos([int]($x0 + ($X - $x0) * $e), [int]($y0 + ($Y - $y0) * $e))
        Start-Sleep -Milliseconds $StepMs
    }
}

function Move-ToCtl {
    param($Ctl)
    if (-not $Ctl) { throw 'Move-ToCtl was given nothing to move to' }
    $r = $Ctl.Current.BoundingRectangle
    Move-To -X ([int]($r.X + $r.Width / 2)) -Y ([int]($r.Y + $r.Height / 2))
}

function Invoke-Glide {
    <#  .SYNOPSIS Travel to a control, pause, then click it. #>
    param($Ctl, [double]$Before = 0.35, [int]$SettleMs = 400)
    Move-ToCtl $Ctl
    Beat $Before
    Invoke-Ctl -Ctl $Ctl -SettleMs $SettleMs
}

function Set-CtlTextGlide {
    param($Ctl, [string]$Text, [double]$Before = 0.35)
    Move-ToCtl $Ctl
    Beat $Before
    Set-CtlText -Ctl $Ctl -Text $Text
}

function Find-BrowseForBox {
    <#  .SYNOPSIS
        The Browse button on the same row as a given box.

        Find-RowButton matches a button to its LABEL's row, which works for every
        row but one: the disc icon's label sits on its own line at y=268 and its
        box and button are together at y=290, so matching on that label finds
        nothing at all.
    #>
    param($Win, $Box)
    if (-not $Box) { throw 'Find-BrowseForBox was given no box' }
    $br = $Box.Current.BoundingRectangle
    $by = $br.Y + ($br.Height / 2)
    $all = $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)
    $best = $null; $bestD = 9999
    for ($i = 0; $i -lt $all.Count; $i++) {
        $e = $all.Item($i)
        try {
            if ($e.Current.Name -notlike 'Browse*') { continue }
            $r = $e.Current.BoundingRectangle
            $d = [Math]::Abs(($r.Y + ($r.Height / 2)) - $by)
            if ($d -lt $bestD) { $bestD = $d; $best = $e }
        } catch { }
    }
    if ($bestD -gt 30) { throw 'no Browse button on that row' }
    return $best
}

function Find-Exact {
    <#  .SYNOPSIS
        The one control with exactly this name.

        "Manual" is a checkbox and "Manual file:" is a label, so a wildcard match
        reaches whichever the enumeration happens to hit first.
    #>
    param($Win, [string]$Name)
    $all = $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        try { if ($all.Item($i).Current.Name -eq $Name) { return $all.Item($i) } } catch { }
    }
    throw "no control named exactly '$Name'"
}

if (-not ('DwWin' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DwWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
}

# ---------------------------------------------------------------------------
# The menu preview.
#
# It is an HTA, which means Internet Explorer hosting a document, and UI
# Automation publishes nothing for it at all: no rectangle, no descendants, not
# even the window. Everything below therefore works in Win32 and in pixels.
# ---------------------------------------------------------------------------

$script:MenuW = 760
$script:MenuH = 480

function Wait-MenuWindow {
    <#  .SYNOPSIS The preview window, once mshta has one. #>
    param([int]$TimeoutSec = 20)
    for ($i = 0; $i -lt $TimeoutSec * 4; $i++) {
        $p = Get-Process mshta -ErrorAction SilentlyContinue |
             Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($p) { return $p }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Move-MenuTo {
    <#  .SYNOPSIS
        Put the preview where the recording can see it. mshta opens it wherever
        it likes, which is usually half outside the captured region.
    #>
    param($Proc, [int]$X, [int]$Y)
    [void][DwWin]::MoveWindow($Proc.MainWindowHandle, $X, $Y, $script:MenuW, $script:MenuH, $true)
    Start-Sleep -Milliseconds 700
}

function Get-MenuRect {
    param($Proc)
    $r = New-Object DwWin+RECT
    [void][DwWin]::GetWindowRect($Proc.MainWindowHandle, [ref]$r)
    return $r
}

function Get-MenuButtonRows {
    <#  .SYNOPSIS
        Where the menu's buttons are, read off the screen.

        The panel is 250px wide at x=490 in a 760x480 stage, but how far down the
        buttons start depends on how many there are and whether the screen has a
        caption - the menu centres them itself. Rather than reimplement that
        arithmetic and have it drift, this finds them: a button is a flat dark
        block 46px tall and the artwork behind it is a photograph, which is not
        flat on any three columns at once.
    #>
    param($Proc)
    Add-Type -AssemblyName System.Drawing
    $r = Get-MenuRect $Proc
    $bmp = New-Object System.Drawing.Bitmap($script:MenuW, $script:MenuH)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()

    $cols = @(500, 616, 730)
    $flat = New-Object bool[] $script:MenuH
    for ($y = 0; $y -lt $script:MenuH; $y++) {
        $hits = 0
        foreach ($x in $cols) {
            $p = $bmp.GetPixel($x, $y)
            # The two button greens-of-black, normal and hovered, and nothing
            # else in the stage is either.
            if (([Math]::Abs($p.R - 10) -le 8 -and [Math]::Abs($p.G - 21) -le 8 -and [Math]::Abs($p.B - 25) -le 8) -or
                ([Math]::Abs($p.R - 18) -le 8 -and [Math]::Abs($p.G - 36) -le 8 -and [Math]::Abs($p.B - 43) -le 8)) {
                $hits++
            }
        }
        $flat[$y] = ($hits -ge 2)
    }
    $bmp.Dispose()

    $rows = @()
    $start = -1
    for ($y = 0; $y -lt $script:MenuH; $y++) {
        if ($flat[$y] -and $start -lt 0) { $start = $y }
        elseif (-not $flat[$y] -and $start -ge 0) {
            # The inner brackets are load-bearing: "@($start, $y - 1)" parses as
            # "($start, $y) - 1", because the comma binds tighter than the minus,
            # and subtracting from an array is what it then complains about.
            if (($y - $start) -ge 25) { $rows += ,@($start, ($y - 1)) }
            $start = -1
        }
    }
    if ($start -ge 0 -and ($script:MenuH - $start) -ge 25) { $rows += ,@($start, ($script:MenuH - 1)) }
    return ,$rows
}

function Invoke-MenuButton {
    <#  .SYNOPSIS Click the Nth button of the menu, counting from the top. #>
    param($Proc, [int]$Index, [double]$Before = 0.6)
    $rows = Get-MenuButtonRows $Proc
    if ($Index -ge $rows.Count) {
        throw ("the menu shows {0} buttons, so there is no button {1}" -f $rows.Count, $Index)
    }
    $r = Get-MenuRect $Proc
    $y = $r.T + [int](($rows[$Index][0] + $rows[$Index][1]) / 2)
    $x = $r.L + 615          # the panel is 250 wide at x=490
    Move-To -X $x -Y $y
    Beat $Before
    [DwInput]::ClickAt($x, $y)
    Start-Sleep -Milliseconds 900
}

function Close-Menu {
    <#  .SYNOPSIS
        Shut the preview. Its own X sits at the top right of a window that is
        wider than the recording frame, so this closes the process instead; on
        camera the window simply goes away, which is what clicking X looks like.
    #>
    Get-Process mshta -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
    Start-Sleep -Milliseconds 600
}

function Find-BoxOnRow {
    <#  .SYNOPSIS
        The text box on the same row as a label, for the rows inside the menu
        group box where Get-BoxAfter does not apply: it looks for the row BELOW a
        numbered step's label, and these sit beside their label instead.
    #>
    param($Win, [string]$LabelLike)
    $lbl = Find-Ctl -Root $Win -NameLike $LabelLike -TimeoutSec 6
    if (-not $lbl) { throw "no label matching '$LabelLike'" }
    $lr = $lbl.Current.BoundingRectangle
    $ly = $lr.Y + ($lr.Height / 2)
    $all = $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $c = $all.Item($i)
        try {
            if ($c.Current.ClassName -notmatch '\.EDIT\.') { continue }
            $r = $c.Current.BoundingRectangle
            if ($r.Width -lt 60) { continue }
            if ([Math]::Abs(($r.Y + $r.Height / 2) - $ly) -le 8) { return $c }
        } catch { }
    }
    throw "no text box on the row of '$LabelLike'"
}

function Complete-ParentPicker {
    <#  .SYNOPSIS
        Answer "Which game?" - the dialog that asks what an add-on belongs to.

        It only appears when there is more than one game to choose between, which
        is why the window suite has never had to drive it. The list is a
        DropDownList, so the first letter of the name selects, and OK stays
        disabled until something is selected.
    #>
    param($Win, [string]$Name, [double]$Paint = 1.2)
    $dlg = Find-Ctl -Root $Win -NameLike 'Which game?' -TimeoutSec 10
    if (-not $dlg) { throw 'the Which game? dialog did not appear' }
    Beat $Paint
    $cmb = $null
    $all = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        try { if ($all.Item($i).Current.ClassName -match 'COMBOBOX') { $cmb = $all.Item($i); break } } catch { }
    }
    if (-not $cmb) { throw 'the Which game? dialog has no list' }
    Invoke-Glide -Ctl $cmb -Before 0.4 -SettleMs 600
    Send-Keys $Name.Substring(0, 1) 500
    Send-Keys '{ENTER}' 700
    $ok = Find-Ctl -Root $dlg -NameLike 'OK' -TimeoutSec 2
    if ($ok) { Invoke-Glide -Ctl $ok -Before 0.3 -SettleMs 1200 }
}

function Set-FolderTreeFocus {
    <#  .SYNOPSIS
        Give the folder tree the keyboard, which it does not start with.

        Browse For Folder opens with the focus on its OK button, so arrow keys
        sent at it go to a button and the selection never moves - which is why
        every step count came back pointing at the folder the dialog was seeded
        with. Tabbed rather than clicked: a click inside the tree lands on
        whichever node happens to be under the pointer and selects it.
    #>
    param($Dlg, [int]$MaxTabs = 6)
    $tree = Find-Ctl -Root $Dlg -NameLike 'Navigation Pane' -TimeoutSec 5
    if (-not $tree) { throw 'the folder dialog has no tree' }
    for ($i = 0; $i -le $MaxTabs; $i++) {
        if ($tree.Current.HasKeyboardFocus) { return $tree }
        Send-Keys '{TAB}' 260
    }
    throw 'the folder tree never took the keyboard focus'
}

function Complete-FolderDialogShown {
    <#  .SYNOPSIS
        Finish a Browse For Folder, on camera.

        The tree is invisible to UI Automation, so it is walked with the keyboard
        from whatever node the app seeded: RIGHT opens that node, DOWN steps
        through its children. Slowly, and with a pause on the opened dialog,
        because at ten frames a second an instant selection reads as a glitch.
    #>
    param($Win, [int]$Expand = 1, [int]$Down = 1, [double]$Paint = 1.4)
    $dlg = Find-Ctl -Root $Win -NameLike 'Browse For Folder' -TimeoutSec 12
    if (-not $dlg) { throw 'no folder dialog appeared' }
    Beat $Paint
    $null = Set-FolderTreeFocus $dlg
    for ($i = 0; $i -lt $Expand; $i++) { Send-Keys '{RIGHT}' 450 }
    for ($i = 0; $i -lt $Down;   $i++) { Send-Keys '{DOWN}'  260 }
    Beat 0.7
    Invoke-Glide -Ctl (Find-Ctl -Root $dlg -NameLike 'OK' -TimeoutSec 5) -Before 0.3 -SettleMs 1600
}

function Complete-FileDialogShown {
    <#  .SYNOPSIS
        Finish a file dialog, on camera.

        Complete-FileDialog types the instant the dialog exists, which on this
        machine is while the shell is still painting it: the recording gets a
        white rectangle and then a closed dialog, and a viewer sees a flash of
        nothing. So this waits for the paint, travels to the name box, types, and
        travels to Open.
    #>
    param($Win, [string]$TitleLike, [string]$File, [double]$Paint = 1.6)
    $dlg = Find-Ctl -Root $Win -NameLike $TitleLike -TimeoutSec 10
    if (-not $dlg) { throw "no file dialog matching '$TitleLike'" }
    Beat $Paint
    $box = Find-Ctl -Root $dlg -NameLike 'File name:*' -TimeoutSec 5
    if (-not $box) { throw 'the file name box was not found' }
    Set-CtlTextGlide -Ctl $box -Text ('"' + $File + '"') -Before 0.3
    Beat 0.7
    Invoke-Glide -Ctl (Find-Ctl -Root $dlg -NameLike 'Open' -TimeoutSec 5) -Before 0.3 -SettleMs 1800
}
