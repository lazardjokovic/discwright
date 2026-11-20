using System.Text.RegularExpressions;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// A heading for the main menu that is not the disc's name, or no heading at
/// all, discwright#98: "I'd like to see a separate option for Disc Label and
/// Menu Label", 12 November, and "can the text directly above the menu
/// buttons be hidden?", 3 October.
/// </summary>
/// <remarks>
/// What each screen is headed with is menu.js's and the page's, held in
/// <see cref="TheMenuScript"/> and <see cref="ThePageOnADiscOfASet"/>.
/// </remarks>
public class TheMenusHeading
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\r\n", null)]
    [InlineData("GOG COLLECTION", "GOG COLLECTION")]
    [InlineData("  GOG Collection  ", "GOG Collection")]
    [InlineData("Two\r\nlines", "Two lines")]
    [InlineData("A\ttab", "A tab")]
    public void A_heading_is_one_line_with_nothing_round_it_or_it_is_nothing(string? typed, string? kept) =>
        Assert.Equal(kept, MenuLook.TidiedHeading(typed));

    [Fact]
    public void A_heading_is_never_longer_than_the_menu_is_given()
    {
        string sixty = new('A', 60);

        Assert.Equal(sixty, MenuLook.TidiedHeading(sixty));
        Assert.Equal(sixty, MenuLook.TidiedHeading(sixty + "B"));

        // Cut where a space was, it does not end in one.
        Assert.Equal(new string('A', 59), MenuLook.TidiedHeading(new string('A', 59) + " BBBB"));
        Assert.Equal(60, MenuLook.LongestHeading);
    }

    [Fact]
    public void Nothing_typed_is_the_discs_own_name_and_no_look_at_all()
    {
        MakeADisc screen = TheScreen();

        Assert.Equal(string.Empty, screen.MenuHeading);
        Assert.False(screen.MenuHeadingHidden);
        Assert.True(screen.MenuHeadingCanBeTyped);
        Assert.Null(screen.AsAProject().Options.Look);
    }

    [Fact]
    public void What_is_typed_heads_the_menu_tidied_and_stays_in_the_box_as_typed()
    {
        MakeADisc screen = TheScreen();

        // The space between two words is typed before the second word is,
        // and a box that tidied it away would never let the second arrive
        // after a space.
        screen.MenuHeading = "GOG ";

        Assert.Equal("GOG ", screen.MenuHeading);
        Assert.Equal("GOG", screen.AsAProject().Options.Look?.Heading);

        screen.MenuHeading = "GOG Collection";

        Assert.Equal("GOG Collection", screen.AsAProject().Options.Look?.Heading);
    }

    [Fact]
    public void Emptying_the_box_again_is_a_disc_nobody_touched()
    {
        MakeADisc screen = TheScreen();
        screen.MenuHeading = "GOG Collection";

        screen.MenuHeading = "  ";

        Assert.Null(screen.AsAProject().Options.Look);
    }

    [Fact]
    public void Ticking_the_box_takes_the_heading_off_and_greys_where_it_is_typed()
    {
        MakeADisc screen = TheScreen();
        screen.MenuHeading = "GOG Collection";

        screen.MenuHeadingHidden = true;

        Assert.True(screen.AsAProject().Options.Look?.HeadingHidden);
        Assert.False(screen.MenuHeadingCanBeTyped);

        // What was typed is kept for when the box is unticked again.
        Assert.Equal("GOG Collection", screen.MenuHeading);
        Assert.Equal("GOG Collection", screen.AsAProject().Options.Look?.Heading);

        screen.MenuHeadingHidden = false;

        Assert.True(screen.MenuHeadingCanBeTyped);
        Assert.Equal(new MenuLook(Heading: "GOG Collection"), screen.AsAProject().Options.Look);
    }

    [Fact]
    public void It_keeps_the_side_the_corner_and_the_small_buttons()
    {
        MakeADisc screen = TheScreen();
        screen.ListOnTheLeft = true;
        screen.CornerAtTheBottom = true;
        screen.DiscsOwnAsSmallButtons = true;
        screen.OpenTheDiscButton = true;

        screen.MenuHeading = "GOG Collection";
        screen.MenuHeadingHidden = true;

        Assert.Equal(
            new MenuLook(MenuSide.Left, true, true, true, true, "GOG Collection", true),
            screen.AsAProject().Options.Look);
    }

    [Fact]
    public void The_window_hears_about_the_box_the_tick_and_the_greying()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.MenuHeading = "GOG";

        Assert.Contains(nameof(MakeADisc.MenuHeading), told, StringComparer.Ordinal);

        told.Clear();
        screen.MenuHeadingHidden = true;

        Assert.Contains(nameof(MakeADisc.MenuHeadingHidden), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.MenuHeadingCanBeTyped), told, StringComparer.Ordinal);
    }

    [Fact]
    public void An_opened_project_fills_the_box_and_ticks_the_tick()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.OpenThis(new Reopened(
            new DiscProject(
                "OPENED",
                [AGame("Hollow Knight")],
                new DiscOptions(Look: new MenuLook(Heading: "GOG Collection", HeadingHidden: true)),
                Artwork: null,
                Extras: null,
                AddOns: []),
            null,
            null,
            [],
            []));

        Assert.Equal("GOG Collection", screen.MenuHeading);
        Assert.True(screen.MenuHeadingHidden);
        Assert.False(screen.MenuHeadingCanBeTyped);
        Assert.Contains(nameof(MakeADisc.MenuHeading), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.MenuHeadingHidden), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.MenuHeadingCanBeTyped), told, StringComparer.Ordinal);
    }

    [Fact]
    public void A_project_opened_after_one_with_a_heading_does_not_keep_it()
    {
        MakeADisc screen = TheScreen();
        screen.MenuHeading = "The last disc";

        screen.OpenThis(new Reopened(
            new DiscProject("OPENED", [AGame("Hollow Knight")], new DiscOptions(), Artwork: null, Extras: null, AddOns: []),
            null,
            null,
            [],
            []));

        Assert.Equal(string.Empty, screen.MenuHeading);
        Assert.Null(screen.AsAProject().Options.Look);
    }

    // ------------------------------------------------------ the project file

    [Fact]
    public void A_saved_project_carries_both_and_comes_back_with_them()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.MenuHeading = " GOG Collection ";
        screen.MenuHeadingHidden = true;

        string written = Saved(screen);

        Assert.Contains("\"Heading\": \"GOG Collection\"", written, StringComparison.Ordinal);
        Assert.Contains("\"HeadingHidden\": true", written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(new MenuLook(Heading: "GOG Collection", HeadingHidden: true), back.Options.Look);
    }

    [Fact]
    public void A_project_with_no_heading_says_so_in_the_file()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));

        string written = Saved(screen);

        Assert.Contains("\"Heading\": null", written, StringComparison.Ordinal);
        Assert.Contains("\"HeadingHidden\": false", written, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value.Options.Look);
    }

    [Theory]
    [InlineData("\"Heading\": 7")]
    [InlineData("\"Heading\": true")]
    [InlineData("\"Heading\": \"   \"")]
    [InlineData("\"Heading\": [\"GOG\"]")]
    public void A_heading_that_is_not_words_is_no_heading(string doctored)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.MenuHeading = "GOG Collection";

        string written = Saved(screen).Replace("\"Heading\": \"GOG Collection\"", doctored, StringComparison.Ordinal);

        Assert.Contains(doctored, written, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value.Options.Look);
    }

    [Fact]
    public void A_heading_somebody_wrote_into_the_file_is_tidied_on_the_way_in()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.MenuHeading = "GOG Collection";

        string written = Saved(screen).Replace(
            "\"Heading\": \"GOG Collection\"",
            "\"Heading\": \"  " + new string('Z', 80) + "  \"",
            StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(new string('Z', 60), back.Options.Look?.Heading);
    }

    [Fact]
    public void A_file_saved_before_there_was_a_heading_has_none()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.ListOnTheLeft = true;

        string written = Regex.Replace(
            Saved(screen),
            ",\\s*\"Heading\": null,\\s*\"HeadingHidden\": false",
            string.Empty,
            RegexOptions.CultureInvariant);

        Assert.DoesNotContain("Heading", written, StringComparison.Ordinal);
        Assert.Equal(
            new MenuLook(MenuSide.Left),
            Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value.Options.Look);
    }

    // --------------------------------------------------------------- the page

    [Fact]
    public void A_page_with_nothing_chosen_has_no_heading_of_its_own_and_hides_none()
    {
        string page = MenuWriter.MenuPage(AMenu());

        Assert.Contains("heading: \"\", headingHidden: false, frame: \"window\"},", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_carries_the_heading_and_whether_it_is_hidden()
    {
        string page = MenuWriter.MenuPage(
            AMenu() with { Look = new MenuLook(Heading: "GOG Collection", HeadingHidden: true) });

        Assert.Contains("heading: \"GOG Collection\", headingHidden: true, frame: \"window\"},", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_cannot_end_the_script_it_is_written_into()
    {
        // It is somebody's own words written into the one generated script
        // on the disc, so it goes through the same gate a game's name does.
        string page = MenuWriter.MenuPage(
            AMenu() with { Look = new MenuLook(Heading: "\"; alert(1); </script><b>") });

        Assert.DoesNotContain("heading: \"\"; alert(1);", page, StringComparison.Ordinal);
        Assert.DoesNotContain("</script><b>", page, StringComparison.Ordinal);
        Assert.Contains("heading: \"\\\"; alert(1);", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_with_an_accent_leaves_the_page_plain()
    {
        // The menu has no charset, so anything past ASCII has to be written
        // as its number or mshta reads it in the machine's own codepage.
        string page = MenuWriter.MenuPage(AMenu() with { Look = new MenuLook(Heading: "Pok\u00e9mon") });

        Assert.DoesNotContain('\u00e9', page);
        Assert.Contains("heading: \"Pok\\u00E9mon\"", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_heading_too_long_for_the_menu_is_cut_before_it_reaches_the_disc()
    {
        string page = MenuWriter.MenuPage(AMenu() with { Look = new MenuLook(Heading: "  " + new string('Z', 80)) });

        Assert.Contains("heading: \"" + new string('Z', 60) + "\",", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_heads_the_list_of_games_and_a_disc_of_one_through_the_same_door()
    {
        string page = MenuWriter.MenuPage(AMenu());

        Assert.Contains("    headTheMainMenu(\"discName\", DISC.game);", page, StringComparison.Ordinal);
        Assert.Contains(
            "    if (DISC.games.length < 2) {\r\n        headTheMainMenu(\"gameName\", game.name);".Replace("\r\n", Environment.NewLine, StringComparison.Ordinal),
            page.ReplaceLineEndings(),
            StringComparison.Ordinal);
        Assert.Contains("var says = theMainMenusHeading(DISC.look, otherwise);", page, StringComparison.Ordinal);

        // Nothing sets either heading behind its back.
        Assert.DoesNotContain("getElementById(\"discName\").innerText", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_disc_carries_the_heading_of_its_project()
    {
        RecordingWriter wrote = new();
        GameFolder found = new(@"C:\games\x", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", null, [], [], null);
        PlannedDisc disc = new(1, [], 0);

        await new WriteTheMenuStep().Run(
            new BuildContext(
                new DiscProject("HOLLOW KNIGHT", [found], new DiscOptions(Look: new MenuLook(Heading: "Hallownest"))),
                new DiscLayout([disc], 1_000_000, true),
                disc,
                @"C:\stage",
                wrote),
            CancellationToken.None);

        string page = wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        Assert.Contains("heading: \"Hallownest\", headingHidden: false, frame: \"window\"},", page, StringComparison.Ordinal);

        // The window's own title bar and what This PC shows are the disc's.
        Assert.Contains("<title>Hollow Knight</title>", page, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the dialog

    [Fact]
    public void The_box_is_bound_as_it_is_typed_and_holds_no_more_than_the_menu_is_given()
    {
        System.Xml.Linq.XElement box = Assert.Single(
            Dialog().Descendants(Xaml + "TextBox"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "MenuHeading");

        Assert.Equal("{Binding MenuHeading, UpdateSourceTrigger=PropertyChanged}", (string?)box.Attribute("Text"));
        Assert.Equal(
            MenuLook.LongestHeading.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (string?)box.Attribute("MaxLength"));
        Assert.Equal("{Binding MenuHeadingCanBeTyped}", (string?)box.Attribute("IsEnabled"));
    }

    [Fact]
    public void The_tick_is_bound_to_hiding_it()
    {
        System.Xml.Linq.XElement tick = Assert.Single(
            Dialog().Descendants(Xaml + "CheckBox"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "MenuHeadingHidden");

        Assert.Equal("{Binding MenuHeadingHidden}", (string?)tick.Attribute("IsChecked"));
    }

    private static readonly System.Xml.Linq.XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static System.Xml.Linq.XElement Dialog() =>
        System.Xml.Linq.XDocument.Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

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
