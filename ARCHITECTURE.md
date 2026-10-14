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
| `DiscWright.Staging` | Core | COM, UI |
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
public sealed record Medium(string Key, string Shown, long Capacity, bool Rewritable, long LargestFile);

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

public abstract record Outcome<T>
{
    public sealed record Ok(T Value) : Outcome<T>;
    public sealed record No(Refusal Refusal) : Outcome<T>;
}
```

So `DiscPlanner.Plan(...)` returns `Outcome<DiscLayout>`, and the window cannot forget to handle the refusal because it cannot get at the layout without looking. No exceptions for things that are not errors. A file too big for a disc is an answer, not a failure, and the old app learned that the hard way when a refusal arrived in front of the words "The build failed:".

## The ports, and burning worked through

```csharp
public interface IDiscRecorder
{
    IReadOnlyList<RecorderDevice> Devices();
    MediaInDrive Look(RecorderDevice device);
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
    string Describes { get; }                      // "a GOG download"
    GameFolder? Recognise(FolderListing folder);   // null means "not mine"
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

## The two machines, as they stand

Both prepared 2026-10-14.

| | This machine | The test VM |
|---|---|---|
| .NET SDK | 10.0.401 | 10.0.402 |
| WindowsDesktop runtime | 8.0.31 and 10.0.12 | 10.0.13 |
| Visual Studio | 18.10.3 | none, and needs none |
| git | 2.53.0 | 2.55.0 |

Two things came out of preparing them, both worth knowing before the first commit.

**Neither machine could reach nuget.org.** This one had only Visual Studio's offline package folder, the VM had no source at all, so a plain build worked while anything needing a package died with NU1101. That is what made the first self contained publish fail. Both now have nuget.org registered.

**The two SDKs differ in the patch**, 10.0.401 here against 10.0.402 there. So the repository gets a `global.json` pinning the SDK with `rollForward: latestFeature`, or the same solution quietly builds differently in two places and the difference surfaces as something else entirely.

## The order to build it in

Each step ends with something that runs and something that is tested. No step is bigger than a sitting.

1. `Core` with `Medium`, `Refusal`, `Outcome<T>`, and the architecture test. Nothing that does anything, but the shape is fixed and guarded.
2. `DiscPlanner`. Pure arithmetic, and the single richest source of cases: fits, does not fit, one game too big, four games packed, a file over the old ceiling. All of it testable with no files at all.
3. `GameFolderReader` against `IFileSystem`, with `InMemoryFileSystem`. The twenty source folders in the testing kit become its cases.
4. `Platform`, the real `IFileSystem`, and then the same reader tested against the kit on disk.
5. `Staging` and `Menu`, up to a disc folder on disk with a menu in it. Openable by hand, with no imaging and no burning yet.
6. `Imaging`, so there is an ISO.
7. The window, over the view models that already exist and are already tested.
8. `Burning`, last, behind its interface, with `FakeRecorder` carrying every failure before a real disc is ever touched.

Printing stays parked where the design left it, because there is still no printer here and every number on that board is a claim.
