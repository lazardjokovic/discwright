<#
.SYNOPSIS
    Installs a DiscWright installer inside Windows Sandbox, checks it, and
    uninstalls it, on a clean Windows that has never seen DiscWright.

.DESCRIPTION
    Smart App Control blocks an unsigned installer on the machine this is
    developed on, so the installer shipped in 0.8.0 went out having never been
    run. A sandbox is a throwaway Windows with none of that policy, and a
    release check that does not change anything about this machine.

    What it does, all of it unattended:

      1. copies the installer into a folder the sandbox can see
      2. starts the sandbox, which runs Install-Check.ps1 inside it
      3. that script installs silently, looks at what landed, starts the app,
         closes it, uninstalls, and looks again
      4. the sandbox writes its answers back out and shuts itself down
      5. this prints them

    Nothing is installed on this machine. A sandbox window appears while it
    runs, and closes itself.

.PARAMETER Installer
    The setup .exe. Defaults to the newest DiscWright-*-setup.exe beside this
    script, or under build\.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File packaging\sandbox\Test-Installer.ps1

.EXAMPLE
    .\packaging\sandbox\Test-Installer.ps1 -Installer C:\Downloads\DiscWright-0.8.0-setup.exe
#>
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'A runner whose printed, coloured summary is its entire output. Nothing consumes it as data.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingEmptyCatchBlock', '',
    Justification = 'Waiting on a sandbox that has already closed itself is the expected path, and there is nothing to log.')]
[CmdletBinding()]
param(
    [string]$Installer,
    [int]$TimeoutMinutes = 15,

    # Add VBScript to the sandbox before installing, which tests the other half
    # of the installer's choice: with it there, the shortcut should be wscript
    # and the quiet .vbs launcher rather than powershell. Needs the network, to
    # fetch the Feature on Demand, so it is off by default.
    [switch]$WithVBScript
)
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path (Split-Path $here -Parent) -Parent

if (-not (Test-Path "$env:SystemRoot\System32\WindowsSandbox.exe")) {
    throw ("Windows Sandbox is not turned on. In an admin PowerShell:`n" +
           "  Enable-WindowsOptionalFeature -Online -FeatureName 'Containers-DisposableClientVM' -All`n" +
           "then reboot. It needs Windows Pro or Enterprise.")
}

if (-not $Installer) {
    $Installer = @(Get-ChildItem -Path $here, (Join-Path $repo 'build') -Filter 'DiscWright-*-setup.exe' `
                       -Recurse -ErrorAction SilentlyContinue |
                   Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not $Installer -or -not (Test-Path $Installer)) {
    throw ("No installer found. Build one with packaging\Build-Release.ps1, or download the " +
           "release's own with:`n  gh release download v0.8.0 --pattern '*-setup.exe'`n" +
           "then pass it with -Installer.")
}
$Installer = (Resolve-Path $Installer).Path

# A folder the sandbox can write back into. Not the repo: what comes out is a
# log from a throwaway machine, not something to keep.
$work = Join-Path $env:TEMP ('dwsandbox_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path (Join-Path $work 'in'), (Join-Path $work 'out') | Out-Null
Copy-Item $Installer (Join-Path $work 'in')
Copy-Item (Join-Path $here 'Install-Check.ps1') (Join-Path $work 'in')

# Networking off by default: nothing here needs it, and a sandbox with no
# network is one fewer thing to think about when the thing being run is an
# unsigned installer. -WithVBScript needs it, to fetch the Feature on Demand.
$net = if ($WithVBScript) { 'Default' } else { 'Disable' }
$args = if ($WithVBScript) { ' -WithVBScript' } else { '' }
$wsb = Join-Path $work 'discwright.wsb'
@"
<Configuration>
  <Networking>$net</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$work</HostFolder>
      <SandboxFolder>C:\dw</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -ExecutionPolicy Bypass -WindowStyle Normal -File C:\dw\in\Install-Check.ps1$args</Command>
  </LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $wsb -Encoding UTF8

Write-Host "Testing $(Split-Path $Installer -Leaf) in Windows Sandbox."
if ($WithVBScript) {
    Write-Host "Adding VBScript in there first, so this tests the other half of the choice."
}
Write-Host "A sandbox window will open and close itself. Nothing is installed on this machine."

$result = Join-Path $work 'out\result.txt'
$proc = Start-Process "$env:SystemRoot\System32\WindowsSandbox.exe" -ArgumentList $wsb -PassThru
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path $result) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }

if (-not (Test-Path $result)) {
    Write-Host "The sandbox wrote nothing within $TimeoutMinutes minutes." -ForegroundColor Red
    Write-Host "Its window is still open if you want to look; close it when done."
    exit 1
}
# Written before the sandbox shuts itself down, so give the last lines a moment.
Start-Sleep -Seconds 3
Get-Content -LiteralPath $result | ForEach-Object { Write-Host "  $_" }

$failed = @(Get-Content -LiteralPath $result | Where-Object { $_ -match '^\s*FAIL' })
Write-Host ""
if ($failed.Count) {
    Write-Host "$($failed.Count) check(s) failed." -ForegroundColor Red
} else {
    Write-Host "Every check passed on a clean Windows." -ForegroundColor Green
}
Write-Host "The full log is $work\out"
try { if (-not $proc.HasExited) { $null = $proc.WaitForExit(60000) } } catch {}
exit ([int]($failed.Count -gt 0))
