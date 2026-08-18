# Changelog

Notable changes to DiscWright, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the
version stays below 1.0.0, the project file format and the on-disc layout may change
between minor versions.

## [Unreleased]

### Added

- **DiscWright now says which version it is.** The title bar reads `DiscWright 0.2.0`,
  the log box opens with the version and the PowerShell it is running under, and every
  `discproject.json` records the version that wrote it — so a project file attached to a
  bug report says for itself what built the disc.

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

[Unreleased]: https://github.com/lazardjokovic/discwright/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/lazardjokovic/discwright/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/lazardjokovic/discwright/releases/tag/v0.1.0
