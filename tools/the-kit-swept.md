# The kit, through the window

Every folder in `1 SOURCES - add these`, opened in the real DiscWright and built, driven through UI Automation.

Run 2026-10-15 22:30, against d110b32, every folder aimed at DVD5

## Two things that moved since the last sweep

**Every built disc has one more file on it** than it did on 2026-10-14. What is there now is coherent: `autorun.inf`, `checksums.sha256`, `READ ME.txt`, the game's own files, and `AUTORUN` holding `menu.hta` and `menu.js`. No commit in between adds a new kind of file to a disc, and this has not been traced to one. It is written down here rather than explained away.

**I and J went from two games to one game and one add-on.** "A game and its add-on" and "a game and a patch for it" are exactly one game each, so this looks like a fix rather than a loss, somewhere in the fifteen pull requests since.

## The size is set on purpose now

DiscWright starts from the size of the disc that is actually in a drive, which is right for a person and wrong for a sweep: with a CD in the tray the 4.3 GB folder was refused, and with the drive empty it builds. **Two runs stopped being comparable.** The sweep now chooses the size itself, reads it back, and refuses to continue if it did not take.

| Folder | Size | Named it | Says it will make | Games | Also had | Built | Files | Add-ons | Note |
|---|---|---|---|---|---|---|---|---|---|
| A - GOG download (Hollow Knight) | 2 MB | HOLLOW KNIGHT | one disc | 1 | one more file | yes | 8 | 0 |  |
| B - game files with an exe (Gothic) | 1.6 MB | B - GAME FILES WITH AN EXE (GOTHIC) | one disc | 1 |  | yes | 9 | 0 |  |
| C - game files, NO installer (Arcanum) | 1 MB | C - GAME FILES, NO INSTALLER (ARCANUM) | one disc | 1 |  | yes | 8 | 0 |  |
| D - nine games at once (xniwo's DWTEST) | 1.1 MB | GAME | one disc | 9 | That folder holds 9 games | yes | 15 | 0 |  |
| E - very long game name | 0.5 MB | THE WITCHER | one disc | 1 | one more file | yes | 7 | 0 |  |
| F - game that brings its own autorun.inf | 0.3 MB | OLD GAME | one disc | 1 | 3 more files | yes | 9 | 0 |  |
| G - game with its own manual and extras | 1.3 MB | BG | one disc | 1 | 4 more files | yes | 10 | 0 |  |
| H - extra content for the disc root | 1.1 MB | H - EXTRA CONTENT FOR THE DISC ROOT | one disc | 1 |  | yes | 10 | 0 |  |
| I - a game and its add-on | 2.5 MB | GRIM FANDANGO REMASTERED | one disc | 1 | 2 more files | yes | 9 | 1 |  |
| J - a game and a patch for it | 3.1 MB | THE WITCHER | one disc | 1 | 2 more files | yes | 9 | 1 |  |
| K - the same game, a newer version | 2 MB | HOLLOW KNIGHT | one disc | 1 | one more file | yes | 8 | 0 |  |
| L - a bundle, one big and three small | 6.2 MB | GRIM FANDANGO REMASTERED | one disc | 4 | That folder holds 4 games | yes | 12 | 0 |  |
| M - a file too big for any disc (sparse) | 6247.2 MB | THE WITCHER |  | 1 | one more file | refused | 0 | 0 | One file is bigger than a whole disc |
| N - a file over the old 4 GB limit (sparse) | 4301.5 MB | A LARGE GAME | one disc | 1 | one more file | asked only | 0 | 0 | too big to build in a sweep |
| O - names that have broken things before | 2.6 MB | ROCK AND ROLL | one disc | 4 | That folder holds 4 games; 2 more files; one more file; 2 more files; one more file | yes | 16 | 0 |  |
| P - folders named like the disc's own | 0.8 MB | A GAME | one disc | 1 | 5 more files | yes | 11 | 0 |  |
| Q - which exe starts the game | 22.3 MB | Q - WHICH EXE STARTS THE GAME | one disc | 1 |  | yes | 14 | 0 |  |
| R - an add-on with no game | 0.4 MB |  |  | 0 | That looks like an add-on, not a game | button dead | 0 | 0 | nothing to press it for |
| S - a community patch, a mod and a translation | 4.4 MB | S - A COMMUNITY PATCH, A MOD AND A TRANSLATION | one disc | 1 |  | yes | 15 | 0 |  |
| T - manuals and extras to point the options at | 6.1 MB | T - MANUALS AND EXTRAS TO POINT THE OPTIONS AT | one disc | 1 |  | yes | 15 | 0 |  |
