<#
.SYNOPSIS
    Make a Windows virtual machine quiet enough to drive a window suite on.

.DESCRIPTION
    A fresh Windows is not a quiet desktop. It opens a search flyout, asks about
    backing up to OneDrive, offers to finish setting up the device, and reboots
    itself for updates. Each of those takes the foreground, and a window test
    that loses the foreground fails for a reason that has nothing to do with the
    code.

    The important one is not a popup at all. ForegroundLockTimeout is 200 seconds
    on a fresh install, deliberately, so that background programs cannot pull
    another program's window to the front. The window suite does precisely that,
    by design, and on a machine whose only job is running it there is nothing
    left for the rule to protect. Until it is zero, every test fails at
    Start-DiscWright saying something else owns the foreground, with nobody at
    the machine.

    Run against the VM, not the host. It changes settings you would not want on
    a machine you actually use.

.EXAMPLE
    Invoke-Command -VMName DiscWright-Win11 -Credential $cred -FilePath .\Harden-TestVM.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Continue'

if (-not $PSCmdlet.ShouldProcess($env:COMPUTERNAME, 'turn off focus stealing and the popups that cause it')) { return }

# The one that matters. Without it nothing else here helps.
Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name ForegroundLockTimeout -Value 0 -Type DWord
Add-Type -Name FgSpi -Namespace Dw -MemberDefinition @'
[DllImport("user32.dll", SetLastError=true)]
public static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
'@
# SPI_SETFOREGROUNDLOCKTIMEOUT, with update-and-notify so it applies to the
# session already running rather than only after the next logon.
[void][Dw.FgSpi]::SystemParametersInfo(0x2001, 0, [IntPtr]::Zero, 3)

# OneDrive, seen opening a backup prompt in front of a running suite.
Get-Process OneDrive -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$setup = "$env:SystemRoot\SysWOW64\OneDriveSetup.exe"
if (-not (Test-Path $setup)) { $setup = "$env:SystemRoot\System32\OneDriveSetup.exe" }
if (Test-Path $setup) { Start-Process $setup -ArgumentList '/uninstall' -Wait -ErrorAction SilentlyContinue }
New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' -Name DisableFileSyncNGSC -Value 1 -Type DWord

# Toasts, which draw over the window being driven.
$push = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications'
if (-not (Test-Path $push)) { New-Item -Path $push | Out-Null }
Set-ItemProperty $push -Name ToastEnabled -Value 0 -Type DWord

# "Finish setting up your device", tips, and the rest of the first-run furniture.
$cdm = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
if (-not (Test-Path $cdm)) { New-Item -Path $cdm -Force | Out-Null }
foreach ($v in 'SubscribedContent-310093Enabled', 'SubscribedContent-338388Enabled',
                'SubscribedContent-338389Enabled', 'SubscribedContent-353698Enabled',
                'SystemPaneSuggestionsEnabled', 'SoftLandingEnabled', 'ScoobeSystemSettingEnabled') {
    Set-ItemProperty $cdm -Name $v -Value 0 -Type DWord -ErrorAction SilentlyContinue
}
New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' -Name DisableWindowsConsumerFeatures -Value 1 -Type DWord

# Windows Update, which otherwise reboots the machine out from under a run.
New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -Name NoAutoUpdate -Value 1 -Type DWord
Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -Name NoAutoRebootWithLoggedOnUsers -Value 1 -Type DWord

# Error reporting, whose dialogs are modal and sit in front of everything.
New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting' -Name DontShowUI -Value 1 -Type DWord

Write-Information 'Hardened. Reboot, then check that the foreground can be TAKEN, not merely that a window can be seen.' -InformationAction Continue
