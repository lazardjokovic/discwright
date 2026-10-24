# The seven things only a real disc can answer

Everything else in this folder is covered by the suite. These are not, and no
amount of faking will cover them, because what they test is whether the real
drive does what the fakes have been told it does.

They are written so your part is **putting a disc in and pushing a tray**.
Every step says what to expect, so nothing is left to judgement.

## The two drives, and why each one is here

| | |
|---|---|
| **D:** ASUS DRW-24D5MT | shuts its own tray. The ordinary case. |
| **E:** HL-DT-ST BD-RE BU40N | **never** shuts its own tray, so it is the only way to reach the code that waits for a hand. It lives on a USB cable. |

That difference is the whole reason E: is in these cases. It is not a fault, it
is the rig.

**Give E: a minute after plugging it in.** Asked straight away it answers
nonsense and then worse nonsense: on 2026-10-24 it reported a blank CD-RW as
"not blank, erasing would help", then "this drive cannot write data discs" with
nothing readable, and only after the app let go of it and it had settled did it
report what was actually in it, which was a blank CD-RW all along. The program
is right to refuse a drive that says it cannot write, so twenty minutes went
into a drive that was still waking up. Ask it twice before believing it.

## What to have ready

- **two CD-RWs**, because they can be erased and used again. Everything here
  uses them; nothing needs a disc you cannot get back.
- the fixtures: `tests\by-hand\Build-TheFixtures.ps1`
- about forty minutes, most of it waiting

Before each run, know what is in each drive. This prints it without touching
anything:

```powershell
Get-CimInstance Win32_CDROMDrive | Select-Object Drive, Caption, MediaLoaded
```

---

## 1. A disc really reads back after it is written

**Covered in the suite by** a fake that says the files match.
**Not covered:** whether a real disc, written by IMAPI and read back off the
real drive, actually matches.

1. Put a **blank or erased CD-RW in D:**.
2. Add `fixtures\size-fits-a-cd`, choose **CD-R 700 MB**, pick an output folder.
3. **Build it**, then **Burn to a disc**.
4. Leave it alone. The tray will open and shut by itself.

**Expect:** it says the disc is good, naming no files. If it names files that
could not be read, the disc is spent and that is the finding.

> If two files fail on the same disc twice running after a full erase, it is
> the disc. That happened here with a CD-RW that had bad regions. Try the other
> one before reporting anything.

---

## 2. A tray that will not shut stops the set, and says which disc

**This is the one that needs E:**, and the one your hands are for.

1. Put a **blank or erased CD-RW in E:**.
2. Build a set of **two** discs: `fixtures\size-three-cds` at **CD-R 700 MB**
   makes more than two, so use `size-one-too-big-for-a-cd` with
   **Split a file too big for one disc** ticked, which makes exactly two.
3. **Burn to a disc** and choose **E:** when asked.
4. When it asks for the next blank, **do not push the tray in.** Wait.

**Expect:** it waits, and says it is waiting, rather than carrying on. Nothing
is written to disc 2.

5. Now push the tray in.

**Expect:** it picks up and writes disc 2.

**Then do it again and answer no**, or close the question, and expect:

> No disc of the set is finished. It stopped at disc 1 of 2…

and **never** "Nothing was written" about a blank that is spent.

---

## 3. Erasing a real rewritable

1. Put a **used CD-RW in D:** (one of the discs from case 1 will do).
2. **Expect:** *Erase a disc* lights up on its own.

   > The line under WHAT SIZE OF DISC says nothing here, and that is right.
   > It only ever speaks when the disc and the chosen size **disagree**, and
   > putting a CD-RW in moves the size to CD-R 700 MB by itself, so they
   > agree. This step used to expect that line and it was the document that
   > was wrong, not the program.
3. Press it.

**Expect:** it names the disc and the drive and asks before doing anything. Say
no once and check nothing happened. Then say yes.

**Expect:** under a minute, and afterwards the drive reports a blank CD-RW and
*Erase a disc* has gone grey again.

---

## 4. Swapping a disc while the window is open

The suite covers this with a fake drive that changes its mind. What it cannot
cover is whether Windows tells the program at all.

1. Open the app with **nothing in either drive**.
2. Put a **blank CD-RW in D:**.

**Expect, without touching anything:** WHAT SIZE OF DISC moves to **CD-R
700 MB**, the drive appears in WHICH DRIVE, and if a game is on the window the
disc count and the red line follow.

3. Take it out and put a **blank DVD-R** in instead.

**Expect:** the size moves to **DVD5 4.7 GB** on its own.

4. Now choose a size by hand, and swap the disc again.

**Expect:** the size stays where you put it, and the amber line says what is in
the drive disagrees with it.

---

## 5. The menu on a real disc

An HTA is invisible to UI Automation, so **nothing on the disc's own menu is
testable at all**. All of it is this case.

Build and burn one disc carrying everything: a game, an icon, a background, a
manual, a folder of extras and the tune from `fixtures\music\tune.mp3`.

Put it in a machine and check, in this order:

- [ ] it opens by itself, or This PC shows the disc's name and icon
- [ ] the background is the picture you chose
- [ ] **the tune plays, and loops**
- [ ] the music control is at the **top left** and silences it
- [ ] the big button says **Install** for a GOG game
- [ ] **Manual** and **Extras** are there and open the right things
- [ ] **Exit** closes it and the music stops with it

> Known and not worth reporting: the backdrop is cropped by about 38 pixels at
> the bottom, because the stage and the window are both 480 tall and the window
> has a title bar. Anything at the bottom of the panel is invisible, which is
> why the music control is at the top.

---

## 6. An add-on is dead until its game is installed

On the disc from case 5, with a game that has add-ons:

1. Open the menu on a machine **without** the game installed.

**Expect:** the add-on buttons look dead, and the line under them says why.

2. **Press one anyway.**

**Expect:** nothing happens. A patch applied to a game that is not there
half-applies to nothing and leaves no sign that it did, which is why it is
refused in the code as well as greyed in the page.

3. Install the game, go back to the menu.

**Expect:** the big button now says **Play**, and the add-ons are live.

---

## 7. A folder of files, with one thing to run and with two

The suite proves the window asks and that the menu is told. Only a real disc
proves the game starts.

1. Burn `fixtures\files-one-to-run`. Nothing is asked when it goes on.

**Expect on the disc:** **Copy and play** copies the folder and **runs it**.

2. Burn `fixtures\files-two-to-run`. **You are asked which file starts it.**

**Expect:** pick `TheGame1.exe` and the disc runs that one.

3. Burn it again and press **Cancel** at the question.

**Expect on the window:** a line saying the menu will copy it and open the
folder. **Expect on the disc:** exactly that, and no attempt to run anything.

---

## What the last pass found, and when

**2026-10-24, on this machine**, with two CD-RWs. Cases 1, 2 and 3 were judged
and all three pass. The rest still want a second machine, or the menu, which no
amount of driving the window will cover.

| | |
|---|---|
| **1** a disc really reads back | **pass.** D:, 1.8 MB written at 10x in 51 seconds, the drive reported ten times while it wrote, it shut its own tray, and all three files hashed back off the disc matching what was built. |
| **2** a tray that will not shut | **pass.** Built "a set of 2 discs" from the 1.2 GB fixture with splitting on, asked which drive because both held blanks, wrote disc 1 to E: in eight minutes and read it back, then said *"Disc 1 is done. Put a blank in for disc 2 of 2."* with *"waiting for a blank disc for disc 2 of 2"* on the bar, and touched nothing until the blank was in. It picked up the moment it was told, wrote disc 2, and finished with *"All 2 discs are written and every file checked."* The note beside the discs says **done** against both. |
| **3** erasing a real rewritable | **pass.** *Erase a disc* lit on its own, the question named both discs by letter and by drive model, Cancel left both alone, and after saying yes the disc came back blank and the button went grey. |
| **D12** *Use that one* with nothing picked | **confirmed**, and already known: it closes as if you had cancelled. |

Two things worth knowing for the next pass, both about how to watch rather than
what to watch:

- **The sentence about the whole set arrives in a `MessageBox`**, which is a
  separate top-level window. Anything reading "the window" through UI
  Automation is blind to it, and it waits for OK, so a watcher that only polls
  the main window sees the run stop dead and nothing more.
- **The question between discs and the question about the tray are different
  questions.** Watching for the words "blank" or "next" catches the first and
  misses the second.

## When something here fails

Say which case, which drive, and what was in it. The drives report themselves
with the command at the top, and that is usually the first thing worth knowing.

A burn that fails its read-back is not automatically a bug: try a different
blank first, because a rewritable with bad regions fails the same files every
time and looks exactly like a program that cannot write.
