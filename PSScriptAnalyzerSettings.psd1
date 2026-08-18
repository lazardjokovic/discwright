@{
    # Two rules are switched off, both because they encode conventions for cmdlets
    # people type at a prompt - and nothing here is that. DiscWright is a WinForms
    # app whose functions are internal helpers, called from event handlers and from
    # each other, never from a command line and never down a pipeline.
    #
    # Everything else stays on. The point of switching these two off is that what
    # is left is worth reading: the repo reports 31 warnings with every rule
    # enabled and 6 with these two excluded, and a list of 31 is a list nobody
    # checks. Notably PSAvoidUsingEmptyCatchBlock stays enabled - the empty catches
    # in the app are deliberate and have been reviewed one at a time, so those 6
    # are the known set and a careless new one arrives as a seventh rather than
    # hiding among thirty.
    ExcludeRules = @(

        # Wants New-*/Set-*/Remove-* functions to declare SupportsShouldProcess so
        # they honour -WhatIf and -Confirm. That matters for a module someone
        # scripts against. New-TitleFont builds a font object.
        'PSUseShouldProcessForStateChangingFunctions'

        # Wants singular nouns: Get-PayloadByte rather than Get-PayloadBytes.
        # These functions genuinely deal in plurals, and renaming them would make
        # both the English and the code worse.
        'PSUseSingularNouns'
    )
}
