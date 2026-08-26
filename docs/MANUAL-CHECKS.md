# Manual checks before a release

The test suite covers a lot — 282 logic tests and 56 window tests — but three
things it structurally cannot reach:

- **whether a burned disc actually works.** Mounting an ISO, reading the drive
  icon out of This PC and watching AutoRun fire is a different kind of test from
  the ones in `tests/`.
- **whether the menu looks right.** A test can prove the menu's JavaScript parses
  (`tests/DiscWright.Tests.ps1`, *The menu's JavaScript is valid JavaScript*). It
  cannot tell you the caption sits where it should.
- **whether an old project file still opens.** Only real files written by older
  versions prove that, and they live on your machine, not in the repo.

So: this list. Work down it, tick as you go. Each check says what to do, what you
should see, and what a failure looks like — because "it looked fine" is not a
result if you did not know what wrong would have looked like.

**Start DiscWright** by double-clicking `Run DiscWright.cmd` in the repo root, or
the `DiscWright.lnk` shortcut.

---

## 1. The menu preview

The most valuable check on this page. The menu is ~23,000 characters of
JavaScript embedded in a PowerShell string; a test now proves it parses, but
nothing automated can see it.

- [ ] **Step 1** — *Add game...* and pick any GOG folder.
- [ ] **Step 4** — make sure the **Autorun menu** checkbox is ticked, then
      *Browse...* next to **Background image** and pick any PNG or JPG.
- [ ] **Preview menu** (top right) is now enabled. Click it.

**Should see:** the menu window opens, with the game's name as the caption above
the buttons.

**Failure looks like:** the window not opening at all (a JavaScript error the
parse test could not catch — an undefined reference rather than a syntax error);
or an **empty gap under the game's name**, which would mean the removed
*"Disc 2 of 3"* caption left an empty element behind.

> Install and Manual do nothing in preview. That is expected — the log window
> says so when the preview opens.

## 2. A real disc, end to end

This is the path where the multi-disc build function was removed and everything
now goes through the single-disc one.

- [ ] Fill in steps 1–6 for one game and **BUILD ISO**.
- [ ] Find the ISO in your output folder, right-click → **Mount**.
- [ ] Open **This PC**.

**Should see:** the mounted drive carries your disc icon and your disc label —
not a generic drive icon, not the volume id.

- [ ] Double-click the drive.

**Should see:** the menu opens. **Install** launches GOG's installer.

- [ ] Right-click the drive → **Eject** when done.

**Failure looks like:** a generic icon (the icon did not make it onto the disc),
the wrong name in This PC, or the menu not opening on double-click.

## 3. A long disc label

`Get-VolumeLabel` was simplified — it used to reserve room for a `_D2` suffix and
no longer does. The ISO9660 volume id is capped at 16 characters.

- [ ] **Step 2** — type a label clearly longer than 16 characters, e.g.
      `THE WITCHER ENHANCED EDITION`.
- [ ] Build, mount, look at This PC.

**Should see:** a sensibly truncated name. The full label is what This PC shows
via AutoRun; the 16-character limit applies to the volume id underneath.

**Failure looks like:** a name cut mid-word in an ugly place, a trailing
underscore, or a leftover `_D` fragment.

## 4. An old project file

Only if you have one written before this release.

- [ ] **Step 6** — point the output folder at a folder holding an older
      `discproject.json`.
- [ ] **Open existing disc...**

**Should see:** the games, label, icon and menu settings all come back. If that
project named a target disc, **Target disc** in step 2 comes back on it.

**Failure looks like:** an error on open, or Target disc falling back to
*Recommend a disc for me* when the project named a real one.

> Old projects carry an `ExtrasEveryDisc` field from the disc-sets feature. It is
> ignored now, which is correct — it should not cause an error either.

---

## If something fails

Note which check, what you saw, and what the log said. The log is the read-only
box beside the **BUILD ISO** button at the bottom of the window, and it records
every step of a build.

For a build failure, the disc staging folder is `disc\` inside your output
folder — **Show disc folder** opens it. What is or is not in there usually says
more than the error did.
