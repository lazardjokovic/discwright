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

**More than one installer on a disc.** This arrived as two separate requests that turn out
to be one feature. Several games on a single disc — a 25 GB BD-R holds a lot of older
games, and somebody asked for the Resident Evil trilogy on one. And DLC, expansions or a
mod that ships as its own installer, which today has to go in Extras where it is just a
file in a folder, and which deserves a button next to Install instead.

Both are the same thing underneath: this disc holds more than one installer, and each gets
its own entry in the menu. The only difference is whether the entry is labelled a game or
an add-on.

Half of it is already built. A disc can stage any number of installers into numbered
`NN - Name` folders, and the project file records them. What is missing is the interface
for adding them and a menu that presents the choice — plus a rethink of how Play decides a
game is already installed when there is more than one candidate.

Three people asked for some form of this, more than for anything else on this page.

## Later

**Multi-disc splitting.** Listed today as a known limitation: anything larger than a
single disc is out of scope. It was requested twice, independently, within hours of the
first release, so it moves onto the list. Needs a splitting strategy, a volume label
convention, and a menu that can ask for disc 2 — which is, admittedly, most of what made
the original experience feel like the original experience.

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
