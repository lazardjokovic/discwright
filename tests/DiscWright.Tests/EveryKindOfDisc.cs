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
/// The ten axes in <c>tests/by-hand/a-disc.pict</c> multiply out to 138,240
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
        // ones allowed to be missing. WHICH ONES COMES OFF THE MODEL'S OWN IF
        // LINES rather than a list kept here, because a list kept here was
        // already wrong: it excused every missing pair holding overTwoGiB,
        // all twenty eight of them, where the model rules out two.
        List<ARuledOutPair> ruledOut = WhatTheModelRulesOut();

        Assert.NotEmpty(ruledOut);

        List<string> notRuledOut =
        [
            .. missed.Where(each => !ruledOut.Any(pair =>
                each == $"{pair.Axis}={pair.Value} with {pair.OtherAxis}={pair.OtherValue}"
                || each == $"{pair.OtherAxis}={pair.OtherValue} with {pair.Axis}={pair.Value}"))
        ];

        Assert.True(
            notRuledOut.Count == 0,
            "These pairs appear nowhere and no constraint rules them out: "
                + string.Join("; ", notRuledOut.Take(12)));
    }

    [Fact]
    public void No_row_asks_for_a_disc_the_model_rules_out()
    {
        // The other way round. A row holding a pair the model forbids is a
        // table that was not generated from this model, and every such row
        // tests a disc the program is right to refuse to make.
        string[] lines = File.ReadAllLines(Repository.File("tests", "by-hand", "a-disc.txt"));
        string[] axes = lines[0].Split('	');
        List<ARuledOutPair> ruledOut = WhatTheModelRulesOut();

        Assert.NotEmpty(ruledOut);

        List<string> asked = [];

        foreach (string[] row in lines.Skip(1)
            .Where(each => each.Trim().Length > 0)
            .Select(each => each.Split('	')))
        {
            foreach (ARuledOutPair pair in ruledOut)
            {
                int one = Array.IndexOf(axes, pair.Axis);
                int two = Array.IndexOf(axes, pair.OtherAxis);

                if (one >= 0 && two >= 0 && row[one] == pair.Value && row[two] == pair.OtherValue)
                {
                    asked.Add($"{pair.Axis}={pair.Value} with {pair.OtherAxis}={pair.OtherValue}");
                }
            }
        }

        Assert.True(
            asked.Count == 0,
            "The model rules these out and the rows ask for them anyway, so the table and the model "
                + "have come apart: " + string.Join("; ", asked.Distinct()));
    }

    /// <summary>One pair of values the model says cannot happen together.</summary>
    private sealed record ARuledOutPair(string Axis, string Value, string OtherAxis, string OtherValue);

    /// <summary>
    /// Every pair the model forbids, worked out from its own IF lines.
    /// </summary>
    /// <remarks>
    /// <c>THEN [B] = "x"</c> forbids B being anything but x, so it rules out
    /// one pair per other value B has. <c>THEN [B] &lt;&gt; "x"</c> rules out
    /// exactly one. Reading them is the only way the exemptions stay the size
    /// of the constraints: a constraint dropped from the model turns at once
    /// into a pair this suite insists on covering.
    /// </remarks>
    private static List<ARuledOutPair> WhatTheModelRulesOut()
    {
        string[] lines = File.ReadAllLines(Repository.File("tests", "by-hand", "a-disc.pict"));
        Dictionary<string, string[]> values = TheAxesOfTheModel();

        List<ARuledOutPair> ruledOut = [];

        foreach (string line in lines
            .Select(each => each.Trim())
            .Where(each => each.StartsWith("IF [", StringComparison.Ordinal)))
        {
            string then = line[(line.IndexOf("THEN", StringComparison.Ordinal) + 4)..];
            string axis = Between(line, '[', ']');
            string value = Between(line, '"', '"');
            string otherAxis = Between(then, '[', ']');
            string otherValue = Between(then, '"', '"');
            bool mustBe = then[(then.IndexOf(']', StringComparison.Ordinal) + 1)..].TrimStart().StartsWith('=');

            foreach (string other in mustBe
                ? values[otherAxis].Where(each => each != otherValue)
                : [otherValue])
            {
                ruledOut.Add(new ARuledOutPair(axis, value, otherAxis, other));
            }
        }

        return ruledOut;
    }

    /// <summary>Each axis of the model and the values it offers.</summary>
    private static Dictionary<string, string[]> TheAxesOfTheModel()
    {
        Dictionary<string, string[]> values = [];

        foreach (string line in File.ReadAllLines(Repository.File("tests", "by-hand", "a-disc.pict")))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0 || line.StartsWith('#') || line[..colon].Any(char.IsWhiteSpace))
            {
                continue;
            }

            values[line[..colon]] =
            [
                .. line[(colon + 1)..]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            ];
        }

        return values;
    }

    [Fact]
    public void Says_how_big_the_model_is_and_says_it_right_everywhere()
    {
        // How many projects the ten axes stand for is written down in four
        // places, and every one of them had it at twice the truth from the day
        // it was written, because nothing read it. A number nothing reads is a
        // number nothing keeps honest, so this reads all four.
        Dictionary<string, string[]> axes = TheAxesOfTheModel();

        Assert.Equal(10, axes.Count);

        long all = 1;

        foreach (string[] values in axes.Values)
        {
            all *= values.Length;
        }

        string written = all.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        // The model spells the multiplication out as well, in order.
        Assert.Contains(
            string.Join(" x ", axes.Values.Select(each => each.Length)),
            File.ReadAllText(Repository.File("tests", "by-hand", "a-disc.pict")),
            StringComparison.Ordinal);

        string[][] saidIn =
        [
            ["tests", "by-hand", "a-disc.pict"],
            ["tests", "by-hand", "Build-TheCombinations.ps1"],
            ["tests", "by-hand", "README.md"],
            ["tests", "DiscWright.Tests", "EveryKindOfDisc.cs"],
        ];

        foreach (string[] where in saidIn)
        {
            string[] words = File.ReadAllText(Repository.File(where))
                .Replace("///", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            List<string> numbers =
            [
                .. Enumerable.Range(1, words.Length - 1)
                    .Where(at => words[at].StartsWith("projects", StringComparison.Ordinal)
                        && words[at - 1].Length > 0
                        && char.IsDigit(words[at - 1][0]))
                    .Select(at => words[at - 1])
            ];

            Assert.True(
                numbers.Count > 0,
                string.Join('/', where) + " used to say how big the model is and no longer does.");

            Assert.All(numbers, each => Assert.Equal(written, each));
        }
    }

    private static string Between(string line, char open, char close)
    {
        int from = line.IndexOf(open) + 1;
        return line[from..line.IndexOf(close, from)];
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

        // Everything on the disc is said, and only what is on it.
        // The loose files are in that line now. This invariant used to allow
        // them out of it, on the reasoning that they are not chosen in the
        // panel it describes: they are carried over when a folder holding
        // several games is taken apart. That reasoning held for the panel and
        // not for the disc. They went on it, the total above counts the tree
        // alone, and so they were the one thing aboard that the window never
        // mentioned anywhere.
        bool nothingOnIt = wanted.Artwork == "none" && wanted.Extras == "none";

        if (nothingOnIt
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
