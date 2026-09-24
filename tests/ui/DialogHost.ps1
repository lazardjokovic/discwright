<#
    Hosts one of DiscWright's dialogs on its own, so the window suite can look at
    it and click it.

    The folder question (Show-FolderInstallerDialog) is otherwise only reachable
    by picking a real folder in the shell's folder browser, which no test can
    navigate to a fixture reliably. Hosting it puts the real dialog on the real
    desktop with a folder of the test's choosing, and writes back what the
    dialog returned - which is the part that decides whether an entry is added.

        powershell -STA -File DialogHost.ps1 -Folder <dir> -ResultFile <file>

    The result file holds one line: CANCELLED, NONE, or INSTALLER<tab><path>.
#>
# Both parameters are used, inside the Shown handler - which the analyzer does
# not follow into, so it reports them as never read.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '',
    Justification = 'Read inside the form Shown handler, which the analyzer does not follow into.')]
param(
    [Parameter(Mandatory)][string]$Folder,
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

# The dialog centres on the main window and borrows its font, so there has to be
# one. Named for the test, never for a person to use.
$form = New-Object System.Windows.Forms.Form
$form.Text = 'DiscWright dialog host'
$form.ClientSize = New-Object System.Drawing.Size(420, 160)
$form.StartPosition = 'CenterScreen'

$form.Add_Shown({
    try {
        $answer = Show-FolderInstallerDialog $Folder
        $line = if ($null -eq $answer) { 'CANCELLED' }
                elseif ($answer -eq '') { 'NONE' }
                else { "INSTALLER`t$answer" }
    } catch {
        $line = "THREW`t$($_.Exception.Message)"
    }
    Set-Content -LiteralPath $ResultFile -Value $line -Encoding UTF8
    $form.Close()
})

[void]$form.ShowDialog()
