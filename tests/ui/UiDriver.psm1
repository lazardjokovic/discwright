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
    $win = Wait-Win -TitleLike 'DiscWright*' -TimeoutSec $TimeoutSec
    if (-not $win) { try { $proc.Kill() } catch {}; throw 'the DiscWright window never appeared' }
    Set-WindowFocus $win; Start-Sleep -Milliseconds 600; Set-WindowFocus $win
    return [pscustomobject]@{ Process = $proc; Window = $win }
}

function Stop-DiscWright {
    param($App)
    if (-not $App) { return }
    try { $App.Process.Kill() } catch {}
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
    param($Win)
    [void][DwInput]::SetForegroundWindow((New-Object IntPtr($Win.Current.NativeWindowHandle)))
    Start-Sleep -Milliseconds 300
}

function Invoke-Ctl {
    <#  .SYNOPSIS Click a control at the centre of its rectangle. #>
    param($Ctl, [int]$SettleMs = 400)
    if (-not $Ctl) { throw 'Invoke-Ctl was given nothing to click' }
    $r = $Ctl.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { throw "'$($Ctl.Current.Name)' has no clickable area" }
    [DwInput]::ClickAt([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds $SettleMs
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
    [System.Windows.Forms.SendKeys]::SendWait('^a' + (ConvertTo-SendKeys $Text))
    Start-Sleep -Milliseconds 350
}

function Send-Keys {
    param([string]$Keys, [int]$SettleMs = 300)
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds $SettleMs
}

function Get-BoxAfter {
    <#  .SYNOPSIS
        The text box belonging to a numbered step. They carry no accessible name,
        so each is identified by sitting directly after its label in the window's
        child order.
    #>
    param($Win, [string]$LabelLike)
    $kids = $Win.FindAll($script:UiScope::Children, $script:UiAny)
    for ($i = 0; $i -lt $kids.Count; $i++) {
        try { if ($kids.Item($i).Current.Name -like $LabelLike) { return $kids.Item($i + 1) } } catch {}
    }
    return $null
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

Export-ModuleMember -Function Test-UiAvailable, Start-DiscWright, Stop-DiscWright, Wait-Win,
    Find-Ctl, Set-WindowFocus, Invoke-Ctl, Invoke-CtlNamed, Test-CtlEnabled, Set-CtlText,
    Send-Keys, Get-BoxAfter, Get-StatusText, Get-EntryCount, Select-ListRow,
    Complete-FolderDialog, Complete-FileDialog, Read-MessageBox, Save-WindowShot, ConvertTo-SendKeys
