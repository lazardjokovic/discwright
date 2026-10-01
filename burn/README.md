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

These are done by hand, with a disc meant to be spent, and the answers belong in
`CLAUDE.md` once they are known:

- [ ] An ISO burns and the disc mounts with the volume label DiscWright set.
- [ ] Every file on the disc matches the staging folder by SHA-256.
- [ ] The autorun menu appears on insert, on a real optical drive rather than a
      mounted image. Windows treats the two differently and only the disc
      settles it.
- [ ] The menu's buttons work from the disc, including a game's installer.
- [ ] A disc burned here reads in a different machine.
- [ ] A multi-disc set, which 700 MB CD-Rs make cheap to test: a few GB spans
      several discs and each one burns in minutes rather than the better part of
      an hour.
- [ ] `LegacyFs` on, read on something old.
