<#
    Hosts the Burn with chooser (Select-IsoHandoff) on its own, so the window
    suite can click the box that #98 asked for: Add a program..., and the
    Forget that undoes it.

    The chooser is otherwise only reachable with an ISO built and a walk or a
    button mid-flight, and what the suite needs to prove has nothing to do
    with ISOs: that a program picked through the box lands on the list and in
    the store, that the reopened dialog offers it selected, and that Forget
    takes it back out. Hosting the real function on the real desktop is the
    whole test; the picks come from the machine's own registry the way they
    would in production.

        powershell -STA -File BurnWithHost.ps1 -StoreKey <HKCU:\...> -ResultFile <file>

    The result file holds one line: CANCELLED, or CHOSEN<tab>name<tab>exe<tab>ADDED|FOUND.
#>
# The parameters are read inside the Shown handler, which the analyzer does
# not follow into, so it reports them as never read.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '',
    Justification = 'Read inside the form Shown handler, which the analyzer does not follow into.')]
param(
    [Parameter(Mandatory)][string]$StoreKey,
    [Parameter(Mandatory)][string]$ResultFile,
    [string]$AppPath
)

$ErrorActionPreference = 'Stop'

# Worked out here rather than as a parameter default: run with -File, a default
# that reads $PSScriptRoot sees it empty and the script dies before it starts.
if (-not $AppPath) {
    $AppPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'DiscWright.ps1'
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($AppPath, [ref]$null, [ref]$errs)
if ($errs -and $errs.Count) { throw "DiscWright.ps1 has $($errs.Count) parse errors" }
foreach ($f in $ast.FindAll({ param($n)
        $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($f.Extent.Text))
}

# The chooser leans on the burn module for the store and the rows, exactly as
# the real callers do after dot-sourcing it. The store is then pointed at the
# test's own key, because a test that writes into the real one would leave its
# burners on whoever runs it.
. (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'burn\DiscWright.Burn.ps1')
$script:TestStoreKey = $StoreKey
function Get-OwnBurnerKey { return $script:TestStoreKey }

# What the chooser expects to find around itself in the real app: the main
# form for the font, and $state for the file dialog's opening folder.
$state = @{ LastFileBrowse = '' }
$null = $state   # read inside functions the AST walk lifted, not here

$form = New-Object System.Windows.Forms.Form
$form.Text = 'DiscWright dialog host'
$form.ClientSize = New-Object System.Drawing.Size(420, 160)
$form.StartPosition = 'CenterScreen'

$form.Add_Shown({
    try {
        $picks = @(Get-IsoHandoffs)
        $answer = Select-IsoHandoff $picks $form
        $line = if ($null -eq $answer) { 'CANCELLED' }
                else { "CHOSEN`t$($answer.Name)`t$($answer.Exe)`t$(if ($answer.Added) { 'ADDED' } else { 'FOUND' })" }
    } catch {
        $line = "THREW`t$($_.Exception.Message)"
    }
    Set-Content -LiteralPath $ResultFile -Value $line -Encoding UTF8
    $form.Close()
})

[void]$form.ShowDialog()
