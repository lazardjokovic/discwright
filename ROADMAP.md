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

**Multi-disc splitting.** The largest thing still outstanding, and the one most likely to
be asked for next: anything bigger than a single disc is a known limitation today. It was
requested twice, independently, within hours of the first release. Needs a splitting
strategy, a volume label convention, and a menu that can ask for disc 2 — which is,
admittedly, most of what made the original experience feel like the original experience.

## Delivered

**More than one installer on a disc** — shipped in 0.3.0, and it was the most-asked-for
thing on this page. Several games on one disc with a chooser in the menu; DLC, expansions,
patches and mods as their own Install button on the game they belong to; and each game's
manual and extras filed with that game rather than with the disc.

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

(Two entries used to sit here. **PowerShell 7** was ruled out because the ISO builder
needed `Add-Type -CompilerParameters` with `/unsafe`, which PowerShell 6 removed; that
dependency is gone, so it moved up to *Considering*. **Non-Windows support** was ruled out
on the grounds that WinForms and IMAPI2FS have no portable replacement — still true, but
it was asked for twice and the cost is a rewrite rather than an impossibility, so it moved
up to *Considering* as well.)

## How this list changes

[Issues](../../issues) is where requests live. Forum threads scroll away; issues do not.
A request that other people turn up and agree with moves up.
