---
name: demo-gifs
description: Re-record the README demonstration GIFs by driving DiscWright, capturing frames and assembling them. Use when the demos are stale, the window has changed, or a new feature needs to be shown.
---

# Recording the demo GIFs

The GIFs in `docs/` are the first thing anyone sees of DiscWright, and until now they were
cut by hand from a screen recording, which is why they went stale. This drives the real
window the way `tests/ui` does, photographs it, and assembles the frames.

Two recordings come out of one run:

| file | what it shows |
|---|---|
| `docs/demo.gif` | one game, start to a finished ISO, ending on the build |
| `docs/multi-game.gif` | two games with add-ons, artwork and music, ending on the menu chooser |

`docs/disc.gif` is not made here. It shows Explorer with a disc mounted, not the app.

## Before running

- **The demo folder.** Everything on camera comes from `F:\DWdemo`, whose own
  `RECORDING-STEPS.md` is the human version of this procedure. That path exists so no
  username appears in any of the six text boxes the window keeps on screen. Never record
  with assets under a home directory.
- **`out` must be empty**, or the button reads REBUILD ISO instead of BUILD ISO.
- **Nothing else may touch the machine.** This drives the real pointer and the real
  foreground window. The harness stops the moment something else takes the foreground, and
  whatever that is gets photographed. Tell the user when the machine is theirs again.
- **Pillow** is needed for the assembler.

## Running it

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File .claude\skills\demo-gifs\Record-DemoGifs.ps1
python .claude\skills\demo-gifs\assemble.py "$env:TEMP\discwright-gifs\frames-demo"  docs\demo.gif
python .claude\skills\demo-gifs\assemble.py "$env:TEMP\discwright-gifs\frames-multi" docs\multi-game.gif
```

`-Only demo` or `-Only multi` records one of them. `-DemoRoot` and `-PrimeFrom` move the
fixture. Budget about ten minutes: most of it is a real 7.79 GB build, which is the point of
that GIF.

## What had to be worked out, and must not be lost

**Nobody needs to touch the machine.** The one thing that used to need a person was teaching
the game picker where the demo folder is, since with no memory it opens at GOG Galaxy's
download folder. `-PrimeFrom` solves it: opening any previously built project puts a game on
the form, the picker learns that game's parent, and New disc clears the form while keeping
what it learned.

**Folder dialogs open with the keyboard focus on OK.** Arrow keys sent at a freshly opened
Browse For Folder go to a button and the selection never moves. Tab until the Navigation
Pane reports focus first. This is why `Complete-FolderDialog`'s `-Expand` and `-Down`
arguments in `tests/ui/UiDriver.psm1` have never actually moved anything: every caller so
far just clicked OK on a path the app had already seeded.

**Step counts are measured, not assumed.** `treemap.ps1` walks the tree and reports which
DOWN count reaches which folder. Re-run it after anything is added to or removed from the
demo folder, and update the constants at the top of the recorder.

**Every folder dialog is cut from the finished GIF.** Its tree shows the OneDrive node and a
personal folder, both carrying a real name, and seeding it three levels deep does not scroll
them out of view. The recorder writes the stretches to cut by the clock, the capture writes
a timestamp for every frame, and the assembler drops exactly those frames. The 0.4.0 GIFs
cut the same segment, presumably having hit the same thing.

**The menu preview is invisible to UI Automation.** It is an HTA, so there is no rectangle
and no descendants to find, not even the window. It is moved into the captured region with
MoveWindow and its buttons are clicked by position, found by scanning three columns of the
panel for the flat button colour rather than by recomputing the layout arithmetic the menu
does itself.

**The pointer has to be drawn in.** Screen capture does not include the cursor, and a form
whose fields fill themselves with nothing on screen causing it reads as a slideshow. The
capture composites the real cursor, shape and hotspot included, so the I-beam still shows
over the text boxes. The driver glides the pointer to each control instead of teleporting.

**Dialogs need time to paint.** Typing the instant the dialog exists catches the shell
mid-draw and the recording gets a white rectangle.

**One palette for the whole film.** Give each frame its own and Pillow stores every frame
whole: 19 MB instead of 1.3 MB, because a GIF can only store the rectangle that changed
while the colours stay put. Split it by what the two halves need, 208 entries from the
busiest frame and 48 from the plainest, or the menu artwork bands into contour rings. Do not
dither: error diffusion spreads one changed pixel across everything below it, so a moving
pointer redraws the whole frame and the file goes to 13 MB.

`tools/New-DemoGif.ps1` is an earlier attempt at this from PR #10. Nothing references it and
it never produced the published GIFs.
