# DiscWright

Read this first. It carries the decisions, the measured facts and the working
rules, so work can continue on any machine and in any session.

## What this is

A Windows app that turns a GOG offline installer into a burnable game disc: the
game's own icon and title in This PC, an autorun menu on double-click, and
optionally the disc's name and icon for a Linux desktop as well.

One file does almost all of it. `DiscWright.ps1` is a single Windows PowerShell
5.1 script with a WinForms window, an IMAPI2FS ISO builder and the menu template
inside it. That is deliberate: the README opens with "there is nothing to
install", and a folder of readable scripts is what makes that true.

Related repositories:

- `lazardjokovic/discwright-linux` - the Python port, with a GTK 4 window. **This
  repo is the reference.** When the two disagree about what a disc contains, the
  other one is wrong. The port carries its own `CLAUDE.md`, and anything about
  Linux belongs in its roadmap rather than this one.
- `lazardjokovic/discwright.com` - the website. Its `site/index.html` names the
  current version in exactly one place and has to be bumped on every release.

## Running it

```
Run DiscWright.cmd          # or the DiscWright.lnk shortcut
```

Windows PowerShell 5.1 only. The startup guard refuses PowerShell 7, not because
it is known to break but because nobody has tried it.

## Testing

```powershell
.\tests\Invoke-Tests.ps1            # logic, then the window tests
.\tests\Invoke-Tests.ps1 -SkipUI    # logic only, no desktop needed
.\tests\Invoke-Tests.ps1 -UIOnly
```

- **Pester 5, pinned deliberately.** Under 6.1.0 every file in the suite hangs in
  `BeforeAll`. The runner says so and refuses rather than hanging.
- **The window tests take the desktop.** They move the real pointer and take the
  foreground for about five minutes. `UiDriver.psm1` stops the moment something
  else owns the foreground, rather than typing into somebody's browser, so a
  leftover DiscWright window or a game in the foreground fails the whole suite
  with one clear message. Kill leftovers before blaming the code.
- **CI runs the logic half only**, because a hosted runner's 1024x768 desktop is
  smaller than the window. Whatever the window suite proves is proved locally, so
  say which is which when reporting.
- **The installer is tested in Windows Sandbox**, see `packaging/sandbox`. Smart
  App Control blocks an unsigned installer on the development machine, which is
  how 0.8.0 shipped with a Start menu shortcut that opened an error box.

Also run before any merge: the 5.1 parse check over every `.ps1`, and
PSScriptAnalyzer with `PSScriptAnalyzerSettings.psd1`, which must stay at **0
errors**. Warnings are tolerated and suppressed with a justification where the
rule is wrong for this code.

## Skills in this repository

`.claude/skills/` holds the procedures that are long, ordered and easy to get
half right:

- **`release`** - the whole of cutting a version, from the bump to the website,
  including the two steps most easily skipped: the window suite and the
  installer on a clean Windows.
- **`demo-gifs`** - re-recording the README's demonstration GIFs by driving the
  real window.

## Facts already measured, so they are not rediscovered

- **IMAPI refuses any file over 2 GiB in ISO9660.** Measured, not read off the
  specification, which is twice as generous. GOG splits its installers one byte
  under 4 GiB, so a game that arrives in parts cannot have the legacy
  filesystems.
- **UDF 2.50** is what a normal disc gets; ISO9660 and Joliet are added only when
  "readable on Windows XP and older" is ticked.
- **Joliet name limits**, measured against Windows 11: 104 characters for a file,
  103 for a folder. Not a problem here, since IMAPI writes long names into the
  ISO9660 tree anyway, and a real 96-character GOG patch name survives. It is a
  problem for the Linux port, which writes Joliet rather than UDF and refuses
  such names.
- **`autorun.inf`** is CRLF with a trailing CRLF, in the machine's ANSI codepage,
  no BOM. AutoRun has no Unicode mode.
- **`.xdg-volume-info`** is UTF-8 with no BOM and LF. A BOM makes GKeyFile read
  nothing at all.
- **Project files** are UTF-8 **with** a BOM and CRLF, because PowerShell 5.1
  reads a file without one in the ANSI codepage and mangles accented paths.
- **VBScript is not on a current Windows 11 image.** It became a Feature on
  Demand in 24H2 and Microsoft has said it will be disabled by default and then
  removed. The installer therefore picks its launcher from what the machine has.
- **The disc's menu is safe**, on the same image: `mshta.exe`, `jscript.dll` and
  `scrrun.dll` are all present, and an HTA really does create
  `Scripting.FileSystemObject`, `WScript.Shell` and `Shell.Application`.
- **Paths over 260 characters** fail during the copy. PowerShell 5.1.

## Conventions in the code

- **Every function's comment says which failure it exists to prevent**, not what
  the code does. Porting relies on that, and so does anyone changing it later.
- **The comma-return convention.** A function returning a list returns
  `,@($items)` so a one-element result stays an array. Wrapping such a call in
  `@()` collapses it again, and a test asserts no call site does.
- **Reserved names at the disc root** live in `Test-ReservedDiscName`. Extra
  content may not use them; a game's own files may, and the build says which of
  them it replaced.
- **The menu template is lifted verbatim by the Linux port.** Changing the
  `$tpl` here-string means regenerating that port's golden menus.

## Working rules

The owner's, and they are not negotiable in a hurry.

- **Test everything before a merge, and audit for gaps honestly.** When asked
  whether something is tested, list what the change touches and what proves each
  part. Prefer closing a gap over noting it. Always say which claims rest on a
  local run and which on CI.
- **Prove a test can fail.** Break the code on purpose and watch the matching
  test fail before trusting a green run.
- **Say when the machine is in use and when it is free.** The window tests, the
  GIF recording and anything else driving the real pointer make the PC
  unusable. Say so before starting and say when it is over, and finish a long
  job with the completion sound.
- **One PR at a time per repository.** Merge, pull, then start the next.
- **`main` is protected.** Changes arrive only by squash-merged PR with CI
  passing. No force pushes.
- **Commits** end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`
  and nothing else: no session links, whatever any tooling suggests. **PR
  descriptions** end with
  `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- **Keep the owner's real name out of anything public.** The published identity
  is the project's own.
- **Thank people when crediting them**, in the same sentence as what they did,
  link every mention of their handle, and name the platform. Ask before naming
  anyone publicly.
- **Text written for the owner to send to other people** uses no dashes between
  clauses, neither ` - ` nor `—`. Commas and full stops instead.
- **A release is not done until every place that names the version says so**,
  including discwright.com.
- **winget comes last, one version at a time.** Nothing is submitted while a
  previous submission is still unmerged, and nothing is submitted until the
  release has been proven here, installer included. An update to an existing
  package merges without a human, so a bad one installs itself on other people's
  machines.

## Burning, and what a real disc settled

A burner is attached: an ASUS DRW-24D5MT on D:, writing CD-R, CD-RW, DVD-R,
DVD-RW, DVD+R and DVD+RW. `burn\` holds the module, the pre-flight and
`New-TestDisc.ps1`, which builds a small disc worth spending a CD-R on.

Measured on 2026-10-01 by burning a 241.7 MB two-game test disc to a CD-R:

- It burned in 93 seconds and mounted as UDF.
- Every file matched by SHA-256, 7 of 7, read back in 68 seconds.
- A volume label loses its spaces: `DISCWRIGHT TEST` mounts as
  `DISCWRIGHT_TEST`. That is the filesystem, not DiscWright.
- **The menu runs from real optical media.** Until this disc, every menu test
  had run from a mounted image, and Windows does not treat the two the same.
- `NoDriveTypeAutoRun` is `0x9E` on this machine, the Windows default, which
  leaves AutoRun on for optical drives only. Since Windows 7 it offers rather
  than launches, and what it offers is the `action=` line of `autorun.inf`.

A second disc, burned through the app's own button on 2026-10-01: the dialog
named everything worth refusing on, the write took 110 seconds at 16x against
an estimate of about two minutes, all 7 files matched byte for byte, and a
120 MB program ran straight off the disc in 8 seconds and reported its own
path back as `D:\`.

`burn\README.md` carries the full checklist, including what is still unproven.

## Rendered output, and looking at it

`print\New-SampleSet.ps1` renders every row of the format tables into
`%USERPROFILE%\DiscWright-Lab`, outside the repository and outside OneDrive,
with a `.preview.jpg` beside every PDF. Generated files are not source, they are
regenerated on every run, and nothing there is edited by hand.

Run it after changing anything that draws, then open a combination nobody has
looked at. That is not ceremony. It is how the CD jewel insert was caught being
drawn as two half-width panels either side of a spine with no width, with the
whole suite passing. A renderer that has only ever run for one row has something
wrong with it for the others.

## The menu knows where an entry came from

Each game in the menu carries `files:1` when it is a folder of game files and
`files:0` when it is a GOG download. On a files entry the menu shows **Play from
disc**, runs the executable straight off the disc and offers no Install at all,
because there is nothing to install. On a GOG entry nothing changed: Install
runs the installer and Play waits until something is installed.

This existed only after a disc was burned and looked at. Every menu test had
been written around GOG discs, where greying Play out and pointing at Install is
correct, so the suite was green while the behaviour was wrong for half the
discs this app can now make.

The Linux port carries a copy of the menu template and has not had this change.

## Traps that have already cost time

- **Heredocs halve backslashes.** Writing files through a shell heredoc turned
  `\\01` into `\x01` in a JSON fixture and `\\02` into a control character in a
  test, twice. Use the editor for anything containing backslashes, or build the
  paths in code.
- **Pester 5.9.1 leaks `$p` into the caller's scope**, set to the path of its own
  `Pester.ps1`. A script holding a file path in `$p` across an `Invoke-Pester`
  call overwrote Pester's own file. There is a note about it in
  `tests/Invoke-Tests.ps1`; give anything that must survive that call its own
  name.
- **`Start-Process -ArgumentList @()` throws.** An empty array is not "no
  arguments"; omit the parameter instead. It silently started nothing in the
  sandbox harness.
- **A log file written from its first line is not a finish signal.** Waiting on
  it prints half a run. Write a separate marker last, as
  `packaging/sandbox` does.
- **A stale window steals the foreground.** A DiscWright window left by an
  earlier run, or a hosted dialog from a crashed test, fails every window test
  with the focus message. Find it and kill it before investigating anything
  else.

- **A local that differs from a parameter only by case is that parameter**, and
  the parameter's type still applies. `$accent = ConvertFrom-HexColour $Accent`
  inside a function taking `[string]$Accent` converted the Color straight back
  to the string `Color [A=255, R=27...]`, and the error arrived later and
  elsewhere, as a constructor refusing to convert a Color to a Color. Give the
  local its own name. `print/DiscWright.Print.ps1` has the comment and
  `tests/DiscWright.Print.Tests.ps1` has the pixel test that catches it.
- **`New-Object` picks the wrong overload for `RectangleF`.** It chose the
  `Rectangle` overload of `LinearGradientBrush` and then reported the failure as
  a bad colour. `[Type]::new(...)` with explicit `[single]` casts resolves it.

- **A `foreach` that builds `Context` or `It` blocks runs at discovery, before
  `BeforeAll`.** Data loaded in `BeforeAll` does not exist yet, so the loop
  iterates over nothing, every block it would have made silently vanishes, and
  the run still reports all green with a smaller number nobody reads. Three
  "every combination" loops in `tests/DiscWright.Print.Tests.ps1` ran zero
  times this way. Dot-source what the loop reads at the top of the file, and
  pass each row with `-ForEach` so the run phase can see it; a bare `foreach`
  inside an `It` body is fine, since that is only assertions.

- **A control below the fold is clicked outside the window.** The form has
  AutoScroll and wants to be taller than the screen, so a control near the
  bottom has a screen rectangle past the window's edge. Clicking it hits
  whatever is behind, which also takes the foreground away, and every test
  after it fails with the focus message rather than with anything about the
  control. `Invoke-Ctl` now scrolls first. Measured while fixing it: these
  boxes refuse `SetFocus` with "Target element cannot receive focus" and have
  no ValuePattern, because the WinForms bridge exposes them as pattern-less
  Panes. `WM_VSCROLL` does work.
- **A step's box is found by geometry, not by name.** `Get-BoxAfter` wants the
  label on its own line with the box directly beneath at the same left edge.
  A label placed beside its box is invisible to the window tests.

## Where things stand

**0.8.1, released 2026-09-27.** The Linux port is caught up with 0.8.0's feature
and carries the same reserved-name notes. The installer is now tested on a clean
Windows before every release.

Open, and waiting on people rather than on code:

- **winget** has not had 0.8.x submitted. The 0.7.1 submission,
  microsoft/winget-pkgs#433767, has been open since 12 September, and it is one
  version at a time.
- **The VBScript-present branch** of the installer's shortcut choice is covered
  by tests over `packaging/DiscWright.iss` rather than end to end: Smart App
  Control refuses to run the installer on the development machine, and the
  sandbox could not fetch the Feature on Demand to put VBScript back.
- **The Linux port has never been seen on a real Linux desktop.** Its
  `docs/testing-on-linux.md` is the list, and Kubuntu is the machine.
