# How the C# DiscWright is put together

Written 2026-10-14, before any code exists, so that the first file lands in the right place. The design of what it looks like lives on the canvas. This is about what the code looks like.

## The pattern you were reaching for

Three names, because they are three different ideas and it helps to keep them apart.

**Separation of concerns** is the principle: one piece of code, one job. **Ports and adapters**, sometimes called hexagonal architecture, is the shape that makes the principle hold instead of just being good intentions: the part of the program that knows the rules defines interfaces for everything it needs from the outside world, and the messy outside world is implemented behind those interfaces. **Open/closed** is what you get out of it: new behaviour arrives as a new thing added, not as an edit threaded through five existing files.

Your example is exactly right. Burning a disc is the messiest thing this program does, it talks to COM, it fails in a dozen ways, and it is the thing most likely to change. So burning gets an interface that says what burning is, and the IMAPI code lives behind it in its own project. When you change the burning, the only thing anyone else can see is the interface, and if the interface did not change then nothing else can break.

## The one rule

**Arrows point inward, and the middle points at nothing.**

```
    DiscWright.App            the window. WPF, MVVM.
          |
          v
    DiscWright.Core           the rules. pure. knows no COM, no disc, no file.
          ^     ^     ^
          |     |     |
     Staging  Imaging  Burning  Printing  Platform      the messy edges
```

`Core` is allowed to reference nothing of ours. Everyone else may reference `Core`. Nobody may reference `App`. That is the whole architecture, and it is checkable in five seconds by a human and by a test, which is below.

The reason this is worth the layering: most of what this program gets wrong is arithmetic and judgement, not COM. Does it fit, where does the cut fall, is this folder a GOG download, should this be refused, what does the refusal say. All of that goes in `Core`, and all of it can then be tested with no disc, no drive and no machine.

## The projects

| Project | May reference | Must never contain |
|---|---|---|
| `DiscWright.Core` | nothing of ours | COM, `System.IO` beyond paths as strings, WPF |
| `DiscWright.Platform` | Core | anything about discs |
| `DiscWright.Staging` | Core, **Menu** | COM, UI |
| `DiscWright.Imaging` | Core | the recorder. it makes images, it does not write them |
| `DiscWright.Burning` | Core | the image builder, the filesystem beyond reading a stream |
| `DiscWright.Printing` | Core | everything else |
| `DiscWright.Menu` | Core | the filesystem. it returns text |
| `DiscWright.ViewModels` | Core, Staging | **WPF, Windows, Platform.** Everything the window knows, with no window in it |
| `DiscWright.App` | ViewModels, Platform | arithmetic, judgement, any refusal wording |
| `DiscWright.Tests` | all of the above | a real drive, by default |

`Imaging` and `Burning` are split on purpose even though both are IMAPI. Making an ISO and writing a disc fail differently, are wanted separately, and one of them already needs to run when there is no drive at all.

## The types, named carefully

```csharp
// What was found on disk. A reading, not a decision.
public sealed record GameFolder(
    string Path,
    GameKind Kind,
    string Name,
    string? Installer,     // the GOG setup, when there is one
    string? Launcher,      // what starts the game, when there is no installer
    IReadOnlyList<SourceFile> Files);

public enum GameKind { GogInstaller, FilesWithLauncher, FilesOnly }

// What the disc is meant to be. The whole intent, and the only thing saved.
public sealed record DiscProject(
    string Label,
    IReadOnlyList<GameOnDisc> Games,
    DiscExtras Extras,
    DiscArtwork Artwork,
    DiscOptions Options,
    string OutputFolder);

// A medium is data, not a branch. Adding BD-XL is one line, below.
//
// It carries no Rewritable flag and no file size ceiling, and both omissions
// were corrections. The tiers are TARGET SIZES, so whether the disc in the tray
// is blank or rewritable is read off the drive at burn time and belongs to that
// reading. And a file size ceiling belongs to the filesystem, never to the
// medium: a 6 GB file is impossible on ISO9660 and fine on UDF, on the same
// disc, in the same drive. That lives in FileSystems, and it is the reason the
// "readable on Windows XP" option can be dead while everything else works.
public sealed record Medium(string Key, string Name, long Capacity);

// The answer to "how does this go on discs". Pure arithmetic over sizes.
public sealed record DiscLayout(IReadOnlyList<PlannedDisc> Discs, long Spare);
public sealed record PlannedDisc(int Number, IReadOnlyList<PlannedFile> Files, long Bytes);
public sealed record PlannedFile(string RelativePath, string Source, long Bytes, FilePart? Part);
public sealed record FilePart(int Index, int Count, long Offset, long WholeBytes);
```

Naming rules that keep this readable. No `Manager`, `Helper`, `Util`, `Service`, `Handler`, `Data` or `Info` in any name; if that word is the only one that fits, the class is doing more than one thing. A class is a thing (`DiscLayout`, `ChecksumList`, `Medium`) or a doer with a real job (`DiscPlanner`, `GameFolderReader`, `MenuWriter`, `BurnJob`). A method is a verb that says the whole truth: `Plan`, `Stage`, `Write`, `Verify`, `Erase`. A question is named so it cannot lie: `CanFit`, not `Check`. No variable called `tmp`, `x`, `data` or `result` where the thing has a name.

## Saying no is a value, not a string

Every refusal in the design names the thing, gives both numbers and offers the fix. That only stays true if a refusal is a type, because a type has fields somebody has to fill in.

```csharp
public sealed record Refusal(string Headline, string Because, IReadOnlyList<Figure> Figures, Remedy? Remedy);
public sealed record Figure(string Label, string Value);     // "needs", "2.4 GB"
public sealed record Remedy(string Label, RemedyKind Kind);  // "Use a double layer DVD"

// Both halves of a Figure are strings because the unit is part of the answer: a
// person wants "4.37 GB", not 4692251770. Every one of them comes from
// ByteSize.Describe, so a size reads the same everywhere, in binary units
// labelled the way Explorer labels them, in the machine's own culture.
//
// RemedyKind lists what the WINDOW CAN DO, not why a refusal happened:
// ChangeMedium, EnableSet, DisableOption, FocusField, PickFolder, Nothing.
// Framed as reasons it was already three short, with no way to say "rename the
// disc", "rename that folder" or "choose somewhere else", and it would have
// grown a case per refusal for ever. Framed as capabilities it is closed,
// because it is a list of what one window can be asked to do.

public abstract record Outcome<T>
{
    public sealed record Ok(T Value) : Outcome<T>;
    public sealed record No(Refusal Refusal) : Outcome<T>;
}
```

So `DiscPlanner.Plan(...)` returns `Outcome<DiscLayout>`, and the window cannot forget to handle the refusal because it cannot get at the layout without looking. No exceptions for things that are not errors. A file too big for a disc is an answer, not a failure, and the old app learned that the hard way when a refusal arrived in front of the words "The build failed:".

Use it where the refusal is something **a person has to read**. Not for a programming error, where an exception is right and a refusal would be a lie. Not where the only possible answer is "there is no such thing", where a nullable return says it more plainly: that is why `Media.ByKey` hands back a `Medium?` rather than one of these. Reached for everywhere, it stops carrying meaning and becomes ceremony. Read one with a switch over the two cases rather than a `Match` method, because positional patterns on records already say it in a line each and add nothing to learn.

## The ports, and burning worked through

```csharp
public interface IDiscRecorder
{
    IReadOnlyList<RecorderDevice> Devices();
    // Async because reading a disc means waiting for it to spin up, which is
    // seconds. Synchronous, this one line would freeze the window.
    Task<MediaInDrive> Look(RecorderDevice device, CancellationToken ct);
    Task Write(RecorderDevice device, Stream image, IProgress<BurnProgress> progress, CancellationToken ct);
    Task Erase(RecorderDevice device, IProgress<ErasePhase> progress, CancellationToken ct);
}
```

That interface lives in `Core`. The IMAPI implementation lives in `Burning` and is the only file in the solution that mentions IMAPI. The orchestration, which disc to ask for, when to offer an erase, what to do when the disc is not blank, lives in `Core` as `BurnJob` and talks only to the interface.

What that buys, concretely. `FakeRecorder` in the tests can hand back a disc that is not blank, a drive that is busy, a write that dies at forty per cent, a rewritable with data on it, and two drives where one is empty. Every one of those is a case in the catalogue, every one is a test that runs in milliseconds, and none of them needs a disc. Then when the IMAPI code changes, those tests still pass or the interface changed, and the interface changing is the signal that other code needs looking at.

The same shape for the rest: `IFileSystem`, `IDiscImageWriter`, `IHasher`, `IInstalledGames`, `IProcessRunner`, `IClock`. `IClock` looks like overkill until the copy bar needs testing, which estimates from elapsed time, and a test cannot wait eight minutes.

## Patterns, and the ones to refuse

No single pattern suits an application. They are a vocabulary for parts, and the cost of the wrong one is exactly the thing we are trying to avoid: a codebase nobody can read. Four earn their place here.

**Chain of Responsibility, for what a folder is.** The best fit in the whole app, because recognition is where new cases keep arriving: a GOG download, files with a launcher, files only, a patch, an add-on, somebody's mod.

```csharp
public interface IGameFolderRecogniser
{
    string Describes { get; }   // "a GOG download"

    // Three answers, not two. null means "not mine, ask the next one", Ok means
    // "mine, and here it is", and No means "MINE, AND BROKEN", which must never
    // fall through to be reported as "nothing recognised here". A folder holding
    // setup_x.exe with setup_x-1.bin missing is unmistakably a GOG download, and
    // v1 already names that exact fault. Only the recogniser that claims a
    // folder knows what is wrong with it.
    Outcome<GameFolder>? Recognise(FolderListing folder);
}

public sealed class GameFolderReader(IReadOnlyList<IGameFolderRecogniser> recognisers)
{
    public Outcome<GameFolder> Read(FolderListing folder)
    {
        foreach (var r in recognisers)
            if (r.Recognise(folder) is { } found)
                return new Outcome<GameFolder>.Ok(found);

        return new Outcome<GameFolder>.No(Refusals.NothingRecognisedIn(folder));
    }
}
```

The order is explicit data, `[GogDownload, Patch, AddOn, FilesWithLauncher, FilesOnly]`, so a folder that could be read two ways goes to the earlier recogniser and a test pins that order. Adding a kind of source is one class and one line in the list.

**Facade, for burning and imaging.** `ImapiRecorder` hides about six COM objects behind four methods and is the only file allowed to say IMAPI. Strictly a Facade, since it simplifies a subsystem, with a touch of Adapter, since it also makes IMAPI fit an interface we chose. The thin `IFileSystem` over `System.IO` is the pure Adapter case.

**Command, for the build steps.** Stage, write autorun, write the menu, copy artwork, write checksums, write the set manifest, make the image.

```csharp
public interface IBuildStep
{
    string Says { get; }                   // "Writing the menu"
    bool AppliesTo(DiscProject project);
    Task Run(BuildContext context, CancellationToken ct);
}
```

This also answers a real complaint about the installer window having a timer and no bar: the pipeline knows how many steps apply before it starts, so progress is "step 4 of 7, writing the menu" without anybody estimating anything.

**Strategy, where behaviour truly varies.** The packing algorithm, and what a menu button does. Not where there is one implementation, because an interface with one implementor and no test fake is indirection with no payer.

**Free with the platform, so never hand rolled:** Observer arrives as `INotifyPropertyChanged` and `IProgress<T>`, Command again as `ICommand`, Iterator as `IEnumerable`.

**For the tests:** Builder, as test data builders, `AProject.WithOneGame().AndAManual().OnADvd()`, so a new case costs a line instead of fifteen lines of setup.

### Refused, with the reason

| Pattern | Why not |
|---|---|
| Singleton | One registration in the container gives one instance, without the global and with a seam for tests. |
| Abstract Factory | One platform, one recorder. A factory choosing between one thing is ceremony. |
| State | Five window states read better as an enum with computed properties than as five classes. Revisit only if transitions get genuinely tangled. |
| Composite | Games with add-ons under them is two levels, not a tree. Composite pays at arbitrary depth. |
| Memento | There is no undo in this app. |
| Visitor | Nothing needs double dispatch. It costs more to read than it saves. |
| Mediator, Bridge, Flyweight, Prototype, Proxy, Interpreter | No real use here, and adding them is how a C# codebase becomes unreadable while looking professional. |

Decorator sits in the middle: worth it the day logging or retry is wanted around `IFileSystem` or `IDiscRecorder` without touching either, and not a day before.

**The rule for introducing any of these: a second implementation already exists, or a test needs a seam.** A pattern added in anticipation is a pattern nobody can justify later.

## Small chunks, with numbers

Rules with a number are followed. Rules that say "keep it small" are not.

- One public type per file, and the file is named after it.
- A method that does not fit on one screen, call it thirty lines, is two methods.
- A file over three hundred lines is a module boundary being ignored.
- More than three constructor parameters means the parameters are a record that does not exist yet.
- No method takes a `bool` that selects behaviour. Two methods, two names.

## Testing every case

The catalogue on the canvas is the test list. Not a parallel document that drifts, the same list: every case in it becomes one named test, and a case with no test is a visible hole.

Test names are sentences, because a failure should read as one:

```csharp
[Fact] public void A_file_too_big_for_any_disc_is_refused_before_anything_is_built()
[Fact] public void A_game_is_never_cut_merely_to_even_out_two_discs()
[Fact] public void An_add_on_is_dead_until_its_game_is_installed()
[Fact] public void A_label_that_cannot_be_written_in_the_system_codepage_is_warned_about()
```

Hand-written fakes, not a mocking framework. `InMemoryFileSystem` and `FakeRecorder` are read by humans and reused by fifty tests; a mock built inline in each test is five lines of noise per test and unreadable at the hundredth.

And the architecture gets a test of its own, so the one rule is enforced rather than remembered:

```csharp
[Fact]
public void Core_references_nothing_of_ours()
{
    var ours = typeof(DiscPlanner).Assembly
        .GetReferencedAssemblies()
        .Where(a => a.Name!.StartsWith("DiscWright"));
    Assert.Empty(ours);
}
```

Watch it fail once, by adding a reference on purpose, before trusting it. A guard nobody has seen fail is not known to guard anything.

## What stays data

This is what "easy to change" means in practice: the things that vary live in tables, so changing them is editing a row.

```csharp
public static class Media
{
    public static readonly Medium Cd      = new("CD",      "CD, 700 MB",             700L * 1024 * 1024, false, FourGig);
    public static readonly Medium Dvd     = new("DVD",     "DVD, 4.7 GB",           4_700_372_992,       false, FourGig);
    public static readonly Medium DvdDl   = new("DVD-DL",  "Double layer DVD, 8.5 GB", 8_547_991_552,    false, FourGig);
    public static readonly Medium Bd      = new("BD",      "Blu-ray, 25 GB",       25_025_314_816,       false, long.MaxValue);
    public static IReadOnlyList<Medium> All => [Cd, Dvd, DvdDl, Bd];
}
```

Likewise the menu. It stays a template with named placeholders, and one test refuses to let an unresolved placeholder reach a disc, which is the `%%SINGLE%%` lesson written down:

```csharp
[Fact]
public void No_placeholder_survives_into_a_written_menu()
    => Assert.DoesNotMatch("%%[A-Z_]+%%", new MenuWriter().Write(AProject.WithOneGame()));
```

Options live in one record with defaults, so adding an option touches three named places: the record, its binding in the window, and its test. Not eleven.

## Comments

The existing codebase already does this better than most, so the rule is just to keep doing it. **A comment says why, never what.** Where there was a surprise, it records what was actually measured, because that is the part nobody can reconstruct later:

```csharp
// The flag file appears when the redirect OPENS, not when the copy ends, so a
// reader that trusts its existence sees a half-written file. Measured: 7 of 8
// reads refused mid-copy. Written to .part and moved, because a move is atomic.
```

That comment is worth twenty lines of guessing. A comment that says `// loop over the games` is worth less than nothing.

### Which drive, before the burn

A drive could only be chosen **in the middle of a burn**, and only when two were ready. That is the worst moment to ask: the question arrives after somebody has walked away, and on a machine with two writers the answer decides whether the run needs a hand at all, since one of these two cannot shut its own tray.

So the window offers them, and leaving it alone keeps the old behaviour exactly: it works the drive out and asks only if there is something to ask. **A machine with one writer has no question to answer**, and asking somebody to pick the only drive they own is a decision shaped like a chore.

A chosen drive is kept **by its letter and not by the record**, because what is in a drive has almost certainly changed since it was picked, and it is forgotten entirely when that drive stops being there: the slim writer here lives on a USB cable.

Once one is chosen, the tray warning is about **that** drive. A sentence about the drive somebody is not using is noise.

An empty dropdown reads as broken, so it says `Whichever one is ready` until somebody picks.

## What else goes on the disc

The disc icon, the menu background, the music, the manual, the extras and the printed artwork **had no way in from the window at all**. Every one of them worked in the build pipeline from the first day and could only be set by *opening* a project somebody else had made, so a disc made here could not have had any of it.

They are a section of their own now, beside More options. The cover and the disc face are in it too, because choosing them is not printing them: a picture can be picked tonight and printed whenever a printer turns up.

**Music was worse than unreachable.** `AsAProject` built its extras without it, so a project saved with music lost it on the way back in, and nothing could have put any there to begin with.

**A picture is looked at the moment it is picked.** The icon used to be checked when it was chosen and the background never was, so a file Windows cannot decode sailed through the whole window and failed at the end as "A generic error occurred in GDI+", which tells somebody nothing about which file is at fault.

### The column scrolls

It has twice grown past the bottom of a window drawn for what was in it before, and both times the answer was a taller window. It is a scroller now: the bar appears only when it is needed, and nothing can be lost off the bottom again whatever gets added next.

## The window

**WPF on .NET 10, MVVM, 920 by 620 fixed.** The view models hold no WPF types and are unit tested like anything else; the window holds no arithmetic and no refusal wording.

WPF rather than WinUI 3 for one reason that outweighs the nicer default look: the tests drive the real window through UI Automation, and this project has been bitten twice by things that are invisible to it. `AccessibleName` and `Name` on a Panel do not reach UIA, only a GroupBox's `Text` does, and the disc menu is invisible to UIA entirely. So every control the tests touch gets an explicit, stable `AutomationId`, set deliberately and never left to the framework, and that is a review item rather than an afterthought.

## What ships

**Self contained, single file, compressed.** Measured here today with an empty WPF app, so these are the floor rather than the finished size:

| How it is published | Size | What the user needs |
|---|---|---|
| Self contained, single file | 133.2 MB | nothing |
| Self contained, single file, compressed | **61.8 MB** | nothing |
| Framework dependent | 0.2 MB | the .NET Desktop Runtime installed first |

Compressed and self contained is the answer. The promise this program makes is that a disc still works in ten years and that the app runs on a machine somebody has just turned on, and 62 MB is a cheap price for the second half of that. Trimming is not an option to reach for later, because WPF does not support it.

Which is exactly why the test VM is worth keeping free of the runtime, and why there is a checkpoint called `testkit in place, no dotnet`, taken before the SDK went on it. Reverting to that gives a Windows which has never had .NET installed, and running the published app there is the only honest test that the publish settings are right.

## A thing that will bite the autorun step

Not a gap so much as a trap already seen, written down before anybody walks into it.

v1 writes `autorun.inf` with PowerShell's `Encoding Default`, which on PowerShell 5.1 means **the system ANSI codepage**. That is deliberate and its comment says why: ASCII turned every accented character into a literal question mark, so "Uber Alles" reached Explorer as "?ber Alles", while ANSI carries the Latin-1 accents and typographic dashes GOG titles are full of. UTF-8 is wrong for a different reason: PowerShell writes a byte order mark and AutoRun does not understand one.

**In .NET, `Encoding.Default` is UTF-8.** A direct port of that line would compile, run, look right in a text editor, and quietly produce an `autorun.inf` that AutoRun reads wrongly. **Done 2026-10-14**, in `WindowsAutorunEncoding`, with one correction to what this note used to say: on .NET 10 the `System.Text.Encoding.CodePages` package is **built in**, and NuGet refuses an explicit reference to it as redundant. `CodePagesEncodingProvider` still has to be registered, or `GetEncoding(1252)` throws, because only UTF-8 and ASCII are there to begin with. Both machines are codepage 1252, where accents survive and Cyrillic and Japanese do not.

So the encoding is the port's business, not `Core`'s: a step hands over text and asks for it to be written the way AutoRun reads it, and `Platform` owns what that means on this machine. The same port has to answer the other half of it, which is what a label will *look* like once it has been through that codepage, so a disc can be refused for being called `???` before it is burned rather than after.

## Smart App Control, and why 2.0 may need signing

**This machine has Smart App Control ON and enforcing**, which is the default on a clean Windows 11. It refuses code it cannot attribute to a known publisher, and on 2026-10-14 it refused *ours*: Visual Studio's MSBuild was blocked loading our own freshly built class libraries during WPF markup compilation, logged as CodeIntegrity events 3033 and 3077, naming each referenced project in turn.

The build still succeeded, and the immediate cause is gone: the window referenced all seven projects while using none of them, so the references were removed until it actually needs them. They go back one at a time, as it starts using them.

**The lesson is about shipping, not about building.** What this project publishes is a self-contained, unsigned executable. v1 is a PowerShell script started by a `.vbs`, and it runs here today; an unsigned `.exe` is a different kind of thing to Windows, and on a machine configured like this one it may simply be refused. Nobody would discover that until somebody downloaded a release.

So, before 2.0 ships: **run the published exe on this machine, with Smart App Control left on**, because that is what a careful stranger's machine does. If it is refused, the answer is a code signing certificate, which is a cost and a lead time rather than a line of code. It is the same question the disc already faces if a helper binary ever ships on it.

### Later the same day, it blocked the tests as well

The first test that actually *used* `DiscWright.Platform` made the runtime load that assembly, and Code Integrity refused it outright:

```
System.IO.FileLoadException : Could not load file or assembly 'DiscWright.Platform.dll'.
An Application Control policy has blocked this file. (0x800711C7)
```

Nine tests, repeatably, on every run. The other 72 pass, because they stay inside `Core`, and why `Core` is allowed to load while `Platform` is refused is not understood. Both are equally unsigned and built seconds apart.

**So the suite runs on the test VM** through `tools/Invoke-TestsOnTheVM.ps1`, where Smart App Control is in *Evaluation*: it audits and does not block. That script sends the working tree rather than the last commit, and stamps each run with the local commit and the time, which the VM echoes back and the script checks, because a result from a run that never fired reads exactly like a pass. The window suite in v1 learned that when `job.done` survived a run that never started. The same commit passes everything there. Running it here is still worth doing, because the 72 that need no machine are the ones worth running constantly, and they are unaffected.

That split is not a workaround to be embarrassed about. It leaves this machine as the honest answer to the only question that matters at release: **what does a careful stranger's Windows do with an unsigned build of DiscWright?** Today the answer is "refuses to load part of it", and that will not change by itself.

**Do not turn Smart App Control off to make development quieter.** It cannot be turned back on without reinstalling Windows, and a machine that is strict about unsigned code is the most useful test environment this project has.

## The two machines, as they stand

Both prepared 2026-10-14.

| | This machine | The test VM |
|---|---|---|
| .NET SDK | 10.0.401 | 10.0.402 |
| WindowsDesktop runtime | 8.0.31 and 10.0.12 | 10.0.13 |
| Visual Studio | 18.10.3 | none, and needs none |
| git | 2.53.0 | 2.55.0 |
| Smart App Control | **ON, enforcing** | Evaluation, audits only |
| Runs the whole suite | no, 72 of 81 | **yes** |

Two things came out of preparing them, both worth knowing before the first commit.

**Neither machine could reach nuget.org.** This one had only Visual Studio's offline package folder, the VM had no source at all, so a plain build worked while anything needing a package died with NU1101. That is what made the first self contained publish fail. Both now have nuget.org registered.

**The two SDKs differ in the patch**, 10.0.401 here against 10.0.402 there. So the repository gets a `global.json` pinning the SDK with `rollForward: latestFeature`, or the same solution quietly builds differently in two places and the difference surfaces as something else entirely.

## What this plan does not cover yet

Written down because a gap nobody has named is a gap somebody fills badly in a hurry.

**Opening an existing project or disc.** The journey above runs folder to disc and has no Open path, which is not a small omission: v1's `Import-Project` carries schema versions 1 to 12, and `Import-DiscFolder` rebuilds a project from a disc that has no project file at all, by scraping `autorun.inf` and `menu.hta`. It wants to be pure and in `Core`, taking the text of a project file and handing back a `DiscProject`, which also makes it cheap to test, because the manual testing kit already holds eleven project files including a deliberately preserved schema 11. A round trip, write then read then compare, is the most valuable single test in that area.

**The disc's menu. SOLVED 2026-10-14, and it now has tests for the first time in either version.**

The menu scans a set, copies with progress, launches an installer, orders add-ons, verifies checksums and offers a chooser, in JScript, on a machine that may be Windows XP, and it is **invisible to UI Automation**, so neither xUnit nor the window suite can drive it. In v1 that logic sits inside the HTA alongside the generated data, where nothing can reach it.

So it is a file of its own, `AUTORUN/menu.js`, and the HTA includes it. The split is what makes it testable:

- **`menu.js` is identical on every disc.** No substitution, no placeholder, nothing generated. So the tested file and the shipped file are the same file.
- **Everything that differs from disc to disc lives in a data block in the HTA**, which is where generation and escaping belong, and where an unresolved placeholder can still be caught by its own test.

Both halves were measured before anything was built on them. `mshta` loads an external script with a relative `src`, and `cscript.exe` runs the same file through a `.wsf` job with a real `src` rather than an `eval`, so a mistake in `menu.js` is reported with a line and a column instead of vanishing. Both produce identical answers from the same functions.

Two rules it has to keep. It must parse and run under **both** hosts, so nothing at the top level may touch `window`, `document` or `WScript`: functions only, and arithmetic over values handed in. And it is **JScript, not modern JavaScript**, because the oldest machine it has to run on is Windows XP: no `let`, no `const`, no arrow functions, no `JSON`, no `Array.forEach`.

One unexpected benefit: those tests run on the development machine as well as the VM. They load no assembly of ours, so Smart App Control never gets involved.

**Add-ons and patches. DONE 2026-10-14, and not as recognisers.**

They were going to be two more links in the recogniser chain, and that was the wrong shape. An add-on is **picked as a file, not a folder**: a patch or a DLC usually sits in the same folder as the game it belongs to, so pointing at the folder would just find the game again. The chain answers "what is this folder"; an add-on is not an answer to that question.

So an add-on is a **role**, given to an executable when somebody attaches it to a game, and `AddOn` carries which game it belongs to. **Any executable is accepted.** The `setup_` rule exists to identify a GOG game folder, and an add-on's identity comes from the game it was attached to rather than from its file name, which is what lets a mod or an overhaul go on a disc at all.

**`BuildContext`. SETTLED 2026-10-14.** Five fields: the project, the layout, the disc being made now, the folder that becomes it, and the writer. What keeps it from becoming a god object is what was left out: **progress and logging are not in it.** They belong to the pipeline, which is the only thing that knows how many steps there are and which one is running. A step that could report its own progress would have to know its own number, and then every step would need telling where it sits in a list it is not part of.

**Per disc against per set. SETTLED the same way:** a `BuildContext` describes **one disc**. A set runs the pipeline once per disc with a different disc and staging folder each time, which is what stops "copy the files" and "write the set manifest" from having to be the same shape. The step order is data and the pipeline runs it in order, because the checksum step hashes what the copy step wrote; a test pins that the order is kept.

**Tests that want the manual testing kit. SETTLED 2026-10-14: they do not need it.** A recogniser only ever looks at names and sizes, so a case is a list rather than a folder. `TheRecogniser` writes the kit's source folders out as listings, which run in microseconds and work the same on a machine that has never seen the kit. The question comes back only when something has to read the inside of a file, and the one such thing so far, an installer's product name, is behind `IFileFacts` with a fake in the tests.

**The installer placement. DECIDED 2026-10-14: the menu steps aside.**

The design board is marked CHOSEN and says to find the game installer's window by process id and move it with `SetWindowPos`. That window is the disc's own menu rather than this app, and **an HTA cannot call `SetWindowPos`**: there is no WSH or HTA API for moving another process's window. Reaching one would mean shipping a helper binary on the disc, which costs the ten year no dependency promise and invites SmartScreen on a stranger's machine.

So it is done from the other side. **The menu cannot move the installer, but it can move itself**, which is a thing it is plainly allowed to do: it shrinks or steps aside so the installer is not covered, and says underneath what is running. If even that fails the installer simply sits where Windows put it, which the board already calls acceptable in its own words.

## A numbered set holds one game. Several games get a disc each.

**DECIDED 2026-10-14.** This replaces both the behaviour v1 removed and the refusal it left behind.

| What was added | What comes out |
|---|---|
| Several games, and they fit one disc | One disc, with a chooser. Unchanged from v1. |
| Several games, too big for one disc | **A disc each.** Standalone, each with its own name, menu and cover. |
| One game, too big for one disc | A numbered set, D1 and D2, because the continuity is real. |
| Several games, one of which alone needs two discs | A disc each for the others, and a numbered set for that one. |

### Why this and not a packer

v1 had a packer and removed it. Its own source says why:

> Which games belong together on a disc is curation: a compilation is something a person assembles, and a packer working from byte counts was making that choice silently. It also produced discs labelled D1 and D2 as though one continued the other, when nothing on either disc ever depended on the other, and, because the label is seeded from the FIRST game, a two-disc build could label a disc with a game that was not on it.

The removal was right and the refusal that replaced it was too timid. **A numbered set is a promise that disc 2 continues disc 1.** That promise is true for one game split across discs and a lie for four unrelated games, so the answer is not to pack them better, it is not to call them a set at all.

Three things follow, and all three are improvements rather than compromises:

- **The labelling bug cannot happen.** A numbered set only ever holds one game, so seeding its label from the first game is always right, because there is only ever one.
- **Nothing is decided on anybody's behalf.** The app never groups games, so it never makes a curation choice silently. One game, one disc.
- **A disc says what is on it.** In ten years a disc labelled `Gothic` is obvious and one labelled `Games, 2 of 3` is not, which is the whole promise of the thing.

### What it costs

More blanks, sometimes. Four games of 3 GB, 1 GB, 0.5 GB and 0.4 GB would fit two DVDs packed and take four this way. That is two blanks against four discs that each say what they hold, and it is the right side of that trade. If it ever is not, the answer is an option that offers to combine them, never a default that does it quietly.

### What it means for the code

`DiscPlanner` is unaffected and stays as it is: arithmetic over a flat list of files, told nothing about games. What this decides is the layer **above** it, which runs the planner once per game and hands back a batch of outputs, most of them a single disc and occasionally one of them a numbered set. That layer does not exist yet.

The `L - a bundle, one big and three small` fixture is the test for it: four games, a target they do not all fit on, and the expected answer is four discs rather than a refusal and rather than a packed set.

## Staging is allowed to reach the Menu, and only Staging is

Added 2026-10-14, when `DiscBuilder` was written and the architecture test refused it.

The builder assembles the list of steps, and one of those steps writes the menu. `Core` holds the pipeline and the steps that need nothing but ports; `Menu` holds the menu and its step. Something has to know about both, and the choice was:

- put the builder in `Core`, and have **`Core` reference `Menu`**, which is the one rule this whole arrangement exists to stop, or
- put the builder in `Staging`, and let **`Staging` reference `Menu`**, which is a new edge in the graph and nothing else gains one.

The second, because `Staging` is the one place that assembles a whole disc and a menu is one of the things on a disc. The allowed graph in `TheArchitecture` says so, with the reason beside it, since the test is where somebody will meet this.

What it is not: a general loosening. `Core` still references nothing, nothing references the window, and every other edge is unchanged.

## What a disc holds, and where

Settled as the extras were written, because every one of these is a place something could have gone and did not.

| What | Where on the disc | Which disc of a set |
|---|---|---|
| A GOG download | the root | spread across them, cut only if one file is too big for any |
| A folder of game files | the root, keeping its own shape | the same |
| Several games | `01 - name`, `02 - name` | they share one disc, or get one each |
| The menu | `AUTORUN/menu.hta` and `menu.js` | every disc |
| The background | `AUTORUN/background.png` | every disc |
| Music | `AUTORUN/music.<ext>` | **every disc**, and so it is overhead |
| The icon | `disc.ico` at the root | every disc |
| The name | `autorun.inf` at the root | every disc, numbered |
| The printable artwork | `ARTWORK/` | every disc |
| The checksum list | `checksums.sha256` at the root | every disc, covering that disc |
| A manual | `Extras/` under its own name | **one disc**, the one with the slack |
| An extras folder | its *contents* into `Extras/` | one disc |
| Loose files and folders | the root, exactly as they are | one disc |

Three of those are decisions rather than placements.

**Music rides every disc and is therefore overhead.** It belongs to the menu, and every disc of a set has a menu of its own to play it. Everything else in `Extras` rides one disc, because copying a three gigabyte texture pack onto five discs costs five times the room, and the planner already knows which disc has the slack.

**An extras folder contributes its contents, not itself.** Somebody picking a folder called "stuff for the disc" did not mean to put a folder called "stuff for the disc" on the disc. A *loose* folder is different and keeps its name, because it was added as itself rather than as a container.

**The menu only offers what the disc can honour.** A Manual button on a disc with no manual is worse than no button: it is a promise the disc cannot keep, found by somebody who has already burned it. On a set, only the disc carrying the extras offers them.

### Names a disc keeps for itself

`autorun.inf`, `.xdg-volume-info`, `disc.ico`, `disc.png`, `checksums.sha256`, `AUTORUN`, `ARTWORK`, `Extras`, `Games`, `Add-ons`, `Start Here.hta`, `discproject.json`, and anything beginning `setup_`.

A loose extra with one of those names is **refused by name**, not renamed around. Quietly putting their file somewhere else is worse: they would burn a disc believing it holds a file it does not hold, under a name they could find it by. The `setup_` rule is there because a loose file called that at the root would be read back as a game the next time the disc was opened.

## One menu, two screens

A disc with several games opens on a chooser; a disc with one opens on that game. **Both screens are in every menu**, and which one it opens on is decided when it runs rather than when it is built.

That is one template, tested once, instead of two that have to be kept in step. `whichScreenToOpenOn` lives in `menu.js` with the rest of the logic, so the decision is tested under `cscript` like everything else there.

Three things on that screen are decisions:

**A game's name is set as text, never built into the markup.** The rows are written empty and filled with `innerText` afterwards. Built in, a game called "Command & Conquer" would arrive as "Command  Conquer", and one with a bracket in it as something worse. The name is the one thing on that screen somebody else wrote.

**`gameAt` is bounds checked**, because the index comes from whatever the menu was told. An index past the end in JScript is not an error: it is `undefined`, the next line asks `undefined` for its name, and the whole menu stops with nothing on screen to say why.

**A game with nothing to install says "Copy and play" rather than "Play".** A game run off a disc cannot save: the disc is read only, and the first time it tries to write a save it either fails or silently writes nowhere.

### Where a game's folder is decided

In `OnTheDisc`, and in one place only. The builder asks it where to copy a game's files, and the menu asks it what to call the folder it has to run something out of. Worked out twice they would agree until somebody changed one of them, and then the menu's only button would point at a folder that is not there.

### Still missing from a game's screen

The chosen launcher. A folder of game files has several executables and the recogniser deliberately picks none of them: which one starts a game is a question for the person, and the window is where it gets asked. Until then such a game offers to copy its folder, which is a true and useful answer, just not the whole one.

## Any one disc of a set can speak for the whole set

Every disc of a set carries `Disc set.txt`, and every copy of it describes **every file on every disc**. Somebody who finds disc 2 in a drawer gets a disc that knows what it belongs to, what is missing, and how to put the game back without this program.

**Three things read that file**: the PowerShell snippet inside it, the menu on the disc, and a person. All three take a table row to be a line that *starts with a digit and holds a star*, so **every other line is indented**. That one rule is what keeps the prose out of the parsers, and it is why the file can be both an explanation and a database.

### The set id

Twelve hexadecimal characters over every file in the set: disc, path, size and hash. Not a security measure, a label, and it answers the one question a failed join cannot.

A join that produces the wrong hash says the finished file is wrong. It does **not** say that disc 2 came from a later download than disc 1. Two discs that disagree about the set id were built from different sources, and the menu can say so when the second disc goes in rather than after four minutes of copying. That closes the gap recorded earlier as "a set's identity carries no version".

### Written after the discs, not as a step

It describes every file on every disc, so it cannot be written until all of them have been hashed. The builder writes it in a pass of its own once the discs are staged, and it **costs nothing**: the hashes are read back out of the checksum lists that were just written, so nothing is hashed twice.

The price is that `Disc set.txt` is the second file `checksums.sha256` does not cover, after the list itself. Both are the disc's own bookkeeping, and the set file carries its own identity in the set id inside it. Worth knowing rather than worth hiding.

### A disc on its own gets none

Telling somebody that one disc of one is all there is would be noise on every disc that never needed it.

## Add-ons, and the gap they close

### An add-on is dead until its game is installed

**This was the first of the three gaps found in the shipped app.** On the menu as it was, an add-on is just another button: press the soundtrack installer before the game and it runs, and a patch run before its base is worse than that, because it half-applies to nothing and leaves no sign that it did.

So an add-on is dead until the game is on the machine, and says so in the one line it has: *"Install the game first, then this."* The button looks like one that cannot be pressed rather than one that does nothing when pressed.

**Whether the game is installed is not answerable yet.** It needs the registry lookup that the Play button will also want, and neither exists. Until then the menu answers "not installed", so every add-on stays dead and says why. That is wrong in one direction only, and it is the harmless one.

### Patches first, and in version order

A patch applies to the version before it, so running 1.3 before 1.2 is how somebody ends up with a game that will not start. Everything else keeps the order it was added in, which is the order somebody chose.

### A patch is named after the version it moves to

`patch_hollow_knight_1.5.12620_to_1.5.12700.exe` becomes **Update 1.5.12700**.

The version it moves *to* is what tells two patches apart, and it goes at the front where a button will not clip it.

**Never from the product name inside the file.** Every GOG patch reports the product name of the *game* it patches, so all four Hollow Knight patches call themselves "Hollow Knight", and a disc built from them would offer four identical buttons.

### Where they go

Beside their game, in an `Add-ons` folder inside the game's own folder. A disc holding four games keeps each one's patches with it, rather than in one pile at the root where nobody can tell which belongs to what.

They are planned **with** their game, so they are never cut away onto a disc the game is not on: a soundtrack on disc 3 of a game that finishes on disc 2 is a button somebody can press and nothing else.

## The window, decided

**WPF, MVVM, and an `AutomationId` on everything the tests touch.**

The reason is narrower than it looks, and it comes from v1. That app is **WinForms**, and its window suite fought UI Automation the whole way: `AccessibleName` and `Name` on a Panel do not reach UIA, only a GroupBox's `Text` does, and the disc menu is invisible to it entirely. That is a **WinForms** limitation, not a UIA one: WinForms reaches UIA through a legacy accessibility bridge where plain containers have no peer at all.

WPF has no bridge. `AutomationProperties.AutomationId` is a first-class attached property on every element, surfaced by WPF's own automation peers. Choosing WinForms again would be choosing the same fight.

WinUI 3 would give the Windows 11 look for free and charge for it in packaging, churn and a weaker automation story. The canvas already fixes every colour, the font and the exact 920 by 620, so the free look was going to be overridden anyway.

### What makes it cheap to change

**There is no WPF in `DiscWright.ViewModels` at all.** It is `net10.0`, not `net10.0-windows`, so the compiler enforces it the same way it enforces Core's purity.

That is what turns "what happens when you pick a folder with no game in it" into a test that runs in milliseconds with no window, no desktop and no machine, and leaves an automated window test with only the clicking to do. v1 cannot do that: its logic and its controls are the same eight thousand lines, which is why its suite needs a VM and a workaround for the foreground lock.

It is also the insurance. If WPF turns out to be the wrong answer, what gets thrown away is the markup. None of the view models move.

### Whether somebody has to be in the room

A disc is ejected after it is written so that reading it back sees a freshly mounted volume rather than what Windows remembered about the blank. **A drive that cannot pull its own tray in stops dead at that point and waits for a hand**, and somebody who finds that out afterwards has already walked away.

So it is asked before anything is written, and said in the window:

> E: cannot close its own tray. Every disc has to come out to be checked after it is written, so this will stop and wait for you to push it back in.

#### How a drive is asked

By shutting a tray that is **already shut**. That does nothing at all to a drive that can, and is refused outright by one that cannot. Measured on two here:

| Drive | `CloseTray()` on a shut tray |
|---|---|
| ASUS DRW-24D5MT | it worked |
| HL-DT-ST BD-RE BU40N, a slim one | refused, `0x80070001`, incorrect function |

**Only drives with a disc in them are asked**, because an open tray would close for no reason anybody could see. That rules in a disc that would have to be erased first, which is somebody's next burn once they have said yes to it, and rules out an empty drive, which is nobody's.

This cannot be hardcoded to the two drives here. Other people have other drives, and the only honest way to know is to ask the one in front of you.

### The size starts from the disc that is in there

The size of disc defaulted to DVD5 for everybody always. Somebody with a 700 MB disc in the tray was therefore planned for a 4.7 GB one, and a set that should have been two discs was built as a single 735 MB disc that nothing would fit on.

A `Medium` is a target and a `Recorder` is a tray with something in it, and keeping those apart is deliberate. `TheDiscsThatAreIn` is the one place allowed to put them side by side, and it does two separate things with the answer.

**It offers a size to start from**, taken from what is actually in a drive. Two drives holding two different discs offers nothing, on the same principle as everywhere else here: it refuses to guess between drives rather than taking the first one and being quietly wrong.

**It says so when the two have drifted apart**, in the worry ink, under the dropdown:

> The disc in D: is a CD-RW, and this is being built for DVD5 4.7 GB.

A worry and never a refusal. Planning a DVD with last week's CD still in the drive is an ordinary thing to be doing, and the disc is built for what was asked for either way.

**The moment somebody chooses a size, the drives stop moving it** and only comment. Opening a project file counts as choosing, because the size it was saved with was somebody's answer once. Starting a new disc makes the drives matter again, since a new disc is a new question.

#### Telling one size from another

Nothing in IMAPI's numbers tells a BD-R 25 from a BD-R 50, and a DVD+R is the same number whether it has one layer or two. So the kind of disc narrows it to a family and how much the disc holds settles which one inside it, which works because the figures in `Media` are usable bytes and sit under the raw capacity by design.

That leaves what to do with a disc that has something on it already, where what is **left** is not how big it **is**. A used DVD cannot be told apart from a smaller one, so it answers nothing. **A CD is a CD either way**, because there is only one size of CD this program aims at, and insisting on a blank one left somebody with a used CD-RW in the tray being planned for a DVD, which is the whole fault this exists to fix.

#### Asking Windows rather than asking the drive

The window learns about discs from `WM_DEVICECHANGE` rather than by looking repeatedly. **Polling an optical drive spins it up**, so a window that checked every few seconds would keep somebody's drive awake all day to answer a question nobody asked.

### What the two buttons mean

This program made nothing but an ISO once, and its words outlasted that by a long way. Somebody reading **Make the disc** has every reason to think it burns one.

So the pair say what they do, and a line under them says what the difference is:

> Building puts the discs on this PC. Burning writes one to a real disc.

One sentence, in the window, forever, is worth more than any amount of renaming. The buttons are now **Build it** and **Burn to a disc...**, and the one at the top is **Open one you built before...**, because what it opens is a folder on this PC and not a disc in a drive.

### The one option that depends on somebody else

A disc opens its menu by itself **only where AutoRun is switched on**, and Windows has been turning that off for years. Somebody whose disc does nothing when it goes in has no way of knowing the reason is on their own machine rather than in the disc, so the option says so on hover, and `TheWindowsMarkup` holds the rule that it must.

It is the only option here whose effect is not entirely ours, and it is on by default because a menu is what the disc is for.

#### The way in when AutoRun is off

So every disc carries **`Start Here.hta` at the top**, where somebody looking at it in Explorer will see it. It shows no window of its own: it works out where it is, starts `AUTORUN\menu.hta`, and closes.

Ported from v1 with its comments, because the hard part was learned there. **AutoRun can start a disc file with no drive letter**, so the launcher cannot use a relative path and has to work its own folder out of `document.URL`, allowing for the leading slashes a file URL arrives with, which are not part of the path except on a network share where two of them are.

Every backslash in it is built from `String.fromCharCode(92)` and there is not one literal backslash in the file, which a test asserts. A typed backslash has been halved on the way into a generated file more than once in this project, and a launcher with a broken path is a disc that cannot be opened by the one person whose AutoRun is off.

The name is reserved in `ReservedDiscNames`, so a game bringing its own file of that name is moved aside rather than quietly overwriting it.

### A window drawn for four options does not hold five

The menu checkbox was moved out of More options and then back into it, which took the drawer from four entries to five, and the last two fell off the bottom of a window that had been 620 tall since it was drawn.

The window is 700 now and the markup test says so. **The size is a decision, not a constant**: when the drawing changes the number changes with it, deliberately and in the same commit.

### The accent is a colour, not a size

The button that matters wore a bigger font and twice the padding as well as the accent colour, which made it read as a different kind of thing rather than a louder one. Every button in the program is now the same shape, and the one that matters is the one that is teal.

`TheWindowsMarkup` holds the rule that **no button sets its own padding, width, height or font size**. A button wearing its own measurements is a button that stops matching the one beside it the moment either is touched, which is exactly how the two came to differ.

### A line with nothing to say is not there

The refusal and the worry used to sit in the window always, empty most of the time. An empty line still takes the height of a line, and two of them were most of the gap at the bottom of the window.

They are collapsed now, which has a consequence worth stating: **a collapsed thing is not in the automation tree at all**. That is right rather than unfortunate, since an empty label is not something to read out, and it means `See-TheWindow.ps1` cannot simply ask for them. It checks the useful half instead: that they are absent while nothing is wrong, and that the worry comes back the moment a name arrives that AutoRun cannot carry.

### Starting a new disc keeps the machine and forgets the disc

The games, the name, the artwork, the manual and everything the last run left behind all go. **Where the discs get built, and what is in More options, stay**, because they are how this person works rather than what they are making, and having to find that folder again every time is its own small insult.

It does nothing at all while a build or a burn is running. There is no sensible meaning for clearing the screen out from under a laser.

It asks first, but **only when there is something to lose**. Confirming an empty screen is how somebody learns to click through the question that matters, and the question that matters here is the one about erasing a disc.

### Two rules the window itself keeps

**Everything the tests touch gets an `AutomationId`**, set deliberately and never left to the framework.

**A refusal is worked out as things are chosen, not when the button is pressed.** A refusal that arrives after a click is one somebody has already committed to; the same words beforehand are just a sentence.

### The markup is a tested thing

`TheWindowsMarkup` reads `MainWindow.xaml` **as XML from the source tree**, rather than loading the built window. Nothing may reference the window project, including the tests; and Smart App Control on this machine refuses to load our own unsigned assemblies into a test host, so a test that instantiated a window would pass on the VM and fail here.

It guards six things: every interactive control carries an id, no two ids are the same, the window is still the size the design was drawn at, exactly one button wears the accent colour, no colour is written out in figures instead of named, and the button that makes the disc is **dead rather than hidden** when it cannot be pressed.

**This is the test that stops v1 happening again.** In v1 somebody added a control, UI Automation could not reach it, and nothing failed; the only sign was a test that had to be written a different way. Here it fails the build.

### What the markup test cannot prove

That the ids **arrive**. `tools/See-TheWindow.ps1` opens the real window, asks UI Automation for all eighteen by id, types a disc name in through the automation pattern and reads it back, photographs the window and closes it.

A collapsed `Expander` hides its contents from the automation tree, which is correct: they are not on screen to be clicked either. The script opens it and asks again, which is what a window test will have to do.

**It also showed something about Smart App Control.** The app itself runs here, although the test host cannot load the same assemblies. That is almost certainly because a locally built exe carries no Mark of the Web, so it says nothing either way about a downloaded release, which will carry one. **The signing problem is unchanged.**

### The buttons do things now

The one big button was wired to close the window, and the add-on buttons to nothing at all. Both now do what they say.

| What it says | What it does |
|---|---|
| Play | runs the installed executable, from where it is installed |
| Install | runs the installer on the disc |
| Copy and play | opens the game's folder, which is the honest offer for a game that is just files |
| an add-on | runs it, and only once its game is on the machine |

**Deciding and doing are separate.** `whatPressingItDoes` works out which of the three acts it is and where the thing lives; `startIt` does it. Both are in `menu.js`, which matters more here than anywhere else in the program: **an HTA is invisible to UI Automation**, so nothing can press a button in that page, and whatever logic stays in the page can never be tested at all.

So `startIt` is tested by really starting something: a script that writes a file and stops, with the test waiting for the file. It is the only proof that a menu button does anything, and a menu button that does nothing when pressed is the worst outcome in this program, because somebody is standing in front of a disc they burned and no amount of correct behaviour elsewhere makes up for it.

It also says what went wrong rather than failing in silence, and an add-on checks again that its game is installed before running, because a dead button somebody clicks anyway is still a click.

#### Copying a game off the disc

A game with nothing to install is **copied and played from where it lands**, not run off the disc. A disc is read only, and the first time a game tries to write a save it either fails or silently writes nowhere.

**One file per tick, not one call to CopyFolder.** A window that is not given the thread back cannot repaint, so the bar would not move and Windows would call the menu unresponsive; and a copy that stops half way has to be able to say which file it stopped on.

Where it goes is **offered rather than decided**: somebody with a small system disc and a big one for games knows better than this menu does. The suggestion is under their own profile, because a game copied somewhere they cannot write to half copies and stops, and because a game that cannot write cannot save.

**What runs afterwards is only ever something the disc named**, and the disc names one only when the recogniser found exactly one candidate. Guessing which of six executables starts a game is how somebody ends up running an uninstaller. With nothing named, the folder is opened instead, which leaves them with the game and knowing where it is.

**The disc's own files stay on the disc.** A game that is the only thing on a disc sits at the root, beside the menu, the autorun and the checksums, and copying it should not carry those onto somebody's hard disk. The list comes from `ReservedDiscNames`, written into the menu's facts, rather than a second copy of it in JScript that would drift. Only the top of the disc counts: a game with a folder of its own called `AUTORUN` inside it is the game's business.

**Checked by size at the end, not by hash.** A menu has no business reading several gigabytes twice, and what this catches is the copy that stopped when somebody took the disc out.

#### The manual, the extras and the icon

`ButtonsFor` had worked out for a while that a disc should offer a Manual and Extras, and the page never drew them, so they were computed and unused. They are drawn now, and **only on the disc that carries them**: the one disc of a set that has the extras is the only one that offers them, because a Manual button on a disc with no manual is a promise found by somebody who has already burned it.

The menu window wears the disc's own icon where one was chosen, and **DiscWright's where none was**, rather than the blank default that makes a program look unfinished. Ours is trimmed to the sizes a window actually shows, 16 up to 64: the full set is 363 KB and almost all of that is a 256 pixel frame nothing on a disc will ever ask for. 33 KB, and only written when it is needed.

That wanted one new thing on `IStageWriter`: a way to write a file that is not text. `Copy` needs a file to copy from and there is not one, because this icon lives inside the program.

#### Making the .msix

`packaging\Build-TheMsix.ps1 -Pack` produces one with `MakeAppx`, which ships with the Windows SDK.

**MakeAppx lives under a version folder, beside which sit `x86`, `x64` and `arm64` folders of the same depth.** Sorting them all by name and taking the top looks in `x86\x64` and concludes the SDK is not installed, which is exactly what was reported here for a day while the SDK had been installed since February. Only folders beginning with a digit are versions.

The package comes out **unsigned on purpose**. The Store signs what it accepts, and installing one locally needs a certificate of your own trusted on the machine, which is a different job from building it.

#### The background is drawn, not merely carried

For the whole of the rewrite the background was resized, written to `AUTORUN\background.png` on every disc, and **never referred to by the page**. `CopyTheArtworkStep` even carries a comment about a disc "carrying a file nothing ever opens", about a different case.

The rule goes on `#stage`, which is the whole 760 by 480 window with the panel drawn over it, and it is written **only when there is a picture**: a `background-image` that does not resolve leaves the panel floating on whatever the browser decides, so the disc looks broken rather than plain.

A CSS url resolves against the page, unlike [[an HTA's icon attribute]], so a bare file name beside `menu.hta` is enough.

**The test that let this ship checked the file arrived.** That is not the same as the page showing it, and the difference is the whole bug. There is now a test that the page names the picture, and the rendering was confirmed by photographing a preview the app generated.

#### A preview does not pretend it is a disc

A preview stages the menu, the artwork and the music, and **nothing else**. No installer, no manual, no extras. So every button on it points at a file that is not there, and pressing one produced whatever Windows says about a missing path, which reads as the disc being broken rather than as the preview doing its job.

The page could not tell: `forADisc` only ever reached the markup as `singleinstance`, never the data the script reads. It is in the data now as `onADisc`, and the page calls `thisIsAPreview()` **before it draws anything**, so no button can be pressed before the script has been told.

The flag lives in `menu.js` rather than being read from `DISC`, because **the page cannot be tested**: an HTA is invisible to UI Automation, so nothing can press its buttons. `menu.js` runs under cscript, where there is no page at all. The page being the only thing that can switch the guard on is why there is a separate test that it does.

#### Every disc carries its own recipe, and now one gets written

`AProjectFile` read `discproject.json` from the first day of the rewrite, and **nothing ever wrote one**, so every disc 2.0 built was a disc nothing could reopen. The promise in that file, that somebody who finds a disc in a drawer can open it, change one thing and build it again, was half kept: the reading half.

`TheProjectAsJson` writes version 14, the shape v1 wrote and the shape the reader understands. A field the reader reads is written **even when it carries a default**, because the reader treats a missing field as "what the version that lacked it did", so leaving out whatever happens to be false writes a file that means something else.

It goes **beside the discs, not on one**, which is where v1 put it and therefore where "Open one you built" looks. A disc is a copy of a game; the project is the recipe, and a recipe on the disc is a file on every copy nobody asked for. Written once per build, not once per disc: a set is one project.

The guard is the **round trip**, never a list of expected field names. A writer checked against what its author believed the reader wanted is how a file nothing can open gets written with complete confidence.

#### A drive that has not finished thinking is not a drive that refused

Asked the moment a tray shuts, a writer can take **ten seconds** to say what is in it, measured on the ASUS here. Until then, IMAPI throws rather than answering, and `WindowsRecorders` turns that into `Recorder.WhyNot`.

Taking that as an answer is worse than it sounds, because **`ErasingWouldHelp` is false whenever `WhyNot` is set**. So a used disc put in for the next disc of a set never gets as far as the offer to erase it: the erasable list comes back empty, the question is skipped, and the answer is "the drive would not say what is in it" with nothing to do about it.

Both ways in now give the drive fifteen seconds to wake up before its silence counts, and the one that is waiting on a named drive waits only on that one.

#### 0.12.0, not 2.0.0

The rewrite carries on from the PowerShell app's numbering rather than starting again. That one goes to 0.11.0, so this picks up at **0.12.0** and the two read as one project at different stages, which is what they are. A round 2.0.0 claimed a finished thing, and an app nobody can be given yet does not get one.

The installer is named **DiscWright**, not "DiscWright 2.0", and installs to `DiscWright (rewrite)`: a folder named for what it is rather than for a version it will outgrow. It keeps its own AppId so it sits beside the PowerShell install instead of taking over a working one.

#### The cover art always goes on the disc

There was a setting and the usual answer was no, because **the usual answer to a setting nobody understands is to leave it alone**. A cover and a label are small, and a disc that carries them is one somebody can reprint the sleeve from in ten years without still having the folder it was built from.

`ArtworkOnDisc` is gone from `DiscOptions` and from the window. It is still **written to the project file as true**, because v1 reads that field and a file saying false would open there as a disc without artwork.

#### An HTA is IE5 unless you tell it otherwise

Rendered the menu and asked it: `documentMode: 5`, `compatMode: BackCompat`. An HTA with no doctype and no compatibility meta runs the **IE5 quirks** engine, which has no `rgba`, no `box-sizing`, and no modern anything. A see-through colour written in that file is simply dropped, and the menu comes out looking exactly as it always did.

So `<meta http-equiv="X-UA-Compatible" content="IE=edge" />` is what makes any of it work: `documentMode: 11`, `compatMode: CSS1Compat`.

That switch moves the box model from quirks to standards, where padding sits outside the declared width. `#panel` is 240px wide with 22px of padding each side, so it would have grown to 284 and pushed itself off the edge of a 760px window. `box-sizing: border-box` holds it, measured at **240 both before and after**, with the buttons at 196 both times.

The menu's own script survives the switch because it never used `attachEvent`, `document.all` or `window.event`, and `ActiveXObject` is an HTA host privilege rather than a DOM-mode feature. Checked against the old build rendered the same way: same seven buttons, same labels, same widths.

#### InvalidComObjectException is not a COMException

The burning code catches `COMException` around every call into IMAPI. **`InvalidComObjectException` does not derive from it**, so it walks straight past, out of the burner, out of the view model, and into the window's catch-all, where it is shown as raw exception text in a message box. It reads as a crash and is not one.

What it means is that something used a COM object after it was released. The drive is handed a managed callback for the length of an erase, and the teardown calls `Marshal.FinalReleaseComObject` with a forced collection behind it, so a late call into that callback is enough to produce it. Seen at the end of an erase in the middle of a two-disc set, which stopped the set.

Both the erase and the burn catch it by name now and answer with a sentence.

**And the view model no longer lets anything out.** Whatever escapes becomes a refusal saying it was not accounted for and is worth reporting, because an answer somebody can act on beats exception text every time, and the window's catch-all should never be what somebody sees.

#### An erase has no unit of work but seconds

The drive hands the eraser **elapsed and expected seconds** and the eraser threw both away into a sentence, so the bar sat at nothing for the whole half minute while the words counted up beside it, which reads as a program that has stopped.

`EraseProgress` carries a fraction as well as the words. It is seconds over seconds, which is an honest guess rather than a measurement: erasing is the one job here with no byte count. It is held below one, so the bar never claims to be finished while the disc is still spinning and an estimate that turns out short does not make it go backwards.

**A test watching `HowFar` must look for a value strictly between nought and one.** `EraseADisc` ends by saying "blank again" at one, so "greater than nought" passes with the plumbing cut, which is how the first version of that test passed.

And `Progress<T>` **posts** its callbacks rather than calling them. A fake that reports and returns instantly has them arrive after the window has stopped working, which throws them away. A real erase takes half a minute and never notices.

#### A tick and Remove mean different things

**Unticked** is *not on this disc*, still listed, one click from coming back. **Removed** is *gone*, and comes back only by adding the folder again.

So Remove takes **the row that is selected**, whatever it is, and when nothing is selected it clears out **everything unticked**, which is the tidying gesture after deciding what is not going on. The first version took the whole game whatever was selected, so clicking a file and pressing Remove emptied the window.

A disc carrying a patch whose game is not on it is **allowed and said out loud**. Somebody making a disc of patches is doing something unusual, not something wrong, and this window refuses by explaining rather than by greying things out. It is the kind of disc somebody makes by accident far more often than on purpose, which is why it is worth a line.

#### A tree, because a game is not one thing

A game is an installer, its parts, whatever folders came with it, and the patches taken on beside it. Shown as one row, the only way to leave any of that off was to not add the game at all.

Every row carries a tick. Everything under a row follows it and a row follows what is under it, so **a folder with some of its files left out shows neither on nor off** rather than lying about what will be written.

The tree holds everything that was found and never forgets a row that was turned off, because turning it back on has to put the same file back. What will be written is read off the tree whenever a tick changes: `Games` gets `game with { Files = kept }`, and since every size, refusal, disc count, staged folder and ISO is worked out from that file list, **nothing else had to learn about leaving a file off**.

Leaving the installer off is allowed, because this window refuses by explaining rather than by greying things out. `Installer` is cleared rather than left pointing at a file nobody put on the disc, and the menu already copes by writing no Install button.

#### A mutation run has to prove it ran

`tools\Mutate.py` refuses to draw a conclusion without seeing `Passed!` or `Failed!`. Smart App Control blocks a freshly rebuilt unsigned test assembly here, `0x800711C7`, so the run never starts, there is no `[FAIL]` anywhere, and a harness looking only for failure calls that a survivor. A `caught` verdict is always sound; only `SURVIVED` is suspect. A mutant that stays inconclusive gets planted and run on the VM, which has no Application Control.

#### The Microsoft Store route is open

A packaged DiscWright **can burn**. Tested rather than reasoned about: a disc written and read back clean by the MSIX-registered app, under real package identity, on real hardware.

The worry was that IMAPI2 could not reach a drive from inside the container, which would have killed the Store route outright, and nobody on the internet appears to have written down an answer either way. The answer is that the question was misframed. A packaged desktop app declaring `EntryPoint="Windows.FullTrustApplication"` runs at medium integrity and **not in an AppContainer**, and the capability-gated device access that stops a UWP app touching hardware is an AppContainer mechanism. Outside it, COM and device handles behave exactly as they do for an ordinary exe.

`packaging\Test-ImapiInsideThePackage.ps1` asks the question without needing a disc, and **refuses to answer unless it can prove it is inside the package**: `GetCurrentPackageFullName` returns 15700 for a process with no package identity, and a probe that quietly slipped outside would report success for the wrong reason.

The Store still matters for a different reason: a package it signs is trusted, which is the actual answer to Smart App Control rather than the single-file workaround above.

#### One file, because of Smart App Control

The app ships as **one self-contained file** and that is not a packaging preference.

Smart App Control is on and enforcing on a current Windows 11. A freshly built unsigned DLL has no reputation, so Code Integrity refuses it by name, ids 3033 and 3077 in `Microsoft-Windows-CodeIntegrity/Operational`. The exe is allowed to start and then dies with `0xE0434352` a second later, which looks like a crash rather than a block.

Inside a single-file bundle the managed assemblies load from memory, so there is no file to check and nothing is refused. 62.5 MB, first run about 2.4 s, later runs under a second.

**This is not signing.** It removes one class of block on one machine. An unsigned exe has no reputation anywhere else either, so the certificate is still what stands between this and anybody else running it.

`packaging\Build-TheInstaller.ps1` refuses to wrap a payload with a loose DLL in it, and that check sits **outside** the publish, because the case it guards is `-SkipPublish` reusing a payload from an ordinary publish.

#### A dropdown's open list is somewhere else

The sizes are worded in `MakeADisc` and not in the markup, because a `ComboBox`'s open list lives in a visual tree of its own. A binding inside a row that reaches out to ask the window a question is a well known way to end up with rows that quietly show nothing, and nothing in the build would say so.

`Medium` stays the one true answer and `ASizeToAimAt` is a view of it, so a saved project file goes on naming a size rather than a sentence about one. The row is found **by key and never by identity**: a size restored from a file is equal to the one in the list without being the same object.

Three places set the size without going through its setter, and each has to say the row moved as well: the setter itself, a disc settling it from the drive, and a project being opened. A dropdown only redraws for a property it was told about.

#### An HTA will not resolve a relative icon

`icon="menu.ico"` leaves the window wearing Windows' blank default even with `menu.ico` sitting beside the page. An HTA wants an absolute path, and **a disc has no idea which letter it will be given**.

So the icon is set from script as well, out of the page's own address, the same way the launcher at the top of the disc finds the menu. The attribute stays for the machines where it does work, and nothing turns on either of them: a window with the wrong icon still works.

#### Names cut to fit a button

An add-on is named after the file it came from, and a GOG installer's name carries its version and its build number: `Grim Fandango Remastered Soundtrack 2.0.0.9 (31045)` is fifty characters on a button that is one of three.

Cut at a word, since a name chopped mid-word looks like a fault rather than a choice, and with any trailing punctuation removed, because a comma before an ellipsis reads as a typo. It cuts mid-word only when backing up to a space would throw most of the name away. **The whole name stays in the tooltip**, because somebody with two patches has to tell them apart.

#### Where the disc is

Worked out from the page's own address, as the launcher does, because AutoRun can start a disc file with no drive letter. The menu sits in `AUTORUN`, so the disc is one folder further up. The paths in the menu's facts are written with forward slashes and relative to the root of the disc, since a disc does not know which letter it will be given.

## Seeing the menu before the disc

**It runs the same step the build runs.** A preview written by a second piece of code that draws something similar is a preview that drifts from the disc, and the day it drifts is the day somebody burns a disc because the preview looked right.

So it plans the discs exactly as building would, takes disc one, and runs `WriteTheMenuStep` over a folder that is not a disc. A test holds the two outputs side by side and asserts they are **the same text**, bar one difference.

That difference: a disc insists on being the only one of itself open, so putting the same disc in twice refocuses its window rather than opening a second. A preview must not, or previewing one project would refocus the stale window left from previewing another a minute ago, and show somebody the wrong games.

It goes somewhere temporary, in a fresh folder each time. A preview is not an output and has no business in the folder somebody keeps their discs in, and reusing one folder means a menu that quietly shows the last project's games when this one refuses to plan.

It is opened with `mshta`, which is what the disc itself uses, so what is on screen is the thing that would be on the disc rather than an approximation of it in a browser.

**What it does not show yet is the artwork.** Only the menu step runs, so a background chosen for the disc is not there to be drawn on. That waits with the rest of the artwork.

### A property nobody is told about

`CanPreview` was right every time it was read and the button stayed dead for ever, because nothing raised it: the window read it once, got false, and never looked again. Found by pressing the button in the real window, not by a test, since a test that reads the property gets the right answer.

## Asking whether there is a newer one

In the corner under the name, where a program usually keeps its version, and as quiet as the line above it. It reads `2.0.0  Check for a new version`: which version this is, shown without anybody asking, because that is a question nobody should have to press a button to answer.

It is barely an action and does not look like one. A button the size of the ones that build and burn discs, for something checked once in a blue moon, was the wrong weight entirely.

A button, and only a button. **Checking on every start is a network call nobody made**, from a program whose whole point is putting things on a disc so they keep working with no network at all.

**It only ever says that a version exists.** Downloading and replacing a running program is a far bigger thing than reading a number, and doing it to an unsigned program that Windows already distrusts would be worse than not offering it at all. When there is something newer it offers the page; the browser does the rest.

One request to `api.github.com/repos/lazardjokovic/discwright/releases/latest`, which needs no account and no key, and which GitHub allows sixty times an hour from one address. Nothing identifying goes with it: the user agent is the program's name.

### Reading and comparing are separate things

`HowThisBuildCompares` is handed what was said and works out a sentence, so every case can be tested without a network. The asking is a port, and it **reports a failure rather than throwing one**, because not being able to reach GitHub is an ordinary afternoon and not a fault in anything.

Each failure gets its own sentence. Somebody told only that it did not work has no idea whether to try again in a minute or give up:

| What happened | What it says |
|---|---|
| no releases yet | Nothing has been published there yet. |
| rate limited | GitHub is asking for a rest. Try again in a few minutes. |
| no network | There is no way out to the internet from here. |
| timed out | It took too long to answer. |

### A machine with no internet is not an edge case

This is a program for putting things on a disc so they keep working with no network at all, so the machine with no network is the one it is for. **Nothing may break and nothing may throw.**

Those paths are tested through the real class with a stubbed way out, rather than against a hand written stand in, because the catches being tested are in the real one. No route out, a timeout, a refusal, a page that is not there, and a hotel captive portal answering every request with its own login page and a cheerful 200, which is valid HTTP and unreadable JSON.

Being cancelled on purpose is deliberately **not** swallowed with the rest, or the stop button would do nothing.

### Newer than anything released

The program had no version at all: with none set, .NET stamps 1.0.0 on everything, and a program that cannot say which version it is cannot tell whether a newer one exists. It is **2.0.0** now, read from the assembly rather than written down twice, since two places that both state the version will disagree eventually and the wrong one will be the one shown.

The newest thing published in the public repository is v1's **0.10.0**, so every build between now and shipping is newer than anything released. Saying "you are up to date" to somebody running something newer than the shop sells would be a small lie, so it says what is true:

> This is 2.0.0, which is newer than anything released. The newest release is 0.10.0.

A tag that is not a version number is not guessed at either. Tags are written by people, and deciding which of two unrelated words is larger would be worse than saying it cannot tell.

## The testing kit

Everything DiscWright is tested against by hand lives in one place, `%USERPROFILE%\DiscWright-Kit`: twenty source folders, eleven discs already built, the artwork, a set that needs two CDs, and the real GOG downloads linked in rather than copied.

**It is found and not written down.** The tools and the tests look for `DISCWRIGHT_KIT` and fall back beside whoever is logged in, so the same run works here, on the test VM where the user is called `dw`, and on anybody else's machine. A path with a name in it means a test that only ever runs on one machine, and a test that never runs reports as a pass.

**It is inputs, and nothing is ever written to it.** Everything the program produces goes to `DiscWright-Lab`, which gets wiped without warning. The kit lived inside that folder until this was noticed.

It says 11 GB and occupies 130 MB, because the large files are sparse: the filesystem knows they are mostly nothing and does not store the nothing. That is enough for every piece of arithmetic and every refusal, since those happen before a byte is read, and no good at all for a real burn.

### A test that passes by doing nothing

`OpeningTheDiscsThatExist` reads the kit during an ordinary test run and returns early when it is not there. **Absence is indistinguishable from success**, so it had been passing on the test VM by opening nothing at all. Putting the kit on the VM made it fail at once, because a saved project records where its games were, absolutely, and those paths have a name in them that does not exist on that machine. `Send-TheKitToTheVM.ps1` repoints them as they land, byte order marks and all.

### The sweep has to ask the same question every time

`Sweep-TheKit.ps1` drives all twenty folders through the real window and writes down what happened to each, which is the only regression test the window has.

It now **sets the disc size itself and reads it back**, and stops if it did not take. DiscWright starts from the size of the disc that is in a drive, which is right for a person and wrong for a regression run: with a CD in the tray the 4.3 GB folder was refused and with the drive empty it built, so two runs were no longer comparable. A feature that makes the program cleverer about its surroundings makes its harness less deterministic, and the harness has to pin down what it used to assume.

## What driving a real window found

Everything had been tested against folders a test invented. `tools/Drive-TheWindow.ps1` drives the real window through UI Automation against the real testing kit: it presses **Add a game**, types a path into Windows' own folder dialog, reads back what the window says it will make, presses the button, catches it mid-build, answers the dialog at the end and lists what landed on the disc.

It found three things no test had.

### The button was alive with nothing on the list

An empty window says nothing, because somebody who has not added a game yet has done nothing wrong. **Saying nothing is not the same as being ready**, and the button believed it was. Pressing it built nothing and showed a refusal that should have been impossible by then.

### The list showed nothing while building the right disc

`Games` returned the same `List` object every time, so saying *the property changed* told a bound list nothing: it looked, saw the same object, and showed what it showed before. The disc came out correct while the window looked empty.

It is a `ReadOnlyObservableCollection` now, which says what changed rather than that something did.

### A finished build could show a bar stuck halfway

A progress report can be in flight on another thread while the build finishes. Checking whether it is still running and then writing are two steps, and the end can happen between them. The check and the write now happen together under a lock, and the notification goes out after it, because what a listener does with it is not this class's business.

**That one failed on the test VM and passed here**, which is the whole reason the VM is the default.

### A row shows one thing and says another

`DisplayMemberPath` and an `ItemTemplate` both set what a row **shows**, and neither sets what it **says**. Without being told otherwise a row answers to whatever the thing inside it prints as, so the disc size list announced:

> Medium { Key = CD, Name = CD-R 700 MB, Capacity = 730144440 }

That is what a screen reader reads out, and it is also what a test driving the window reads back, which is how it was found: a script asked which size was selected and got the whole record.

The same slip was already fixed once in the games list and then made again in the dropdown, so `TheWindowsMarkup` now holds the rule for every window: **any `ListBox` or `ComboBox` bound to a source must give its rows a name of their own.** It found a third one the moment it was written. That one, the picker, holds plain words and read correctly anyway, and was fixed regardless, because the rule is worth keeping absolute rather than conditional on what happens to be in the list today.

**No unit test sees this.** What a row announces is only visible from outside the program, which is the argument for driving the real window rather than trusting the markup to be what it looks like.

### Windows' own dialogs have the v1 problem

The folder dialog exposes every control as a `Pane` with no patterns on it: the box is id `1152`, Select Folder is id `1`, Cancel is id `2`, and not one of them can be invoked or set. That is the same legacy accessibility bridge that made v1 hard to test, in Microsoft's own dialog. They are driven with the keyboard instead. The first Enter walks **into** a folder and the second accepts it.

### What was left behind, and the question that closes it

A GOG download folder that also held a patch and a `READ ME.txt` put **only the game** on the disc. Nothing said so. Losing a file quietly is the worst way to lose it.

So when a folder holds more than the game, the window asks, listing everything with its size and what it would be:

- Something that installs onto the game, a patch or a DLC or a mod, offered as an **add-on**: it lands in the game's `Add-ons` folder and stays dead until the game is installed.
- Anything else, a README or a manual or cover art, offered as something to **copy onto the disc**: it becomes part of the game's own files, so it lands in the game's folder, gets planned with it, gets cut with it and appears in the checksum list like everything else.

**The kind is a guess and only a guess.** An executable beside a game is nearly always something that installs onto it, but a standalone tool is not, so the guess decides what the window suggests and never what happens. Every row can be changed or unticked.

**Everything starts ticked**, because the surprise worth avoiding is losing a file rather than gaining one.

**Nothing is asked when nothing was left behind**, which is every folder of game files and many downloads. A dialog that appears to say "there is nothing to tell you" is worse than silence.

This is also the first way into the add-on work: until now nothing on the window could attach one.

### Opening DiscWright with a game

`DiscWright.exe "C:\path\to\a game"` adds that folder on the way up, asking about anything left behind exactly as the button does. It is the obvious thing for a right-click on a folder, and it is how the driven window test gets a game in without Windows' own folder picker, which is the one part of this that would not hold still.

It is queued on the dispatcher rather than called from `OnStartup`, because at that point the message loop has not begun and anything that wants to put a window in front of somebody has nowhere to put it yet.

## Where a game sits on the disc

One game **is** the disc, so its files sit at the root of it, which is what somebody expects when they put the disc in. Several games each get `NN - name`.

**Unless the lone game carries a name the disc writes itself.** A game that brings its own `autorun.inf`, or its own `AUTORUN` folder, would have it quietly replaced by the disc's: the disc builds, it burns, and it is missing something somebody chose to put on it. Such a game gets a folder of its own and keeps everything.

This was found by putting the whole testing kit through the real window. Folder F exists to test exactly it, and the file was **ticked, copied, and then overwritten**, inside the feature written to stop files being lost quietly. Folder P was worse in a quieter way: the game's own `AUTORUN`, `ARTWORK`, `Extras` and `Games` folders were merged in beside the disc's.

Two things make the rule safe:

**It minds only the root of the disc.** A file called `autorun.inf` inside a folder of the game's own collides with nothing, and a game should not be moved for it.

**It does not use `ReservedDiscNames.Claims`.** That one also refuses anything beginning `setup_`, which is right for a loose extra somebody dropped on the disc and catastrophic here: every GOG installer ever made begins `setup_`, so every game on earth would be given a folder it does not need.

`OnTheDisc.FolderFor` is still the single home for the answer, and it now takes the game's files, because the question cannot be answered without them. The builder and the menu both ask it, and a disc where they disagreed would be a menu pointing at nothing.

## The kit, swept

`tools/Sweep-TheKit.ps1` puts all twenty source folders through the real window, one at a time, and writes down what each produced. It is the regression run this rewrite did not have: everything before it was either a unit test against folders a test invented, or one folder driven by hand.

It takes the desktop for a few minutes and the two sparse folders are asked about rather than built, because copying 10 GB would take the run from minutes to most of an hour and what the window *says* about them is the interesting part.

## Two questions the window asks on the way in

Both came out of the sweep, and **v1 had already settled both**. Neither is a refusal, because in both cases refusing would be worse than being wrong.

### A folder that holds games rather than being one

Pointing at the folder that *holds* the downloads is the likeliest way to arrive here by mistake. Taking it whole puts every game somebody owns onto one disc entry named after the folder.

`GamesInside.Of` finds the folders beneath it that hold an installer of their own, three deep at most, so that pointing at a drive does not walk the drive. A folder that holds an installer **itself** is a download, not a folder of them, whatever is further down.

**Nothing refuses it**, which is v1's rule and the right one: a real game folder can have an installer buried somewhere too, and turning that away would be worse than taking it whole. The window asks, and says what each answer costs.

Anything sitting **loose** in that folder belongs to none of the games inside it, so it belongs to the disc, and goes on as a loose extra either way. Taking a folder apart must not cost somebody a file: the first version of this silently dropped the `READ ME.txt` from two of the kit's folders.

### Something that installs onto a game, with no game

v1 says it plainly, about the orphan left behind when a game is removed: *a promoted patch shows in the menu as a top-level game, and installing it runs a patch against a game that is not there.* The same is true of one added on its own.

A patch is certain, because `patch_x_to_y` says so outright. The rest is a guess from the words GOG puts in the name of a thing that is not the game, and **it is used only to ask, never to refuse**, so a game genuinely called "Artbook Simulator" costs somebody one dialog and nothing else.

It still goes on if somebody wants it to. It just must not go on in silence.

## The image

Every disc that gets built gets an image too, every time. The folder is what somebody checks and the image is what they burn, and having to ask for the second one is a step nobody wants.

### Everything decidable was decided before any COM ran

`ImagePlan` is four numbers and a worry, in Core, with no Windows anywhere near it. All four were wrong in v1 at least once.

**How big to say the medium is.** v1 used a fixed 12,500,000 blocks, about 25 GB, which silently capped every build at a quarter of a BD-R XL. It is sized to the payload now, with a twentieth on top for the file system, and that old fixed figure kept only as a floor.

**What the older file systems can hold: 2 GiB, not 4.** The format allows four and the writer stops at two. v1 carried a comment claiming the opposite and *a real build died on it after copying 7.79 GB*. When one file is over it the image is still made, without them, with a worry saying so: a disc that reads everywhere except on a twenty-five year old machine is worth far more than no disc.

**The volume name**, which is not the disc's label: sixteen characters, letters, digits and underscores. `Disc 1` and `Disc-1` arrive at the same volume name, and that is fine, because nothing identifies a disc by it. The set manifest does that.

**How often to say anything**, which works out at about two hundred times over the whole write whatever its size. A fixed interval fires a handful of times on a CD and tens of thousands of times on a BD-R XL.

### Two things carried over from v1 that cost it dearly

**Every COM object is released by hand.** `AddTree` opens a handle on each staged file and the result image holds them until it is released, so leaving it to the collector means DiscWright is still locking its own disc folder when the build returns. In v1 that showed up as a rebuild failing with *something is still using it*, blaming Explorer or a mounted image for a lock DiscWright was holding itself. There is a test that deletes the staged folder immediately afterwards.

**There is no unsafe code in the copy loop, deliberately.** v1's first version took the address of a local with a pointer, which meant compiling with `/unsafe`. Four unmanaged bytes do the same job, and the difference matters: **Smart App Control refuses an assembly that combines unsafe pointers with a delegate invoked inside a copy loop**, because that is the shape of a shellcode loader. Measured on this machine, the pointer version was refused every time and this one accepted every time. The rule for `Copy.cs` is therefore: no pointers, whatever it costs in neatness.

### Proved by mounting it

The window built a disc from a real GOG download, made the image, and then the image was mounted and read back:

```
volume name: HOLLOW_KNIGHT   file system: UDF
every file on the mounted disc matches what the list says
```

That is the whole chain in one run: a folder somebody downloaded, through the recognisers, the planner, the staging steps and the checksum list, into one file, mounted by Windows as a disc, and every byte on it checked against the list the disc carries.

## Burning: everything asked before the laser comes on

**A CD-R is written once.** Every mistake caught by asking the drive a question costs nothing, and every mistake not caught costs a disc. So everything answerable is answered first, and all of it is in Core, where it can be tested with invented drives.

### What a number from the drive means

IMAPI reports what is in the tray as a number. A report saying *"media type 9"* is a report nobody can act on, so there is a table: what to call it, whether it can be written at all, and whether it can be erased.

**Writable and rewritable are different questions.** A CD-R can be written once and never again; a CD-RW can be written, erased and written again. v1 told everybody their disc was not rewritable, including people holding a CD-RW, which is the sort of thing that makes somebody doubt the rest of the dialog.

### Speed, and the mistake that halves it

**IMAPI measures speed in sectors per second, not KB/s**, and a sector per second means a different multiple depending on what is loaded: 75 for a CD, 680 for a DVD, 2195 for a Blu-ray, 4568 for an HD DVD.

Getting that base wrong halves or doubles every speed in the program. v1 had it wrong once: a drive offered 750 for a High Speed CD-RW and the application called it **5x**, which is not a speed any CD-RW is made in. It is **10x**, and the real burns said so.

The drive in this machine holds exactly that disc, and the enumeration reads it as 10x. There is a test with those figures in it.

**The speed chosen is slower than the drive would pick**: 16x for a CD, 8x for everything else, which are v1's figures, arrived at by burning discs rather than by reading a specification. Cheap media written at full speed is the usual way to make a coaster. If the drive offers nothing that slow, the slowest it has, because refusing to write on the grounds that every speed is fast would be a strange way to protect somebody.

### Which drive, and the two things it will not do

**It refuses rather than guesses.** Two drives with blanks in them is a question, not a choice to make on somebody's behalf: the one it picked would be wrong half the time, and the disc in the other drive is often their only blank.

**Nothing erases on its own initiative.** When nothing is ready but a rewritable disc is sitting there, that is said, and the person holding it decides. Erasing takes under a minute and takes everything on the disc with it.

**With more than one disc that could be erased, it asks which.** The rule that it never picks one to destroy is right; the first version then fell through to *"that disc cannot be written to"*, which is false about a rewritable disc and is precisely the wording that makes somebody bin a usable one. Found by asking the program what it would do with two real CD-RWs in two real drives, before writing anything.

For a set, the drive is settled once before the first disc. A set that wanders between two writers is a set whose discs were written by two different lasers at two different speeds.

### Asking costs nothing, so it never throws

A machine with no burner, or a drive with an empty tray, must not take the program down on its way to telling somebody their disc is ready. Every drive comes back either with what is in it or with why it could not say, never both and never neither. The machine this was written on has two drives, one holding a CD-RW and one empty, and the test suite prints what it found so that a run which quietly found nothing cannot be mistaken for a run that checked something.

### The write

Done, and proved on a real CD-RW in a real drive: erased, written at 10x in 47 seconds, ejected, and **every file read back off the disc and compared with what was built**. Written is not the same as readable, which is the entire point.

The disc is closed, so it reads everywhere rather than only on the machine that wrote it. It is ejected at the end so that whatever reads it next sees a freshly mounted volume rather than what Windows remembered about the blank.

`BurningARealDisc` is the test that does it, and **it spends a disc, so it does nothing unless it is told to**: set `DISCWRIGHT_SPEND_A_DISC` to a drive letter. Left unset, which is every ordinary run and every run on the VM, it says what it would have done and stops. It is a test rather than a script because something nobody can run is something nobody runs.

### Four ways to get a silent progress bar, all of them met

The drive took the listener and reported nothing, three separate times, each for a different reason. Every one of them writes the disc perfectly and leaves a bar that never moves, which is why v1's rule about naming the two silences apart matters so much.

**The number was wrong.** A dispatch interface is called by number, and 0x100 is not it. Both of Windows' burning event interfaces deliver under **0x200**, which is now read out of the type library by a test and checked against the constant the code uses.

**The interface was imported rather than declared.** `ComImport` says *a COM object implements this and we call it*, and here it is the other way about. Marked as imported, the wrapper around our sink does not offer the interface under its own identifier, the drive takes the listener anyway, and nothing is ever called.

**The report was counted after it was read.** A report that arrived and could not be read counted as no report at all, so the burn reported silence while the drive had been talking the whole time. The call is counted first now, and every figure in it is asked for on its own.

**The figures were cast rather than converted.** They come back as thirty-two bit numbers, and casting a boxed one to a wider type through `dynamic` throws rather than widening. Every sector figure came back as nothing, so the bar had phases and no percentage. And `RemainingTime` is its name: there is no `EstimatedRemainingTime` on it, and asking for one throws where nobody is looking.

### Erasing

A quick erase, which blanks the table of contents in well under a minute, rather than a full one, which rewrites the whole surface and takes twenty minutes on a CD-RW. The disc is usable either way.

**Nothing erases on its own initiative**, and it is behind an interface of its own rather than on the burner, because it is a different act with a different cost: writing uses up a blank somebody meant to use, and erasing takes away what is already on a disc.

### Shutting the tray

The drive is asked to shut its own tray so that the check afterwards can happen without somebody being in the room. **A slim drive, and most of the ones in a laptop, cannot**: it has to be pushed. That is an answer and not a fault, and it is said out loud so that a run which is waiting for hands says so.

## A drive root is not a folder with the slash taken off

Every folder in this program is listed the same way, and it was wrong for days without anything noticing, because **a drive root is the only path whose trailing separator cannot be taken off**.

Trimmed to `E:`, it stops meaning the root of E: and starts meaning *wherever this process last was on drive E*. Windows then hands back paths with no separator in them, and the arithmetic that worked out where each name began took one character too many. A disc read back held `utorun.inf`, `hecksums.sha256` and `EAD ME.txt`, so every file was reported missing and the disc was called wrong when the disc was perfect.

**It only ever showed at a root, and a burned disc is the only thing read from one.** The relative path is worked out now rather than sliced, because the length of the root is not the same as where the name starts.

The test for it makes a drive with `subst`, lists it, and puts the letter back afterwards. Watched failing against the old arithmetic.

### Proved on two drives at once

A machine with a DVD writer and a Blu-ray writer, each holding a disc with something on it, exercises every question the burn can ask.

**Both discs in the way:** it offered the choice, named both, and erased only the one chosen. The other was still there afterwards, untouched.

**Both discs blank:** it offered the choice of drive, and wrote to the one chosen. The other was still a blank afterwards.

**The two drives behave differently and the program says so.** The DVD writer shuts its own tray, so the disc is written, ejected, taken back in and read through without anybody in the room. The slim Blu-ray writer cannot, so it says so and waits. That is an answer rather than a fault, and the run that needs hands makes a noise rather than hanging quietly.

## The burn button

The window burns now. It asks only where there is something to ask, and everything a person has to answer is handed in from the window: which drive, whether a disc may be erased, and getting the tray shut again. **The screen asks; it never decides.**

### When the button is alive

An image on disk, and nothing else. **Whether a recorder exists and what is in it is asked at the moment of pressing**, because a disc can be put in after the window opened, and asking the burning service on every keystroke would make the whole window crawl. That is v1's rule and it is the right one.

### The two questions, and the one that is not asked

**Which drive**, only when more than one holds a blank. One ready drive is not a question.

**Whether a disc may be erased**, only when nothing is ready and an erase would fix it. Everything on that disc goes, so it is never assumed.

**Which image is not asked when there is only one**, which is nearly always.

### Reading the disc back

`TheDiscAgainstWhatWasBuilt` reads every file off the burned disc and hashes it against the folder it was built from. Hashed rather than compared byte by byte, because that is the same answer the disc's own checksum list gives and somebody can repeat it by hand with `sha256sum`.

A wrong size is checked before hashing, because it is the same answer for none of the reading time, which on a full disc is minutes.

**Something extra on the disc does not make it wrong.** Everything that was meant to be there being there, readable, and the same, does make it right.

**A disc that cannot be read back is not reported as a success.** The disc may well be perfect; what is missing is the proof, and saying "done" without it would be claiming something nobody checked.

### Nine arguments became one machine

`TheMachine` carries everything the screen needs from the machine it happens to be running on: the encoding, the builder, the image maker, the drives, the burner, the eraser, the disk and the hasher. They arrived one at a time as the program grew, and a constructor somebody has to count the commas in is a constructor nobody reads.

Every one of them is an interface, and not one of them is Windows as far as `ViewModels` is concerned. That is what lets the whole burn, erase, check and all, be driven by a test with no disk, no drive and no disc.

## Opening a disc

Every disc DiscWright has ever built carries `discproject.json`. Somebody who finds one in a drawer should be able to open it, change one thing and build it again, and **that is the promise a rewrite has to keep**. So this reads every version back to the first rather than only the one being written now.

The rule throughout: **a field that is not there is not an error, it is an older file**, and what it meant then is what it means now. Every default is the behaviour of the version that lacked the field, not a value that seemed sensible.

| Missing | Read as | Because |
|---|---|---|
| `Games` | one game from `SourceFolder` | Version 1 held it there |
| `Kind`, `Parent` | every entry a plain game | Version 3 added add-ons |
| `Manual`, `Extras` per game | none of its own | Version 4; the menu then falls back to the disc-wide ones, as those discs behaved |
| `MediaKey` | automatic | Version 5; the automatic setting is what those projects described |
| `MatchName` | whatever the folder gives now | Version 6; so a project renamed under older code **repairs itself on opening** |
| `Source` | a GOG download | Version 9; anything older could only hold those |
| `ShowCaption` | **true** | The one that is not false. Every disc made before the option showed a caption |

### The mark at the front

Every project file on every disc begins `ef bb bf`. Windows PowerShell writes a byte order mark and **v1 is Windows PowerShell**, so a reader that will not look past it can open none of them. Found by reading the real files rather than inventing some.

### Reading the file is only half of it

The file holds paths. The games behind them are read again, which is the only way to know they are still there and still the same game, and it is also how a project repairs itself.

**A folder that has gone is not a failure.** It is the commonest thing that happens to a project a year later, and the answer is to say which ones and open the rest rather than refuse a whole disc over one game somebody moved. An add-on whose game did not come back goes with it, because an add-on with no game is a button that does nothing.

**The file counts entries and a disc counts games**, and they stop being the same number the moment an add-on sits anywhere but last. The entry number is mapped to the game number rather than used as one.

Proved on the eleven discs in the testing kit, ten at version 14 and one deliberately at 11: all of them open, and all of their games come back off the disk.

### The button, and what it carries

**Open a disc you made** sits with the title rather than beside *Add a game*, because it is about the whole window and not about one more game. It asks before replacing anything, since what is on the window may be somebody's afternoon.

**The window carries what it cannot show.** There is no artwork or manual on it yet, and a disc opened from a file has both. Dropping them because this cannot edit them would mean rebuilding a disc without the cover somebody picked, so they are carried untouched. A disc opened and built again comes out the same disc.

**The size of disc is only taken when the file chose one.** A project saved on automatic says nothing about it, and the one already on the window is a better answer than a guess.

**A disc whose games have all moved is refused rather than opened empty.** An empty window tells somebody nothing about which folders to go and find; the refusal names them.

### Two things driving it found

**Every row in the games list answered to its whole record.** A list item takes its name from whatever the thing in it prints as, which here was every file and every size. A screen reader would have read all of it aloud, and a window test would have seen it instead of a name. The rows are named now.

**A folder path is long**, and a scrollbar along the bottom of the list to read the end of one is a poor trade for the room it takes. Trimmed, with the whole path on hover.

## Is the game already on the machine?

The one question the menu could not answer. Until now every add-on stayed dead for ever and the main button always said **Install**, even to somebody who had the game installed and only wanted to play it.

GOG writes what it installs into the registry, and that is what the menu reads. The answer changes two things: an add-on stops being dead, and the button says **Play**, because offering to install it again over the top is how a save game dies.

### Read through WMI, not WScript.Shell

The games sit under a key named by a number nobody knows in advance, and only WMI can list the keys under a key. Both views are looked in: a 32 bit game on a 64 bit Windows registers under `WOW6432Node` and a 64 bit one does not.

**The executable is checked on the disk, not believed.** An entry left behind by an uninstall that did not finish is the common case, and it would otherwise light up a Play button that cannot work.

### The names never match as written

GOG's registry name drops and changes punctuation, so the comparison is on letters and digits alone. The real case, on the machine this was written on:

```
  The Witcher - Enhanced Edition  ->  installed:
      C:\Program Files (x86)\GOG Galaxy\Games\The Witcher Enhanced Edition\launcher.exe
```

The disc says *The Witcher - Enhanced Edition*; the registry says *The Witcher Enhanced Edition Director's Cut*. Either may be the longer, so it is a containment test both ways, **with one guard: a registry name shorter than six letters may not swallow ours.** "war" sits inside "starwars" and "ana" inside "analogue", and a Play button that runs the wrong game is worse than one that does nothing.

**The trademark marks come off first and on their own**, because they are the one punctuation that leaves a letter behind. GOG registers Empire at War as `STAR WARS(R): Empire At War(TM)`, and dropping only the brackets leaves an `r` and a `tm` wedged into the middle of the name, matching nothing.

### The disc remembers the name it was registered under

`MatchName` is on the menu now, beside the name somebody reads. They are not the same thing: a folder can be renamed and a project can be given a name somebody typed, while what GOG wrote stays as it was. A disc built before this carries only a name, and the lookup falls back to it.

### Not knowing is not the same as knowing it is absent

If WMI cannot be reached at all, the answer is the same as "not installed", because the menu has only one honest move either way: leave the add-ons alone.

## Burning a whole set

**A set is only a set once every disc is written**, and the two things most likely to go wrong are burning them out of order and losing count. So the burn button walks them: disc 1, then ask, disc 2, then ask. Burning one disc of three and calling it done is not offered.

**The drive is settled once, before the first disc**, and the chooser is asked once. A set that wanders between two writers is a set whose discs were written by two different lasers at two different speeds, and being asked the same question three times is its own annoyance.

**A disc that does not read back stops the walk.** Disc 2 is no use at all if disc 1 came out wrong, and carrying on would spend the rest of somebody's blanks on a set that is already broken.

**Every way out leaves a sentence.** A set half burned is something somebody comes back to tomorrow needing to know where they stopped, so `TheSetSoFar` says which discs are done and which one to carry on from, whether it finished, was stopped, or failed.

Between discs the person is asked for the next blank, and told which disc it is and how many there are, because losing count halfway through a set of four is exactly the mistake this is here to prevent.

### A set's discs were all the same disc

Two bugs, both found by going to write one.

**Every disc of a set wrote to the same image file.** The name came from the label, and every disc of a set carries the same label, so disc 2 landed on top of disc 1 and a set of three was one file claiming to be three. The image is named `LABEL D2.iso` beside `disc D2`, and `TheImageOf.That` is the one place that decides it.

**Every disc of a set answered to the same volume name**, which v1 had already paid for:

> A disc in a set reserves its number BEFORE the name is cut, not after. Truncating "LONG NAME D2" at sixteen takes the number off the end and hands every disc in the set the same volume id.

v1 removed sets, and the comment survived in a function that no longer needed it. The rewrite builds sets again, so the rule is recovered: the `_D2` is reserved out of the sixteen first, and the name is trimmed again **after** cutting as well as before, because a cut can land where a space was folded and leave `THE_WITCHER_ENH_`.

### Coming back to a set

A set half burned is a thing somebody comes back to tomorrow, so DiscWright leaves a note beside the discs saying how far it got. `Written so far.txt`, in the folder the discs were built into, listing every disc and whether it is done.

**It is written as something a person can read**, because the question it answers, where did I get to, is worth answering whether or not this program is what they ask. It is also what the program parses when burn is pressed again.

Three rules hold it together.

**The note goes down after a disc reads back, never before.** One written on the way in would count a coaster as done, and the whole point of it is that it gets believed later.

**It is believed only while it still describes the discs beside it**, by label and by every image's name and size. Build them again and it is ignored rather than trusted. That is the safe way round: ignoring a good note costs one disc that did not need writing, and trusting a stale one puts somebody else's disc 1 into the set.

**It is offered and never acted on.** What it records is that a disc was written and read back clean, which is true and is still not the same as that disc being in the room: it may have been thrown away, or somebody may be making a second copy on purpose. So the window asks, and says why it is asking.

A set that is finished is deliberately not part way through, so pressing burn on one again writes the whole thing. That is how a second copy gets made.

`TheSetSoFar` carries what was brought forward separately from what was written, because saying every file was checked, in one breath, about discs this run never saw would be claiming more than it knows:

> All 3 discs are written and checked. Disc 3 was written just now, and discs 1 to 2 earlier.

## The order to build it in

Each step ends with something that runs and something that is tested. No step is bigger than a sitting.

1. `Core` with `Medium`, `Refusal`, `Outcome<T>`, and the architecture test. Nothing that does anything, but the shape is fixed and guarded.
2. `DiscPlanner`. **Done.** Pure arithmetic, and the single richest source of cases: fits, does not fit, fills exactly, one file bigger than a whole disc, a cut and its pieces, where the extras land. 22 tests, no files at all. Three mutations were made to it on purpose to watch the tests catch them.
3. `GameFolderReader` and its recognisers. **Done** for the three base kinds: a GOG download, game files with something to run, and game files with nothing to run. 18 tests, and no `InMemoryFileSystem` was needed after all, because a recogniser only looks at names and sizes, so a case is a written listing rather than a folder.
4. `Platform`, the real `IFileSystem`, and then the same reader tested against the kit on disk.
5. `Staging` and `Menu`, up to a disc folder on disk with a menu in it. Openable by hand, with no imaging and no burning yet. **The pipeline, the copy step, the menu and autorun.inf are done**, with `BuildContext` settled and progress reported as "step 4 of 7" because the pipeline counts the steps that apply before running any of them. Autorun, artwork, the checksum list and the menu are still to come.
6. **A build that runs.** `DiscBuilder` turns a project into disc folders you could open: it plans, makes each staging folder, and runs the steps in the order they have to happen. **Done**, and proved by building a disc from the manual testing kit on the VM, checking its list with `Get-FileHash` alone, and running its menu under `mshta` until it reported back from inside itself.
7. `Imaging`, so there is an ISO.
8. The window, over the view models that already exist and are already tested.
9. `Burning`, last, behind its interface, with `FakeRecorder` carrying every failure before a real disc is ever touched.

Printing stays parked where the design left it, because there is still no printer here and every number on that board is a claim.
