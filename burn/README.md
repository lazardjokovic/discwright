# Burning

Writing a DiscWright ISO to a real disc, and proving afterwards that the disc
holds what was built.

This is the one step in the whole project that cannot be undone. A CD-R is
written once, so everything the drive will answer for free is asked before
anything is written, and the write refuses rather than guesses.

## Before anything is written

```powershell
.\Test-BurnerSetup.ps1                       # what the drive can do, what is in it
.\Test-BurnerSetup.ps1 -IsoPath path\to.iso  # and whether that ISO would fit
```

Costs no media. It reports the recorders, the formats the hardware writes, the
disc currently loaded, whether it is blank, and how much of it is free.

## Rehearsing

`Write-IsoToDisc` supports `-WhatIf`. It runs every check and writes nothing,
which is how this gets exercised without spending a disc:

```powershell
. .\DiscWright.Burn.ps1
Write-IsoToDisc -IsoPath path\to.iso -Drive D -WhatIf
```

It refuses, by name, when there is no disc, when the disc is not blank, when the
media cannot be written, and when the ISO is larger than the free space. This
never erases rewritable media and never appends to a disc.

## Afterwards

```powershell
Test-BurnedDisc -DiscRoot D:\ -StagingFolder path\to\out\disc
```

Every file hashed on both sides. A burn that ends without an error is not the
same thing as a disc holding the right bytes, and the hash is the only check
that would catch a drive writing something subtly wrong. `-SkipHashes` compares
names and lengths only, which is faster and gives up exactly the failure worth
catching.

## What the tests cannot cover

`tests\DiscWright.Burn.Tests.ps1` covers the media table, the sector
arithmetic, the refusals and the comparison, all with no disc in the drive. The
write itself is not mocked, because a mocked burn proves nothing about a drive.

These are done by hand, with a disc meant to be spent.

Done, on a CD-R on 2026-10-01, with a 241.7 MB two-game test disc:

- [x] **An ISO burns and the disc mounts with its label.** 93 seconds on an ASUS
      DRW-24D5MT. It mounts as UDF, and `DISCWRIGHT TEST` comes back as
      `DISCWRIGHT_TEST`: the space becomes an underscore, which is the
      filesystem's rule and not something DiscWright chose.
- [x] **Every file matches by SHA-256.** 7 files, nothing missing, nothing
      unexpected, no wrong lengths, no wrong bytes. 68 seconds to read back.
- [x] **`autorun.inf` reads correctly from the disc**, CRLF and all.
- [x] **The menu runs from real optical media.** Started from the disc's own
      `AUTORUN\menu.hta`, it drew its chooser with one button per game and
      Exit. This had only ever been tested from a mounted image before, and
      Windows does not treat the two the same.

- [x] **AutoPlay offers the disc on insert and the menu opens from it.** Windows
      showed `Run DISCWRIGHT TEST`, which is the `action=` line of
      `autorun.inf`, and clicking it opened the menu. Confirmed by the person at
      the machine, not by a script. `NoDriveTypeAutoRun` is `0x9E` here, the
      Windows default, which leaves AutoRun on for optical drives alone.
- [x] **The menu launches an installer from the disc, at the right path.** It
      ran `\\?\D:\Games\02 - arcanum\setup_arcanum_1.0_(90210).exe` and showed
      its "Reading from disc can take a minute" status while doing it. The
      extended-length prefix is the menu's own, and it works from a disc.

Still open:

- [ ] An installer that actually *runs* from the disc. The first test disc
      carried 120 MB of random bytes named `.exe`, so Windows could not parse a
      PE header, assumed MS-DOS and said "Unsupported 16-Bit Application". That
      is the fixture being fake, not DiscWright being wrong, and it still proved
      the launch and the path. `New-TestDisc.ps1` now compiles a real console
      program and pads it to size afterwards: bytes appended past the end of a
      PE image are ignored by the loader, so the file is both a working
      executable and big enough to make the drive work. Verified at 5 MB and
      again at 120 MB from the staging folder. The next burn settles it on
      optical media.
- [ ] A disc burned here read in a different machine.
- [ ] A multi-disc set, which 700 MB CD-Rs make cheap to test: a few GB spans
      several discs and each burns in minutes rather than most of an hour.
- [ ] `LegacyFs` on, read on something old.

One thing to know before writing another test harness for this: the suite counts
menu buttons by sampling screen pixels, so the menu has to be in front and
uncovered, and Windows refuses the foreground to a process that does not already
hold it. A count of 0 or 1 from a script launched in the background means the
screenshot caught something else, not that the menu failed. `Set-WindowFocus`
refuses to continue for exactly this reason; a hand-written probe should do the
same rather than report a number it cannot trust.
