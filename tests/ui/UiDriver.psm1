<#
    Driving the DiscWright window from outside it.

    WHAT UI AUTOMATION GIVES US HERE, AND WHAT IT DOES NOT

    WinForms publishes its controls through the legacy accessibility bridge, so
    every control - and every control of the shell's file and folder dialogs -
    arrives as a ControlType.Pane supporting no patterns at all. There is no
    InvokePattern to call and no ValuePattern to set.

    What IS exposed, and correctly: the control's name, whether it is enabled,
    and its rectangle on screen. So this module reads state through UIA and acts
    through synthesized input at the rectangle - a real click and real keystrokes,
    which the application cannot distinguish from a person's.

    Consequences worth knowing before writing a test with it:

      - ListView rows are not exposed. The list appears as an empty pane. Assert
        on the status label instead, which is generated from the same list, and
        click rows by offset from the list's own rectangle.
      - The folder dialog's tree is not exposed either. Navigate it from its
        seeded selection with the keyboard: New-FolderDialog sets SelectedPath,
        so the dialog opens with a known node selected and RIGHT/DOWN move
        relative to it.
      - Nothing else may touch the machine while a test runs. The pointer and the
        foreground window are shared with whoever is sitting there.
#>

# Suppressed for this file only, rather than switched off in
# PSScriptAnalyzerSettings.psd1 where it would stop applying to the app. Every
# empty catch here guards a read of a UI Automation element's property: the
# application destroys and rebuilds controls while this module is walking them,
# and a read of one that has just gone throws. Skipping it is the whole intent,
# and there is nothing worth logging.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'Reads of UI Automation elements that the app destroyed mid-walk. Skipping the vanished element is correct and there is nothing to log.')]
param()

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

if (-not ('DwInput' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DwInput {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public static void ClickAt(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(60);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(40);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
}
"@
}

# Not $A / $T. PowerShell variable names are case-insensitive, so a loop written
# "foreach ($t in ...)" silently destroys a TreeScope held in $T - which cost an
# afternoon once already.
$script:UiEl    = [System.Windows.Automation.AutomationElement]
$script:UiScope = [System.Windows.Automation.TreeScope]
$script:UiAny   = [System.Windows.Automation.Condition]::TrueCondition

function Test-UiAvailable {
    <#  .SYNOPSIS Is there a desktop to drive at all? #>
    try {
        if ([System.Windows.Forms.SystemInformation]::VirtualScreen.Width -lt 200) { return $false }
        $null = $script:UiEl::RootElement
        return $true
    } catch { return $false }
}

function Start-DiscWright {
    param([string]$AppPath, [int]$TimeoutSec = 60)
    # -WindowStyle Hidden, or the launcher's own console sits on top of the form
    # and swallows every click aimed at it.
    $proc = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $AppPath)
    # Matched on the process id, not on the title. Waiting for any window called
    # DiscWright* means driving whichever one is found first, and a copy orphaned
    # by an earlier test - one that already has a project loaded - looks exactly
    # like a fresh one until half the suite has failed on state it never set.
    $win = Wait-WinForProcess -ProcessId $proc.Id -TimeoutSec $TimeoutSec
    if (-not $win) { try { $proc.Kill() } catch {}; throw 'the DiscWright window never appeared' }
    Set-DrivenWindow $win
    try {
        $null = Set-WindowFocus $win
        Start-Sleep -Milliseconds 600
        $null = Set-WindowFocus $win
    } catch {
        # No foreground means no way to drive it, and the half-started app would
        # otherwise be left on the user's screen doing nothing.
        try { $proc.Kill() } catch {}
        throw
    }
    return [pscustomobject]@{ Process = $proc; Window = $win }
}

function Wait-WinForProcess {
    param([int]$ProcessId, [int]$TimeoutSec = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        foreach ($k in $script:UiEl::RootElement.FindAll($script:UiScope::Children, $script:UiAny)) {
            try {
                if ($k.Current.ProcessId -eq $ProcessId -and $k.Current.Name -like 'DiscWright*') { return $k }
            } catch {}
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Stop-DiscWright {
    param($App)
    if (-not $App) { return }
    try { $App.Process.Kill() } catch {}
    # Waited for, not slept past. A window that is still closing is still findable,
    # and the next test would attach to a corpse.
    try { $null = $App.Process.WaitForExit(5000) } catch {}
    Start-Sleep -Milliseconds 300
}

function Wait-Win {
    param([string]$TitleLike, [int]$TimeoutSec = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        foreach ($k in $script:UiEl::RootElement.FindAll($script:UiScope::Children, $script:UiAny)) {
            try { if ($k.Current.Name -like $TitleLike) { return $k } } catch {}
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Find-Ctl {
    <#  .SYNOPSIS First descendant whose name matches, or $null. #>
    param($Root, [string]$NameLike, [int]$TimeoutSec = 8)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $all = $Root.FindAll($script:UiScope::Descendants, $script:UiAny)
        for ($i = 0; $i -lt $all.Count; $i++) {
            try { if ($all.Item($i).Current.Name -like $NameLike) { return $all.Item($i) } } catch {}
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Set-WindowFocus {
    <#
    .SYNOPSIS
        Bring the window to the front, and prove that it worked.

    .DESCRIPTION
        Windows refuses SetForegroundWindow from a process that does not already
        own the foreground. It reports the refusal in its return value and
        otherwise does nothing at all.

        Ignoring that is dangerous rather than merely flaky. Every click this
        module makes is a click at a screen COORDINATE: if the window did not
        come forward, those clicks land on whatever is in front of it - the
        browser, an editor, somebody's actual work - and the keystrokes go there
        too. The suite then sits through hundreds of lookups that can never
        succeed while the app stands untouched, which is exactly what it looks
        like from the outside.

        So this refuses to continue instead. A test that cannot drive the window
        must stop, not type into someone else's.
    #>
    param($Win, [switch]$Optional)
    $h = New-Object IntPtr($Win.Current.NativeWindowHandle)
    for ($try = 0; $try -lt 3; $try++) {
        [void][DwInput]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 300
        if ([DwInput]::GetForegroundWindow() -eq $h) { return $true }
    }
    if ($Optional) { return $false }
    throw ("DiscWright could not be brought to the front, so its window cannot be driven. " +
           "Windows only grants that to a process that already owns the foreground. " +
           "Something else has it - most likely you are using the machine. " +
           "Every click from here would land on whatever window IS in front, so this stops instead. " +
           "Run the window tests again when the desktop is free.")
}

function Invoke-Ctl {
    <#
    .SYNOPSIS Click a control at the centre of its rectangle.

    .DESCRIPTION
        Checks that the click will land inside the application before making it.
        A click is a screen coordinate and nothing more: if the app is no longer
        the foreground window - because somebody touched the machine mid-run -
        the coordinate now belongs to whatever is in front, and the click goes
        into their window instead. Stopping is the only safe response.
    #>
    param($Ctl, [int]$SettleMs = 400, [switch]$NoFocusCheck)
    if (-not $Ctl) { throw 'Invoke-Ctl was given nothing to click' }
    $r = $Ctl.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { throw "'$($Ctl.Current.Name)' has no clickable area" }
    if (-not $NoFocusCheck -and -not (Test-DrivingOurWindow)) {
        throw ("The foreground window is no longer the one being tested, so clicking '" +
               $Ctl.Current.Name + "' would land in whatever is in front of it. Stopped. " +
               "The window tests need the desktop to themselves.")
    }
    [DwInput]::ClickAt([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds $SettleMs
}

function Set-DrivenWindow {
    <#  .SYNOPSIS Remember which window this run is allowed to type into. #>
    param($Win)
    $script:DrivenHandle = [IntPtr]$Win.Current.NativeWindowHandle
}

function Test-DrivingOurWindow {
    <#  .SYNOPSIS Is the window under test still the one receiving input? #>
    if (-not $script:DrivenHandle) { return $true }   # nothing claimed yet
    $fg = [DwInput]::GetForegroundWindow()
    if ($fg -eq $script:DrivenHandle) { return $true }
    # A dialog the app itself opened is a different window and perfectly fine to
    # drive; it belongs to the same process.
    try {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($fg)
        $own = [System.Windows.Automation.AutomationElement]::FromHandle($script:DrivenHandle)
        return ($el.Current.ProcessId -eq $own.Current.ProcessId)
    } catch { return $false }
}

function Invoke-CtlNamed {
    param($Root, [string]$NameLike, [int]$TimeoutSec = 8, [int]$SettleMs = 400)
    $c = Find-Ctl -Root $Root -NameLike $NameLike -TimeoutSec $TimeoutSec
    if (-not $c) { throw "no control named '$NameLike'" }
    if (-not $c.Current.IsEnabled) { throw "'$NameLike' is disabled - refusing to click it" }
    Invoke-Ctl -Ctl $c -SettleMs $SettleMs
    return $c
}

function Test-CtlEnabled {
    <#  .SYNOPSIS $true / $false, or $null when the control is not there at all. #>
    param($Root, [string]$NameLike, [int]$TimeoutSec = 4)
    $c = Find-Ctl -Root $Root -NameLike $NameLike -TimeoutSec $TimeoutSec
    if (-not $c) { return $null }
    return [bool]$c.Current.IsEnabled
}

function ConvertTo-SendKeys {
    <#  .SYNOPSIS Escape the characters SendKeys treats as syntax. #>
    param([string]$Text)
    return ($Text -replace '([+^%~(){}\[\]])', '{$1}')
}

function Set-CtlText {
    <#  .SYNOPSIS Click a box and replace its contents by typing. #>
    param($Ctl, [string]$Text)
    Invoke-Ctl -Ctl $Ctl -SettleMs 250
    Send-Keys ('^a' + (ConvertTo-SendKeys $Text)) 350
}

function Send-Keys {
    <#
    .SYNOPSIS Type into the window under test.

    .DESCRIPTION
        SendKeys goes to whatever holds the foreground, not to any control this
        module chose. If the application has lost it, the text is typed into
        somebody else's window - a browser, an editor - which is a good deal
        worse than a failing test. So the foreground is checked first.
    #>
    param([string]$Keys, [int]$SettleMs = 300)
    if (-not (Test-DrivingOurWindow)) {
        throw ("The foreground window is no longer the one being tested, so these keystrokes " +
               "would be typed into whatever is in front of it. Stopped. " +
               "The window tests need the desktop to themselves.")
    }
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds $SettleMs
}

function Get-BoxAfter {
    <#
    .SYNOPSIS
        The text box belonging to a numbered step.

    .DESCRIPTION
        Found by where it sits, not by what position it holds in the enumeration.

        It used to take the next child after the label, which is only right while
        UI Automation happens to enumerate in creation order - and that ordering
        is not creation order, it follows the layout. Moving the controls to make
        the window shorter reshuffled it, so this started handing back the Browse
        BUTTON instead of the box. Set-CtlText then clicked Browse, a folder
        dialog opened, and every test after that waited on an application sitting
        behind a modal dialog: from the outside, an app that starts and then does
        nothing at all.

        A step's input is the control on the row directly below the label,
        starting at the same left edge - a text box for most steps, the installer
        ListView for step 1.

        Which control that is gets decided by ClassName, which the WinForms
        accessibility bridge fills in accurately even though it exposes every
        control as a pattern-less Pane: boxes are WindowsForms10.EDIT, the list
        is .SysListView32, buttons are .BUTTON, labels are .STATIC and the icon
        preview is .Window.8.

        That distinction used to be made by looking for an EMPTY name, on the
        reasoning that buttons are named and boxes are not - which is true only
        while the box is empty. A WinForms text box reports its CONTENTS as its
        accessible name, so the moment a project was loaded and the boxes had
        text in them, every one of them was skipped as though it were a button
        and this returned nothing. That looked like the app failing to open a
        disc; it was the harness failing to find the box.
    #>
    param($Win, [string]$LabelLike)
    $kids = $Win.FindAll($script:UiScope::Children, $script:UiAny)
    $label = $null
    for ($i = 0; $i -lt $kids.Count; $i++) {
        try { if ($kids.Item($i).Current.Name -like $LabelLike) { $label = $kids.Item($i); break } } catch {}
    }
    if (-not $label) { return $null }
    $lr = $label.Current.BoundingRectangle

    $best = $null; $bestTop = [double]::MaxValue
    for ($i = 0; $i -lt $kids.Count; $i++) {
        try {
            $c = $kids.Item($i)
            # Something you can type in or pick from, not a button, label or picture.
            if ($c.Current.ClassName -notmatch '\.(EDIT|SysListView32|COMBOBOX)\.') { continue }
            $r = $c.Current.BoundingRectangle
            if ($r.Width -lt 60 -or $r.Height -lt 10) { continue }
            if ([Math]::Abs($r.Left - $lr.Left) -gt 8) { continue }           # same left edge
            if ($r.Top -lt $lr.Top + 8 -or $r.Top -gt $lr.Top + 60) { continue }  # the row below
            if ($r.Top -lt $bestTop) { $best = $c; $bestTop = $r.Top }
        } catch {}
    }
    return $best
}

function Get-StatusText {
    <#  .SYNOPSIS
        The line under the installer list. It is the only readable proof of what
        the list holds, since the ListView's rows are not exposed.
    #>
    param($Win)
    $all = $Win.FindAll($script:UiScope::Descendants, $script:UiAny)
    $found = ''
    for ($i = 0; $i -lt $all.Count; $i++) {
        try {
            $n = $all.Item($i).Current.Name
            if ($n -match 'Disc: |Detected: |setup_\*|already on this disc|no installer') { $found = $n }
        } catch {}
    }
    return $found
}

function Get-EntryCount {
    <#  .SYNOPSIS How many installers the status line says are on the disc. #>
    param($Win)
    $s = Get-StatusText $Win
    if ($s -match '^Detected: ') { return 1 }
    $n = 0
    if ($s -match '^(\d+) game')      { $n += [int]$Matches[1] }
    if ($s -match '\+ (\d+) add-on')  { $n += [int]$Matches[1] }
    return $n
}

function Select-ListRow {
    <#  .SYNOPSIS
        Click a row of the installer list by offset. The rows are not addressable,
        but the list's own rectangle is, and the rows are a fixed height.
    #>
    param($Win, [int]$Index = 0)
    $lv = Get-BoxAfter -Win $Win -LabelLike '1)  Installers*'
    if (-not $lv) { throw 'the installer list was not found' }
    $r = $lv.Current.BoundingRectangle
    [DwInput]::ClickAt([int]($r.X + 60), [int]($r.Y + 30 + ($Index * 19)))
    Start-Sleep -Milliseconds 600
}

function Complete-FolderDialog {
    <#  .SYNOPSIS
        Finish a "Browse For Folder" that the app has opened.

        The tree is invisible to UI Automation, so navigation is relative to the
        node the app seeded through SelectedPath: Expand goes down into it, then
        Down steps through its children.
    #>
    param($Win, [int]$Expand = 0, [int]$Down = 0, [switch]$Cancel, [int]$TimeoutSec = 10)
    $dlg = Find-Ctl -Root $Win -NameLike 'Browse For Folder' -TimeoutSec $TimeoutSec
    if (-not $dlg) { throw 'no folder dialog appeared' }
    for ($i = 0; $i -lt $Expand; $i++) { Send-Keys '{RIGHT}' 400 }
    for ($i = 0; $i -lt $Down;   $i++) { Send-Keys '{DOWN}'  400 }
    $btn = if ($Cancel) { 'Cancel' } else { 'OK' }
    Invoke-Ctl -Ctl (Find-Ctl -Root $dlg -NameLike $btn -TimeoutSec 5) -SettleMs 1500
    return $true
}

function Complete-FileDialog {
    <#  .SYNOPSIS
        Finish an open-file dialog by typing into its name box. Several files are
        given the way the shell expects them - each quoted, separated by spaces.
    #>
    param($Win, [string]$TitleLike, [string[]]$Files, [switch]$Cancel, [int]$TimeoutSec = 10)
    $dlg = Find-Ctl -Root $Win -NameLike $TitleLike -TimeoutSec $TimeoutSec
    if (-not $dlg) { throw "no file dialog matching '$TitleLike'" }
    if ($Cancel) {
        Invoke-Ctl -Ctl (Find-Ctl -Root $dlg -NameLike 'Cancel' -TimeoutSec 5) -SettleMs 1000
        return $true
    }
    $box = Find-Ctl -Root $dlg -NameLike 'File name:*' -TimeoutSec 5
    if (-not $box) { throw 'the file name box was not found' }
    $value = ($Files | ForEach-Object { '"' + $_ + '"' }) -join ' '
    Set-CtlText -Ctl $box -Text $value
    Invoke-Ctl -Ctl (Find-Ctl -Root $dlg -NameLike 'Open' -TimeoutSec 5) -SettleMs 2000
    return $true
}

function Read-MessageBox {
    <#
    .SYNOPSIS
        Read the text out of a message box the app has put up, and dismiss it.

    .DESCRIPTION
        Returns the wording, so a test can assert that the refusal explains
        itself rather than merely that something appeared. Dismisses it either
        way - a message box left standing is modal, and every later click in the
        same test would land on nothing.
    #>
    param($Win, [string]$TitleLike = 'DiscWright', [string]$Button = 'OK', [int]$TimeoutSec = 8)
    $dlg = Find-Ctl -Root $Win -NameLike $TitleLike -TimeoutSec $TimeoutSec
    if (-not $dlg) { return $null }
    $text = ''
    $all = $dlg.FindAll($script:UiScope::Descendants, $script:UiAny)
    for ($i = 0; $i -lt $all.Count; $i++) {
        try {
            $n = $all.Item($i).Current.Name
            # The longest child that is not a button is the message itself.
            if ($n -and $n -notin @('OK','Cancel','Yes','No') -and $n.Length -gt $text.Length) { $text = $n }
        } catch {}
    }
    $btn = Find-Ctl -Root $dlg -NameLike $Button -TimeoutSec 4
    if ($btn) { Invoke-Ctl -Ctl $btn -SettleMs 600 }
    return $text
}

function Save-WindowShot {
    param($Win, [string]$Path)
    if (-not $Path) { return }
    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $r = $Win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size); $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

Export-ModuleMember -Function Test-UiAvailable, Start-DiscWright, Stop-DiscWright, Wait-Win, Wait-WinForProcess,
    Find-Ctl, Set-WindowFocus, Invoke-Ctl, Invoke-CtlNamed, Test-CtlEnabled, Set-CtlText,
    Send-Keys, Get-BoxAfter, Get-StatusText, Get-EntryCount, Select-ListRow,
    Complete-FolderDialog, Complete-FileDialog, Read-MessageBox, Save-WindowShot, ConvertTo-SendKeys, Set-DrivenWindow, Test-DrivingOurWindow
