using System.Text.RegularExpressions;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The disc's own manual and extras as small buttons in the corner of the
/// main menu, and a small button that opens the disc, discwright#98, 12 and
/// 16 November: "they would only show on the main menu as icons, and be
/// completely separate from any game menus", and "an icon in the corner of
/// any disc menu to show the related explorer directory (optional)".
/// </summary>
/// <remarks>
/// Which screen each shows on and what each opens is menu.js's and the
/// page's, held in <see cref="TheMenuScript"/> and
/// <see cref="ThePageOnADiscOfASet"/>. This is the way there.
/// </remarks>
public class TheDiscsOwnInTheCorner
{
    [Fact]
    public void Neither_is_on_until_somebody_ticks_it()
    {
        MakeADisc screen = TheScreen();

        Assert.False(screen.DiscsOwnAsSmallButtons);
        Assert.False(screen.OpenTheDiscButton);
        Assert.Null(screen.AsAProject().Options.Look);
    }

    [Fact]
    public void Each_box_changes_its_own_thing_and_no_other()
    {
        MakeADisc screen = TheScreen();

        screen.DiscsOwnAsSmallButtons = true;

        Assert.Equal(new MenuLook(DiscsOwnAsSmallButtons: true), screen.AsAProject().Options.Look);

        screen.DiscsOwnAsSmallButtons = false;
        screen.OpenTheDiscButton = true;

        Assert.Equal(new MenuLook(OpenTheDiscButton: true), screen.AsAProject().Options.Look);

        screen.OpenTheDiscButton = false;

        Assert.Null(screen.AsAProject().Options.Look);
    }

    [Fact]
    public void They_keep_whatever_side_and_corner_were_chosen()
    {
        MakeADisc screen = TheScreen();
        screen.ListOnTheLeft = true;
        screen.CornerAtTheBottom = true;

        screen.DiscsOwnAsSmallButtons = true;
        screen.OpenTheDiscButton = true;

        Assert.Equal(new MenuLook(MenuSide.Left, true, true, true), screen.AsAProject().Options.Look);
    }

    [Fact]
    public void The_window_hears_about_each()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.DiscsOwnAsSmallButtons = true;
        screen.OpenTheDiscButton = true;

        Assert.Contains(nameof(MakeADisc.DiscsOwnAsSmallButtons), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.OpenTheDiscButton), told, StringComparer.Ordinal);
    }

    [Fact]
    public void An_opened_project_ticks_the_boxes_it_was_saved_with()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.OpenThis(new Reopened(
            new DiscProject(
                "OPENED",
                [AGame("Hollow Knight")],
                new DiscOptions(Look: new MenuLook(DiscsOwnAsSmallButtons: true, OpenTheDiscButton: true)),
                Artwork: null,
                Extras: null,
                AddOns: []),
            null,
            null,
            [],
            []));

        Assert.True(screen.DiscsOwnAsSmallButtons);
        Assert.True(screen.OpenTheDiscButton);
        Assert.Contains(nameof(MakeADisc.DiscsOwnAsSmallButtons), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.OpenTheDiscButton), told, StringComparer.Ordinal);
    }

    // ------------------------------------------------------ the project file

    [Theory]
    [InlineData(false, false, "\"DiscsOwn\": \"InTheList\"", "\"OpenTheDisc\": false")]
    [InlineData(true, false, "\"DiscsOwn\": \"SmallButtons\"", "\"OpenTheDisc\": false")]
    [InlineData(false, true, "\"DiscsOwn\": \"InTheList\"", "\"OpenTheDisc\": true")]
    [InlineData(true, true, "\"DiscsOwn\": \"SmallButtons\"", "\"OpenTheDisc\": true")]
    public void A_saved_project_says_so_and_comes_back_saying_it(bool small, bool open, string first, string second)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.DiscsOwnAsSmallButtons = small;
        screen.OpenTheDiscButton = open;

        string written = Saved(screen);

        Assert.Contains(first, written, StringComparison.Ordinal);
        Assert.Contains(second, written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(small, back.Options.LookOrPlain.DiscsOwnAsSmallButtons);
        Assert.Equal(open, back.Options.LookOrPlain.OpenTheDiscButton);
        Assert.Equal(screen.AsAProject().Options.Look, back.Options.Look);
    }

    [Theory]
    [InlineData("\"DiscsOwn\": \"SmallButtons\"", "\"DiscsOwn\": \"Icons\"")]
    [InlineData("\"DiscsOwn\": \"SmallButtons\"", "\"DiscsOwn\": true")]
    [InlineData("\"OpenTheDisc\": true", "\"OpenTheDisc\": \"true\"")]
    [InlineData("\"OpenTheDisc\": true", "\"OpenTheDisc\": 1")]
    [InlineData("\"OpenTheDisc\": true", "\"OpenTheDisc\": null")]
    public void A_word_this_program_never_wrote_turns_nothing_on(string wrote, string doctored)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.DiscsOwnAsSmallButtons = true;
        screen.OpenTheDiscButton = true;

        string written = Saved(screen);
        Assert.Contains(wrote, written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(
            AProjectFile.Read(written.Replace(wrote, doctored, StringComparison.Ordinal))).Value;

        // The one that was doctored is off, and the other is as it was.
        bool itWasTheFirst = wrote.Contains("DiscsOwn", StringComparison.Ordinal);

        Assert.Equal(!itWasTheFirst, back.Options.LookOrPlain.DiscsOwnAsSmallButtons);
        Assert.Equal(itWasTheFirst, back.Options.LookOrPlain.OpenTheDiscButton);
    }

    [Fact]
    public void A_file_saved_before_these_two_existed_has_neither()
    {
        // 2.0.5's first look had the side and the corner alone.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.ListOnTheLeft = true;

        string written = Regex.Replace(
            Saved(screen),
            ",\\s*\"DiscsOwn\": \"InTheList\",\\s*\"OpenTheDisc\": false",
            string.Empty,
            RegexOptions.CultureInvariant);

        Assert.DoesNotContain("DiscsOwn", written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(new MenuLook(MenuSide.Left), back.Options.Look);
    }

    // --------------------------------------------------------------- the page

    [Theory]
    [InlineData(false, false, "discsOwn: \"list\", openTheDisc: false},")]
    [InlineData(true, false, "discsOwn: \"small\", openTheDisc: false},")]
    [InlineData(false, true, "discsOwn: \"list\", openTheDisc: true},")]
    [InlineData(true, true, "discsOwn: \"small\", openTheDisc: true},")]
    public void The_page_carries_both(bool small, bool open, string carried)
    {
        string page = MenuWriter.MenuPage(AMenu() with { Look = new MenuLook(DiscsOwnAsSmallButtons: small, OpenTheDiscButton: open) });

        Assert.Contains(carried, page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("discManual", "openTheDiscsManual();", "The disc's manual")]
    [InlineData("discExtras", "openTheDiscsExtras();", "The disc's extras")]
    [InlineData("openDisc", "openTheDiscItself();", "Open the disc in Explorer")]
    public void Each_small_button_does_one_thing_and_says_what_when_hovered(string button, string does, string says)
    {
        string page = MenuWriter.MenuPage(AMenu());

        Match tag = Regex.Match(page, "<div id=\"" + button + "\"[^>]*>", RegexOptions.CultureInvariant);

        Assert.True(tag.Success);
        Assert.Contains("title=\"" + says + "\"", tag.Value, StringComparison.Ordinal);
        Assert.Contains("onclick=\"" + does + " return false;\"", tag.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Their_pictures_come_from_a_font_every_Windows_has()
    {
        // No picture file on the disc and no font newer than the machine:
        // Wingdings has shipped since Windows 3.1.
        string page = MenuWriter.MenuPage(AMenu());

        Assert.Contains(".corner.wing { font-family: Wingdings;", page, StringComparison.Ordinal);
        Assert.Matches("<div id=\"discManual\" class=\"corner wing\"[^>]*>&amp;</div>", page);
        Assert.Matches("<div id=\"discExtras\" class=\"corner wing\"[^>]*>3</div>", page);
        Assert.Matches("<div id=\"openDisc\" class=\"corner wing\"[^>]*>1</div>", page);
    }

    [Fact]
    public void The_way_into_the_disc_is_shown_before_the_corner_is_laid_out()
    {
        string page = MenuWriter.MenuPage(AMenu());
        page = page[page.IndexOf("function start() {", StringComparison.Ordinal)..];

        int shown = page.IndexOf("    showTheWayIntoTheDisc();", StringComparison.Ordinal);
        int placed = page.IndexOf("    placeTheCornerButtons();", StringComparison.Ordinal);

        Assert.True(shown > 0 && shown < placed);
    }

    // ---------------------------------------------------------- a built disc

    [Theory]
    [InlineData(false, "Install,Manual,Extras,Exit")]
    [InlineData(true, "Install,Exit")]
    public async Task A_games_screen_stops_counting_the_discs_own_once_they_are_small_buttons(
        bool small, string buttons)
    {
        RecordingWriter wrote = new();
        GameFolder found = new(@"C:\games\x", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", null, [], [], null);
        PlannedDisc disc = new(1, [], 0);

        await new WriteTheMenuStep().Run(
            new BuildContext(
                new DiscProject(
                    "HOLLOW KNIGHT",
                    [found],
                    new DiscOptions(Look: new MenuLook(DiscsOwnAsSmallButtons: small).OrNothingWhenPlain()))
                {
                    Extras = new DiscExtras(ManualPath: @"C:\stuff\manual.pdf", ExtrasFolderPath: @"C:\stuff\goodies")
                },
                new DiscLayout([disc], 1_000_000, true),
                disc,
                @"C:\stage",
                wrote),
            CancellationToken.None);

        string page = wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        Match list = Regex.Match(page, "^    buttons: \\[([^\\]]*)\\],", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        Assert.True(list.Success);
        Assert.Equal(buttons, list.Groups[1].Value.Replace("\"", string.Empty, StringComparison.Ordinal).Replace(", ", ",", StringComparison.Ordinal));

        // Where they are is named either way: the small buttons open them.
        Assert.Contains("    manual: \"Extras/manual.pdf\",", page, StringComparison.Ordinal);
        Assert.Contains("    extras: \"Extras\",", page, StringComparison.Ordinal);
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

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }
}
