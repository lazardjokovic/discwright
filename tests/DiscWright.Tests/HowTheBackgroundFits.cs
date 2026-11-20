using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Four ways for a background to cover the menu where there was a tick box
/// for two, discwright#98, 15 November: "fill (doesn't show whole image but
/// fills the screen and keeps ratio), fit (keeps ratio and doesn't lose
/// image, may show black border), stretch (fills screen, ignoring ratio) and
/// tile (to repeat the image to fill the window without changing its size)".
/// </summary>
/// <remarks>
/// What each draws is <see cref="TheRealIconWriter"/>'s, by the pixel. This
/// is the list, the line under it, and a project file that both this
/// version and the ones before it can read.
/// </remarks>
public class HowTheBackgroundFits
{
    [Fact]
    public void The_list_offers_the_four_with_the_one_most_people_want_first()
    {
        MakeADisc screen = TheScreen();

        Assert.Equal(
            ["Fill and crop", "Fit inside", "Stretch", "Tile"],
            screen.TheWaysABackgroundFits.Select(each => each.InWords));
        Assert.Equal(
            [BackgroundFit.Fill, BackgroundFit.Fit, BackgroundFit.Stretch, BackgroundFit.Tile],
            screen.TheWaysABackgroundFits.Select(each => each.How));

        // What a reader says for a row, and what a list with no template shows.
        Assert.All(screen.TheWaysABackgroundFits, each => Assert.Equal(each.InWords, each.ToString()));
    }

    [Fact]
    public void Every_way_there_is_has_a_row_and_a_sentence()
    {
        foreach (BackgroundFit how in Enum.GetValues<BackgroundFit>())
        {
            AWayToFit row = WaysToFit.For(how);

            Assert.Equal(how, row.How);
            Assert.False(string.IsNullOrWhiteSpace(row.InWords));
            Assert.EndsWith(".", row.WhatItDoes, StringComparison.Ordinal);
        }

        Assert.Equal(Enum.GetValues<BackgroundFit>().Length, WaysToFit.All.Count);
    }

    [Fact]
    public void Nothing_chosen_is_filled_and_no_artwork_at_all()
    {
        MakeADisc screen = TheScreen();

        Assert.Equal(BackgroundFit.Fill, screen.HowTheBackgroundFits?.How);
        Assert.Equal(WaysToFit.For(BackgroundFit.Fill).WhatItDoes, screen.WhatThatFitDoes);
        Assert.Null(screen.AsAProject().Artwork);
    }

    [Theory]
    [InlineData(BackgroundFit.Fill, false, null)]
    [InlineData(BackgroundFit.Stretch, true, null)]
    [InlineData(BackgroundFit.Fit, false, BackgroundFit.Fit)]
    [InlineData(BackgroundFit.Tile, false, BackgroundFit.Tile)]
    public void Choosing_a_row_fits_the_disc_that_way_and_keeps_the_old_yes_or_no_honest(
        BackgroundFit how, bool asIs, BackgroundFit? shape)
    {
        MakeADisc screen = TheScreen();
        screen.Background = @"C:\art\back.png";

        screen.HowTheBackgroundFits = WaysToFit.For(how);

        DiscArtwork art = screen.AsAProject().Artwork!;

        Assert.Equal(how, art.HowItFits);

        // The yes or no is what an older version reads, so it is true for
        // stretched and for nothing else.
        Assert.Equal(asIs, art.BackgroundAsIs);
        Assert.Equal(shape, art.Shape);
        Assert.Equal(asIs, screen.BackgroundAsIs);
        Assert.Equal(how, screen.HowTheBackgroundFits?.How);
        Assert.Equal(WaysToFit.For(how).WhatItDoes, screen.WhatThatFitDoes);
    }

    [Fact]
    public void Going_from_tiled_to_stretched_and_back_to_filled_leaves_nothing_behind()
    {
        MakeADisc screen = TheScreen();

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Tile);
        Assert.Equal(BackgroundFit.Tile, screen.AsAProject().Artwork?.HowItFits);

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Stretch);
        Assert.Equal(new DiscArtwork(BackgroundAsIs: true), screen.AsAProject().Artwork);

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Fill);
        Assert.Null(screen.AsAProject().Artwork);
    }

    [Fact]
    public void The_old_tick_still_means_stretched_and_takes_a_shape_off()
    {
        MakeADisc screen = TheScreen();
        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Tile);

        screen.BackgroundAsIs = true;
        Assert.Equal(BackgroundFit.Stretch, screen.HowTheBackgroundFits?.How);

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Fit);
        screen.BackgroundAsIs = false;
        Assert.Equal(BackgroundFit.Fill, screen.HowTheBackgroundFits?.How);
        Assert.Null(screen.AsAProject().Artwork);
    }

    [Fact]
    public void A_list_emptying_its_choice_for_a_moment_changes_nothing()
    {
        MakeADisc screen = TheScreen();
        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Tile);

        screen.HowTheBackgroundFits = null;

        Assert.Equal(BackgroundFit.Tile, screen.HowTheBackgroundFits?.How);
    }

    [Fact]
    public void The_window_hears_about_the_list_the_line_and_the_old_tick()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Fit);

        Assert.Contains(nameof(MakeADisc.HowTheBackgroundFits), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhatThatFitDoes), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.BackgroundAsIs), told, StringComparer.Ordinal);
    }

    [Fact]
    public void Choosing_a_picture_or_a_colour_keeps_the_way_it_fits()
    {
        MakeADisc screen = TheScreen();
        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Tile);

        screen.Background = @"C:\art\back.png";
        Assert.Equal(BackgroundFit.Tile, screen.AsAProject().Artwork?.HowItFits);

        screen.BackgroundColourText = "#1B3A4B";
        Assert.Equal(BackgroundFit.Tile, screen.AsAProject().Artwork?.HowItFits);
    }

    // ------------------------------------------------------ the project file

    [Theory]
    [InlineData(BackgroundFit.Fill, "\"BgAsIs\": false", "\"BgFit\": \"Fill\"")]
    [InlineData(BackgroundFit.Stretch, "\"BgAsIs\": true", "\"BgFit\": \"Stretch\"")]
    [InlineData(BackgroundFit.Fit, "\"BgAsIs\": false", "\"BgFit\": \"Fit\"")]
    [InlineData(BackgroundFit.Tile, "\"BgAsIs\": false", "\"BgFit\": \"Tile\"")]
    public void A_saved_project_says_it_both_ways_and_comes_back_the_same(
        BackgroundFit how, string theOldWay, string theWord)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";
        screen.HowTheBackgroundFits = WaysToFit.For(how);

        string written = Saved(screen);

        Assert.Contains(theOldWay, written, StringComparison.Ordinal);
        Assert.Contains(theWord, written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(how == BackgroundFit.Stretch, back.Artwork.BackgroundAsItIs);
        Assert.Equal(how, back.Artwork.BackgroundShape);
    }

    [Theory]
    [InlineData(false, BackgroundFit.Fill)]
    [InlineData(true, BackgroundFit.Stretch)]
    public void A_file_from_before_there_was_a_word_is_read_by_its_yes_or_no(bool asIs, BackgroundFit how)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";
        screen.BackgroundAsIs = asIs;

        string written = System.Text.RegularExpressions.Regex.Replace(
            Saved(screen), ",\\s*\"BgFit\": \"[A-Za-z]+\"", string.Empty);

        Assert.DoesNotContain("BgFit", written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Null(back.Artwork.BackgroundShape);
        Assert.Equal(how, back.Artwork.HowItFits);
    }

    [Theory]
    [InlineData("\"BgFit\": \"Sideways\"")]
    [InlineData("\"BgFit\": \"3\"")]
    [InlineData("\"BgFit\": 3")]
    [InlineData("\"BgFit\": null")]
    [InlineData("\"BgFit\": \"\"")]
    public void A_word_that_names_no_way_to_fit_leaves_it_to_the_yes_or_no(string doctored)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";
        screen.BackgroundAsIs = true;

        string written = Saved(screen).Replace("\"BgFit\": \"Stretch\"", doctored, StringComparison.Ordinal);

        Assert.Contains(doctored, written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Null(back.Artwork.BackgroundShape);
        Assert.Equal(BackgroundFit.Stretch, back.Artwork.HowItFits);
    }

    [Theory]
    [InlineData("tile", BackgroundFit.Tile)]
    [InlineData("FIT", BackgroundFit.Fit)]
    [InlineData(" Stretch ", BackgroundFit.Stretch)]
    [InlineData("fill", BackgroundFit.Fill)]
    public void The_word_is_read_whatever_its_capitals(string word, BackgroundFit how) =>
        Assert.Equal(how, WaysToFit.Named(word));

    [Fact]
    public void The_word_wins_when_a_file_somebody_edited_says_two_things()
    {
        // Stretched by its yes or no and tiled by its word: the word is the
        // newer and the more exact, and what opens agrees with itself.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";
        screen.BackgroundAsIs = true;

        string written = Saved(screen).Replace("\"BgFit\": \"Stretch\"", "\"BgFit\": \"Tile\"", StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.True(back.Artwork.BackgroundAsItIs);
        Assert.Equal(BackgroundFit.Tile, back.Artwork.HowItFits);

        // And made into artwork that way, the yes or no gives way to it.
        Assert.False(new DiscArtwork().Fitting(back.Artwork.HowItFits).BackgroundAsIs);
    }

    [Fact]
    public void An_opened_project_shows_its_way_in_the_list()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.OpenThis(new Reopened(
            new DiscProject(
                "OPENED",
                [AGame("Hollow Knight")],
                new DiscOptions(),
                Artwork: new DiscArtwork(BackgroundPath: @"C:\art\back.png").Fitting(BackgroundFit.Fit),
                Extras: null,
                AddOns: []),
            null,
            null,
            [],
            []));

        Assert.Equal(BackgroundFit.Fit, screen.HowTheBackgroundFits?.How);
        Assert.Contains(nameof(MakeADisc.HowTheBackgroundFits), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhatThatFitDoes), told, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------- the dialog

    [Fact]
    public void The_dialog_has_a_list_where_the_tick_box_was_and_a_line_under_it()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        System.Xml.Linq.XElement list = Assert.Single(
            dialog.Descendants(xaml + "ComboBox"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "BackgroundFit");

        Assert.Equal("{Binding TheWaysABackgroundFits}", (string?)list.Attribute("ItemsSource"));
        Assert.Equal("{Binding HowTheBackgroundFits}", (string?)list.Attribute("SelectedItem"));
        Assert.Equal("InWords", (string?)list.Attribute("DisplayMemberPath"));

        // Each row says its words to a reader, and not what the row prints as.
        Assert.Contains(
            list.Descendants(xaml + "Setter"),
            each => (string?)each.Attribute("Property") == "AutomationProperties.Name"
                && (string?)each.Attribute("Value") == "{Binding InWords}");

        System.Xml.Linq.XElement line = Assert.Single(
            dialog.Descendants(xaml + "TextBlock"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "WhatThatFitDoes");

        Assert.Equal("{Binding WhatThatFitDoes}", (string?)line.Attribute("Text"));

        // And the tick box is gone, not left beside it to disagree.
        Assert.DoesNotContain(
            dialog.Descendants(),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "BackgroundAsIs");
    }

    private static string Saved(MakeADisc screen) =>
        TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "DVD5", @"C:\out", DateTimeOffset.UtcNow);

    // ------------------------------------------------------------- helpers

    private static MenuFacts AMenu() =>
        new("Hollow Knight", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true);

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(
            @"C:\games\somewhere", GameKind.GogInstaller, name, name, only, [only], [], null);
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
