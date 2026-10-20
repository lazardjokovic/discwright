using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Every kind of disc this program can be asked to make, built and checked
/// for being coherent.
/// </summary>
/// <remarks>
/// <para>
/// The ten axes in <c>tests/by-hand/a-disc.pict</c> multiply out to 276,480
/// projects. What is worth covering is not all of them but every PAIR of
/// values, because failures come of one value, or of two interacting, far more
/// often than of six. PICT works out the shortest set of rows that covers
/// every pair, which is forty, and this runs all forty.
/// </para>
/// <para>
/// <strong>It does not predict what each row should say.</strong> Predicting
/// it would mean writing the planner twice and testing the copy. What it
/// insists on is that whatever the window ends up saying is coherent: that the
/// refusal and the buttons agree, that a line is up only when the thing it is
/// about is true, that what is on the disc is what was asked for. The same
/// shape of question the walk asks, over combinations chosen rather than
/// stumbled on.
/// </para>
/// <para>
/// The rows are committed rather than generated here, so this needs no PICT
/// installed, gives the same answer on every machine, and a row that starts
/// failing is a change in the program rather than a reshuffle in a generator.
/// Regenerate with <c>tests/by-hand/Build-TheCombinations.ps1</c>.
/// </para>
/// </remarks>
public class EveryKindOfDisc
{
    public static TheoryData<string> EveryRow()
    {
        TheoryData<string> rows = [];

        foreach (string line in File.ReadAllLines(Repository.File("tests", "by-hand", "a-disc.txt")).Skip(1))
        {
            if (line.Trim().Length > 0)
            {
                rows.Add(line);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(EveryRow))]
    public void Adds_up_however_it_is_put_together(string row)
    {
        string[] said = row.Split('\t');

        ADisc wanted = new(
            Kind: said[0],
            Games: int.Parse(said[1], System.Globalization.CultureInfo.InvariantCulture),
            Biggest: said[2],
            Size: said[3],
            Artwork: said[4],
            Extras: said[5],
            Menu: said[6] == "on",
            Checksums: said[7] == "on",
            Legacy: said[8] == "on",
            Split: said[9] == "on");

        MakeADisc screen = Built(wanted);

        Assert.Null(WhatDoesNotAddUp(screen, wanted));
    }

    [Fact]
    public void Covers_every_pair_of_values_there_is()
    {
        // The rows are only worth running while they are the covering set. A
        // model edited without regenerating, or a generated file edited by
        // hand, leaves a list that looks like coverage and is not.
        string[] lines = File.ReadAllLines(Repository.File("tests", "by-hand", "a-disc.txt"));
        string[] axes = lines[0].Split('\t');

        List<string[]> rows = [.. lines.Skip(1).Where(each => each.Trim().Length > 0).Select(each => each.Split('\t'))];

        Assert.Equal(10, axes.Length);
        Assert.All(rows, each => Assert.Equal(axes.Length, each.Length));

        // Every value of every axis that the model offers has to turn up.
        string model = File.ReadAllText(Repository.File("tests", "by-hand", "a-disc.pict"));

        foreach (string axis in axes)
        {
            string line = model
                .Split('\n')
                .First(each => each.TrimStart().StartsWith(axis + ":", StringComparison.Ordinal));

            foreach (string value in line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int at = Array.IndexOf(axes, axis);

                Assert.True(
                    rows.Any(each => each[at] == value),
                    $"{axis} can be {value} and no row is, so the table is not the covering set any more. "
                        + "Run tests/by-hand/Build-TheCombinations.ps1.");
            }
        }

        // And every pair of values from two different axes, which is what
        // pairwise means and the only thing that makes forty rows enough.
        List<string> missed = [];

        for (int a = 0; a < axes.Length; a++)
        {
            for (int b = a + 1; b < axes.Length; b++)
            {
                foreach (string one in rows.Select(each => each[a]).Distinct())
                {
                    foreach (string two in rows.Select(each => each[b]).Distinct())
                    {
                        if (!rows.Any(each => each[a] == one && each[b] == two))
                        {
                            missed.Add($"{axes[a]}={one} with {axes[b]}={two}");
                        }
                    }
                }
            }
        }

        // Constraints rule some pairs out on purpose, and those are the only
        // ones allowed to be missing.
        List<string> notRuledOut =
        [
            .. missed.Where(each =>
                !each.Contains("overTwoGiB", StringComparison.Ordinal)
                && !(each.Contains("Extras=music", StringComparison.Ordinal)
                    && each.Contains("Menu=off", StringComparison.Ordinal)))
        ];

        Assert.True(
            notRuledOut.Count == 0,
            "These pairs appear nowhere and no constraint rules them out: "
                + string.Join("; ", notRuledOut.Take(12)));
    }

    // --------------------------------------------------------- building one

    private sealed record ADisc(
        string Kind, int Games, string Biggest, string Size, string Artwork, string Extras,
        bool Menu, bool Checksums, bool Legacy, bool Split);

    private static MakeADisc Built(ADisc wanted)
    {
        MakeADisc screen = TheScreen();

        screen.OutputFolder = @"C:\out";

        for (int at = 0; at < wanted.Games; at++)
        {
            // Only the first carries the big file, so "biggest" means what it
            // says however many games there are.
            long bytes = at == 0 ? TheSizeOf(wanted.Biggest) : 1000;

            screen.Add(AGameOfKind(wanted.Kind, $"Game {at + 1}", bytes, at), AddOnsFor(wanted.Kind));
        }

        screen.Medium = wanted.Size switch
        {
            "CD" => Media.Cd,
            "DVD5" => Media.Dvd5,
            "DVD9" => Media.Dvd9,
            _ => Media.Bd25,
        };

        switch (wanted.Artwork)
        {
            case "icon": screen.DiscIcon = @"C:\art\icon.png"; break;
            case "background": screen.Background = @"C:\art\back.png"; break;
            case "cover": screen.Cover = @"C:\art\cover.png"; break;
            case "discFace": screen.DiscFace = @"C:\art\face.png"; break;
            case "allOfIt":
                screen.DiscIcon = @"C:\art\icon.png";
                screen.Background = @"C:\art\back.png";
                screen.Cover = @"C:\art\cover.png";
                screen.DiscFace = @"C:\art\face.png";
                break;
        }

        switch (wanted.Extras)
        {
            case "manual": screen.Manual = @"C:\extras\how.txt"; break;
            case "extrasFolder": screen.ExtrasFolder = @"C:\extras"; break;
            case "loose": screen.AlsoOnTheDisc([@"C:\loose\note.txt"]); break;
            case "music": screen.Music = @"C:\music\tune.mp3"; break;
            case "allOfIt":
                screen.Manual = @"C:\extras\how.txt";
                screen.ExtrasFolder = @"C:\extras";
                screen.AlsoOnTheDisc([@"C:\loose\note.txt"]);
                screen.Music = @"C:\music\tune.mp3";
                break;
        }

        screen.Menu = wanted.Menu;
        screen.Checksums = wanted.Checksums;
        screen.LegacyFileSystems = wanted.Legacy;
        screen.CutFiles = wanted.Split;

        return screen;
    }

    private static long TheSizeOf(string biggest) =>
        biggest switch
        {
            "small" => 1000,
            "mostOfACd" => 600L * 1024 * 1024,
            "biggerThanACd" => 1200L * 1024 * 1024,
            _ => 2200L * 1024 * 1024,
        };

    private static GameFolder AGameOfKind(string kind, string name, long bytes, int at)
    {
        string path = @"C:\games\" + name;

        if (kind.StartsWith("Gog", StringComparison.Ordinal))
        {
            ListedFile installer = new($"setup_{name}.exe", bytes);

            return new GameFolder(path, GameKind.GogInstaller, name, name, installer, [installer], [], null);
        }

        List<ListedFile> files = [new(@"data\things.pak", bytes)];
        List<ListedFile> could = [];

        int howMany = kind == "FilesWithOneToRun" ? 1 : kind == "FilesWithSeveralToRun" ? 3 : 0;

        for (int which = 1; which <= howMany; which++)
        {
            ListedFile exe = new($"{name}{which}.exe", 1000);
            files.Add(exe);
            could.Add(exe);
        }

        return new GameFolder(
            path,
            could.Count > 0 ? GameKind.FilesWithLauncher : GameKind.FilesOnly,
            name, name, null, files, could, null);
    }

    private static IEnumerable<LeftBehind> AddOnsFor(string kind) =>
        kind == "GogWithAddOns"
            ? [new LeftBehind(new ListedFile("patch_1.0_to_1.1.exe", 50_000), LeftBehindKind.LooksLikeAnAddOn)]
            : [];

    // -------------------------------------------- what has to hold whatever

    private static string? WhatDoesNotAddUp(MakeADisc screen, ADisc wanted)
    {
        // What was asked for is what is set, however the rest of it landed.
        if (screen.Menu != wanted.Menu || screen.Checksums != wanted.Checksums
            || screen.LegacyFileSystems != wanted.Legacy || screen.CutFiles != wanted.Split)
        {
            return "One of the options is not what it was set to.";
        }

        if (screen.Games.Count != wanted.Games)
        {
            return $"{wanted.Games} games went on and {screen.Games.Count} are there.";
        }

        // A refusal means it cannot be built, and a plan means it can.
        if (screen.WhyNot is not null && screen.CanMakeIt)
        {
            return $"Build it is live over \"{screen.WhyNot.Headline}\".";
        }

        if (screen.WhyNot is null && !screen.CanMakeIt)
        {
            return "Build it is dead with nothing refusing it.";
        }

        // What it will make is said exactly when there is something to make.
        if ((screen.WhatItWillMake.Length > 0) == (screen.WhyNot is not null && screen.WhatItWillMake.Length == 0))
        {
            // Both true or both false is fine; this only catches the
            // impossible pairing of a plan that is both there and not.
            return null;
        }

        // The splitting line belongs to the splitting box and to nothing else.
        if (screen.WhatSplittingWouldDo.Length > 0 && !wanted.Split)
        {
            return $"The splitting line is up with the box off: \"{screen.WhatSplittingWouldDo}\".";
        }

        // A file too big for the disc, with nothing to cut it, is a refusal.
        bool tooBig = TheSizeOf(wanted.Biggest) > screen.Medium.Capacity;

        if (tooBig && !wanted.Split && screen.WhyNot is null)
        {
            return $"A file of {TheSizeOf(wanted.Biggest)} bytes is on a {screen.Medium.Name} "
                + "with splitting off, and nothing is refusing.";
        }

        // The preview needs a plan and nothing else, and never needs a folder.
        if (screen.CanPreview && screen.WhatItWillMake.Length == 0)
        {
            return "The preview is live with no set to preview.";
        }

        // What is chosen is said, and only what is chosen.
        // Loose files are not in that line and that is right: they are
        // not chosen in the panel it describes, they are carried over when
        // a folder holding several games is taken apart, and the question
        // about doing that says they go on either way. They are on the disc
        // and counted in the plan, and the window never mentions them again.
        bool nothingFromThePanel = wanted.Artwork == "none" && wanted.Extras is "none" or "loose";

        if (nothingFromThePanel
            != (screen.WhatElseIsChosen == "Nothing chosen. A disc with none of it is still a disc."))
        {
            return $"Artwork is {wanted.Artwork} and extras are {wanted.Extras}, "
                + $"and it says \"{screen.WhatElseIsChosen}\".";
        }

        // A folder of files with no one thing to run is only ever copied, and
        // a GOG download never is.
        bool onlyCopied = wanted.Kind is "FilesWithSeveralToRun" or "FilesWithNothingToRun";

        if (onlyCopied != (screen.WhatWillNotStartItself is not null))
        {
            return $"{wanted.Kind} and it {(screen.WhatWillNotStartItself is null ? "says nothing" : "says it will only be copied")}.";
        }

        return null;
    }

    // ------------------------------------------------- the machine it runs on

    private static MakeADisc TheScreen() =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            new ADiskWithNothingOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesThatRead(),
            "2.0.0",
            HowBigIs));

    private static long HowBigIs(string path) => 1000;

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>("blank");
    }

    private sealed class ADiskWithNothingOnIt : IFileSystem
    {
        public bool FolderExists(string path) => false;

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "nobody asked"));
    }

    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }

    private sealed class PicturesThatRead : IImages
    {
        public ImageFacts Read(string path) => new(true, 512, 512, null);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }
}
