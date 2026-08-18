# DiscWright

Turn a GOG offline installer into a real game disc — one that shows the game's own icon and title in This PC, and opens a menu when you double-click it.

The way PC games came in a box.

![A DiscWright disc mounted in Windows: the drive carries the game's own icon and title in This PC, and double-clicking it opens the menu](docs/disc.gif)

## Why

GOG sells DRM-free installers. You own them outright, you can back them up, and nothing phones home. But they sit in a folder called `setup_alan_wake_1.1_music_lang_fix_(80728).exe` and that is not quite the same as a shelf.

Burn one with any normal ISO tool and Windows shows you `DVD RW Drive (E:)`. Burn one with DiscWright and Windows shows you the game.

Insert the disc and you get:

- **The game's icon** in This PC, not a generic disc
- **The game's name** as the drive label
- **A menu** on double-click — Play, Install, Game Manual, Extras, Exit
- **Music**, if you want it

Play knows whether the game is already installed and greys itself out until it is. Install runs the GOG installer straight off the disc. Extras opens whatever you put there — manuals, soundtracks, wallpapers, the making-of video.

## What you need

- **Windows 10 or 11**
- **Windows PowerShell 5.1** — the one that ships with Windows. Not PowerShell 7; the ISO builder uses a compiler option that PowerShell 6 removed. DiscWright checks on startup and tells you if you launch it the wrong way.
- **A blank disc and something to burn it with.** DiscWright makes the ISO. Burning is deliberately out of scope — [ImgBurn](https://www.imgburn.com/), Nero, and Windows' own built-in burner all do it well, and there is no reason to write a worse one.

No installation. No dependencies. It is a single PowerShell script.

## Download and run

There is nothing to install. DiscWright is a folder of scripts that runs from wherever you put it.

1. **Download it.** [Releases](../../releases) for a fixed version, or **Code → Download ZIP** for the latest.
2. **Unblock the ZIP *before* extracting.** Right-click it → **Properties** → tick **Unblock** → OK. Windows tags everything that came from the internet, and its built-in extractor copies that tag onto every file inside. Clearing it on the ZIP clears the whole folder in one go; skip this and you get a security prompt on every launch, and some setups will refuse to run the script at all.
3. **Extract it anywhere.** Desktop, Documents, a USB stick. The launchers use relative paths, so there is no fixed install location and nothing to add to PATH.
4. **Double-click `Run DiscWright.cmd`.**

`DiscWright.vbs` starts the same app with the console window hidden, if you would rather not have a black box flash up first. Try `Run DiscWright.cmd` to begin with — if something goes wrong, it is the one that shows you the error.

Want it on the Desktop or Start menu? Right-click `DiscWright.vbs` → **Show more options → Send to → Desktop (create shortcut)**, then set its icon to `DiscWright.ico` via the shortcut's Properties. Shortcuts are not shipped in the repo because they store an absolute path and would point at someone else's folder.

### What Windows is going to say

**"Windows protected your PC."** That is SmartScreen, on the first run of an unsigned launcher that came from the internet — **More info → Run anyway**. Treat that prompt as a reason to go read the code, not as a formality to click through.

**Your antivirus may take an interest.** A PowerShell script that compiles a helper class and writes a multi-gigabyte ISO looks unusual to heuristics.

The launchers run the script with `-ExecutionPolicy Bypass`, the flag Windows requires for any unsigned `.ps1`. You should be suspicious of anything that asks you to do that, so: the entire tool is one readable script in this repo. Read it before you run it. That is the point of shipping source instead of an `.exe` — a compiled binary would hide the code *and* trip more antivirus, and it would still be unsigned.

## Making a disc

![The whole build in one pass: pick the GOG folder, add artwork and extras, hit BUILD ISO, preview the menu](docs/demo.gif)

1. **Pick the GOG folder** — the one holding `setup_*.exe` and any `.bin` parts. DiscWright reads the game's name out of the installer and tells you which disc size it needs.
2. **Set the disc label** — what This PC will call the drive.
3. **Choose an icon** — a `.ico`, or any PNG/JPG and it builds a proper multi-size icon for you.
4. **Set up the menu** — background artwork, which side the buttons sit on, which buttons you want, optional music, optional title text over the artwork.
5. **Add extra content** — a manual, an Extras folder, or any loose files and folders to drop at the disc root.
6. **Pick an output folder** and hit BUILD ISO.

**Preview menu** renders the menu with your current settings without building anything, so you can nudge the layout without waiting on a rebuild.

**Test it before you burn.** Right-click the finished `.iso` and choose **Mount**. Windows gives you a virtual drive carrying the real icon, the real label and the real menu — exactly what the burned disc will do, at no cost in discs. That is what the animation at the top of this page is showing.

Every build writes a `discproject.json` next to the ISO. **Open existing disc...** loads it back so you can change one thing and rebuild, months later.

### Where to get artwork

The disc icon wants something square-ish and at least 256px. The menu background wants something around 760×480 or larger — it gets cropped to fill.

GOG's own store pages, [SteamGridDB](https://www.steamgriddb.com/), and the wallpapers that ship in a lot of GOG extras all work well. Cover art usually already has the game's logo on it, which is why the title overlay is off by default.

## What ends up on the disc

```
E:\
├─ autorun.inf              drive icon, drive label, menu launcher
├─ HollowKnight.ico         named after the disc, not "disc.ico" (see below)
├─ setup_hollow_knight_....exe
├─ AUTORUN\
│   ├─ menu.hta             the menu itself
│   ├─ bg.png               composed background
│   ├─ music.mp3            optional
│   └─ HollowKnight.ico
└─ Extras\                  manual, bonus content, whatever you added
```

The icon is named after the disc rather than a fixed `disc.ico` for a specific reason: Explorer caches icons **by file path**, so `E:\disc.ico` is the same cache key for every disc that ever passes through that drive letter. Swap discs and Explorer will happily redraw the previous game's icon. Naming it after the disc gives each one its own key.

## Burning the ISO

DiscWright does not burn. But two things are worth knowing, because both cost real discs to learn.

**Burn slower than the maximum.** A disc rated 6× does not mean your setup can feed it at 6×. An external USB burner behind a USB 2.0 link has roughly 30 MB/s to work with, and 6× Blu-ray wants 27 MB/s of that, with a 4 MB buffer absorbing any hiccup. Dropping to 4× halves the demand and costs a few extra minutes. On a 25 GB BD-R, 6× failed 7.4 GB in with a write error; 4× wrote the whole 9.2 GB without complaint.

**Check the link, not the label.** A USB 3.0 drive in a USB 3.0 port with a USB 3.0 cable can still negotiate a USB 2.0 link, and nothing in Windows will tell you unless you go looking. Your burning software's log usually names the bus it actually got.

## Known limitations

Stated up front rather than discovered later.

- **`mshta.exe` must be available.** The menu is an HTA. It ships with Windows 11, but a lot of hardened and enterprise environments disable it. The disc icon and label still work; only the menu is affected.
- **AutoPlay has to be allowed.** If you have previously told Windows to "Take no action" for this drive, the menu will not launch on insert. Double-clicking the drive still opens it.
- **Paths longer than 260 characters** will fail during the copy. PowerShell 5.1 limitation.
- **One music track and one manual** per disc, by design.
- **No multi-disc splitting.** Anything larger than a 100 GB BD-R XL is out of scope.
- **Disc labels are limited to what Windows can encode.** AutoRun reads `autorun.inf` in the system ANSI codepage and has no Unicode mode at all, so accented Latin characters are fine but Cyrillic, Greek and CJK are not. DiscWright shows you exactly what This PC will display and asks before building one it cannot represent.

Not all of these are permanent — multi-disc splitting in particular is on the [roadmap](ROADMAP.md). [Issues](../../issues) is the place to ask for something, and [CHANGELOG.md](CHANGELOG.md) records what has changed between releases.

## Media sizes

| Payload | Disc |
|---|---|
| up to 0.68 GB | CD-R 700 MB |
| up to 4.37 GB | DVD5 4.7 GB |
| up to 7.95 GB | DVD9 8.5 GB dual layer |
| up to 23.3 GB | BD-R 25 GB |
| up to 46.6 GB | BD-R DL 50 GB |
| up to 93 GB | BD-R XL 100 GB |

## License

MIT. See [LICENSE](LICENSE).

## Not affiliated with GOG

DiscWright is an independent hobby project. It is **not affiliated with, endorsed by, or connected to GOG.com, GOG sp. z o.o., or CD PROJEKT**. "GOG" is their trademark and is used here only to describe what the tool reads.

It works with GOG offline installers because those installers are DRM-free by design — that is GOG's whole proposition, and it is the only reason a tool like this can exist. DiscWright circumvents nothing. Use it with games you own, to make copies for yourself.
