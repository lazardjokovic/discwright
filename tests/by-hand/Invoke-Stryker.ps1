<#
.SYNOPSIS
    Runs a mutation pass over one project, on the test VM.

.DESCRIPTION
    tools\Mutate.py plants faults somebody chose, one round at a time, and is
    the right tool while a change is being written: the faults are the ones
    that change is about, and watching each guard fail is the point.

    This is the other half. Stryker plants every fault it can think of across a
    whole project and says what fraction the suite objects to, which is the
    only honest answer to "are these tests worth anything".

    IT RUNS ON THE VM, and that is not a preference. Stryker rebuilds the test
    assembly for every batch of mutants, and Smart App Control on the
    development machine refuses to load a freshly built unsigned one
    (0x800711C7). A blocked run looks exactly like a suite that caught nothing,
    which is the trap Mutate.py carries a whole paragraph about.

.PARAMETER Project
    Which project to mutate. The view models hold most of the decisions, so
    they are the default.

.PARAMETER VMName
    The Hyper-V machine. Needs DISCWRIGHT_VM_PASSWORD set, as the test runner
    does.

.EXAMPLE
    $env:DISCWRIGHT_VM_PASSWORD = '...'
    tests\by-hand\Invoke-Stryker.ps1
    tests\by-hand\Invoke-Stryker.ps1 -Project DiscWright.Core.csproj
#>

[CmdletBinding()]
param(
    [string] $Project = 'DiscWright.ViewModels.csproj',
    [string] $VMName = 'DiscWright-Win11',
    [string] $User = 'dw',
    [string] $Dest = 'C:\dw\cs\repo'
)

$ErrorActionPreference = 'Stop'

$secret = $env:DISCWRIGHT_VM_PASSWORD
if (-not $secret) { $secret = Read-Host "Password for $User on $VMName" -AsSecureString -OutVariable null }

$password = if ($secret -is [securestring]) { $secret } else { ConvertTo-SecureString $secret -AsPlainText -Force }
$credential = New-Object System.Management.Automation.PSCredential($User, $password)

Write-Host "Sending the working tree, then mutating $Project on $VMName." -ForegroundColor Cyan
Write-Host "This takes minutes rather than seconds. It plants every fault it can think of." -ForegroundColor DarkGray

# The same send the test runner does, so the mutants are planted in the code as
# it is on disk this second rather than as it was at the last commit.
& "$PSScriptRoot\..\..\tools\Invoke-TestsOnTheVM.ps1" -Filter 'FullyQualifiedName~ThereIsNoSuchTest' | Out-Null

$found = Invoke-Command -VMName $VMName -Credential $credential -ArgumentList $Project, $Dest -ScriptBlock {
    param($project, $dest)

    $ErrorActionPreference = 'Continue'
    Set-Location (Join-Path $dest 'tests\DiscWright.Tests')

    & dotnet-stryker --project $project 2>&1
}

$found | Select-Object -Last 14

# The score, said again on its own, because it is the only line that matters
# and it arrives at the bottom of several hundred.
$score = $found | Select-String 'final mutation score' | Select-Object -Last 1

if ($score) {
    Write-Host ""
    Write-Host $score.ToString().Trim() -ForegroundColor Green
}

Write-Host ""
Write-Host "What survived is on the VM, under tests\DiscWright.Tests\StrykerOutput." -ForegroundColor DarkGray
Write-Host "A survivor is a fault the suite does not object to. Each one is either a" -ForegroundColor DarkGray
Write-Host "test worth writing or a line that cannot be wrong; say which, do not" -ForegroundColor DarkGray
Write-Host "lower the threshold." -ForegroundColor DarkGray
