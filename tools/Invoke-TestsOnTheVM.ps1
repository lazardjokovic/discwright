<#
    Runs the test suite on the Windows VM.

    This existed because Smart App Control was on and enforcing on the
    development machine and refused to load our own freshly built, unsigned
    assemblies: anything touching DiscWright.Platform failed there with
    0x800711C7 and passed here. It was turned off on 2026-10-16 and the whole
    suite now runs on either machine, so this is a second machine rather than
    the only one that can finish. ARCHITECTURE.md has the whole story.

    It copies the WORKING TREE, not the last commit, because the point of a
    test loop is to run the code as it is on disk this second.

    The credentials are never in this file. Set the password once per terminal:

        $env:DISCWRIGHT_VM_PASSWORD = '...'

    and this asks for it if it is not there. This repository will be made
    public, and going public publishes the whole history.
#>
[CmdletBinding()]
param(
    # Passed to dotnet test, e.g. -Filter 'FullyQualifiedName~ThePlanner'
    [string]$Filter,

    [string]$VMName = 'DiscWright-Win11',
    [string]$User = 'dw',
    [string]$Dest = 'C:\dw\cs'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = (& git rev-parse --show-toplevel)
if (-not $root) { throw 'Not inside the repository.' }
$root = $root -replace '/', [IO.Path]::DirectorySeparatorChar

# Exactly what git would track: every source file, and nothing from bin, obj
# or .vs, because .gitignore already says what does not belong.
$files = @(& git -C $root ls-files --cached --others --exclude-standard)
if (-not $files.Count) { throw 'git listed no files to send.' }

# A run that never fired must not read as a pass from the last one. The VM
# echoes this back and it is checked, which is the same trap the window suite
# fell into when job.done survived a run that never started.
$head = (& git -C $root rev-parse --short HEAD).Trim()
$dirty = if ((& git -C $root status --porcelain)) { ' with uncommitted changes' } else { '' }
$stamp = '{0}{1} sent {2:yyyy-MM-dd HH:mm:ss.fff}' -f $head, $dirty, (Get-Date)

$zip = Join-Path $env:TEMP ('dwvm_' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.zip')
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($rel in $files) {
        $full = Join-Path $root $rel
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $full, $rel)
        }
    }
    $entry = $archive.CreateEntry('vm-run-stamp.txt')
    $writer = New-Object IO.StreamWriter($entry.Open())
    try { $writer.Write($stamp) } finally { $writer.Dispose() }
} finally { $archive.Dispose() }

"{0} files, {1:N0} KB  [{2}]" -f $files.Count, ((Get-Item $zip).Length / 1KB), $stamp

$password = $env:DISCWRIGHT_VM_PASSWORD
$credential = if ($password) {
    New-Object System.Management.Automation.PSCredential($User, (ConvertTo-SecureString $password -AsPlainText -Force))
} else {
    Get-Credential -UserName $User -Message "Password for $User on $VMName"
}

$session = New-PSSession -VMName $VMName -Credential $credential
try {
    Invoke-Command -Session $session -ArgumentList $Dest -ScriptBlock {
        param($dest)
        # Wiped rather than copied over. A source file deleted here but left
        # there would keep compiling, and the suite would be testing something
        # that no longer exists.
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
    }

    Copy-Item -LiteralPath $zip -Destination "$Dest\payload.zip" -ToSession $session -Force

    $result = Invoke-Command -Session $session -ArgumentList $Dest, $Filter -ScriptBlock {
        param($dest, $filter)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $repo = Join-Path $dest 'repo'
        [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $dest 'payload.zip'), $repo)

        $env:DOTNET_NOLOGO = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        Set-Location $repo

        $arguments = @('test', '--nologo', '-v', 'q')
        if ($filter) { $arguments += @('--filter', $filter) }

        $output = & dotnet @arguments 2>&1 | Out-String

        [pscustomobject]@{
            Stamp    = (Get-Content (Join-Path $repo 'vm-run-stamp.txt') -Raw).Trim()
            Summary  = (($output -split "`n") | Where-Object { $_ -match 'Passed!|Failed!|error ' } | Select-Object -First 4) -join "`n"
            Failures = (($output -split "`n") | Where-Object { $_ -match '\[FAIL\]' }) -join "`n"
            Output   = $output
        }
    }

    if ($result.Stamp -ne $stamp) {
        throw "The VM answered for a different run. Sent [$stamp], it replied [$($result.Stamp)]. Nothing below can be trusted."
    }

    ''
    '--- on DiscWright-Win11 ---'
    if ($result.Failures) { $result.Failures; '' }
    $result.Summary.Trim()

    if ($result.Summary -match 'Failed!') { exit 1 }
} finally {
    Remove-PSSession $session
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
}
