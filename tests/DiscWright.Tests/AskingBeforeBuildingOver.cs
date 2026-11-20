using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Asking before a build deletes an earlier one, discwright#98, 15 November
/// 2026, settled by the owner on the 20th: always ask.
/// </summary>
/// <remarks>
/// A build empties its <c>disc</c> folder and writes over its image, and did
/// both without a word. What is looked for, how it is worded, which answer
/// the button offers, and that the window asks before it builds.
/// </remarks>
public class AskingBeforeBuildingOver
{
    private const string Out = @"C:\out\Hollow Knight Disc";

    // ------------------------------------------------------- what is looked for

    [Fact]
    public void An_empty_folder_holds_nothing_to_replace()
    {
        AnEarlierBuild there = AnEarlierBuild.In(new ADisk(Out), Out, "HOLLOW KNIGHT");

        Assert.False(there.IsThere);
        Assert.Empty(there.Folders);
        Assert.Empty(there.Images);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\not there")]
    public void Nor_does_a_folder_that_is_not_there_or_not_named(string folder)
    {
        ADisk disk = new(Out) { Folders = { "disc" }, Files = { "HOLLOW KNIGHT.iso" } };

        Assert.False(AnEarlierBuild.In(disk, folder, "HOLLOW KNIGHT").IsThere);
    }

    [Fact]
    public void One_disc_built_before_is_its_folder_and_its_image()
    {
        ADisk disk = new(Out) { Folders = { "disc" }, Files = { "HOLLOW KNIGHT.iso", "discproject.json" } };

        AnEarlierBuild there = AnEarlierBuild.In(disk, Out, "HOLLOW KNIGHT");

        Assert.True(there.IsThere);
        Assert.Equal(["disc"], there.Folders);
        Assert.Equal(["HOLLOW KNIGHT.iso"], there.Images);
        Assert.Equal("a disc folder and its image", there.InWords);
    }

    [Fact]
    public void A_set_built_before_is_every_disc_of_it()
    {
        ADisk disk = new(Out)
        {
            Folders = { "disc D1", "disc D2", "disc D3" },
            Files = { "WITCHER D2.iso", "WITCHER D1.iso", "WITCHER D3.iso" },
        };

        AnEarlierBuild there = AnEarlierBuild.In(disk, Out, "WITCHER");

        Assert.Equal(["disc D1", "disc D2", "disc D3"], there.Folders);
        Assert.Equal(["WITCHER D1.iso", "WITCHER D2.iso", "WITCHER D3.iso"], there.Images);
        Assert.Equal("3 disc folders and 3 images", there.InWords);
    }

    [Fact]
    public void A_set_is_numbered_without_a_gap_so_the_first_one_missing_ends_it()
    {
        ADisk disk = new(Out) { Folders = { "disc D1", "disc D3" } };

        Assert.Equal(["disc D1"], AnEarlierBuild.In(disk, Out, "WITCHER").Folders);
    }

    [Fact]
    public void Somebody_elses_images_in_the_folder_are_not_an_earlier_build()
    {
        // Asking about these would teach people to press Enter at a question
        // that sometimes matters.
        ADisk disk = new(Out)
        {
            Files = { "ubuntu.iso", "WITCHER 3.iso", "WITCHER.iso.txt", "MY WITCHER.iso", "WITCHER D.iso", "notes.txt" },
        };

        Assert.False(AnEarlierBuild.In(disk, Out, "WITCHER").IsThere);
    }

    [Theory]
    [InlineData("WITCHER", "witcher.ISO", true)]
    [InlineData("WITCHER", "Witcher D2.iso", true)]
    [InlineData("WITCHER", "WITCHER D12.iso", true)]
    [InlineData("WITCHER", "WITCHER 3.iso", false)]
    [InlineData("WITCHER", "WITCHER D2.img", false)]
    [InlineData("WITCHER", "WITCHER", false)]
    // A label of nothing is built as "game", so that is the name looked for.
    [InlineData("", ".iso", false)]
    [InlineData("  ", " D1.iso", false)]
    [InlineData("", "game.iso", true)]
    public void An_image_is_this_discs_by_the_name_a_build_gives_it(string label, string file, bool itIs) =>
        Assert.Equal(itIs, TheImageOf.IsOneOfADiscCalled(label, file));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void The_name_looked_for_is_the_name_a_build_writes(int ofDiscs)
    {
        // The two are in one class so they cannot drift, and this holds
        // them together anyway, for one disc and for the last of a set, and
        // for a label the disc cannot wear as it was typed.
        string written = Path.GetFileName(TheImageOf.That(
            new BuiltDisc("Hollow: Knight", ofDiscs, ofDiscs, Path.Combine(Out, "disc"), 0)));

        Assert.True(TheImageOf.IsOneOfADiscCalled("Hollow: Knight", written), written);
    }

    [Fact]
    public void Only_the_folder_itself_is_read_and_never_what_is_under_it()
    {
        // A disc folder is sixty gigabytes in thousands of files.
        ADisk disk = new(Out) { Folders = { "disc" }, Files = { "HOLLOW KNIGHT.iso" } };

        _ = AnEarlierBuild.In(disk, Out, "HOLLOW KNIGHT");

        Assert.Equal(0, disk.TimesWalked);
    }

    [Theory]
    [InlineData(1, 0, "a disc folder")]
    [InlineData(2, 0, "2 disc folders")]
    [InlineData(0, 1, "a disc image")]
    [InlineData(0, 4, "4 images")]
    [InlineData(1, 1, "a disc folder and its image")]
    [InlineData(2, 2, "2 disc folders and 2 images")]
    public void What_is_there_is_said_in_words(int folders, int images, string words) =>
        Assert.Equal(
            words,
            new AnEarlierBuild(
                [.. Enumerable.Range(1, folders).Select(each => $"disc D{each}")],
                [.. Enumerable.Range(1, images).Select(each => $"X D{each}.iso")]).InWords);

    // --------------------------------------------------------------- the screen

    [Fact]
    public void Nothing_is_asked_when_the_folder_holds_no_earlier_build()
    {
        MakeADisc screen = TheScreen(new ADisk(Out));

        Assert.True(screen.CanMakeIt, screen.WhyNot?.ToString());
        Assert.Null(screen.WhatBuildingHereReplaces());
    }

    [Fact]
    public void An_earlier_build_is_asked_about_with_the_folder_and_what_is_in_it()
    {
        MakeADisc screen = TheScreen(new ADisk(Out) { Folders = { "disc" }, Files = { "HOLLOW KNIGHT.iso" } });

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatBuildingHereReplaces());

        Assert.Equal("Build over the disc that is already in this folder?", asked.Headline);
        Assert.Contains(Out, asked.Because, StringComparison.Ordinal);
        Assert.Contains("a disc folder and its image", asked.Because, StringComparison.Ordinal);
        Assert.Contains("deletes them", asked.Because, StringComparison.Ordinal);
        Assert.Contains("choose another folder", asked.Because, StringComparison.Ordinal);

        // Building again after changing something is what the button is
        // nearly always pressed for, so that is the one Enter answers.
        Assert.Equal("Build over it", asked.TheLikelyOne);
        Assert.Equal("Cancel", asked.TheOtherWay);
    }

    [Fact]
    public void One_thing_there_is_it_and_not_them()
    {
        MakeADisc screen = TheScreen(new ADisk(Out) { Folders = { "disc" } });

        Assert.Contains("deletes it and", screen.WhatBuildingHereReplaces()!.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disc_of_another_name_in_the_same_folder_is_asked_about_for_its_folder_alone()
    {
        // The disc folder is emptied whatever the last disc was called. Its
        // image is not this build's to write over, and is not counted.
        MakeADisc screen = TheScreen(new ADisk(Out) { Folders = { "disc" }, Files = { "ANOTHER GAME.iso" } });

        string because = screen.WhatBuildingHereReplaces()!.Because;

        Assert.Contains("holds a disc folder from", because, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_asked_while_the_button_could_not_build_anyway()
    {
        MakeADisc screen = TheScreen(new ADisk(Out) { Folders = { "disc" } });
        screen.Label = string.Empty;

        Assert.False(screen.CanMakeIt);
        Assert.Null(screen.WhatBuildingHereReplaces());
    }

    [Fact]
    public void Asking_changes_nothing_on_the_screen()
    {
        MakeADisc screen = TheScreen(new ADisk(Out) { Folders = { "disc" } });
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        _ = screen.WhatBuildingHereReplaces();

        Assert.Empty(told);
        Assert.Null(screen.Made);
        Assert.False(screen.IsWorking);
    }

    // --------------------------------------------------------------- the window

    [Fact]
    public void The_window_asks_before_it_builds_and_does_not_build_on_a_no()
    {
        // The dialog itself is the window's and no test can press it. This
        // reads the handler: the question comes before MakeIt, and a no
        // returns before it.
        string window = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string handler = window[window.IndexOf("private async void MakeTheDisc(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("private async void BurnIt(", StringComparison.Ordinal)];

        int asks = handler.IndexOf(
            "if (_screen.WhatBuildingHereReplaces() is AQuestion replacing && !Ask(replacing))",
            StringComparison.Ordinal);
        int builds = handler.IndexOf("await _screen.MakeIt();", StringComparison.Ordinal);

        Assert.True(asks >= 0, "The handler does not ask.");
        Assert.True(builds > asks, "The handler builds before it asks.");
        Assert.Contains("return;", handler[asks..builds], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static MakeADisc TheScreen(ADisk disk)
    {
        MakeADisc screen = new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            disk,
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesOfOneSize(512, 512),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

        ListedFile only = new("setup_hollow_knight.exe", 1000);

        screen.Add(new GameFolder(
            @"C:\games\Hollow Knight", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", only, [only], [], null));
        screen.Label = "HOLLOW KNIGHT";
        screen.OutputFolder = Out;

        return screen;
    }

    /// <summary>
    /// One output folder with whatever is put in it. Every other folder is
    /// there and empty, as a game's folder has to be for the button to light.
    /// </summary>
    private sealed class ADisk(string output) : IFileSystem
    {
        public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Files { get; } = [];

        public int TimesWalked { get; private set; }

        public bool FolderExists(string path)
        {
            if (path.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Folders.Contains(path[(output.Length + 1)..]);
            }

            return !path.Contains("not there", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(path);
        }

        public FolderListing List(string path)
        {
            TimesWalked++;

            return ListHere(path);
        }

        public FolderListing ListHere(string path)
        {
            // As the real disk does: a folder that is not there cannot be
            // read, and asking is an exception, not an empty answer.
            if (!FolderExists(path))
            {
                throw new DirectoryNotFoundException(path);
            }

            return new(path, string.Equals(path, output, StringComparison.OrdinalIgnoreCase)
                ? [.. Files.Select(each => new ListedFile(each, 1000))]
                : []);
        }
    }

    private sealed class PicturesOfOneSize(int wide, int tall, bool readable = true) : IImages
    {
        public int TimesAsked { get; private set; }

        public ImageFacts Read(string path)
        {
            TimesAsked++;

            return new ImageFacts(readable, wide, tall, readable ? null : "It cannot be read.");
        }

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(
            string source, string destination, int width, int height, BackgroundFit how, KeptPart? kept = null)
        {
        }
    }

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

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
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

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }
}
