# DiscWright, in C#

DiscWright puts a GOG game on a disc that still installs it in ten years, with a menu that runs itself, a printable cover in a folder anybody can open, and a checksum list so somebody can prove the disc is still good without this program.

This repository is the C# rewrite of it. The shipped app, written in PowerShell, lives at [lazardjokovic/discwright](https://github.com/lazardjokovic/discwright). That one is both what people are using today and **the reference**: when the two disagree about what a disc should hold, this one is wrong until proven otherwise.

## Where things stand

It makes discs, and it has made real ones. The window reads a GOG download or a folder of game files, says what is going on the disc and what it will come to, plans that against the size chosen, writes the menu, the autorun, the artwork and the checksum list, builds the image, burns it, reads the disc back against what was written, and erases a rewritable when it is asked to. Everything in that sentence is finished, and `tests/by-hand/WITH-A-REAL-DISC.md` says which parts of it have been judged with a disc in a drive rather than only in a test.

A game too big for one disc comes out as a numbered set, and **any disc of a set can speak for the whole set**. Its menu copies what that disc holds behind a moving bar with the button dead, ejects, asks for the next disc, carries on by itself when one arrives, joins the cut files back together, runs the installer, waits for it, says Play when it closes, and then offers to remove the copy it made. The swap has been done twice on real discs, the second time with Hollow Knight's own GOG installer cut across two of them, and the game installed from the folder afterwards.

**Nothing prints, and nothing will.** A disc gets an `ARTWORK` folder holding the cover and the disc face as ordinary pictures with a readme beside them, which is the ten year promise made real: somebody holding the disc needs a printer, not this program. Laying a cover out for one is the printer software's job, since it knows the case, the paper and the tray. v1 took its own printing out in 0.11.0 for the same reason.

Two things are not finished, and it is worth knowing which:

- **135 of the 203 cases in the pass by hand are still a person's job**, and the suite runs the other 68. The biggest part of what is left is the menu on the disc, which is an HTA and so invisible to UI Automation, although the set flow inside it now has a test that presses its real button under cscript.
- **The version is `0.12.0` and not 2.0.0.** It carries on from v1's numbering rather than starting again, and the round number is held back until this is something somebody can be given. It is set once, in [Directory.Build.props](Directory.Build.props).

Where to read next:

- **[READING-THE-CODE.md](READING-THE-CODE.md)** is a guided tour of what exists, in the order that makes it make sense. Twenty minutes. Start here.
- **[ARCHITECTURE.md](ARCHITECTURE.md)** is the plan and the reasoning: the one rule, the patterns, the decisions made and the gaps still open. Read it before writing anything.
- **[CLAUDE.md](CLAUDE.md)** is the working rules, the facts already measured and the traps already paid for, so none of them is paid for twice.

## The one rule

**Arrows point inward, and the middle points at nothing.** `DiscWright.Core` references nothing of ours; everything else may reference `Core`; nothing may reference the window.

```
    DiscWright.App
      |
      +--> ViewModels --> Staging --> Menu --+
      +--> Platform ------------------------>+
      +--> Imaging ------------------------->+--> DiscWright.Core
      +--> Burning ------------------------->+
```

`Core` targets `net10.0` while the edge projects target `net10.0-windows`, so the rule is enforced by the compiler and not only by a test: `Core` cannot reach a Windows API even by accident.

| Project | What it is for | What it references |
|---|---|---|
| `DiscWright.Core` | the rules, and pure: the planner, the refusals, the project file, the recognisers and the build steps | nothing of ours |
| `DiscWright.Platform` | the answers only Windows can give: the real filesystem, hashes, icons, pictures, a file's version resource, what GitHub has released | `Core` |
| `DiscWright.Imaging` | making an image with IMAPI, which is wanted on its own and works with no drive in the machine | `Core` |
| `DiscWright.Burning` | writing a disc, reading the recorders and erasing a rewritable, all behind an interface | `Core` |
| `DiscWright.Menu` | the menu a disc carries, as text: the page, its script and its icon, all embedded so a stale copy cannot be handed to the writer | `Core` |
| `DiscWright.Staging` | assembling a whole disc, the image of one, and the preview of the menu before anything is burned | `Core`, `Menu` |
| `DiscWright.ViewModels` | everything the window knows and can do, with no window in it | `Staging` |
| `DiscWright.App` | the window. WPF, and nothing but the window: no arithmetic, no judgement, no refusal wording | `ViewModels`, `Platform`, `Imaging`, `Burning` |
| `DiscWright.Tests` | the suite, and deliberately not the window | `Core`, `Staging`, `Menu`, `Platform`, `Imaging`, `Burning`, `ViewModels` |

That third column is what each project **declares**, which is why `ViewModels` shows only `Staging`: it reaches `Core` through it and does not ask for it by name. `TheArchitecture` in the test project reads the same declared references and fails if any of this stops being true, because the compiler drops a reference nothing uses and a dependency that exists on paper is already a dependency. `TheReadme` holds this table to the project files, so a project added, renamed or newly referenced cannot leave the table saying what used to be true.

## Building it, and running the tests

```
dotnet build
dotnet test
dotnet test --filter FullyQualifiedName~ThePlanner
tools\Invoke-TestsOnTheVM.ps1
```

**1472 tests, and about half a minute.** Every pull request runs that same suite on a hosted Windows runner, in Release, and publishes the one self-contained file the app ships as. `.github/workflows/checks.yml` says what it runs, what leaves itself out, and why.

**The test project does not reference `DiscWright.App`, on purpose**, so no test can press a button or open a window, and `dotnet test` does not compile the XAML. The window is covered three other ways instead: tests that read `MainWindow.xaml.cs` as text and fail when a case goes unhandled, tests that read the XAML as XML and hold the ids and the bindings in place, and `TheWindowChecksItself`, a stateful walk over the view models that presses Make, Burn, Erase and Open and asks the same two questions after every step, whether the window still adds up and whether it said what changed. Nearly every bug this rewrite has found came from that walk, from a planted fault, or from reaching code nothing reached. Not one came from an example based test.

Nothing is excluded from a run. The tests that want something a machine may not have read an environment variable and leave themselves out when it is unset, which is every run on the hosted runner:

| | |
|---|---|
| `DISCWRIGHT_CHECK_A_DISC` | a disc in a real drive, with `DISCWRIGHT_CHECK_AGAINST` naming the folder to check it against |
| `DISCWRIGHT_SPEND_A_DISC` | permission to write to one |
| `DISCWRIGHT_KIT` | the testing kit, the folders and discs built by hand |
| `DISCWRIGHT_FIXTURES` | the folders `tests/by-hand/Build-TheFixtures.ps1` makes |
| `DISCWRIGHT_ASK_GITHUB` | a real call to the releases API |
| `DISCWRIGHT_SOAK` | ten thousand random sequences through the walk rather than two hundred |

**Some of this used to be impossible on the machine it is written on.** Smart App Control was on and enforcing there, and it refused to load our own freshly built, unsigned `DiscWright.Platform.dll`: `An Application Control policy has blocked this file (0x800711C7)`. Everything that stayed inside `Core` was unaffected, which was most of the suite and all of the fast part. It has been off here since 2026-10-16 and the whole suite now runs on both machines. The single file publish stays exactly as it is, because somebody who has it on meets all of that.

The VM is still where a long run goes, because it is not this desktop. The runner sends the **working tree**, not the last commit, because the point of a test loop is to run what is on disk this second. It takes a `-Filter` that goes straight through to `dotnet test`, and it needs the VM password once per terminal:

```
$env:DISCWRIGHT_VM_PASSWORD = '...'
```

Every run is stamped with the local commit and the time it was sent, and the VM echoes that stamp back. If the two disagree the run is refused rather than reported, because a result from a run that never fired reads exactly like a pass.

### Whether the tests are worth anything

A suite is worth what it objects to, so there are two ways of asking. `tools\Mutate.py` plants a fault somebody chose, runs the suite, and refuses to draw a conclusion without evidence that the run happened, because a blocked run and a fault nothing caught look exactly alike. `tests\by-hand\Invoke-Stryker.ps1` is the other half: every fault Stryker can think of across one project, which is the only honest answer to whether these tests are worth having. On `DiscWright.ViewModels`, where the decisions live, the score was **89.78% on 2026-10-24**, up from 72.03% four days before that, and `stryker-config.json` is the configuration it runs with.

### Testing by hand

`tests/by-hand/test-matrix.html` is the pass somebody works through with the real program in front of them. Open it in a browser; it is one file, it needs nothing else, and it keeps its ticks, so a pass can be left half finished and picked up later. **203 cases, 68 of them run by the suite and 135 still by hand**, and every case that claims to be covered names the test that covers it, so the claim can be checked rather than believed.

It covers what no automated test can reach: whether the window changes when it should, whether every control is live when it should be, whether a sentence still reads right at none and at one as well as at many, and the menu on the disc, which is an HTA and so invisible to UI Automation.

It is held to the windows by `TheTestMatrix`, so a control added to a window and not written down there breaks the build. `tests/by-hand/README.md` says how.

`tests/by-hand/WITH-A-REAL-DISC.md` is the part of that pass which needs a hand and a disc, written so that the hand is all it needs, and it carries what each pass actually found, including the rewritables that failed to read back after a day of being erased and rewritten while a write once disc took the same burn first time.

### Driving the real window

The scripts in `tools` open the app and work it through UI Automation: `See-TheWindow.ps1` asks for every control the markup promises, which is the only thing that proves the ids arrive rather than merely being written, `Drive-TheWindow.ps1` takes the testing kit through the window all the way to discs on a disk, `Sweep-TheKit.ps1` does that for every folder in the kit in turn, and `Burn-FromTheWindow.ps1` presses Burn and answers everything it asks.

**These take the desktop**, so say so before running one: windows open and take the focus, and typing is not possible while they do. They read the window rather than photographing it, because `SetForegroundWindow` is refused for a background process and two attempts at a screenshot captured whatever was in front instead.

### What the build insists on

The SDK is pinned in `global.json` to the 10.0.4xx band, because this machine has 10.0.401 and the test VM has 10.0.402 and the same solution should not quietly build differently in two places.

`TreatWarningsAsErrors` is on, and `.editorconfig` turns the naming rules in ARCHITECTURE.md into warnings, so a name that breaks the convention breaks the build. If that ever gets in the way more than it helps, it is one line in `Directory.Build.props`.

## Running it, and what ships

```
dotnet run --project src\DiscWright.App
packaging\Build-TheInstaller.ps1
packaging\Build-TheMsix.ps1
```

The app ships as **one self-contained file**, and that is not a preference. Smart App Control refuses a freshly built unsigned DLL by name, so an app published as an exe beside eight loose DLLs starts and then dies with `0xE0434352`; inside a bundle the assemblies load from memory and there is nothing for Code Integrity to check. `Build-TheInstaller.ps1` publishes that file and wraps it with Inno Setup, carrying a guard against wrapping an exe plus loose DLLs because that has gone wrong once already, and the hosted runner does the publish and the same guard on every pull request, so a packaging break is found before a tag is pushed rather than after. `Build-TheMsix.ps1` lays the same payload out as a package for the Store route, which has to be launched by its identity to be worth testing at all. Both read the version from `Directory.Build.props`, so an installer cannot name a different version from the app inside it.

## Where it ends up

This repository is private and temporary. When 2.0 is ready the C# app becomes `main` in the public `discwright` repository, where the releases, the issues, the winget package and discwright.com already point, and the PowerShell app keeps a `v1` branch and its tags.

So it is worth saying twice: **a secret committed here cannot be taken back later.** Going public publishes the whole history. Credentials go in an environment variable or a gitignored `*.local.json`.

## Licence

MIT, the same as v1, and the text is in [LICENSE](LICENSE) rather than in this sentence. It said "to match v1" for as long as there was no file to match it with, which is a licence nobody can actually rely on: GitHub reported this repository as having none, and a repository that goes public carrying an intention instead of a licence is one nobody may use.
