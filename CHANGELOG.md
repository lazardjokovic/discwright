# Changelog

Notable changes to DiscWright, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the
version stays below 1.0.0, the project file format and the on-disc layout may change
between minor versions.

## [Unreleased]

## [0.2.0] — 2026-08-18

### Added

- **The build says what it is doing.** A progress bar and a running clock while the ISO
  is written, the total time in the log and in the "build complete" dialog. The window
  keeps repainting throughout — previously the whole build ran on the interface thread
  with nothing pumping the message queue during the ISO write, so Windows greyed the
  window out and labelled it "Not Responding" for minutes at a time on a full-size game.
  It looked exactly like a crash.
- **DiscWright now says which version it is.** The title bar reads `DiscWright 0.2.0`,
  the log box opens with the version and the PowerShell it is running under, and every
  `discproject.json` records the version that wrote it — so a project file attached to a
  bug report says for itself what built the disc.

### Changed

- **The ISO builder no longer compiles with `/unsafe`.** It used an unsafe pointer to
  hand `IStream.Read` somewhere to put its byte count; four bytes of unmanaged memory do
  the same job. This was not tidying: Smart App Control, which is enforced by default on
  a clean Windows 11, refuses an assembly that combines unsafe pointers with a delegate
  called inside the copy loop — that being the shape of a shellcode loader. Measured on a
  machine with it enforced, the pointer build was blocked every time and this one is
  accepted every time, so without the change the progress bar above would have stopped
  DiscWright writing ISOs at all on those machines.

  A side effect: `/unsafe` required `Add-Type -CompilerParameters`, which PowerShell 6
  removed, and that was the one known reason DiscWright could not run on PowerShell 7.
  It is still untested there and the startup check still refuses it.

## [0.1.1] — 2026-08-18

### Added

- `ROADMAP.md` — planned work, what is under consideration, and what is deliberately out
  of scope.
- `CHANGELOG.md` — this file.

### Fixed

- **Building a disc and then rebuilding it immediately failed**, with "the old disc
  folder cannot be replaced — something is still using it" and a suggestion to close
  Explorer or eject a mounted ISO. Neither was the cause: the ISO builder left its
  COM objects alive, so DiscWright was still holding a handle on every file in its
  own staging folder. Waiting a minute and trying again worked, which made it look
  intermittent rather than reproducible.

## [0.1.0] — 2026-08-16

First public release.

### Added

- Builds a burnable ISO from a GOG offline installer folder, reading the game's name out
  of the installer and reporting which disc size the payload needs.
- Custom drive icon in This PC — takes a `.ico`, or builds a multi-size icon from any
  PNG or JPG. The icon is named after the disc rather than a fixed `disc.ico`, because
  Explorer caches drive icons by file path and would otherwise redraw the previous game's
  icon after a disc swap.
- Custom drive label, checked in advance against what the system ANSI codepage can
  actually encode, since AutoRun reads `autorun.inf` with no Unicode mode at all.
- Autorun menu as an HTA — Play, Install, Game Manual, Extras and Exit, with configurable
  background artwork, button side, optional title overlay and optional background music.
- Play locates an installed copy through the registry and stays greyed out until the game
  is actually installed; Install runs the GOG installer directly from the disc.
- Extra content: a manual, an Extras folder, and any loose files or folders to place at
  the disc root.
- **Preview menu** renders the current settings to a temporary folder so layout changes
  can be checked without rebuilding the ISO.
- Every build writes a `discproject.json` beside the ISO, which **Open existing disc...**
  loads back so a disc can be rebuilt later with one setting changed.
- Media size guidance from CD-R through BD-R XL.
- Startup check for Windows PowerShell 5.1, with an explanation rather than a stack trace
  when launched under PowerShell 7.
- `extras/DiscLabel.ps1`, a parked printable disc-face generator, kept out of the app to
  keep the tool to one job.

[Unreleased]: https://github.com/lazardjokovic/discwright/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/lazardjokovic/discwright/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/lazardjokovic/discwright/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/lazardjokovic/discwright/releases/tag/v0.1.0
