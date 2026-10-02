<#
.SYNOPSIS
    Build a Windows virtual machine that can run DiscWright's suites unattended.

.DESCRIPTION
    The window suite needs a desktop to itself. This makes one, so it stops being
    the desktop you are working at. See README.md in this directory for what can
    and cannot move to a virtual machine.

    Nothing here is required to develop DiscWright. The suites run on the machine
    you are sitting at, and some things only run there.

    Needs Hyper-V, and either an elevated shell or membership of the local
    Hyper-V Administrators group.

.PARAMETER IsoPath
    A Windows 11 installation ISO. Microsoft publishes an Enterprise Evaluation
    image, free and licensed for testing.

.PARAMETER VMPath
    Where the machine's files go. Give it a disk with room: the machine wants
    about 25 GB once the toolchain is on it.

.PARAMETER Password
    For the VM's local account. Asked for if not given. It is written into the
    answer file in plain text, as unattended Windows installs require, so treat
    the machine as disposable and do not reuse a password that matters.

.EXAMPLE
    .\New-TestVM.ps1 -IsoPath F:\ISO\Win11-Enterprise-Eval.iso -VMPath F:\VMs
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$IsoPath,
    [Parameter(Mandatory)][string]$VMPath,
    [string]$VMName = 'DiscWright-Win11',
    [securestring]$Password,
    [int]$CpuCount = 4,
    [int64]$MemoryBytes = 12GB,
    [int64]$DiskBytes = 80GB,
    [string]$SwitchName = 'Default Switch'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $IsoPath)) { throw "No ISO at $IsoPath" }
if (Get-VM -Name $VMName -ErrorAction SilentlyContinue) { throw "$VMName already exists" }
if (-not $Password) { $Password = Read-Host -AsSecureString "Password for the VM's local account" }

$plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
if (-not $plain) { throw 'The password cannot be empty: Windows will not log in automatically without one.' }

$here = Split-Path $PSCommandPath -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) ('dwvm_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    # The answer file carries a placeholder in the repository, because this is a
    # public repository and an unattended install needs the password in clear.
    $answer = (Get-Content (Join-Path $here 'autounattend.xml.template') -Raw).Replace('%PASSWORD%', $plain)
    Set-Content -LiteralPath (Join-Path $work 'autounattend.xml') -Value $answer -Encoding Ascii

    # Windows Setup reads autounattend.xml from the root of any attached volume,
    # so it travels on a second disc rather than being injected into the install
    # media. Built with the app's own ISO writer, which is already proven.
    . (Join-Path (Split-Path $here -Parent) '..\DiscWright.ps1' -Resolve -ErrorAction SilentlyContinue)
    $seed = Join-Path $VMPath 'autounattend.iso'
    if ($PSCmdlet.ShouldProcess($seed, 'write the answer disc')) {
        $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        $fsi.FileSystemsToCreate = 3
        $fsi.VolumeName = 'UNATTEND'
        $fsi.Root.AddTree($work, $false)
        $img = $fsi.CreateResultImage()
        $stream = New-Object -ComObject ADODB.Stream
        $stream.Open(); $stream.Type = 1
        $stream.Write($img.ImageStream.Read($img.BlockSize * $img.TotalBlocks))
        $stream.SaveToFile($seed, 2)
        $stream.Close()
    }

    if (-not $PSCmdlet.ShouldProcess($VMName, 'create the virtual machine')) { return }

    New-VM -Name $VMName -MemoryStartupBytes $MemoryBytes -Generation 2 -Path $VMPath `
           -NewVHDPath (Join-Path $VMPath "$VMName\$VMName.vhdx") -NewVHDSizeBytes $DiskBytes `
           -SwitchName $SwitchName | Out-Null
    Set-VMProcessor -VMName $VMName -Count $CpuCount
    # Static memory: test timings should not wander because the host reclaimed pages.
    Set-VMMemory -VMName $VMName -DynamicMemoryEnabled $false
    # Windows 11 installs with neither of these missing.
    Set-VMKeyProtector -VMName $VMName -NewLocalKeyProtector
    Enable-VMTPM -VMName $VMName
    Set-VMFirmware -VMName $VMName -EnableSecureBoot On -SecureBootTemplate 'MicrosoftWindows'
    # Checkpoints are taken deliberately. An automatic one mid-run changes what
    # the next run starts from.
    Set-VM -Name $VMName -AutomaticCheckpointsEnabled $false -AutomaticStopAction ShutDown
    Enable-VMIntegrationService -VMName $VMName -Name 'Guest Service Interface'
    # A larger screen than a hosted runner's 1024x768, which is the reason CI
    # cannot run the window suite: the form is taller than that.
    Set-VMVideo -VMName $VMName -ResolutionType Single -HorizontalResolution 1920 -VerticalResolution 1200

    Add-VMDvdDrive -VMName $VMName -Path $IsoPath
    Add-VMDvdDrive -VMName $VMName -Path $seed
    Set-VMFirmware -VMName $VMName -FirstBootDevice (Get-VMDvdDrive -VMName $VMName | Where-Object { $_.Path -eq $IsoPath })

    Start-VM -Name $VMName

    # Windows installation media waits at "press any key to boot from CD" and
    # then gives up. Nobody is watching, so tap the key for it.
    $ns = 'root\virtualization\v2'
    $sys = Get-CimInstance -Namespace $ns -ClassName Msvm_ComputerSystem -Filter "ElementName='$VMName'"
    $kb = Get-CimInstance -Namespace $ns -ClassName Msvm_Keyboard -Filter "SystemName='$($sys.Name)'"
    $until = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $until) {
        [void](Invoke-CimMethod -InputObject $kb -MethodName TypeKey -Arguments @{ keyCode = [uint32]0x0D })
        Start-Sleep -Milliseconds 300
    }

    Write-Information "$VMName is installing. It takes about fifteen minutes and needs nobody." -InformationAction Continue
    Write-Information "When it is up, run Harden-TestVM.ps1 against it before trusting any window run." -InformationAction Continue
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
