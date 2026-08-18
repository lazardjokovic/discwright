# Roadmap

What is planned for DiscWright, what is being considered, and what is deliberately out
of scope. Ordered by intent rather than by date — this is a hobby project and nothing
here carries a delivery promise.

Most of what follows came from people replying to the
[r/gog launch thread](https://www.reddit.com/r/gog/comments/1vrmyt0/made_a_free_tool_that_turns_gog_offline/).
If what you want is missing, [open an issue](../../issues).

## Planned — v0.2.0

**Several games on one disc.** A 25 GB BD-R holds a lot of older games. Put more than
one installer on a disc and let the menu ask which to install. Needs a per-game section
in the project file, a chooser in the menu, and a rethink of how Play decides a game is
already installed when there is more than one candidate.

**"Install DLC" as its own menu button.** DLC that ships as a separate installer
currently has to go in Extras, where it is just a file in a folder. It deserves a button
next to Install.

## Planned — later

**Multi-disc splitting.** Listed today as a known limitation: anything larger than a
single disc is out of scope. It was requested twice, independently, within hours of the
first release, so it moves onto the list. Needs a splitting strategy, a volume label
convention, and a menu that can ask for disc 2 — which is, admittedly, most of what made
the original experience feel like the original experience.

**Case and disc-face artwork.** `extras/DiscLabel.ps1` already renders a printable
120 mm disc face at 300 dpi and sits parked in the repo. The request is to go further:
build front and back case inserts from store artwork — cover on the front, screenshots
on the back — so the printed result matches the disc without a detour through an image
editor.

## Considering

- **More than one music track.** One per disc is currently deliberate. A short playlist
  might justify the complexity. A music player will not.
- **Menu themes.** The menu is one layout with configurable artwork. Named presets would
  let a disc look like its own era instead of looking like DiscWright.
- **A presentation site.** Mainly to give people something plainer than a README to read
  before deciding to run an unsigned script.

## Not planned

Stated plainly because these come up.

- **Burning.** DiscWright writes an ISO and stops. ImgBurn, Nero and the burner built
  into Windows all do the rest well, and there is no reason to write a worse one.
- **Anything that removes DRM.** GOG installers are DRM-free by design — that is the only
  reason a tool like this can exist. DiscWright circumvents nothing and never will.
- **Non-Windows support.** The interface is WinForms and the ISO builder is IMAPI2FS.
  Both are Windows-only and neither has a portable replacement worth the rewrite.
- **PowerShell 7.** The ISO builder needs `Add-Type -CompilerParameters` with `/unsafe`,
  which PowerShell 6 removed. Windows PowerShell 5.1 ships with Windows, so requiring it
  costs nobody an installation.

## How this list changes

[Issues](../../issues) is where requests live. Forum threads scroll away; issues do not.
A request that other people turn up and agree with moves up.
