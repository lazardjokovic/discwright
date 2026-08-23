# Manual test - disc sets (PR #24)

Branch `feat/multi-disc-splitting`. Launch with:

```
powershell -ExecutionPolicy Bypass -File C:\Users\lazar\DiscWright\DiscWright.ps1
```

Every expected string here was produced by running the current code against the real
games in `F:\DWdemo`. They are exact, spacing included. A difference is a finding,
not a rounding error.

| Game | Detected name | Size |
|---|---|---|
| Hollow Knight | `Hollow Knight` | 1.16 GB |
| Alan Wake | `Alan Wake` | 7.79 GB |
| Dead Space | `Dead Space` | 8.13 GB |
| The Witcher | `The Witcher - Enhanced Edition` | 9.48 GB |

**Make `C:\dwtest` first and use it as the output folder.** The demo folders are
junctions to `C:`, so an output folder on `C:` lets DiscWright hardlink the installers
instead of copying them across volumes - about a minute for the two-disc build instead
of about three.

Around fifteen minutes end to end, most of it test 6.

---

## 1. Untouched, nothing has changed

The feature has to be invisible until asked for.

1. **Add game...** -> `F:\DWdemo\Hollow Knight`
2. Target disc reads **`Fit on one disc (recommended)`**
3. The line under the list reads exactly:

```
Detected: Hollow Knight  (1 file, 1.16 GB)   ->   Disc: fits DVD5 4.7 GB (single layer)
```

That is 0.4.1's wording. Anything mentioning *discs of* here means the automatic
setting has stopped behaving the way it used to.

Step 4 should read plain **`Manual file:`** and **`Extras folder:`**, and step 5
**`5)  Extra content (copied to the disc root as-is)`**.

---

## 2. The dropdown answers the question

Open **Target disc** with Hollow Knight still the only game. Every row carries its own
answer:

```
Fit on one disc (recommended)
CD-R 700 MB  -  will not fit
DVD5 4.7 GB  -  1 disc
DVD9 8.5 GB (dual layer)  -  1 disc
BD-R 25 GB  -  1 disc
BD-R DL 50 GB (dual layer)  -  1 disc
BD-R XL 100 GB  -  1 disc
```

Rows that cannot work are **selectable on purpose**, not greyed. Pick `CD-R 700 MB`:

```
Detected: Hollow Knight  (1 file, 1.16 GB)   ->   Hollow Knight is 1.16 GB, too big for a CD-R 700 MB
```

That sentence is the reason for not greying the row - a disabled row could name
neither the game nor its size. Then `DVD5 4.7 GB`:

```
Detected: Hollow Knight  (1 file, 1.16 GB)   ->   1 disc, DVD5 4.7 GB
```

**A single disc is never called a set** - `1 disc,` and not `1 discs of`.

Back to `Fit on one disc (recommended)` and the old wording returns.

---

## 3. Two games, and the disc decides

**Add game...** -> `F:\DWdemo\Alan Wake`.

| Choose | Line reads |
|---|---|
| `DVD9 8.5 GB (dual layer)` | `2 games (8.94 GB)   ->   2 discs of DVD9 8.5 GB` |
| `BD-R 25 GB` | `2 games (8.94 GB)   ->   1 disc, BD-R 25 GB` |
| `DVD5 4.7 GB` | `2 games (8.94 GB)   ->   Alan Wake is 7.79 GB, too big for a DVD5 4.7 GB` |

The rows follow along - on `DVD9` the list now reads `2 discs`, and CD-R and DVD5
`will not fit`.

**The refusal names the game and its own size.** The line begins with 8.94 GB, so
without `is 7.79 GB` it would read as though the total were the problem.

---

## 4. The case that is deliberately refused

Select Alan Wake, **Remove**. **Add game...** -> `F:\DWdemo\The Witcher`.

| Choose | Line reads |
|---|---|
| `DVD9 8.5 GB (dual layer)` | `2 games (10.64 GB)   ->   The Witcher - Enhanced Edition is 9.48 GB, too big for a DVD9 8.5 GB` |
| `BD-R 25 GB` | `2 games (10.64 GB)   ->   1 disc, BD-R 25 GB` |

That first line is the longest one these games can produce. **It must not be cut off
mid-word.** Ending in `...` is acceptable if hovering shows the whole line in a
tooltip; a hard cut is a bug.

Splitting one game across discs is the next change, not this one. What matters here is
that it refuses by name instead of building something unburnable.

---

## 5. A game and its patches stay together

1. **New disc**, answer **Yes**
2. **Add game...** -> `F:\DWdemo\Hollow Knight`
3. **Add-on...** -> `F:\DWdemo\Hollow Knight\patch_hollow_knight_1.5.78.11833a_(85515)_to_1.5.12459_(88294).exe`,
   belongs to **Hollow Knight**, name `Update 1.5.12459 (88294)`
4. **Add game...** -> `F:\DWdemo\Alan Wake`
5. Target disc: `DVD9 8.5 GB (dual layer)`

Three entries, and the line reads:

```
2 games + 1 add-on (8.95 GB)   ->   2 discs of DVD9 8.5 GB
```

**Two discs, not three.** Three would mean a group was split and the patch stranded
away from its game.

---

## 6. Build the set and look at it

Keep the form from test 5 and fill in the rest.

1. Disc label: **`RETRO NIGHT`**
2. Step 3 icon: `F:\DWdemo\artwork\alanwake-icon.ico`
3. Step 4 background: `F:\DWdemo\artwork\alanwake-background.jpg`
4. Step 4, tick **Extras** first (Browse stays disabled until you do), then
   **Extras folder: Browse...** -> `F:\DWdemo\media\DiscExtras`. The
   **Also put the manual and extras on every disc of a set** box in step 5 must stop
   being greyed the moment the folder is chosen
5. Output folder: `C:\dwtest`

The line now reads:

```
2 games + 1 add-on (8.95 GB) + 5 MB extra   ->   2 discs of DVD9 8.5 GB
```

### 6a. The form says where the extras go

**Look at the labels, not the boxes** - this is easy to walk straight past. With the
target on `DVD9` and two discs planned, step 4 and step 5 read:

- **`Manual file (disc 1):`**
- **`Extras folder (disc 1):`**
- **`5)  Extra content (copied to disc 1 of the set)`**

Flip the target back to `Fit on one disc` and all three lose the note; flip to `DVD9`
and it returns. If they never show the note at all, that is a bug - it was verified
working, so a failure here means something regressed. This is the disclosure that the every-disc option, which lives down in
step 5, governs step 4 as well.

### 6b. Build

**BUILD ISO.** The confirmation names both files rather than saying "the ISO":

```
Build a set of 2 discs in:
C:\dwtest

These will be written:
  RETRO NIGHT D1.iso
  RETRO NIGHT D2.iso
```

While it runs the elapsed line reads **`D1/2  45%  0:32`** and later **`D2/2 ...`**.
The bar fills twice; without the tag the second pass reads as the first restarting.

Afterwards `C:\dwtest` holds exactly:

```
RETRO NIGHT D1.iso        about 1.2 GB
RETRO NIGHT D2.iso        about 7.8 GB
discproject.json          ONE file, describing the whole set
disc\                     staging, holding disc 2
```

**One project file.** Two ISOs and a project describing only Alan Wake would mean the
last disc overwrote it.

### 6c. Mount both

Right-click each ISO -> **Mount**.

| | Disc 1 | Disc 2 |
|---|---|---|
| Name in This PC | `RETRO NIGHT D1` | `RETRO NIGHT D2` |
| Icon | the Alan Wake icon | the same icon |
| Menu caption | `HOLLOW KNIGHT` above `Disc 1 of 2` | `ALAN WAKE` above `Disc 2 of 2` |
| Chooser | none - one game | none - one game |
| Install buttons | Install, plus one for the patch | Install only |
| `Extras\` at the root | **present** | **absent** |
| Extras button in the menu | works | **greyed** |

The four that matter most:

- **The two names in This PC differ.** The same name on both would mean the volume id
  truncated past the disc number.
- **The menu says which game and which disc.** Without it the two discs are
  indistinguishable - same icon, same artwork.
- **The patch is on disc 1, with Hollow Knight.** Not on disc 2, and not as a game of
  its own in a chooser.
- **`Extras\` is on disc 1 only**, and disc 2 greys the button rather than offering a
  broken one.

---

## 7. Extras on every disc

1. Back in the app, tick **Also put the manual and extras on every disc of a set**
2. The three labels from 6a lose their `(disc 1)` note - the checkbox now says it in
   words, so repeating it would be noise
3. **BUILD ISO** again, **Yes** to replacing both
4. Mount `RETRO NIGHT D2.iso`: `Extras\` is now **present** and the Extras button works

Untick it, rebuild, and disc 2 goes back to having no `Extras\`.

With these extras at 5 MB the disc count does not change, which is correct - the
option moves 5 MB onto each disc, nowhere near enough to need another. It would matter
for a 2 GB folder.

**Then start a new disc with nothing on the form: the checkbox is greyed out**, because
with no manual, no Extras folder and no extra content it governs nothing.

---

## 8. Reopening remembers the disc

1. **New disc**, **Yes**
2. Output folder -> `C:\dwtest`
3. **Open existing disc...**, accept `C:\dwtest`

The dropdown must come back on **`DVD9 8.5 GB (dual layer)  -  2 discs`** and the line
on `2 discs of DVD9 8.5 GB`, with all three entries and the patch still filed under
Hollow Knight. The every-disc tick comes back the way you left it.

This is the one that was broken: the project file was written correctly and the read
path dropped it, so every reopen silently fell back to `Fit on one disc`.

---

## 9. A project from before this change

`F:\DWdemo\out\discproject.json` was written by 0.4.1 and has no target disc in it.

1. **New disc**, **Yes**
2. Output folder -> `F:\DWdemo\out`
3. **Open existing disc...**

It must open on **`Fit on one disc (recommended)`** with the old `Disc: ` wording, and
the every-disc box unticked. Older projects described a single disc and have to keep
describing one.

---

## 10. New disc puts everything back

1. Choose any medium, tick the every-disc box
2. **New disc**, **Yes**
3. Dropdown reads `Fit on one disc (recommended)`, box unticked, labels plain

Also: on a completely fresh form **New disc is greyed out**. Choosing a medium and
nothing else should wake it, since that is now a change worth discarding.

---

## What counts as a bug

- Any disc in a set missing its menu or icon
- Two discs with the same name in This PC
- A menu that does not say which game or which disc it is
- `Extras\` on disc 2 with the box unticked, or missing from disc 2 with it ticked
- A patch on a different disc from its game, or showing as a game of its own
- A chooser on a disc holding one game
- The plan promising a number of discs and the build writing a different number
- A status line cut off mid-word with no tooltip behind it
- Anything at all different while `Fit on one disc` is selected

## Tidying up

Eject both mounted discs, then:

```
Remove-Item C:\dwtest -Recurse -Force
```
