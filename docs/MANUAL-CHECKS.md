# Manual checks before a release

The automated suite is 374 logic tests and 73 window tests, and it runs in about
four minutes:

```
.\tests\Invoke-Tests.ps1
```

It builds a real ISO, mounts it, and reads it back through Windows' own storage
stack. It opens project files written by every older schema, including a real one
0.4.2 wrote. It opens the menu and checks the characters in a game's name come
out the other side. **Do not add a check to this page for anything in that list**
— there is a table at the bottom saying where each of those lives.

What is left here is the short list a machine cannot do: things only a person can
judge, and things only a person can trigger.

**Start DiscWright** by double-clicking `Run DiscWright.cmd` in the repo root, or
the `DiscWright.lnk` shortcut.

## Finding things in the window

The window is one column of numbered steps, and the numbers are on screen — you
never have to count controls to find one.

| On screen | What it is |
| --- | --- |
| **1)  Installers on this disc** | the games list, with **Add game...** and **Add-on...** |
| **2)  Disc label (shown in This PC)** | the label box, and **Target disc:** beside it |
| **3)  Disc icon** | the `.ico` / `.png` / `.jpg` picker |
| **4)  Autorun menu** | the menu on/off tick, background image, title, buttons |
| **5)  Extra content** | files copied to the disc root as-is |
| **6)  Output folder** | where the ISO and the `disc\` staging folder go |

Across the top sit **New disc**, **Open existing disc...**, **Show disc folder**
and **Preview menu**. **BUILD ISO** is at the bottom, beside the log box.

---

## 1. Does the menu look right?

A test opens the preview, proves the JavaScript ran without a script error, and
reads the window title back to confirm the characters survived. UI Automation
cannot see inside the rendered document, so **layout is the whole of this check**.

- [ ] In **4)  Autorun menu**, tick **Autorun menu**.
- [ ] Click **Browse...** beside **Background image** and pick any PNG or JPG.
- [ ] Click **Preview menu**, top right. (It stays greyed until both of the above
      are done — hover it and the tooltip says which is missing. You do not need
      a game.)

**Should see:** the caption sits above the buttons with the buttons evenly spaced
under it, nothing overlapping the panel edge, nothing clipped, and the text
readable against your background.

**Failure looks like:** a caption running under the panel edge or off it; buttons
crowded against the bottom; an empty gap under the caption; text the same tone as
the background behind it.

> **Install** and **Manual** do nothing in preview. The log box says so when the
> preview opens.

The interesting case is a long name with many buttons — a game with four add-ons
already needs eight buttons, and the menu shrinks them to fit. That shrink is
arithmetic no test is watching the result of.

## 2. Does This PC draw your icon and label?

A test mounts a built ISO and confirms `autorun.inf` names your icon, that the
icon file is really on the disc, and that the volume label Windows reports is the
one the app computed. Whether **Explorer draws it** is shell behaviour — and
Explorer caches icon bitmaps per path, which is the whole reason the icon is
named after the disc rather than `disc.ico`.

- [ ] Build a disc, then right-click the ISO → **Mount**.
- [ ] Open **This PC**.

**Should see:** the drive carries your icon and your disc label, in full — not a
generic drive icon, not the shortened volume id underneath.

**Failure looks like:** a generic optical-drive icon; **the icon from a disc you
mounted earlier** (the cache defeating the per-disc naming); the volume id
(`THE_WITCHER_ENHA`) instead of the label you typed.

- [ ] Right-click the drive → **Eject** when done.

## 3. Does AutoRun fire?

Tests confirm `autorun.inf` is present and well formed and that everything it
points at exists on the disc. Whether Windows *acts* on it depends on AutoPlay
policy and a real double-click, neither of which a test can supply.

- [ ] With the ISO still mounted, double-click the drive in This PC.

**Should see:** the menu opens.

**Failure looks like:** the drive opening as a folder, or nothing happening. Check
AutoPlay is not switched off in Settings before blaming the disc.

## 4. Does a real GOG installer install?

Every fixture in the suite is a sparse stub with a GOG-shaped filename. Nothing
in the repo has ever been a real installer, so nothing automated has ever watched
one run.

- [ ] Before you install anything, note what is already registered:

```powershell
.\extras\Show-RegisteredGames.ps1
```

- [ ] From the mounted disc's menu, click **Install**.

**Should see:** GOG's installer starts and completes against a real download.

- [ ] Run it again.

**Should see:** the game you just installed is now in the list, with `ExeThere`
true. **That is the whole of this check** - everything the menu does with those
keys afterwards has a test.

- [ ] If the game registers itself, reopen the menu and click **Play**.

**Should see:** the game launches. Play finds an installed copy through the
registry keys GOG's installer writes, matched on the game's registered name.

> A renamed game matching on its original name is covered by tests now, against
> the menu's own matcher - so what is left for you here is whether a real
> install registers itself at all, which no test can arrange.

To ask the question the menu asks - would Play find this? - give the helper the
name the entry matches on:

```powershell
.\extras\Show-RegisteredGames.ps1 -Match 'Alan Wake'
```

It runs the menu's own `nameHit`, lifted out of `DiscWright.ps1` and executed
under cscript, so its answer cannot drift from the menu's. Useful for the rename
case: rename a game to something **reworded** rather than shortened and the two
names still have to agree.

**Failure looks like:** Install doing nothing (the path on the disc and the path
in the menu disagree), or Play saying it cannot find the game after a successful
install.

## 5. A burned disc

Only if you are burning one. Everything above is about an ISO; a burner is a
different piece of hardware with its own failure modes.

- [ ] Burn, put it in a drive, and work through checks 2 to 4 on the real disc.

---

## What is already automated, and where

Do not re-add any of these as a manual check.

| Covered | Where |
| --- | --- |
| ISO is real UDF 2.50 and passes an integrity read | *Building a disc* → `inside the finished ISO` |
| ISO mounts; drive letter, filesystem, read-only | *The finished ISO as Windows itself reads it* |
| Volume label Windows reports matches the app's own | same |
| No trailing `_` or leftover `_D` in the volume id | same, and *Get-VolumeLabel* |
| Full label survives into `autorun.inf` | same |
| Icon and menu named in `autorun.inf` exist on the disc | same |
| The installer is reachable at the path the menu was given | same |
| Menu JavaScript parses, and defines what it calls | *The menu's JavaScript is valid JavaScript* |
| Menu opens without a runtime script error | *Previewing the menu* |
| `™` and `®` reach the screen intact | same, and *A game name with characters outside plain ASCII* |
| Project files from v1, v2, v3 and a real 0.4.2 file | *Project file* |
| A renamed game still matches on the name GOG registered | *Renaming a game for the menu* |
| A pre-schema-6 project recovers its match name on open | same |
| `ExtrasEveryDisc` from the removed disc-sets feature | same |
| Target disc saved and restored | same, and *Reopening a project that named a target disc* |
| Rebuilding a disc whose own folder holds the installers | *Rebuilding a disc folder that the installers themselves live in* |
| Assets and extra content picked from inside that folder | same, and *Rebuilding over a disc that is already there* |
| A second build straight after the first still works | same |
| Add-ons land inside their game's folder on a real staged disc | *Building a two-game disc that has add-ons on it* |
| Folder numbers count games, and add-ons renumber inside each | *Filing an add-on under the game it belongs to* |
| A single game keeps the flat disc root, add-ons beside it | same |
| A promoted orphan moves back out to the top level | same |
| Change... opens on a one-game disc, which is where the name lives | *Renaming a game for the menu* |
| The add-on choice greys itself when there is no game to attach to | same |
| The name goes back to the one the installer reported | same |
| A build driven from the window, start to ISO on disk | *The form while a real build runs* |
| The form locks during a build and restores what was enabled | *Locking the form while a build runs* |

One thing deliberately has **no** test: whether the form *looks* frozen while a
build runs. The only moment it is observable from outside is while the completion
box is up, and a modal box makes UI Automation report every control on its owner
as disabled anyway — a window test written that way passes with the lock removed
entirely. The lock is tested on real controls instead, and the wiring is checked
in the syntax tree.

---

## If something fails

Note which check, what you saw, and what the log said. The log is the read-only
box beside the **BUILD ISO** button at the bottom of the window, and it records
every step of a build.

For a build failure, the disc staging folder is `disc\` inside your output
folder — **Show disc folder** opens it. What is or is not in there usually says
more than the error did.
