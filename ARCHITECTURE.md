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
| `DiscWright.App` | all of the above | arithmetic, judgement, any refusal wording |
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

**The add-on and patch recognisers are not written.** The chain has two links: a GOG download and a folder of game files. A patch (`patch_x_to_y.exe`) and an add-on (a soundtrack installer beside its game) are each one more class and one more line in `GameFolderReader.InTheUsualOrder`, which is the arrangement earning its keep. They are left until the thing that holds a game's add-ons exists, because an add-on with no parent to attach to is a half-built idea. Source folders I, J and R in the testing kit are waiting for them.

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
