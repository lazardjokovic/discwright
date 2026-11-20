using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Keeping only the image, discwright#98, 15 November 2026: "ISO only,
/// without the disc folder, to halve the space needed".
/// </summary>
/// <remarks>
/// The one thing this must never do is delete a folder whose image was not
/// made, or a folder that is not this program's. Both have tests of their
/// own, against real folders.
/// </remarks>
public sealed class KeepingOnlyTheImage : IDisposable
{
    private const string Out = @"C:\out\Hollow Knight Disc";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "dwonly_" + Guid.NewGuid().ToString("N")[..8]);

    public KeepingOnlyTheImage() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------- the option

    [Fact]
    public void It_is_off_unless_somebody_asks()
    {
        Assert.False(new DiscOptions().OnlyTheImage);
        Assert.False(TheScreen(new ARemover()).OnlyTheImage);
        Assert.False(TheScreen(new ARemover()).AsAProject().Options.OnlyTheImage);
    }

    [Fact]
    public void Ticked_it_reaches_the_project_and_the_window_hears()
    {
        MakeADisc screen = TheScreen(new ARemover());
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.OnlyTheImage = true;

        Assert.True(screen.AsAProject().Options.OnlyTheImage);
        Assert.Contains(nameof(MakeADisc.OnlyTheImage), told, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(true, "\"IsoOnly\": true")]
    [InlineData(false, "\"IsoOnly\": false")]
    public void A_saved_project_says_it_and_comes_back_the_same(bool only, string written)
    {
        MakeADisc screen = TheScreen(new ARemover());
        screen.OnlyTheImage = only;

        string saved = TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "DVD5", Out, DateTimeOffset.UtcNow);

        Assert.Contains(written, saved, StringComparison.Ordinal);
        Assert.Equal(only, Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.OnlyTheImage);
    }

    [Fact]
    public void A_file_from_before_the_option_keeps_the_folder()
    {
        MakeADisc screen = TheScreen(new ARemover());
        string saved = TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "DVD5", Out, DateTimeOffset.UtcNow)
            .Replace("\"IsoOnly\": false", "\"SomethingElse\": false", StringComparison.Ordinal);

        Assert.DoesNotContain("IsoOnly", saved, StringComparison.Ordinal);
        Assert.False(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.OnlyTheImage);
    }

    // --------------------------------------------------------------- the build

    [Fact]
    public async Task Left_off_a_build_deletes_nothing()
    {
        ARemover remover = new();
        MakeADisc screen = TheScreen(remover);

        await screen.MakeIt();

        Assert.NotNull(screen.Made);
        Assert.Empty(remover.Asked);
        Assert.True(screen.TheDiscFoldersWereKept);
        Assert.Equal(("The disc is ready in:", Path.Combine(Out, "disc")), screen.WhatToSayOfWhatWasMade());
    }

    [Fact]
    public async Task Ticked_the_folder_goes_once_the_image_is_made_and_the_words_say_image()
    {
        ARemover remover = new();
        List<string> order = [];
        MakeADisc screen = TheScreen(remover, order: order);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        Assert.Equal([Path.Combine(Out, "disc")], remover.Asked);
        Assert.Null(screen.ItWentWrong);
        Assert.False(screen.TheDiscFoldersWereKept);

        // The image first, and the folder only after it.
        Assert.Equal(["image disc", "remove disc"], order);

        // And the folder offered to open is one that is still there.
        Assert.Equal(("The disc image is ready in:", Out), screen.WhatToSayOfWhatWasMade());
        Assert.Null(screen.AboutTheImage);
    }

    [Fact]
    public async Task Every_disc_of_a_set_goes_and_only_after_every_image()
    {
        ARemover remover = new();
        List<string> order = [];
        MakeADisc screen = TheScreen(remover, discs: 3, order: order);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        Assert.Equal(
            ["image disc D1", "image disc D2", "image disc D3", "remove disc D1", "remove disc D2", "remove disc D3"],
            order);
        Assert.Equal(("All 3 disc images are ready in:", Out), screen.WhatToSayOfWhatWasMade());
    }

    [Fact]
    public async Task A_set_with_its_folders_kept_is_said_as_it_always_was()
    {
        MakeADisc screen = TheScreen(new ARemover(), discs: 3);

        await screen.MakeIt();

        Assert.Equal(("All 3 discs are ready in:", Out), screen.WhatToSayOfWhatWasMade());
    }

    [Fact]
    public async Task An_image_that_was_not_made_keeps_its_folder()
    {
        // The one thing this must never do: a folder deleted with no image
        // to show for it is a build that left nothing.
        ARemover remover = new();
        MakeADisc screen = TheScreen(remover, discs: 2, theImageFailsAt: 2);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        Assert.NotNull(screen.ItWentWrong);
        Assert.Empty(remover.Asked);
        Assert.True(screen.TheDiscFoldersWereKept);
    }

    [Fact]
    public async Task A_build_that_was_refused_deletes_nothing()
    {
        ARemover remover = new();
        MakeADisc screen = TheScreen(remover, theBuildIsRefused: true);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        Assert.NotNull(screen.ItWentWrong);
        Assert.Empty(remover.Asked);
        Assert.Null(screen.WhatToSayOfWhatWasMade());
    }

    [Fact]
    public async Task A_folder_something_has_open_is_left_and_said_without_failing_the_build()
    {
        ARemover remover = new() { HeldOpen = { Path.Combine(Out, "disc") } };
        MakeADisc screen = TheScreen(remover);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        // The image is made and is what was asked for.
        Assert.Null(screen.ItWentWrong);
        Assert.NotNull(screen.Made);
        Assert.True(screen.TheDiscFoldersWereKept);
        Assert.Contains(Path.Combine(Out, "disc"), screen.AboutTheImage, StringComparison.Ordinal);
        Assert.Contains("could not be deleted because something has it open", screen.AboutTheImage, StringComparison.Ordinal);
        Assert.Contains("delete the folder by hand", screen.AboutTheImage, StringComparison.Ordinal);
        Assert.Equal(("The disc is ready in:", Path.Combine(Out, "disc")), screen.WhatToSayOfWhatWasMade());
    }

    [Fact]
    public async Task One_folder_held_open_does_not_stop_the_others_going()
    {
        ARemover remover = new() { HeldOpen = { Path.Combine(Out, "disc D2") } };
        MakeADisc screen = TheScreen(remover, discs: 3);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        Assert.Equal(3, remover.Asked.Count);
        Assert.Equal([Path.Combine(Out, "disc D1"), Path.Combine(Out, "disc D3")], remover.Gone);
        Assert.True(screen.TheDiscFoldersWereKept);
    }

    [Fact]
    public async Task The_next_build_starts_with_its_folders_there_again()
    {
        MakeADisc screen = TheScreen(new ARemover());
        screen.OnlyTheImage = true;
        await screen.MakeIt();
        Assert.False(screen.TheDiscFoldersWereKept);

        screen.OnlyTheImage = false;
        await screen.MakeIt();

        Assert.True(screen.TheDiscFoldersWereKept);
        Assert.Equal(("The disc is ready in:", Path.Combine(Out, "disc")), screen.WhatToSayOfWhatWasMade());
    }

    [Fact]
    public async Task A_machine_with_nothing_to_delete_with_keeps_the_folder_and_says_so_by_its_words()
    {
        MakeADisc screen = TheScreen(remover: null);
        screen.OnlyTheImage = true;

        await screen.MakeIt();

        Assert.Null(screen.ItWentWrong);
        Assert.True(screen.TheDiscFoldersWereKept);
    }

    // ------------------------------------------------- the real thing, on disk

    [Theory]
    [InlineData("disc")]
    [InlineData("disc D2")]
    public void The_real_remover_deletes_a_disc_folder_and_leaves_what_is_beside_it(string named)
    {
        string disc = Path.Combine(_folder, named);
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "menu");
        File.WriteAllText(Path.Combine(disc, "setup.exe"), "game");
        File.WriteAllText(Path.Combine(_folder, "GAME.iso"), "image");
        File.WriteAllText(Path.Combine(_folder, "discproject.json"), "{}");

        new WindowsStageWriter().Remove(disc);

        Assert.False(Directory.Exists(disc));
        Assert.Equal(
            ["discproject.json", "GAME.iso"],
            Directory.GetFileSystemEntries(_folder).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("My Documents")]
    [InlineData("discography")]
    [InlineData("disc 2")]
    [InlineData("Hollow Knight Disc")]
    public void It_refuses_any_folder_this_program_did_not_name(string named)
    {
        string notOurs = Path.Combine(_folder, named);
        Directory.CreateDirectory(notOurs);
        File.WriteAllText(Path.Combine(notOurs, "thesis.docx"), "years of work");

        Assert.Throws<ArgumentException>(() => new WindowsStageWriter().Remove(notOurs));

        Assert.True(File.Exists(Path.Combine(notOurs, "thesis.docx")));
    }

    [Fact]
    public void A_folder_with_a_file_held_open_is_refused_whole_and_nothing_in_it_is_lost()
    {
        string disc = Path.Combine(_folder, "disc");
        Directory.CreateDirectory(disc);
        File.WriteAllText(Path.Combine(disc, "a.bin"), "first");
        File.WriteAllText(Path.Combine(disc, "b.bin"), "second");

        using (File.Open(Path.Combine(disc, "b.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            TheFolderIsInUse held = Assert.Throws<TheFolderIsInUse>(() => new WindowsStageWriter().Remove(disc));

            Assert.Equal(disc, held.Folder);
        }

        // Not half deleted: both files are where they were.
        Assert.Equal("first", File.ReadAllText(Path.Combine(disc, "a.bin")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(disc, "b.bin")));
        Assert.Single(Directory.GetDirectories(_folder));
    }

    [Fact]
    public void A_folder_that_is_already_gone_is_nothing_to_complain_about()
    {
        new WindowsStageWriter().Remove(Path.Combine(_folder, "disc"));

        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    // -------------------------------------------------------------- the window

    [Fact]
    public void The_window_has_the_tick_in_More_options_with_words_that_say_what_it_costs()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement tick = Assert.Single(
            System.Xml.Linq.XDocument.Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml"))
                .Root!.Descendants(xaml + "CheckBox"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "OnlyTheImage");

        Assert.Equal("{Binding OnlyTheImage}", (string?)tick.Attribute("IsChecked"));
        Assert.Equal("{Binding NothingIsRunning}", (string?)tick.Attribute("IsEnabled"));
        Assert.Equal("Keep only the ISO, not the disc folder", (string?)tick.Attribute("Content"));
        Assert.Contains(tick.Ancestors(xaml + "Expander"), each => (string?)each.Attribute("Header") == "More options");

        string says = string.Join(" ", tick.Descendants(xaml + "TextBlock").Single().Value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("Building still needs room for both", says, StringComparison.Ordinal);
        Assert.Contains("checked against its own list", says, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_says_what_the_screen_words_and_opens_the_folder_the_screen_names()
    {
        string window = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string say = window[window.IndexOf("private void Say(BuiltDiscs made)", StringComparison.Ordinal)..];
        say = say[..say.IndexOf("private static string Quoted(", StringComparison.Ordinal)];

        Assert.Contains("_screen.WhatToSayOfWhatWasMade() is not (string words, string folder)", say, StringComparison.Ordinal);
        Assert.DoesNotContain("made.Discs[0].Folder", say, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static MakeADisc TheScreen(
        ARemover? remover,
        int discs = 1,
        int theImageFailsAt = 0,
        bool theBuildIsRefused = false,
        List<string>? order = null)
    {
        order ??= [];

        if (remover is not null)
        {
            remover.Order = order;
        }

        MakeADisc screen = new(new TheMachine(
            new NoEncoding(),
            new ABuilderOfSoMany(discs, theBuildIsRefused),
            new ImagesThatSayWhen(order, theImageFailsAt),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            new ADiskThatIsThere(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesOfOneSize(512, 512),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience,
            Remover: remover));

        ListedFile only = new("setup_hollow_knight.exe", 1000);

        screen.Add(new GameFolder(
            @"C:\games\Hollow Knight", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", only, [only], [], null));
        screen.Label = "HOLLOW KNIGHT";
        screen.OutputFolder = Out;

        return screen;
    }

    /// <summary>Every folder is there and holds nothing, so the button lights and nothing is asked.</summary>
    private sealed class ADiskThatIsThere : IFileSystem
    {
        public bool FolderExists(string path) => !path.StartsWith(Out + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class ARemover : IRemoveADiscFolder
    {
        public List<string> Asked { get; } = [];

        public List<string> Gone { get; } = [];

        public HashSet<string> HeldOpen { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Order { get; set; } = [];

        public void Remove(string discFolder)
        {
            Asked.Add(discFolder);

            if (HeldOpen.Contains(discFolder))
            {
                throw new TheFolderIsInUse(discFolder, "The process cannot access the file.", new IOException());
            }

            Gone.Add(discFolder);
            Order.Add("remove " + Path.GetFileName(discFolder));
        }
    }

    private sealed class ABuilderOfSoMany(int discs, bool refused) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(refused
                ? Refusals.ThereIsNothingToPutOnADisc()
                : new BuiltDiscs(
                [
                    .. Enumerable.Range(1, discs).Select(number => new BuiltDisc(
                        project.Label,
                        number,
                        discs,
                        Path.Combine(outputFolder, discs > 1 ? $"disc D{number}" : "disc"),
                        1000))
                ]));
    }

    private sealed class ImagesThatSayWhen(List<string> order, int failsAt) : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct)
        {
            if (discNumber == failsAt)
            {
                return Task.FromResult<Outcome<MadeImage>>(Refusals.ThereIsNoDiscWriter());
            }

            order.Add("image " + Path.GetFileName(stagedFolder));

            return Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
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

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
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
