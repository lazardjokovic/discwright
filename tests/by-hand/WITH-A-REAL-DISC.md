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
and all three pass. Case 8 was judged the same day and again that evening, and
**cases 4, 5 and most of 6 were judged on 2026-11-04, and case 7 on 2026-11-05**.
Case 4 needed a DVD-R as well. Cases 5 and 6 were done off one disc carrying
everything, and the menu did turn out to be drivable after all, by coordinate
and screenshot rather than by UI Automation. Case 7 took five discs and found
five faults. What is left is the one step of case 6 that needs a game that can
really be installed. **Case 6 is now complete too**, and step 3 failed: it found a bug that made every add-on dead for ever.

| | |
|---|---|
| **1** a disc really reads back | **pass.** D:, 1.8 MB written at 10x in 51 seconds, the drive reported ten times while it wrote, it shut its own tray, and all three files hashed back off the disc matching what was built. |
| **2** a tray that will not shut | **pass.** Built "a set of 2 discs" from the 1.2 GB fixture with splitting on, asked which drive because both held blanks, wrote disc 1 to E: in eight minutes and read it back, then said *"Disc 1 is done. Put a blank in for disc 2 of 2."* with *"waiting for a blank disc for disc 2 of 2"* on the bar, and touched nothing until the blank was in. It picked up the moment it was told, wrote disc 2, and finished with *"All 2 discs are written and every file checked."* The note beside the discs says **done** against both. |
| **3** erasing a real rewritable | **pass.** *Erase a disc* lit on its own, the question named both discs by letter and by drive model, Cancel left both alone, and after saying yes the disc came back blank and the button went grey. |
| **4** swapping a disc while the window is open | **pass**, 2026-11-04. Empty trays said DVD5 4.7 GB and *Whichever one is ready*. A blank CD-RW into D: moved the size to CD-R 700 MB on its own, 78 seconds after the tray shut, and the drive appeared as *D: ASUS DRW-24D5MT, holding CD-RW*. A blank DVD-R in its place moved it to DVD5 4.7 GB and the drive read *holding DVD-R*, which IMAPI agreed with. With BD-R 25 GB then chosen by hand the size stayed put across another swap, and the amber line named both sides: *The disc in D: is a CD-RW, and this is being built for BD-R 25 GB.* |
| **5** the menu on a real disc | **pass**, 2026-11-04, all eight checks. Label *EVERYTHING* with the disc's own icon, the chosen background, the tune playing and looping and silenced by the control at the top left, **Install** for a GOG game, *README.txt* opening in Notepad from **Manual**, *D:\Extras* opening from **Extras**, and **Exit** closing it with the music. |
| **6** an add-on is dead until its game is installed | **steps 1 and 2 pass**, 2026-11-04. Both add-ons render dead with *Install the game first, then this.* underneath, and pressing each of them started no process and no window. **Step 3 FAILED on 2026-11-04 and found a real bug**: the menu could never see a game as installed, on any disc. Fixed, and passing once fixed. |
| **7** a folder of files, with one thing to run and with two | **all three pass**, 2026-11-05, and between them they found **five** faults, two of which made the case unpassable until they were fixed. Part 1 copies a game off the root of its own disc and runs it. Part 2 runs the one launcher that was picked and leaves the other alone. Part 3 runs nothing and opens the folder. See below, because what this case found matters more than that it passed. |
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

## Case 4, judged 2026-11-04

**Pass, all four steps, and it answers the one thing the suite cannot.** The
suite covers this with a fake drive that changes its mind; what no fake can say
is whether Windows tells the program at all when a disc physically arrives. It
does.

Read through UI Automation rather than by eye, which works here because the
window is WPF: `DiscSize`, `WhichDrive` and `AboutTheDiscInTheDrive` were polled
every two seconds and nothing was touched between the steps.

**Worth knowing before judging this one by eye: it took 78 seconds** from the
tray shutting to the size moving. That is Windows mounting and notifying, not
the program being slow, and anybody watching would be forgiven for deciding
nothing had happened and reaching for the mouse.

Three things it does better than this case asks for. The drive list says what
each drive is **holding**, so `E:` read *holding nothing readable* with its tray
open. The amber line names **both** sides rather than reporting a mismatch. And
it appears as soon as a hand-chosen size stops matching the tray, not only after
a swap, so the same sentence covers picking the wrong size as covers changing
the disc. It also disappears entirely while the tray is empty, there being
nothing left to disagree with.

One thing the case does not mention and should: once a size has been chosen by
hand there is nothing on the window that says so, and nothing that gives the
following back. The amber line is the only sign, and it reads as a complaint
about the disc rather than a reminder of a choice.

## Cases 5 and 6, judged 2026-11-04

**The menu is drivable, which this case said it was not.** It opens by saying an
HTA is invisible to UI Automation so nothing on it is testable at all. The first
half is true and the conclusion is not: the window can be found by process and
rectangle, pressed by coordinate, and read by screenshot. Everything here was
judged that way except the tune, which needed ears.

One disc carried the lot: `gog-with-both`, which is a GOG installer with two
add-ons beside it, plus the icon, the 760x480 background, the extras folder with a
subfolder in it, and the tune. 412.6 MB written to a CD-RW in 335 seconds, 558
progress reports, and all seventeen files landed.

**Check 8, the backdrop, is the one worth measuring rather than looking at.** The
window came back 776 x 519 on the outside with a client area of exactly 760 x 480,
so the stage is not cropped. That is the old 38 pixel bug answered by arithmetic.

**Three traps for whoever drives this next.**

The menu must be brought to the front and **verified** before any coordinate click.
Two presses were lost into a terminal that had come forward, and a press that misses
looks exactly like a button that does nothing, which is the same answer a dead
add-on gives. `WindowFromPoint` on each button point, checked against the menu's own
handle, is what settles it.

**A dead button needs a positive control.** Pressing both add-ons started nothing,
which is the pass, but that is indistinguishable from a click going nowhere. Press
something that must work, here **Manual**, and see Notepad appear, before believing
that nothing happening means anything.

**SetForegroundWindow will fail on a machine in use**, because ForegroundLockTimeout
is 200000 on anything that is not a dedicated test box. Making the window topmost
with `SetWindowPos` is enough: the clicks land even when the window never takes focus.

## Case 6 step 3, judged 2026-11-04, and it failed

**The menu could never tell that a game was installed. Not on this disc, not on
any disc, not for any game.** The big button always said Install and every add-on
was dead for ever, on a machine where the game was installed and registered.

Getting here needed a game that really installs, which no fixture above was: they
are sparse by design, because the recognisers only ever measure. So
`Build-TheFixtures.ps1` now hard links a real GOG offline installer as `gog-real`,
1.8 GB that costs nothing on the disk. Hollow Knight, because it is the smallest
and the only one carrying real patches, which is the add-on shape this case wants.
The app built the disc itself and claimed it as `Hollow Knight` with four add-ons
named `Update 1.5.12618 (89712)` and so on, 1.93 GB to a DVD-R in 167 seconds.

**The cause, and it is one line.** The menu asks for document mode 9, deliberately,
because mode 9 is the only one with rgba that still applies the disc's icon.
`GetObject` is a JScript 5 host global and the mode 9 engine does not expose it, so
`whereTheGameIs` threw ReferenceError on its first line every single time. Its own
catch turns that into a quiet null, which is also what an uninstalled game looks
like, so nothing ever complained. `ActiveXObject` survives in mode 9, which is what
hid it: the FileSystemObject a line below kept working.

It was in two places. `installerIsRunning` used the same call, so the menu could
never notice an installer finish either. Both halves of "install it, come back,
play it" were broken by the same word.

**Why 1,469 tests missed it.** `TheMenuScript` runs the real menu.js under cscript,
where `GetObject` exists and works. The real code, in a host that is not the real
host. Three tests now close that: one reads the script and fails if `GetObject` is
called at all, and two run it with `GetObject` taken away before it loads, which is
the engine a disc really gets. Both were watched failing against the bug put back,
and the runtime one names the games it could not find.

**Proved fixed**, by reading the menu's own DOM rather than a screenshot: the big
button renders `Play`, all four add-ons come back `dead=false`, and the line
"Install the game first, then this." is gone. Before the fix, on the same machine
with the same game installed, it was Install and four dead buttons.

**Not yet seen on real media.** The DVD-R carries the broken menu and DVD-R is
write once. The fix is proved from a local copy of the same menu, which is the
same code the disc carries, so what is left is confirmation rather than doubt.


One difference from this case as written, which changed nothing but will confuse
somebody: the tune on the disc is `music.wav` rather than the `tune.mp3` named above.
Both are real ffmpeg sines. The wav is 8 seconds, so the looping shows itself faster.

## Case 7, judged 2026-11-05, and it is the most productive case here

Three presses on three discs. Everything passed in the end and **nothing**
passed first time, which is the argument for this whole folder existing.

**Two faults stopped the case dead and had to be fixed to finish it.**

**A game alone on a disc could never be copied off it.** One game on one disc
sits at the root, so its folder is empty, and `onTheDisc` answered an empty
relative path with an empty string. The copy walked nothing, found no files,
and the menu said *There is nothing in that folder to copy.* about a disc with
the game sitting in plain sight. That is the ordinary way anybody would burn a
single game, so the whole copy and play path was dead for it. Every test
before this gave the game a folder of its own, which is why nothing caught it.

**The disc's icon went into the player's game folder.** The menu is told what
at the root belongs to the disc rather than to the game, and that list is a
list of constants, so it could never name the icon: an icon is named after the
disc it sits on. It still carried `disc.ico`, which no build has written since
the icon started being named per disc. The one file the list was wrong about
was a file the list exists for. Part 1's copy has `ONETORUN.ico` in it and part
2's has no `TWOPICKED.ico`, same fixture, same press, fix in between.

**Three more came out of watching rather than reading.**

**The menu went grey and said not responding.** The bar sat at 25% and stopped,
then Windows painted the hung-program ghost over it, then it came back. The
copy was fine the whole time: progress was added up only as each file
finished, and the last file was 126 MB copied by a single blocking call, so
the page could not repaint for the whole of it. The set restore had fixed this
years earlier and the fix was never brought across. Its own comment says it:
*a bar that only moves when a file finishes is a bar that never moves on a
disc with one file on it.*

**Everything copied off a disc arrived read-only**, which is the entire reason
that button exists. The comment above `whatToDoWith` says a game run off a disc
cannot save because the disc is read-only, so the menu copies it onto the
machine. Every file on a CD is read-only and every way of copying one carries
the attribute over, so the copy could not save either. All three files of
part 2's copy: read-only.

**The button promised a game it was not going to run.** Part 3's disc has
nothing picked to start it, so the menu copies the folder and opens it, and
the window that builds such a disc says exactly that. The disc said **Copy and
play**. The two halves of the program disagreed about the same disc.

**Why none of this was caught by the suite, which is the real finding.** The
copy and play flow lived in `menu.hta.template`, and the suite loads `menu.js`
under cscript and cannot load the template. 21 of the 35 functions in it were
named by no test at all. The file opens by saying so: *an HTA is invisible to
UI Automation, so nothing can press a button in this page: whatever lives here
can never be tested.* True, and the conclusion drawn from it was to put logic
there anyway. The flow is in `menu.js` now and the page is glue.

**One part of part 3 could not be judged by hand at all, and that is the
lesson rather than a caveat.** The copy was whole, nothing had started, and
the icon had stayed on the disc, which is three of the four things to check.
Whether Explorer opened on the copied folder could not be established
afterwards, because a window somebody has closed leaves nothing behind and
the folder picker at the start of the press is a different dialog. The whole
decision lived in the page then, where nothing could ask it anything. It is
whatToDoAfterACopy in menu.js now and the suite asks it directly, so the
answer no longer rests on what anybody remembers seeing.

**Four things this case says that the case above does not.**

The question of which file starts the game is asked by **the window, when the
disc is built**, not by the disc. Steps 2 and 3 read as though the disc asks.
It does not, and it should not: the answer is burned into it.

The fixture holds `TheGame.exe` and `Config.exe`, not the `TheGame1.exe` named
above.

**The menu asks where to copy it**, with a folder picker, which the case does
not mention and which is the first thing that happens after the press.

**Find the window by class, not by MainWindowHandle.** mshta owns a dozen
windows and the one .NET calls the main one is an `Internet Explorer_Hidden`
of no size. Everything measured off it comes back zero, which reads exactly
like a menu that failed to open. The menu is the `HTML Application Host Window`
`Class` with a real size.

**Make the runnable fixtures real before starting, and make them open a**
**window.** A sparse file of zeros is not a program and will not run, so the
case cannot be judged with one. These three were judged with a padded
`cmd.exe`, which runs from anywhere and stays up, and which is a bad stand-in
for two reasons: a terminal is not what anybody's game looks like, and a
padded one cannot find its own banner text, so it opens with *The system
cannot find message text for message number 0x2350* across the top. Every pass
then reads as a failure to whoever is watching it.

The fixture script takes `wscript.exe` now, which opens a plain dialog and waits.
Swept the obvious ones to find it: copied out of System32, charmap, notepad,
msinfo32, resmon, perfmon, cleanmgr, dfrgui and sndvol all quit the instant
they start, and only dxdiag, osk and wscript survive. dxdiag can open a modal
question about driver signing, and anything modal stops a by-hand pass dead.
**Padding is the part to check**, not copying: a program has to still run after
being padded to size, and a single-file .NET publish does not, because its
bundle is read from the end of the file.

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
