# DiscWright, in C#

DiscWright puts a GOG game on a disc that still installs it in ten years, with a menu that runs itself, a cover you can print, and a checksum list so somebody can prove the disc is still good without this program.

This repository is the C# rewrite of it. The shipped app, written in PowerShell, lives at [lazardjokovic/discwright](https://github.com/lazardjokovic/discwright) and is the one people are using today.

## Where things stand

Nothing is finished. The solution is laid out, the dependency rule is guarded by a test, and `Core` holds the planner and the types every refusal is made of.

- **[READING-THE-CODE.md](READING-THE-CODE.md)** is a guided tour of what exists today, in the order that makes it make sense. Twenty minutes. Start here.
- **[ARCHITECTURE.md](ARCHITECTURE.md)** is the plan and the reasoning: the one rule, the patterns, the decisions made and the gaps still open. Read it before writing anything.

## The one rule

**Arrows point inward, and the middle points at nothing.** `DiscWright.Core` references nothing of ours; everything else may reference `Core`; nothing may reference the window.

```
                  DiscWright.App              the window, WPF
                        |
                        v
                  DiscWright.Core             the rules. pure.
                   ^  ^  ^  ^  ^  ^
    Platform  Staging  Menu  Imaging  Burning  Printing
```

`Core` targets `net10.0` while the edge projects target `net10.0-windows`, so the rule is enforced by the compiler and not only by the test: `Core` cannot reach a Windows API even by accident.

`TheArchitecture` in the test project reads the project files and fails if any of that stops being true. It reads the **declared** references rather than the compiled assemblies, because the compiler drops a reference nothing uses, and a dependency that exists on paper is already a dependency.

## Building it

```
dotnet build
dotnet test
```

The SDK is pinned in `global.json` to the 10.0.4xx band, because this machine has 10.0.401 and the test VM has 10.0.402 and the same solution should not quietly build differently in two places.

`TreatWarningsAsErrors` is on, and `.editorconfig` turns the naming rules in ARCHITECTURE.md into warnings, so a name that breaks the convention breaks the build. If that ever gets in the way more than it helps, it is one line in `Directory.Build.props`.

## Where it ends up

This repository is private and temporary. When 2.0 is ready the C# app becomes `main` in the public `discwright` repository, where the releases, the issues, the winget package and discwright.com already point, and the PowerShell app keeps a `v1` branch and its tags.

So it is worth saying twice: **a secret committed here cannot be taken back later.** Going public publishes the whole history. Credentials go in an environment variable or a gitignored `*.local.json`.

## Licence

To match v1, which is MIT.
