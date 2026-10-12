# The runner, which is the one script in here that nothing else can check.
#
# It is parsed and its functions are lifted out, the same way the application's
# are, because running the runner means running the suite, and a suite that
# runs itself is not a test.

BeforeAll {
    $script:RunnerPath = Join-Path $PSScriptRoot 'Invoke-Tests.ps1'
    $script:RunnerText = Get-Content -LiteralPath $script:RunnerPath -Raw
    $errs = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:RunnerPath, [ref]$null, [ref]$errs)
    if ($errs -and $errs.Count) { throw "Invoke-Tests.ps1 has $($errs.Count) parse errors" }
    foreach ($f in $ast.FindAll({ param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
        . ([scriptblock]::Create($f.Extent.Text))
    }

    $script:RBox = Join-Path ([IO.Path]::GetTempPath()) ('dwrun_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $script:RBox | Out-Null

    function New-FakeModule([string]$name, [string]$content, [bool]$withFile = $true) {
        $d = Join-Path $script:RBox $name
        New-Item -ItemType Directory -Force -Path $d | Out-Null
        if ($withFile) { Set-Content -LiteralPath (Join-Path $d 'Pester.ps1') -Value $content -Encoding Ascii }
        return $d
    }
}

AfterAll {
    if ($script:RBox -and (Test-Path $script:RBox)) {
        Remove-Item $script:RBox -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Noticing that Pester itself has been written over' -Tag 'Unit' {

    # On 12 October this suite stopped at
    #
    #     Discovery found 1388 tests in 11.93s.
    #     Running tests.
    #
    # and stayed there, with DiscWright's own window open on the desktop and
    # nobody to close it. Pester runs every block by dot-sourcing a four-line
    # file of its own, Pester.ps1, beside the module; DiscWright.ps1 had been
    # written over it, dated eight days earlier. Pester dot-sourced what it
    # believed was its helper, got an application, and blocked on ShowDialog.
    #
    # Discovery still worked, which is what made it take forty minutes: the run
    # looked like it had begun. The hazard is written up in the runner already,
    # above the Pester import - Invoke-Pester leaves a variable called $p in the
    # caller's scope holding the path of that very file - and a comment had not
    # been enough to stop it happening a second time.

    It 'is happy with the four-line file Pester actually ships' {
        $d = New-FakeModule 'good' "param (`$ScriptBlock)`r`n`r`n. `$ScriptBlock"
        Test-PesterHelper $d | Should -BeNullOrEmpty
    }

    It 'says so when the file is not there at all' {
        $d = New-FakeModule 'gone' '' $false
        Test-PesterHelper $d | Should -Match 'missing'
    }

    It 'says so when something large has been written over it' {
        # The real file is about 17 KB, most of it signature. DiscWright.ps1 is
        # 321 KB. Anything in between is still not a four-line file.
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add("param (`$ScriptBlock)")
        1..1200 | ForEach-Object { $lines.Add('# ' + ('x' * 70)) }
        $d = New-FakeModule 'fat' ($lines -join "`r`n")
        $why = Test-PesterHelper $d
        $why | Should -Match 'written over it'
        $why | Should -Match 'bytes'
    }

    It 'says so when a small file no longer runs what it is handed' {
        # The size check alone would pass this. It is the other half of what
        # tells a helper from a program.
        $d = New-FakeModule 'wrong' "param (`$Path)`r`n`r`nWrite-Host 'hello'"
        Test-PesterHelper $d | Should -Match 'no longer runs the scriptblock'
    }

    It 'names the file it is complaining about, because that is what gets put back' {
        $d = New-FakeModule 'named' "param (`$Path)"
        Test-PesterHelper $d | Should -Match ([regex]::Escape((Join-Path $d 'Pester.ps1')))
    }

    It 'passes the Pester this run is using, which is the whole point of it' {
        # If this one ever fails, the suite it is running in was about to hang.
        $live = (Get-Module Pester).ModuleBase
        $live | Should -Not -BeNullOrEmpty
        Test-PesterHelper $live | Should -BeNullOrEmpty
    }
}

Describe 'What the runner refuses to do' -Tag 'Unit' {

    It 'asks for Pester 5 by major version, never 6' {
        # 6.1.0 hangs every file in this suite in BeforeAll. The runner picks
        # the highest 5.x rather than one exact build, so a patch release does
        # not mean editing it.
        $script:RunnerText | Should -Match ([regex]::Escape('$_.Version.Major -eq 5'))
        $script:RunnerText | Should -Match 'Import-Module Pester -RequiredVersion'
    }

    It 'checks the helper before it imports anything' {
        # After the import is too late to be useful: the import itself is fine,
        # and what breaks is the first block that runs.
        $guard = $script:RunnerText.IndexOf('$why = Test-PesterHelper')
        $import = $script:RunnerText.IndexOf('Import-Module Pester -RequiredVersion $pester5.Version')
        $guard | Should -BeGreaterThan 0
        $guard | Should -BeLessThan $import
    }

    It 'tells whoever hits it how to put Pester back' {
        $script:RunnerText | Should -Match 'Install-Module Pester -RequiredVersion'
        $script:RunnerText | Should -Match 'Save-Module Pester -RequiredVersion'
    }
}
