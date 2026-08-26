# Changelog

Notable changes to DiscWright, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the
version stays below 1.0.0, the project file format and the on-disc layout may change
between minor versions.

## [Unreleased]

### Changed

- **Target disc now answers "does it fit", not "how many discs".** Pick the blank
  you are going to burn and the line under the installer list says *fits DVD5
  4.7 GB*, or how far over it is. Leave it on *Recommend a disc for me* and
  DiscWright picks a size, exactly as it always did.

- **A refusal names what has to come off, and by how much.** *"This disc comes to
  9.12 GB, and a DVD9 8.5 GB (dual layer) holds 7.95 GB - 1.17 GB over. Remove an
  entry in step 1, or choose a larger disc in step 2."*

### Removed

- **Packing several games onto a set of discs.** Built, never released - so
  nothing anyone has run is losing a feature.

  It was dropped because the split was the tool's decision to make and should not
  have been. The packer filled discs in whatever order the rows happened to sit
  in, but which games belong together on a disc is curation, not arithmetic: a
  compilation is something a person assembles. It also labelled the results `D1`
  and `D2` as though one disc continued the other, when nothing on either disc
  ever depended on the other. And because the disc label is seeded from the FIRST
  game, a two-disc build could label a disc with a game that was not on it -
  `ALAN WAKE D2` holding Dead Space.

  Checking the launch thread settled it. What people asked for was *several games
  on one disc* - three requests, more than for anything else, shipped in 0.3.0 and
  untouched by this - and *one game split across discs*, a different feature that
  was tested and refused. A set of several games across several discs was asked
  for by nobody.

  What replaces it: DiscWright says whether what you picked fits the disc you are
  going to burn, and you decide what to leave off. To make a second disc, reopen
  the project, swap the games and rebuild - icon, background, music and buttons
  all carry over.

  Gone with it: the *"Also put the manual and extras on every disc of a set"*
  option, the `D1`/`D2` label and volume-id suffixes, the *Disc 2 of 3* line in
  the menu, and the per-disc progress tag. The menu still names the game whose
  screen you are on, which is worth having on any disc.

### Fixed

- **Play now offers Forces of Corruption on the Empire at War Gold Pack.** The
  0.4.1 fix that asks which game to launch reads GOG's `goggame-<id>.info` and
  keeps the tasks marked `category` `game` or `launcher`. That key is optional,
  and Empire at War's real file leaves it off all eight of its tasks — so every
  one was discarded, the list came back empty, and Play went on launching the
  base game with no chooser at all. The bundle the whole feature was built for
  was the one bundle it did not work on.

  A file that names no categories anywhere is now sorted by what each task points
  at: `.exe` and `.lnk` start something, `.pdf` and `.rtf` are the manual. The
  fallback only applies when the file names no categories at all. The Witcher's
  file names them on five tasks and omits the key on exactly one — Safe Mode,
  which runs the same executable as the real entry with an extra argument — so
  there the omission is deliberate and is still honoured.

- **A launch button could read `... War.lnk`.** Empire at War's primary task
  carries no `name` either, and the filename that stood in for it kept its
  extension.

- **`\u` escapes in a task name came out raw.** GOG writes them — that game's own
  title is `STAR WARS®: Empire At War™ Gold`. Unescaping is now one
  pass over the string rather than a chain of replacements, which also removes
  the question of which order the chain should have run in.

### Still not handled

- **One game bigger than one disc.** A build is refused, naming what will not fit,
  and will stay that way. Spreading a single installer's `.bin` parts across
  several discs looked promising - GOG's installer asks for the next part itself,
  which makes a real "insert disc 2" possible - so it was built as a test harness
  and run against real mounted discs. It works, and it is still not worth
  shipping: the installer asks for parts out of order, keeps returning to discs it
  has already read, and asked seven times in one run and ten in another for the
  same three-disc game. That is not a disc swap, and the count cannot be stated up
  front. For the RAR-packed installers GOG also ships there is no mechanism at all
  - a missing part reports a corrupt download. ROADMAP.md records the full
  evidence under *Not planned*. Use a larger blank.

## [0.4.1] — 2026-08-23

### Fixed

- **A project file could put extra directives into `autorun.inf`.** Found by
  auditing the repo before pointing more people at it. Nobody has reported it, and
  it is not reachable through the window.

  `New-AutorunInf` wrote the disc label straight into a line-based file, so a label
  carrying a carriage return did not stay a label:

  ```ini
  label=My Game
  open=Extras\payload.exe        <- came from inside the "label"
  action=Run My Game
  open=Extras\payload.exe        <- and again, the label is reused here
  ```

  The label box is a single line and cannot produce this. A `discproject.json` can
  — and assigning to a single-line text box does **not** strip control characters,
  so a loaded project carried them all the way to the build. That assumption, "the
  box is single line so it cannot happen", is what let it through.

  Windows 7 and later will not silently run an `open=` from optical media; it
  offers it in the AutoPlay prompt. But the wording of that prompt and the file it
  points at would both have been chosen by whoever wrote the project file, on a
  disc the person burning it believed was theirs — and project files are meant to
  travel. This repo suggests attaching one to a bug report.

  Control characters are now stripped by every writer: `autorun.inf`, the menu's
  JScript strings and the HTML title. They are stripped on load as well, so the box
  shows the label that will actually be built rather than one thing on screen and
  another on the disc.

- **A game name containing a newline broke the whole menu.** Same cause, duller
  consequence: a newline inside a JScript string literal is not a character to
  encode, it ends the literal — so JScript refused the file and the disc opened to
  nothing. One bad name took the entire menu with it.

  Quoting was never the problem. `"`, `\`, `<` and `>` were already escaped
  correctly and the HTA is written as ASCII. It was only the characters that end a
  line, in both writers.

## [0.4.0] — 2026-08-23

### Added

- **`New disc` — start a fresh disc without restarting the app.** There has always
  been an `Open existing disc...` with no counterpart: the only way back to an empty
  form was to close DiscWright and open it again. That is fine once and tiresome by
  the third disc of an evening.

  It clears the installer list, the disc label, the icon, every menu setting and the
  extra content, and puts the log back to the line a fresh window opens with. It asks
  first, and it is greyed out when there is nothing to clear, so the dialog only ever
  appears when something would actually be discarded. Nothing on disk is touched — a
  disc already built stays where it is.

  **The output folder is deliberately kept.** Three discs made in one sitting go to
  the same place, and it is the one field that would have to be retyped every time.

- **Play asks which one, for a bundle that ships two games in a single installer.**
  Star Wars: Empire at War Gold Pack installs both the base game and the Forces of
  Corruption expansion, and registers **one** GOG game name with **one** executable.
  Play could therefore only ever launch Empire at War; Forces of Corruption sat in
  the next folder with nothing able to reach it. Reported by someone who did a full
  build and install and said exactly what they saw, which is why it could be fixed
  at all.

  GOG writes a `goggame-<id>.info` beside every installed game listing its play
  tasks, and a bundle lists both games there. DiscWright now reads it: two or more
  launchable entries and Play opens a chooser, the same way the disc asks which game
  when it holds more than one. Back returns to the game's screen.

  **A game with one launch target is completely unaffected** — Play launches it
  directly, exactly as before. Checked against four real installs — The Witcher
  Enhanced Edition, Resident Evil 0, Alan Wake and Hollow Knight — which all report
  a single target, with manuals, support links, the hidden raw executable behind
  GOG's launcher and the Safe Mode variant all correctly left out.

### Changed

- The top row now holds four buttons in the width that held three, so
  `Open existing disc...`, `Show disc folder` and `Preview menu` are each somewhat
  narrower. Nothing moved rows and no label is clipped.

- **`Add game...` reopens where a game was last picked from**, and remembers it
  across `New disc` and across removing every entry — where your GOG downloads
  live does not change because you started a second disc. On a fresh start with
  nothing to go on it opens GOG Galaxy's own download folder, if that exists.

  Previously the first pick of a session had no starting folder at all, so Windows
  opened the tree wherever it happened to be — which on a machine with a
  OneDrive-redirected Desktop is several expansions away from anything useful. The
  dialog does not remember on its own: it is the old `SHBrowseForFolder` tree, and
  opening it once and cancelling teaches it nothing.

- **The disc icon, background, music, manual and extras pickers are aimed too.**
  They ask, in order: the folder their own box already points at, then wherever a
  file was last picked from, then the first game's folder — because GOG downloads
  its extras beside the installer they belong to. A game's *own* manual and extras
  start in that game's folder rather than the last one touched.

  Only picking a file updates the shared memory; choosing a folder reads it but
  does not write it, so setting an extras folder cannot quietly move where the icon
  picker opens next. A box pointing at something since deleted falls back to its
  parent rather than giving up, which puts you next to where you were.

  This is the same wart as above, on five more dialogs. It is worth naming what it
  actually cost: recording the two README demos, every one of those pickers opened
  on the Desktop and put the author's account name on screen, and both recordings
  had to be re-cut to take it out.

### Fixed

- **`REBUILD ISO` claimed it would replace an ISO it was never going to touch.**
  The ISO is named from the disc label, so building one disc as `ALAN WAKE` and the
  next as `The Witcher` into the same output folder correctly produces two files —
  DiscWright does not delete an ISO it did not write, because it cannot tell its own
  leftovers from something you put there.

  The button did not say that. It went by "is there *any* `.iso` in this folder", so
  it read `REBUILD ISO` for the second disc and offered to "replace the ISO already
  in the output folder", then wrote a new file and left the old one alone. The
  confirmation dialog repeated the same claim.

  Both now go by the ISO **this label** writes, and name the file: `REBUILD ISO`
  means that exact file is about to be overwritten, and `BUILD ISO` means nothing of
  that name is there yet. When a differently named ISO is present the dialog says so
  — that it will be left alone — rather than implying it is about to go.

  A leftover `disc\` staging folder no longer makes the button say `REBUILD` either.
  It is rebuilt from scratch by every build and holds nothing that is not also in the
  ISO, so it was warning about nothing. It is still called out separately in the
  confirmation, since anything added to it by hand really does get lost.

- **An add-on's Install button no longer offers to run before its game exists.**
  A patch, a piece of DLC or a mod is applied *on top of* the game it belongs to.
  The menu enabled those buttons purely on whether the file was present on the
  disc, so on a freshly inserted disc every one of them was live — and clicking one
  produced an error from GOG's installer several clicks later, which is a poor place
  to learn the rule. They now need the game installed as well, using the same
  registry check `Play` already relies on, and the tooltip distinguishes the two
  reasons a button can be grey: the installer is missing from the disc, or the game
  is not installed yet.

  This does not sequence anything. GOG's patches are incremental and have to be
  applied in order; DiscWright shows them in the order they were added and cannot
  tell which have already been applied. Add them in the order they should be run.

- **Removing the last entry left the disc label and the status line behind.** The
  size and media summary under the list stayed exactly as it was — still green,
  still naming a payload and a disc type that nothing on the form accounted for.

  The disc label was worse than untidy. Adding a game to an empty form seeds the
  label from its name, and seeding only fires into an *empty* box. So removing the
  game left its name there, the next game added never replaced it, and swapping
  one game for another built a disc carrying the previous game's name in This PC.
  DiscWright now takes back only the label it typed itself: one loaded from a
  project, or typed by hand, survives an empty list even if it happens to match a
  game's name.

  `New disc` always cleared both of these. `Remove` never did.

## [0.3.1] — 2026-08-21

### Fixed

- **The disc icon preview was drawn on top of its own Browse button.** Step 3's
  44px preview square and the 24px Browse button were placed in the same column on
  the same row, so whichever Windows painted last covered the other. The preview now
  sits on the row below the button.

  The window test suite checks that no two controls overlap and would have caught
  this immediately. It could not run: `tests\Invoke-Tests.ps1` asked for "Pester 5 or
  later" and got Pester 6.1.0, under which every file in the suite hangs before
  running anything — while opening a DiscWright window, so it looked like the
  application freezing on startup rather than the tests failing to start. The runner
  now asks for Pester 5.x specifically, and the suite reported the overlap on the
  first run afterwards.

## [0.3.0] — 2026-08-20

### Added

- **A disc can hold more than one game, and the menu asks which.** Step 1 is a list
  now rather than a single folder box. With two or more games the disc menu opens on a
  chooser; picking one leads to its own Play / Install / Manual / Extras menu, with Back
  to return. A disc with one game behaves exactly as before, chooser and Back button
  included: it has nothing to choose, so it shows neither.
- **Add-ons get their own button next to Install.** DLC, an expansion, a GOG patch or a
  mod that ships as its own installer can be added and told which game it belongs to. It
  then appears as an extra Install button on that game's screen instead of in the
  chooser, which is where a piece of DLC does not belong. Until now the only place for a
  second installer was Extras, where it was a file in a folder.

  An add-on is picked as a **file**, not a folder, and may be **any `.exe`** — the
  `setup_*.exe` rule exists to recognise a GOG game folder, and applying it to add-ons
  excluded the two things people actually have. GOG ships patches as `patch_*.exe`, and a
  mod installer is named whatever its author chose. Several can be added at once.

  Add-ons are named from their filename rather than from the installer's version
  information, because every GOG patch reports the ProductName of the game it patches —
  all four Hollow Knight patches call themselves "Hollow Knight", which would have put
  four identical buttons on the disc. The name can be edited, and the edit is saved.

  These two are one feature. A DLC and a second game are both another installer that
  needs its own entry in the menu; the only difference is where the entry is shown.

- **A game's manual and extras belong to that game.** Each entry can have its own
  manual and its own folder of extras, chosen in the same dialog that says whether it is
  a game or an add-on. On the disc they sit beside that game's installer, and its menu
  screen points at them.

  Before this there was one manual for the whole disc, and its button appeared on every
  game's screen — so on a two-game disc, one of them opened the other game's manual.
  A disc-wide manual and Extras folder still work and are still there; an entry with
  none of its own falls back to them, which is exactly how a single-game disc behaves.

- Project files record which entries are add-ons, what they belong to, and each entry's
  own manual and extras (schema version 4). Files written by 0.1.x and 0.2.0 still open,
  and read back as all games with no add-ons and nothing of their own, which is what
  those discs were.

### Changed

- **Each Browse button now opens where its own box points.** `FolderBrowserDialog`
  remembers the last folder used anywhere in the app, so after a build the game-folder
  Browse reopened on the disc it had just written. The two folders are named alike
  enough that picking the wrong one looked like the app losing the game.
- **Picking a built disc folder says so.** Choosing the output folder instead of the GOG
  download used to give the generic "no `setup_*.exe` here", which sends you looking for
  a problem with your download. If the folder holds a `disc\` with an installer in it,
  DiscWright now says it is a disc it built and which folder step 1 actually wants.

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

[Unreleased]: https://github.com/lazardjokovic/discwright/compare/v0.4.1...HEAD
[0.4.1]: https://github.com/lazardjokovic/discwright/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/lazardjokovic/discwright/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/lazardjokovic/discwright/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/lazardjokovic/discwright/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/lazardjokovic/discwright/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/lazardjokovic/discwright/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/lazardjokovic/discwright/releases/tag/v0.1.0
