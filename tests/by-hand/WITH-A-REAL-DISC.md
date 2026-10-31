# The eight things only a real disc can answer

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

- [ ] **the whole backdrop shows**, down to its bottom edge. It used to be
      cropped by about 38 pixels, the height of the title bar, because the
      window was asked to be 480 tall outside and the stage is 480 tall inside.
      The menu measures itself now and asks for the difference.

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

## 8. Installing a set from its menu

The suite installs a set of two from two folders, under cscript, with the real
robocopy and the real `copy /b`. Only a real drive proves the swap is noticed.

1. Build `fixtures\size-one-too-big-for-a-cd` at **CD-R 700 MB** with
   splitting on, which makes exactly two, and burn both discs.
2. Put **disc 1** in a machine and open the menu.

**Expect:** the big button says **Install, disc 1 of 2**, and the line under
the buttons says *Nothing is copied yet. Install copies this disc, then asks
for disc 2.*

3. Press it.

**Expect:** a thin bar under the buttons and *Copying file N of M off this
disc*, then *Put disc 2 in. This carries on by itself once it is there.* The
menu stays open. Nothing is asked.

4. Swap the disc for **disc 2** and touch nothing.

**Expect:** within two seconds it carries on copying. After the last file,
*Putting setup_….exe back together* if anything was cut, then the installer
starts from `Games\<the game>` under your profile, or on the drive with the
most room when yours has not got it, and the menu closes. The folder holds the
game and nothing of the disc's own: no `AUTORUN`, no `Disc set.txt`.

5. Check the joined file against the `JOIN` line in `Disc set.txt`:

```powershell
(Get-FileHash '...\setup_the_game.exe' -Algorithm SHA256).Hash
```

6. Start again with **disc 2 first**.

**Expect:** the button says **Install, disc 2 of 2**; after copying it asks
for disc 1 and finishes the same way.

7. Press **Exit** halfway through disc 1, then open the disc again and press
   Install.

**Expect:** *N of M files copied so far*, and it copies only what is missing.

8. Open the menu's preview from the window instead and press the button.

**Expect:** *This is a preview of the menu. Nothing starts from here*, and no
folder is made.

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

- **The sentence about the whole set arrived in a `MessageBox`**, which is a
  separate top-level window. Anything reading "the window" through UI
  Automation is blind to it, and it waits for OK, so a watcher that only polls
  the main window sees the run stop dead and nothing more. Since #121 it is a
  line on the window, `HowTheBurnWent`, and a watcher can read it there; the
  refusals still arrive as boxes.
- **The question between discs and the question about the tray are different
  questions.** Watching for the words "blank" or "next" catches the first and
  misses the second.

## Case 8, the swap, judged 2026-10-24

**Pass, with two things found and fixed the same afternoon.** The set was a
1.2 GB installer cut in two at CD-R 700 MB, built through the installed
window and burned to the two CD-RWs through IMAPI, disc 1 in D: and disc 2
in E: at once, about eight minutes each. The installer was a copy of
Character Map with 1.2 GB of nothing on its end, so that it would be
recognised as a GOG download and be big enough to cut.

What happened, from the person at the drive and from a watcher reading the
`Games` folder and the drives from the side:

- The menu said **Install, disc 1 of 2**. Pressing it started the copy, and
  the first piece appeared in `Games\Microsoft® Windows® Operating System`
  under the profile at its full size at once, which is robocopy pre-sizing.
- Disc 1 came out at 15:50:42. Disc 2 mounted at 15:51:23 and the page
  carried on **ten seconds later with nothing pressed**.
- The joined installer appeared at 15:54:10 at exactly 1,288,490,189 bytes,
  and its SHA-256 matched the `JOIN` line in `Disc set.txt` byte for byte.
- The page said *Every file is here*, started the installer and closed.

**Found, and fixed before the discs were out:** the set file lists the
disc's own files too, because any one disc has to be able to say what the
whole set is, and the engine took every row as the game. `AUTORUN`, `Start
Here.hta` and `autorun.inf` would have been copied in beside the installer.
#110 takes them out, and the discs were built and burned again before the
swap was tried.

**Found by the swap:** the two pieces stayed behind after a perfect join. A
file copied off a CD arrives **read-only**, robocopy carries the attribute
over, and `DeleteFile` refuses a read-only file unless forced. #111 forces
it, and both tests that join real pieces make them read-only first.

**Not a finding, but worth knowing:** nothing appeared when the menu
closed. The joined file is byte for byte the padded Character Map, and
started by hand from Explorer it also quits at once with exit code 0 and
no window, while the untouched Character Map opens. Character Map refuses
itself with 1.2 GB stuck on its end. **Next time pad something that does
not mind**, or build a small exe of your own, so that the end of the flow
is a window rather than an inference. Steps 6, 7 and 8 of the case, disc 2
first, a set left half done and the preview, were not judged on a drive
and stand as written.

---

## Case 8 again, the same evening, with a real installer

**Pass.** Hollow Knight's own GOG installer, 1.16 GB, cut in two at CD-R
700 MB, disc 1 on a CD-RW and disc 2 on a write once CD-R, both read back
against their lists before anything was tried. Both discs went through E:.

- 21:07 disc 1 in, the menu up, **Install, disc 1 of 2**, the name Hollow
  Knight everywhere and This PC saying `HOLLOW_KNIGHT_D1`.
- The bar moved on its own under *Copying file 1 of 1*, and the big button
  was grey and did nothing while it copied.
- 21:13 disc 1 out, disc 2 in. The page carried on **fourteen seconds after
  disc 2 mounted** with nothing pressed.
- 21:17 the join done, and the folder held the whole installer and nothing
  else: the pieces were gone, which is #111 working on a real disc.
- The menu closed and the GOG installer opened a second later. **Hollow
  Knight installed from the folder.**

Three things the owner asked for, done in #115: the tray opens when a disc
is done and more are wanted; the title bar follows the disc instead of
saying *disc 1 of 2* for ever; and the menu stays while the installer
runs, grey and saying *Installing*, and says **Play** when it closes with
the game on the machine. A folder of files across discs now says *Copy and
play, disc 1 of 2*, which answers what a non GOG game does.

**What the evening cost, and what to do differently.** Five burns of disc 2
onto CD-RWs failed to read back, on two rewritables and in both drives,
after each had been erased and written a few times that day, while the
same CD-RW had taken disc 1 cleanly an hour earlier. The CD-R took disc 2
first time. Two rules for the lab from that:

- **Read every disc back before trusting it.** The app's own Burn does;
  the IMAPI shortcut used here did not, and the owner's question "did you
  run the verification" is what caught it.
- **Rewritables wear within a day of this.** Keep write once discs for
  the second and third burn of an afternoon, and keep the discs apart and
  labelled, because they look alike and one went back into a drive twice.
  The lab burner now reads a disc's label before any erase and refuses a
  disc of the set being made.

Steps 6, 7 and 8 of the case are still unjudged on a drive.

---

## When something here fails

Say which case, which drive, and what was in it. The drives report themselves
with the command at the top, and that is usually the first thing worth knowing.

A burn that fails its read-back is not automatically a bug: try a different
blank first, because a rewritable with bad regions fails the same files every
time and looks exactly like a program that cannot write.
