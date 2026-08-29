# Roadmap

What is planned for DiscWright, what is being considered, and what is deliberately out
of scope. Ordered by intent rather than by date — this is a hobby project and nothing
here carries a delivery promise.

Most of what is below came from people replying to the
[r/gog launch thread](https://www.reddit.com/r/gog/comments/1vrmyt0/made_a_free_tool_that_turns_gog_offline/).
Where something was asked for more than once, it says so — that is what moves an item up.
If what you want is missing, [open an issue](../../issues).

Version numbers are deliberately not attached to anything below. Releases get numbered
for what they turn out to contain, not the other way around — pinning a number to a
feature only creates pressure to cram things in to justify it.

## Next

**File a game's add-ons under the game, not beside it.** Requested by someone building a
multi-game disc. On the disc today every entry gets its own numbered folder, so a game
and its DLC sit as siblings:

```
Games\01 - Hollow Knight\
Games\02 - Update 1.5.12459\
Games\03 - Update 1.5.12618\
Games\04 - Ori and the Blind Forest\
```

With two games carrying three patches each that is eight top-level folders and nothing
saying what belongs to what. The menu already knows - an add-on shows as a second Install
button on its game's screen, never in the chooser - so the disc layout is the only place
they still look unrelated.

The shape would be the game's own folder holding them, the way its manual and extras
already are:

```
Games\01 - Hollow Knight\
Games\01 - Hollow Knight\Add-ons\Update 1.5.12459\
Games\02 - Ori and the Blind Forest\
```

Two things to settle first. **An orphan needs somewhere to go** - remove a game and its
add-ons become entries in their own right, in the menu and so on the disc, which means
moving back out to the top level. And **the numbering is the menu order**, so grouping
changes what the numbers count; whether add-ons keep numbers of their own inside the
folder or just use their names is a real choice rather than a detail.

Small, and worth doing. It changes where files land on a disc, so it wants its own
release note rather than riding along with something else.

**Let a game be renamed for the menu.** Asked for by the same person who found the
question marks, once the characters were arriving intact and the name was worth
reading. The name today is the installer's own `ProductName`, which is what GOG put
in the file rather than what anyone would choose - full of trademark symbols,
subtitles and edition suffixes, and sometimes just wrong.

This turned out to be half built and half broken, so what is left is smaller and
sharper than the entry above.

The renaming itself has shipped since multi-game discs arrived: the entry dialog's
**Name on the menu** box edits the name, the project file keeps it, and it already
reaches the folder on the disc. What was missing is the separation this entry used to
claim was already there. The menu matched on whatever was typed, so rewording a title
stopped Play finding the installed copy - `Install` still worked, `Play` stayed grey,
and nothing on screen said why. A match name that stays GOG's, fixed in 0.4.4, is what
made the rest of this entry true.

What is genuinely still open is smaller: the rename is only reachable through the entry
dialog, which is two clicks away and named for something else, and there is no way to
put a name back to what the installer said.

## Delivered

**More than one installer on a disc** — shipped in 0.3.0, and the most-asked-for thing
on this page: three separate requests, more than anything else. Several games on one disc
with a chooser in the menu; DLC, expansions, patches and mods as their own Install button
on the game they belong to; and each game's manual and extras filed with that game rather
than with the disc.

The one thing it does not cover is a mod or an installer from somewhere other than GOG
being the *game* on a disc. Add-ons accept any `.exe`, but detecting a game still means
finding a `setup_*.exe` — see *Installers that are not from GOG* under Considering.

## Later

**Artwork without the detour through an image editor.** Two requests that meet in the
middle. One: fetch the game's icon and cover art automatically rather than making you hunt
for a PNG. Two: build printable case inserts as well as the disc face — cover on the
front, screenshots on the back — so the printed result matches the disc.
`extras/DiscLabel.ps1` already renders a printable 120 mm disc face at 300 dpi and sits
parked in the repo, so the printing half is partly done.

The open question is where the artwork comes from. Scraping a store page adds a dependency
that breaks whenever the site is redesigned, on a tool whose whole point is that it runs
offline with nothing installed. A folder of images you point it at, or artwork lifted out
of the installer itself, may be the better trade.

## Considering

- **Installers that are not from GOG.** Detection looks for `setup_*.exe`, which is GOG's
  naming, and Play locates an installed copy through registry keys GOG's installers write.
  Someone asked about other DRM-free sources. Relaxing the filename filter is a small
  change; deciding what DiscWright claims to support is not, and the honest position today
  is that only GOG downloads have been tested.
- **Linux.** Asked for twice. This is a rewrite rather than a port — the interface is
  WinForms and the ISO builder is IMAPI2FS, both Windows-only — and the disc it produces
  is an autorun menu for Windows, which is the half Linux has least use for. What would
  carry over is the disc layout and a menu that is not an HTA. Intent only; nothing here
  is promised.
- **More than one music track.** One per disc is currently deliberate. A short playlist
  might justify the complexity. A music player will not.
- **Menu themes.** The menu is one layout with configurable artwork. Named presets would
  let a disc look like its own era instead of looking like DiscWright.
- **A presentation site.** Mainly to give people something plainer than a README to read
  before deciding to run an unsigned script.
- **PowerShell 7.** The one known blocker is gone: the ISO builder no longer compiles
  with `/unsafe`, so it no longer needs `Add-Type -CompilerParameters`. Whether anything
  else stops it — WinForms, the COM interop, apartment state — has not been tested, and
  the startup guard still refuses PowerShell 7 until somebody actually tries it.

## Not planned

Stated plainly because these come up.

- **Burning.** DiscWright writes an ISO and stops. ImgBurn, Nero and the burner built
  into Windows all do the rest well, and there is no reason to write a worse one.
- **Anything that removes DRM.** GOG installers are DRM-free by design — that is the only
  reason a tool like this can exist. DiscWright circumvents nothing and never will.

### Splitting one game across a set

This sat under *Next* for a while and was dropped in August 2026 after the research came
back. It was tested properly rather than abandoned on a guess, so here is what was found
and why the answer is no.

**It works, for some games.** GOG ships two packaging formats, and the first eight bytes
of the first `.bin` tell them apart: `idska32` is Inno Setup's own disk slices, `Rar!` is
a RAR multi-volume archive that GOG's install script unpacks with `unrar.dll`. Inno
slices span — The Witcher was installed twice from three separate discs, once from
folders and once from real UDF ISOs mounted one at a time on a single drive letter, and
both runs produced an identical 2685 files / 13.97 GB. The swap is as simple as it could
be: the dialog offers a stale path that happens to still be right, so it is eject, insert
the next disc, click OK. Ten times out of ten, no typing, no Browse.

**But that is not the experience the disc is meant to recreate.** A real multi-disc game
asked for disc 2 once, at a defined moment, in order. This asks ten times for three
discs, out of order, and goes back for discs it has already used:

```
prompts          : 10
slices asked for : 3 -> 1 -> 2 -> 1 -> 2 -> 3 -> 2 -> 3 -> 2 -> 3
```

Note prompts 2 and 4 both want slice 1 — the disc the install started from — so no disc
can be set aside once used. Inno does not read slices front to back; it installs files in
its own order and a compressed chunk can straddle a slice boundary, so it fetches
whichever part holds the bytes it needs next. That is Inno's internals showing through,
not a disc swap.

**And the count cannot be promised.** The folder run of the same game with the same three
slices asked seven times in a different order. Same input, different number. There is no
honest thing to put on screen.

**For RAR-packed games it cannot be built at all.** There is no prompt and no mechanism.
A missing volume produces `Installation failed with code: -3` and tells the user their
download is damaged — and Inno then logs `Installation process succeeded`, writes the
shortcuts and registers the game, so the failure is silent as well as misleading. Three
runs, zero prompts; a control run with every volume present completed at 100% with no
dialogs, which is what makes the other two mean anything. How much of the catalogue is
RAR is **unknown** — it cannot be determined from GOG's public metadata, which reports
that game's own part sizes wrongly. So per game it would be a coin flip that cannot be
sized in advance.

**What refusing costs is small.** The fallback is a bigger blank. BD-R XL holds 100 GB;
of the games measured only Baldur's Gate 3 at 137 GB exceeds it. DiscWright already
refuses a set by naming the game that will not fit, which stays the behaviour.

**What it saves is the interface.** Spanning would have added format detection surfaced
per game, a continuation-disc menu mode with Install and Play disabled, a screen
explaining the swap, and format-specific refusal messaging. That is a lot of new surface
for a feature that applies to few games and works poorly on those. Keeping the app simple
was the original idea and it outranks this.

This would be reconsidered if GOG's installers ever asked for slices in order, or if the
number of swaps became something that could be stated up front.

The harnesses and the raw run logs are kept in
[`docs/research/spanning/`](docs/research/spanning/), so none of the above has to be
taken on trust and nobody has to run the experiment twice.

(Two entries used to sit here. **PowerShell 7** was ruled out because the ISO builder
needed `Add-Type -CompilerParameters` with `/unsafe`, which PowerShell 6 removed; that
dependency is gone, so it moved up to *Considering*. **Non-Windows support** was ruled out
on the grounds that WinForms and IMAPI2FS have no portable replacement — still true, but
it was asked for twice and the cost is a rewrite rather than an impossibility, so it moved
up to *Considering* as well.)

## How this list changes

[Issues](../../issues) is where requests live. Forum threads scroll away; issues do not.
A request that other people turn up and agree with moves up.
