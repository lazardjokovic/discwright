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
