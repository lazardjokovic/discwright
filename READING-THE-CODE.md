# Reading the code

A guided tour of everything in `main` today, in the order that makes it make sense. About twenty minutes end to end. `ARCHITECTURE.md` is the plan and the reasoning; this is the walk around what actually exists.

Open `DiscWright.slnx` in Visual Studio and keep this file open beside it. Double-click it in Solution Explorer and it opens in a tab like anything else.

## Before you start: the one rule

Everything is arranged around one sentence. **Arrows point inward, and the middle points at nothing.**

`DiscWright.Core` is pure. It knows no COM, no disc, no file, no window. Everything else may use `Core`; `Core` may use nothing of ours. That is why 54 tests run in under a second without touching a disk.

It is enforced twice: `Core` targets `net10.0` while the edges target `net10.0-windows`, so a Windows API is literally out of its reach, and `TheArchitecture` reads the project files and fails if any dependency appears that the plan does not allow.

## The order to read in

| | File | Lines | Why here |
|---|---|---|---|
| 1 | `tests/ThePlanner.cs` | 270 | The specification, in sentences |
| 2 | `Core/Outcome.cs` | 68 | Why a refusal is not an exception |
| 3 | `Core/Refusal.cs` | 91 | Why a "no" is a type and not a string |
| 4 | `Core/Media.cs` + `Medium.cs` | 46 + 21 | Data staying data |
| 5 | `Core/ByteSize.cs` | 79 | One decision, made once |
| 6 | `Core/DiscPlanner.cs` | 176 | The only file with real logic in it |
| 7 | `tests/TheArchitecture.cs` | 186 | The rule, guarding itself |

Everything not in that list is a record of three or four fields with a comment saying why those fields and not others. Read them when you meet them.

---

## 1. `ThePlanner.cs` — start with the tests

Read the test **names** first, ignoring the bodies:

```
Fills_each_disc_before_starting_the_next
Starts_the_pieces_of_a_cut_file_on_a_disc_of_their_own
Lets_the_files_after_a_cut_share_the_last_piece_disc
Puts_the_extras_on_the_last_disc_where_the_slack_is
Refuses_a_file_bigger_than_a_disc_and_names_it
```

That list is what the program promises. If a promise is missing from it, the program does not make that promise.

The tests use a made-up ten-byte disc, so a test can say `Files(6, 5)` and you can see at a glance that it needs two discs. Real capacities are tested separately in `TheMediaTable`; nothing here depends on their awkward sizes.

**The thing to notice:** not one test touches a file, a folder or a disc. That is the whole payoff of keeping `Core` pure.

---

## 2. `Outcome.cs` — one thing or the other

A method that can refuse returns `Outcome<T>`, which is either `Ok` carrying the answer or `No` carrying a `Refusal`.

```csharp
switch (DiscPlanner.Plan(request))
{
    case Outcome<DiscLayout>.Ok(var layout): Build(layout); break;
    case Outcome<DiscLayout>.No(var refusal): Show(refusal); break;
}
```

You cannot reach the layout without dealing with the refusal, because it is not there until you have looked.

**Why not an exception:** a file too big for a disc is an *answer*, not a failure. v1 threw one and it arrived in front of the words "The build failed:", in front of a sentence calmly explaining that one file was too big.

**The thing to notice:** the constructor is `private`, so only the two cases nested inside it can exist. Nothing anywhere else can add a third.

---

## 3. `Refusal.cs` — a no with its homework done

```csharp
public sealed record Refusal(string Headline, string Because, IReadOnlyList<Figure> Figures, Remedy? Remedy);
```

A type rather than a string, because **a type has fields somebody has to fill in**. Every refusal names the thing, gives both numbers, and offers a way out. Left as a string, the second and third quietly stop happening the first time anyone is in a hurry.

`RemedyKind` lists what the **window can do**, not why the refusal happened: `ChangeMedium`, `EnableSet`, `ChangeOption`, `FocusField`, `PickFolder`, `Nothing`. Framed as reasons it grows a case per refusal for ever; framed as capabilities it is closed, because a window can only be asked to do so many things.

**A known wrinkle, and a fair thing to pick on:** `EnableSet` and `ChangeOption` overlap, because making it a set *is* turning an option on. The two will collapse into one the day a refusal needs to name *which* option it means, which needs a field that does not exist yet. No refusal uses either today, so it is written down rather than guessed at.

See `Refusals.cs` for all four refusals the app can currently make, in one file so the whole voice of the program can be read in one sitting.

---

## 4. `Media.cs` — adding a disc size is one row

```csharp
public static readonly Medium Dvd5 = new("DVD5", "DVD5 4.7 GB", Gib(4.37));
```

Six rows, no branches anywhere. Adding BD-XL or anything else is a line.

**The thing to notice:** `4.37`, not `4.7`. A DVD5 is *sold* as 4.7 GB and *holds* 4.37 GiB. Planning against the number on the box produces a disc that fails at 99 per cent. The sold figure survives only as part of the name.

`Medium` carries **no** `Rewritable` flag and **no** file-size ceiling, and both omissions are deliberate. The tiers are target sizes; whether the disc in the tray is blank or rewritable is read off the drive at burn time. A file-size ceiling belongs to the filesystem, which is why `FileSystems.cs` exists: 6 GB is impossible on ISO9660 and fine on UDF, on the same disc.

---

## 5. `ByteSize.cs` — one decision, made once

Every size a person reads comes from `ByteSize.Describe`. Binary units labelled the way Explorer labels them, because every size somebody compares against came out of Explorer. Being technically right would put our `4.37 GiB` next to Explorer's `4.37 GB` for the same file and leave them wondering which is lying.

Gigabytes get two decimals for a real reason: 4.37 and 4.7 are two different discs, and one decimal prints them as 4.4 and 4.7.

**The thing to notice:** it formats in the machine's own culture, so a Serbian machine reads `4,37 GB`, and there is a test pinning exactly that. That is also why `InvariantGlobalization` is deliberately off for the whole solution. Contrast it with `DiscPlanner`, which formats piece numbers in the **invariant** culture: those are file names, and a file name that changes with the machine's language is a file the join cannot find.

---

## 6. `DiscPlanner.cs` — the only real logic

**Put a breakpoint here.** On this line:

```csharp
long take = Math.Min(room, file.Bytes - offset);
```

Then right-click `Cuts_a_file_too_big_for_one_disc_when_it_is_allowed_to` → **Debug**. Watch `offset` walk 0, 10, 20 as a 25-byte file becomes three pieces on a ten-byte disc. That loop is the entire cutting algorithm.

The shape of the file, top to bottom:

1. **Room** is the disc's capacity minus what the menu and artwork take on every disc. If that leaves nothing, refuse.
2. **Walk the files in order.** First fit: fill a disc, start another. Nothing is reordered to save a disc, because the order is the person's.
3. **A file bigger than a whole disc** is refused by name, or cut if cutting was allowed.
4. **The extras** go on the last disc, where the slack is.

Three decisions in there are not obvious and each has a comment saying why:

- **Pieces start on a fresh disc**, so they run in order and somebody holding disc 3 can see which piece is on it without doing arithmetic.
- **The last piece keeps its slack**, so files after it share its disc instead of starting another.
- **The extras go last, not first.** First fit fills every disc before the last one to the brim, so putting the extras at the front would push a file onto a disc that did not need to exist.

**It is told nothing about games.** It takes a flat list of files. Deciding which games belong on which disc is a layer above it that does not exist yet, and `ARCHITECTURE.md` explains why that layer gives several games a disc each rather than packing them.

---

## 7. `TheArchitecture.cs` — the rule guarding itself

Five tests that read the `.csproj` files and fail if the dependency rule is broken.

**The thing to notice:** it reads the *project files*, not the compiled assemblies. `Assembly.GetReferencedAssemblies` lists only what the compiler actually needed, so a project could reference another, use nothing from it yet, and pass. A dependency on paper is already a dependency.

---

## Five things to do rather than read

| | Do this | What you learn |
|---|---|---|
| 1 | **Test → Test Explorer**, Run All | 54 green in under a second |
| 2 | Debug `Cuts_a_file_too_big_for_one_disc_when_it_is_allowed_to` | the cutting loop, one step at a time |
| 3 | Change `PartSuffix` from `".dwpart"` to `".part"`, Run All | **4 tests go red.** Undo. |
| 4 | Rename a private field from `_was` to `was` | VS underlines it, and the build refuses it |
| 5 | Set `DiscWright.App` as startup, press `F5` | an empty window, because the UI is step 7 |

Number 3 is the important one. A test suite nobody has watched fail is not known to test anything.

## How to review it rather than tour it

Questions worth asking, because the answers should be "no" and if one is "yes" it is worth saying so:

- **Does a comment restate the code?** A comment here should say *why*, and where there was a surprise it should record what was actually measured. `// loop over the files` is worth less than nothing.
- **Does a test assert the same formula on both sides?** One did. It compared `Gib(0.68)` against the capacity computed by `Gib(0.68)`, so a wrong formula passed happily. It is now literal byte counts.
- **Is a name doing two jobs?** `Manager`, `Helper`, `Utils`, `Data`, `Info` are all absent on purpose. If one appears, the class is doing more than one thing.
- **Can a refusal forget its numbers?** It should not be able to, because `Refusal` has fields for them.
- **Is anything in `Core` that needs a machine to run?** It should be impossible, but say so if you spot it.

Push back on anything that reads oddly. Several of the better decisions in here came from something looking wrong on a second reading.

## What is deliberately missing

So you are not looking for it:

- **The window.** `DiscWright.App` is the empty WPF template. It is step 7.
- **Everything that touches a disc.** `Staging`, `Menu`, `Imaging`, `Burning`, `Printing`, `Platform` are empty projects holding their place in the dependency graph.
- **Opening an existing project or disc**, which v1 does across twelve schema versions.
- **Recognising what a folder is**, which is next.

## A PowerShell to C# phrasebook

The constructs you will actually meet in these files, against what you already write every day.

| PowerShell | C# here | Note |
|---|---|---|
| `@{ Ok = $true; Why = '' }` | `record` | Immutable, compared by value, prints itself readably |
| `$script:MediaTypes` | `public static readonly` | Shared, set once |
| `function Get-Room($s) { }` | `static long Room(PlanRequest r) { }` | Types stated, not inferred at runtime |
| `[string]$path` | `string path` | Same idea, checked at compile time |
| `$x -eq $null` | `x is null` | And `string?` means "may be null", `string` means "never" |
| `@($files)` | `IReadOnlyList<PlanFile>` | A list the caller cannot change behind your back |
| `@(1, 2, 3)` | `[1, 2, 3]` | Collection expression. `[.. list]` copies one |
| `$files \| Where-Object { $_.Ok }` | `files.Where(f => f.Ok)` | `=>` is `{ $_ ... }` with a name for `$_` |
| `$files \| ForEach-Object { $_.Bytes }` | `files.Select(f => f.Bytes)` | |
| `if ($s -match 'x')` | `if (thing is Outcome<T>.No no)` | Pattern matching, and it binds a variable |
| `return @{ Ok = $false; Why = '...' }` | `return Refusals.ThatOneFileIsBiggerThanADisc(...)` | A type instead of a loose hashtable |
| `throw 'too big'` | `return` a `Refusal` | Exceptions are for programming errors only |
| `$sb.ToString()` | `$"{a} and {b}"` | String interpolation, same `$` |
| `#` comment | `//` comment, `///` doc comment | `///` shows up in IntelliSense when you hover |

One habit that transfers directly: v1 is full of comments explaining *why* a thing is the way it is, with the measurement that caused it. That style is the house style here too, and it is the most valuable thing in either codebase.

---

**Keeping this honest:** when a file in the tour changes shape, this file changes with it. A tour that describes code that no longer exists is worse than no tour.
