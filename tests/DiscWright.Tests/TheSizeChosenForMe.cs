using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The size of disc chosen for whoever is using the program, discwright#98,
/// 12 November: "'What size of disc' in DW2 is missing an 'Automatically
/// Choose' option, so it can suggest what disc it thinks would be a best
/// fit".
/// </summary>
public class TheSizeChosenForMe
{
    private const long Mb = 1024L * 1024;
    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(300 * Mb, "CD")]
    [InlineData(2 * Gb, "DVD5")]
    [InlineData(6 * Gb, "DVD9")]
    [InlineData(20 * Gb, "BD25")]
    [InlineData(40 * Gb, "BD50")]
    [InlineData(80 * Gb, "BDXL")]
    public void The_smallest_size_that_holds_it_on_one_disc_is_the_one(long bytes, string size)
    {
        Medium? suits = ChoosingASize.TheSmallestThatHoldsItOnOneDisc(AProjectOf(AGame("Big", bytes)), _ => 1000);

        Assert.Equal(size, suits?.Key);
        Assert.Equal(size, ChoosingASize.For(AProjectOf(AGame("Big", bytes)), _ => 1000).Key);
    }

    [Fact]
    public void When_no_size_holds_it_on_one_disc_it_is_the_largest_which_takes_the_fewest()
    {
        DiscProject three = AProjectOf(AGame("One", 40 * Gb), AGame("Two", 40 * Gb), AGame("Three", 40 * Gb));

        Assert.Null(ChoosingASize.TheSmallestThatHoldsItOnOneDisc(three, _ => 1000));
        Assert.Equal("BDXL", ChoosingASize.For(three, _ => 1000).Key);
    }

    [Fact]
    public void It_is_planned_and_not_added_up()
    {
        // 4.36 GiB of game is under a DVD5's 4.37 by the sum alone, and the
        // disc has a menu and a list of what every file should be on it as
        // well. Whatever the plan says of a DVD5 is the answer.
        DiscProject tight = AProjectOf(AGame("Tight", (long)(4.3699 * Gb)));

        bool fitsADvd5 = WhatWouldBeMade.For(tight, Media.Dvd5, _ => 1000)
            is Outcome<PlannedBuild>.Ok(PlannedBuild planned) && planned.Discs == 1;

        Assert.Equal(
            fitsADvd5 ? "DVD5" : "DVD9",
            ChoosingASize.TheSmallestThatHoldsItOnOneDisc(tight, _ => 1000)?.Key);
    }

    // -------------------------------------------------------------- the screen

    [Fact]
    public void Nothing_is_chosen_for_anybody_until_they_ask()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Small", 300 * Mb));

        Assert.False(screen.ChooseTheSizeForMe);
        Assert.True(screen.TheSizeCanBePicked);
        Assert.Equal(string.Empty, screen.WhyThatSize);

        // Where it starts with no drive to say otherwise, and not the CD
        // that would hold it.
        Assert.Equal("DVD5", screen.Medium.Key);
    }

    [Fact]
    public void Ticking_it_moves_the_size_to_the_smallest_that_holds_what_is_on_the_window()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Small", 300 * Mb));

        screen.ChooseTheSizeForMe = true;

        Assert.Equal("CD", screen.Medium.Key);
        Assert.Equal("CD", screen.TheSizeChosen?.Medium.Key);
        Assert.Equal("The smallest that holds all of it on one disc.", screen.WhyThatSize);
        Assert.False(screen.TheSizeCanBePicked);
    }

    [Fact]
    public void It_moves_up_as_games_are_added_and_back_down_as_they_are_taken_off()
    {
        MakeADisc screen = TheScreen();
        screen.ChooseTheSizeForMe = true;

        screen.Add(AGame("Small", 300 * Mb));
        Assert.Equal("CD", screen.Medium.Key);

        screen.Add(AGame("Large", 3 * Gb));
        Assert.Equal("DVD5", screen.Medium.Key);

        screen.Add(AGame("Larger", 3 * Gb));
        Assert.Equal("DVD9", screen.Medium.Key);

        screen.TakeOff(screen.WhatGoesOnIt[2]);
        screen.TakeOff(screen.WhatGoesOnIt[1]);
        Assert.Equal("CD", screen.Medium.Key);
    }

    [Fact]
    public void It_follows_the_ticks_in_the_list_too()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Small", 300 * Mb));
        screen.Add(AGame("Large", 3 * Gb));
        screen.ChooseTheSizeForMe = true;
        Assert.Equal("DVD5", screen.Medium.Key);

        // Left off the disc without being taken off the list.
        screen.WhatGoesOnIt[1].IsOn = false;

        Assert.Equal("CD", screen.Medium.Key);
    }

    [Fact]
    public void With_more_than_any_size_holds_it_is_the_largest_and_the_line_says_why()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("One", 40 * Gb));
        screen.Add(AGame("Two", 40 * Gb));
        screen.Add(AGame("Three", 40 * Gb));

        screen.ChooseTheSizeForMe = true;

        Assert.Equal("BDXL", screen.Medium.Key);
        Assert.Equal(
            "No size holds all of it on one disc, so the largest, which takes the fewest.",
            screen.WhyThatSize);
    }

    [Fact]
    public void With_nothing_on_the_window_it_leaves_the_size_alone_and_says_nothing()
    {
        MakeADisc screen = TheScreen();

        screen.ChooseTheSizeForMe = true;

        Assert.Equal("DVD5", screen.Medium.Key);
        Assert.Equal(string.Empty, screen.WhyThatSize);
        Assert.False(screen.TheSizeCanBePicked);
    }

    [Fact]
    public void The_line_goes_when_the_last_game_does()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Small", 300 * Mb));
        screen.ChooseTheSizeForMe = true;
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.TakeOff(screen.WhatGoesOnIt[0]);

        Assert.Equal(string.Empty, screen.WhyThatSize);
        Assert.Contains(nameof(MakeADisc.WhyThatSize), told, StringComparer.Ordinal);
    }

    [Fact]
    public void Unticking_keeps_the_size_it_arrived_at_and_stops_following()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Small", 300 * Mb));
        screen.ChooseTheSizeForMe = true;

        screen.ChooseTheSizeForMe = false;

        Assert.Equal("CD", screen.Medium.Key);
        Assert.True(screen.TheSizeCanBePicked);
        Assert.Equal(string.Empty, screen.WhyThatSize);

        // More than a CD holds now, and the size stays where somebody left it.
        screen.Add(AGame("Large", 3 * Gb));

        Assert.Equal("CD", screen.Medium.Key);
    }

    [Fact]
    public void The_window_hears_about_the_tick_the_list_the_size_and_the_line()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Small", 300 * Mb));
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.ChooseTheSizeForMe = true;

        Assert.Contains(nameof(MakeADisc.ChooseTheSizeForMe), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.TheSizeCanBePicked), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.Medium), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhyThatSize), told, StringComparer.Ordinal);
    }

    [Fact]
    public void What_it_will_make_is_said_of_the_size_it_moved_to()
    {
        // The plan is made after the size moves, not before: three
        // gigabytes on the CD it started from would be said as five discs.
        MakeADisc screen = TheScreen();
        screen.ChooseTheSizeForMe = true;
        screen.Add(AGame("Small", 300 * Mb));
        Assert.Equal("CD", screen.Medium.Key);

        screen.Add(AGame("Large", 3 * Gb));

        Assert.Equal("one disc", screen.WhatItWillMake);
        Assert.Equal("DVD5", screen.Medium.Key);
    }

    [Fact]
    public void Opening_a_project_unticks_it_because_the_files_size_is_the_answer()
    {
        MakeADisc screen = TheScreen();
        screen.ChooseTheSizeForMe = true;
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.OpenThis(new Reopened(
            new DiscProject("OPENED", [AGame("Small", 300 * Mb)], new DiscOptions(), Artwork: null, Extras: null, AddOns: []),
            Media.Bd25,
            null,
            [],
            []));

        Assert.False(screen.ChooseTheSizeForMe);
        Assert.True(screen.TheSizeCanBePicked);
        Assert.Equal("BD25", screen.Medium.Key);
        Assert.Contains(nameof(MakeADisc.ChooseTheSizeForMe), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.TheSizeCanBePicked), told, StringComparer.Ordinal);
    }

    // -------------------------------------------------------------- the window

    [Fact]
    public void The_window_has_the_tick_under_the_sizes_and_greys_the_list_by_it()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement window = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        System.Xml.Linq.XElement tick = Assert.Single(window.Descendants(xaml + "CheckBox"), each => Id(each) == "ChooseTheSizeForMe");
        System.Xml.Linq.XElement list = Assert.Single(window.Descendants(xaml + "ComboBox"), each => Id(each) == "DiscSize");
        System.Xml.Linq.XElement line = Assert.Single(window.Descendants(xaml + "TextBlock"), each => Id(each) == "WhyThatSize");

        Assert.Equal("{Binding ChooseTheSizeForMe}", (string?)tick.Attribute("IsChecked"));
        Assert.Equal("{Binding NothingIsRunning}", (string?)tick.Attribute("IsEnabled"));
        Assert.Equal("{Binding TheSizeCanBePicked}", (string?)list.Attribute("IsEnabled"));
        Assert.Equal("{Binding WhyThatSize}", (string?)line.Attribute("Text"));

        // The line takes no room while it has nothing to say.
        Assert.Equal("{Binding WhyThatSize, Converter={StaticResource WhenAnything}}", (string?)line.Attribute("Visibility"));

        // Under the list and above the next tick box, in that order.
        List<string?> order = [.. window.Descendants().Select(Id).Where(each => each is "DiscSize" or "ChooseTheSizeForMe" or "WhyThatSize" or "CutFiles")];

        Assert.Equal(["DiscSize", "ChooseTheSizeForMe", "WhyThatSize", "CutFiles"], order);
    }

    private static DiscProject AProjectOf(params GameFolder[] games) =>
        new("A DISC", games, new DiscOptions());

    // ------------------------------------------------------------- helpers

    private static MenuFacts AMenu() =>
        new("Hollow Knight", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true);

    private static GameFolder AGame(string name, long bytes = 1000)
    {
        ListedFile only = new($"setup_{name}.exe", bytes);

        return new GameFolder(
            @"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

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
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

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

    private sealed class PicturesThatRead : IImages
    {
        public ImageFacts Read(string path) => new(true, 512, 512, null);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(
            string source, string destination, int width, int height, BackgroundFit how, KeptPart? kept = null)
        {
        }
    }
}
