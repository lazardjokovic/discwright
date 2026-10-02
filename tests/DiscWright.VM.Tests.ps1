# The virtual machine harness, as far as it can be checked without building one.
#
# Nothing here creates a VM: that takes fifteen minutes and a Windows ISO. What
# it does guard is the handful of facts that cost a working evening to find, and
# which would be silently lost if somebody tidied them away.

BeforeAll {
    $script:VmDir   = Join-Path $PSScriptRoot 'vm'
    $script:Builder = Join-Path $script:VmDir 'New-TestVM.ps1'
    $script:Harden  = Join-Path $script:VmDir 'Harden-TestVM.ps1'
    $script:Answer  = Join-Path $script:VmDir 'autounattend.xml.template'
}

Describe 'The virtual machine harness' -Tag 'Unit' {

    It 'ships a builder, a hardener and an answer file' {
        Test-Path $script:Builder | Should -BeTrue
        Test-Path $script:Harden  | Should -BeTrue
        Test-Path $script:Answer  | Should -BeTrue
    }

    It 'parses under the PowerShell the app itself needs' {
        foreach ($f in $script:Builder, $script:Harden) {
            $errs = $null
            [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$errs)
            @($errs).Count | Should -Be 0 -Because "$(Split-Path $f -Leaf) has to run on a machine with nothing installed"
        }
    }

    It 'carries no password, because this repository is public' {
        # An unattended install needs the password in clear, so the file in the
        # repository carries a placeholder and the builder substitutes one the
        # person supplies. A real password committed here would be published.
        $text = Get-Content $script:Answer -Raw
        $text | Should -Match '%PASSWORD%'
        $text | Should -Not -Match 'DiscWright20\d\d'
    }

    It 'names no particular machine or person' {
        # It is a template for anyone, not a copy of one developer's setup.
        $text = Get-Content $script:Answer -Raw
        # A literal check rather than a regular expression: a Windows path is
        # mostly backslashes, and escaping them is how the first version of
        # this test broke itself.
        $text.Contains('C:' + [char]92 + 'Users') | Should -BeFalse
        $text.ToLower().Contains('lazar') | Should -BeFalse
    }

    It 'is still valid XML after the password was taken out' {
        { [xml](Get-Content $script:Answer -Raw) } | Should -Not -Throw
    }

    It 'turns the foreground lock off, which is the whole reason a VM run works' {
        # Windows waits 200 seconds before letting a background process pull
        # another process's window forward. The window suite does exactly that
        # by design, so until this is zero every test dies at Start-DiscWright
        # claiming somebody is using the machine, with nobody at it. Three runs
        # were lost to looking for popups before this was checked.
        $text = Get-Content $script:Harden -Raw
        $text | Should -Match 'ForegroundLockTimeout'
        $text | Should -Match 'SystemParametersInfo'
    }

    It 'says in the README what cannot move to a virtual machine' {
        # Burning needs the real drive, the films need real timings, and the
        # GOG tests need downloads the VM does not carry. A green VM run is not
        # the same claim as a green run here, and the README has to say so.
        $readme = Get-Content (Join-Path $script:VmDir 'README.md') -Raw
        foreach ($thing in 'Burning', 'demo films', 'GOG downloads') {
            $readme | Should -Match $thing
        }
    }
}
