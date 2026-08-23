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

**Splitting one game across a set.** Half of multi-disc splitting shipped: several games
are now packed onto as many discs as they need. What is left is the harder and better
half — a single installer larger than one disc.

This turns out to be more promising than expected. GOG's installers are Inno Setup with
disk spanning switched on: run one without a `.bin` part beside it and it raises
*"Installer needs the next part (.BIN) file"* with a path box, rather than failing. It
also runs from a temp copy of itself, so ejecting disc 1 mid-install is safe, and it
defaults the path to the installer's own folder — so a set that keeps the same layout on
every disc may need nothing more than a disc swap and OK.

Needs: parts distributed across the set, a menu that explains the swap, Install disabled
on the continuation discs, and a check that the default path really does resolve after a
swap. That last one is testable by mounting two ISOs to the same drive letter in turn, no
burning required.

**What this depends on, and it is worth stating plainly.** DiscWright would not cut
anything up — GOG already ships a large game as numbered `.bin` parts, and all this
feature does is put different existing parts on different discs. Nothing is sliced,
joined or modified. So the whole idea rests on GOG continuing to split its installers
the way it does today. Were they ever to switch to a single enormous file per game,
there would be nothing left to distribute and the feature would stop applying to new
downloads.

That looks safe rather than lucky. Measured across six games from four publishers,
1 GB to 137 GB, every one slices at about 4 GB:

```
Baldur's Gate 3   137.0 GB   33 parts   4.15 GB each
Cyberpunk 2077    113.1 GB   28 parts   4.04 GB each
The Witcher         9.48 GB   3 parts   4.00 / 4.00 / 1.48
Dead Space          8.13 GB   3 parts   3.91 / 3.91 / 0.29
Alan Wake           7.79 GB   2 parts   4.00 / 3.79
Hollow Knight       1.16 GB   1 file    nothing to split
```

There is a reason it holds. 4 GiB is the largest file FAT32 can store, Inno Setup has
`DiskSliceSize` precisely for that, and GOG sets it just under the line. It is a
deliberate constraint rather than a habit, which makes it unlikely to change quietly.

The practical consequence is that **one slice is the smallest thing that can move**.
A 100 GB game is roughly 25 pieces, so two BD-R XLs or six BD-Rs. Whether a disc takes
one piece or two depends on the exact slice size rather than the tier: two of Dead
Space's 3.91 GB parts fit a DVD9's 7.95 GB, two 4.00 GB parts do not.

If a game ever did arrive as one indivisible file, nothing breaks - the planner treats a
slice as an atom, so it would refuse by name exactly as it refuses The Witcher on a DVD5
today.

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
they still look unrelated. Disc sets made that odder rather than better: a game and its
add-ons now travel to the same disc together, and then land in folders that do not say so.

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

## Delivered

**Several games across a disc set** — the first half of multi-disc splitting, requested
twice within hours of the first release. Say which disc you own and the games are packed
onto as many as it takes, in the order they sit on the form, with each game's add-ons
kept alongside it and every disc standing on its own.

**More than one installer on a disc** — shipped in 0.3.0, and it was the most-asked-for
thing on this page. Several games on one disc with a chooser in the menu; DLC, expansions,
patches and mods as their own Install button on the game they belong to; and each game's
manual and extras filed with that game rather than with the disc.

The one thing it does not cover is a mod or an installer from somewhere other than GOG
being the *game* on a disc. Add-ons accept any `.exe`, but detecting a game still means
finding a `setup_*.exe` — see *Installers that are not from GOG* under Considering.

## Later

**Say which blank each disc in a set actually needs.** A set is planned against one
medium, so every disc is assumed to be the same size — and the last disc of a set is
usually nowhere near full. Building Hollow Knight and Alan Wake for DVD9 today gives a
1.8 GB disc and a 7.8 GB one, and the first of those wastes most of an expensive blank
for no reason.

DiscWright already knows each disc's payload, so it can simply say: *disc 1, 1.8 GB, a
DVD5 will do; disc 2, 7.8 GB, needs a DVD9*. Nothing about the packing changes. It is a
line of advice, and it costs one pass over a plan that has already been computed.

The bigger version of this — telling DiscWright how many blanks of each size you own and
letting it pack accordingly — is deliberately **not** the plan. It turns a simple problem
into bin packing with heterogeneous bins, it needs a real inventory in the interface, and
on the cases tried it changes which disc things land on without changing how many discs
or which blanks get used. Advice gets nearly all of the benefit for almost none of it.

Worth doing after splitting one game across a set, not before: a single game sliced at
4 GB is where the waste becomes routine. One 4.00 GB slice on a DVD9 leaves 3.95 GB
unused on every disc in the set, and a DVD5 would have been the right blank throughout.

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

(Two entries used to sit here. **PowerShell 7** was ruled out because the ISO builder
needed `Add-Type -CompilerParameters` with `/unsafe`, which PowerShell 6 removed; that
dependency is gone, so it moved up to *Considering*. **Non-Windows support** was ruled out
on the grounds that WinForms and IMAPI2FS have no portable replacement — still true, but
it was asked for twice and the cost is a rewrite rather than an impossibility, so it moved
up to *Considering* as well.)

## How this list changes

[Issues](../../issues) is where requests live. Forum threads scroll away; issues do not.
A request that other people turn up and agree with moves up.
