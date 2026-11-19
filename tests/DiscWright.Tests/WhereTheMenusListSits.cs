using System.Text.RegularExpressions;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The menu's list on the left or the right, and the small buttons in the
/// corner away from it, discwright#98, 16 November: "could DW2 give the
/// option of positioning the main menu on the left or right? That way you
/// can avoid overlapping logos embedded on one side of a wallpaper".
/// </summary>
/// <remarks>
/// Where each button lands on the page is arithmetic in menu.js, and
/// <see cref="TheMenuScript"/> holds that under cscript. This is everything
/// on the way there: the two boxes, the sentence under them, the project
/// file, and the page.
/// </remarks>
public class WhereTheMenusListSits
{
    [Fact]
    public void A_disc_nobody_touched_has_no_look_at_all()
    {
        MakeADisc screen = TheScreen();

        Assert.False(screen.ListOnTheLeft);
        Assert.False(screen.CornerAtTheBottom);
        Assert.Null(screen.AsAProject().Options.Look);
        Assert.Equal(new DiscOptions(), screen.AsAProject().Options);
    }

    [Fact]
    public void Ticking_the_box_puts_the_list_on_the_left_of_the_disc()
    {
        MakeADisc screen = TheScreen();

        screen.ListOnTheLeft = true;

        Assert.Equal(MenuSide.Left, screen.AsAProject().Options.Look?.ListOn);
        Assert.False(screen.AsAProject().Options.Look?.CornerAtTheBottom);
    }

    [Fact]
    public void Ticking_the_other_moves_the_small_buttons_down_and_nothing_else()
    {
        MakeADisc screen = TheScreen();

        screen.CornerAtTheBottom = true;

        Assert.True(screen.AsAProject().Options.Look?.CornerAtTheBottom);
        Assert.Equal(MenuSide.Right, screen.AsAProject().Options.Look?.ListOn);
    }

    [Fact]
    public void Unticking_both_again_is_a_disc_nobody_touched()
    {
        // Not a plain look spelled out: a project that was asked and then
        // put back has to equal one that was never asked, or opening and
        // saving a file would be a change.
        MakeADisc screen = TheScreen();
        screen.ListOnTheLeft = true;
        screen.CornerAtTheBottom = true;

        screen.ListOnTheLeft = false;
        screen.CornerAtTheBottom = false;

        Assert.Null(screen.AsAProject().Options.Look);
    }

    [Theory]
    [InlineData(false, false, "The small buttons, music and the disc check among them, sit top left, away from the list.")]
    [InlineData(true, false, "The small buttons, music and the disc check among them, sit top right, away from the list.")]
    [InlineData(false, true, "The small buttons, music and the disc check among them, sit bottom left, away from the list.")]
    [InlineData(true, true, "The small buttons, music and the disc check among them, sit bottom right, away from the list.")]
    public void The_line_underneath_says_which_corner_that_leaves(bool left, bool bottom, string says)
    {
        MakeADisc screen = TheScreen();

        screen.ListOnTheLeft = left;
        screen.CornerAtTheBottom = bottom;

        Assert.Equal(says, screen.WhereTheCornerButtonsSit);
    }

    [Fact]
    public void The_window_hears_about_each_box_and_about_the_line()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.ListOnTheLeft = true;

        Assert.Contains(nameof(MakeADisc.ListOnTheLeft), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhereTheCornerButtonsSit), told, StringComparer.Ordinal);

        told.Clear();
        screen.CornerAtTheBottom = true;

        Assert.Contains(nameof(MakeADisc.CornerAtTheBottom), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhereTheCornerButtonsSit), told, StringComparer.Ordinal);
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
                new DiscOptions(Look: new MenuLook(MenuSide.Left, CornerAtTheBottom: true)),
                Artwork: null,
                Extras: null,
                AddOns: []),
            null,
            null,
            [],
            []));

        Assert.True(screen.ListOnTheLeft);
        Assert.True(screen.CornerAtTheBottom);

        // The dialog reads each once and then believes what it read.
        Assert.Contains(nameof(MakeADisc.ListOnTheLeft), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.CornerAtTheBottom), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhereTheCornerButtonsSit), told, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("CheckBox", "ListOnTheLeft", "IsChecked")]
    [InlineData("CheckBox", "CornerAtTheBottom", "IsChecked")]
    [InlineData("CheckBox", "DiscsOwnAsSmallButtons", "IsChecked")]
    [InlineData("CheckBox", "OpenTheDiscButton", "IsChecked")]
    [InlineData("TextBlock", "WhereTheCornerButtonsSit", "Text")]
    public void The_dialog_has_each_of_them_bound_to_what_it_is_named_after(
        string kind, string named, string through)
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        System.Xml.Linq.XElement control = Assert.Single(
            dialog.Descendants(xaml + kind),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == named);

        Assert.Equal($"{{Binding {named}}}", (string?)control.Attribute(through));
    }

    // ------------------------------------------------------ the project file

    [Theory]
    [InlineData(false, false, "\"ListOn\": \"Right\"", "\"Corner\": \"Top\"")]
    [InlineData(true, false, "\"ListOn\": \"Left\"", "\"Corner\": \"Top\"")]
    [InlineData(false, true, "\"ListOn\": \"Right\"", "\"Corner\": \"Bottom\"")]
    [InlineData(true, true, "\"ListOn\": \"Left\"", "\"Corner\": \"Bottom\"")]
    public void A_saved_project_says_where_and_comes_back_saying_it(
        bool left, bool bottom, string side, string corner)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.ListOnTheLeft = left;
        screen.CornerAtTheBottom = bottom;

        string written = Saved(screen);

        Assert.Contains(side, written, StringComparison.Ordinal);
        Assert.Contains(corner, written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(left ? MenuSide.Left : MenuSide.Right, back.Options.LookOrPlain.ListOn);
        Assert.Equal(bottom, back.Options.LookOrPlain.CornerAtTheBottom);
        Assert.Equal(screen.AsAProject().Options, back.Options with { Menu = screen.AsAProject().Options.Menu });
    }

    [Fact]
    public void It_is_never_saved_under_a_name_the_PowerShell_app_writes()
    {
        // PanelSide is one of the keys this program tells the other one's
        // files by, and refuses. A file of its own carrying it would be
        // turned away by the program that wrote it.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.ListOnTheLeft = true;

        string written = Saved(screen);

        Assert.DoesNotContain("PanelSide", written, StringComparison.Ordinal);
        Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written));
    }

    [Fact]
    public void A_file_from_before_there_was_a_choice_is_the_menu_as_it_comes()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));

        string written = Regex.Replace(
            Saved(screen), ",\\s*\"MenuLook\": \\{[^}]*\\}", string.Empty, RegexOptions.CultureInvariant);

        Assert.DoesNotContain("MenuLook", written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Null(back.Options.Look);
    }

    [Theory]
    [InlineData("\"MenuLook\": \"Left\"")]
    [InlineData("\"MenuLook\": null")]
    [InlineData("\"MenuLook\": 3")]
    [InlineData("\"MenuLook\": {}")]
    [InlineData("\"MenuLook\": { \"ListOn\": \"Middle\", \"Corner\": \"Sideways\" }")]
    [InlineData("\"MenuLook\": { \"ListOn\": 1, \"Corner\": true }")]
    public void A_file_edited_into_nonsense_picks_no_side(string nonsense)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.ListOnTheLeft = true;
        screen.CornerAtTheBottom = true;

        string written = Regex.Replace(
            Saved(screen), "\"MenuLook\": \\{[^}]*\\}", nonsense, RegexOptions.CultureInvariant);

        Assert.Contains(nonsense, written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Null(back.Options.Look);
    }

    [Fact]
    public void The_words_are_read_whatever_their_capitals()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.ListOnTheLeft = true;
        screen.CornerAtTheBottom = true;

        string written = Saved(screen)
            .Replace("\"Left\"", "\"left\"", StringComparison.Ordinal)
            .Replace("\"Bottom\"", "\"BOTTOM\"", StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(new MenuLook(MenuSide.Left, CornerAtTheBottom: true), back.Options.Look);
    }

    // --------------------------------------------------------------- the page

    [Fact]
    public void A_page_with_nothing_chosen_says_right_and_top()
    {
        string page = MenuWriter.MenuPage(AMenu());

        Assert.Contains("look: {listOn: \"right\", corner: \"top\", discsOwn: \"list\", openTheDisc: false},", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MenuSide.Left, false, "look: {listOn: \"left\", corner: \"top\", discsOwn: \"list\", openTheDisc: false},")]
    [InlineData(MenuSide.Right, true, "look: {listOn: \"right\", corner: \"bottom\", discsOwn: \"list\", openTheDisc: false},")]
    [InlineData(MenuSide.Left, true, "look: {listOn: \"left\", corner: \"bottom\", discsOwn: \"list\", openTheDisc: false},")]
    public void A_page_carries_the_look_that_was_chosen(MenuSide side, bool bottom, string carried)
    {
        string page = MenuWriter.MenuPage(AMenu() with { Look = new MenuLook(side, bottom) });

        Assert.Contains(carried, page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_moves_the_list_before_it_draws_and_places_the_buttons_after_they_show()
    {
        // What the page does when it opens, and nothing above it: the
        // functions it calls are written out before it.
        string page = MenuWriter.MenuPage(AMenu());
        page = page[page.IndexOf("function start() {", StringComparison.Ordinal)..];

        int side = page.IndexOf("    putTheListOnItsSide();", StringComparison.Ordinal);
        int music = page.IndexOf("    startTheMusic();", StringComparison.Ordinal);
        int check = page.IndexOf("    showTheCheck();", StringComparison.Ordinal);
        int placed = page.IndexOf("    placeTheCornerButtons();", StringComparison.Ordinal);
        int drawn = page.IndexOf("    if (whichScreenToOpenOn(", StringComparison.Ordinal);

        // A button placed before it is shown is not counted, and the corner
        // is left with a gap or with the two on top of each other.
        Assert.True(side > 0 && side < drawn, "The list is moved before a screen is drawn.");
        Assert.True(music > 0 && music < placed, "The music button is shown before it is placed.");
        Assert.True(check > 0 && check < placed, "The check button is shown before it is placed.");
    }

    [Fact]
    public void The_page_asks_the_script_where_and_decides_nothing_itself()
    {
        string page = MenuWriter.MenuPage(AMenu());

        Assert.Contains("if (!listIsOnTheLeft(DISC.look)) {", page, StringComparison.Ordinal);
        Assert.Contains("panel.style.right = \"auto\";", page, StringComparison.Ordinal);
        Assert.Contains("panel.style.left = \"0px\";", page, StringComparison.Ordinal);
        Assert.Contains(
            "at = whereACornerButtonGoes(DISC.look, i, showing.length);", page, StringComparison.Ordinal);
        Assert.Contains("showing[i].style.left = at.left + \"px\";", page, StringComparison.Ordinal);
        Assert.Contains("showing[i].style.top = at.top + \"px\";", page, StringComparison.Ordinal);
        Assert.Contains(
            "var CORNER_BUTTONS = [\"mute\", \"sums\", \"discManual\", \"discExtras\", \"openDisc\"];",
            page,
            StringComparison.Ordinal);

        // The check used to settle its own place from whether the music was
        // showing, which is a second opinion about where the corner is.
        Assert.DoesNotContain("\"52px\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_small_button_is_nearly_solid_behind_so_it_reads_on_a_white_picture()
    {
        // discwright#98, 15 November, the banner screenshot: on a white
        // picture the button was a faint square with nothing in it.
        string page = MenuWriter.MenuPage(AMenu());

        double panel = HowSolid(page, "#panel");
        double behind = HowSolid(page, ".corner");

        // On plain white, ink of #CBD6DB wants a box at least this solid
        // behind it to be read at a glance: 0.58, the panel's own, comes
        // out a mid grey there.
        Assert.True(
            behind >= 0.8 && behind > panel,
            $"A small button is {behind} solid behind its ink and the panel is {panel}.");
    }

    [Theory]
    [InlineData("mute")]
    [InlineData("sums")]
    [InlineData("discManual")]
    [InlineData("discExtras")]
    [InlineData("openDisc")]
    public void Every_small_button_wears_that_one_rule_and_starts_hidden(string button)
    {
        string page = MenuWriter.MenuPage(AMenu());

        Match tag = Regex.Match(
            page, "<div id=\"" + button + "\" class=\"corner( wing)?\" style=\"display: none\" title=\"[^\"]+\"",
            RegexOptions.CultureInvariant);

        Assert.True(tag.Success, $"#{button} is not a hidden small button that says what it is.");

        // And none has a rule of its own that could put it somewhere else.
        Assert.DoesNotContain("#" + button + " {", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_disc_carries_the_look_of_its_project()
    {
        RecordingWriter wrote = new();
        GameFolder found = new(@"C:\games\x", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", null, [], [], null);
        PlannedDisc disc = new(1, [], 0);

        await new WriteTheMenuStep().Run(
            new BuildContext(
                new DiscProject(
                    "HOLLOW KNIGHT",
                    [found],
                    new DiscOptions(Look: new MenuLook(MenuSide.Left, CornerAtTheBottom: true))),
                new DiscLayout([disc], 1_000_000, true),
                disc,
                @"C:\stage",
                wrote),
            CancellationToken.None);

        string page = wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        Assert.Contains("look: {listOn: \"left\", corner: \"bottom\", discsOwn: \"list\", openTheDisc: false},", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// How solid the colour behind one thing on the page is, from its own
    /// rule: the last see-through background it names.
    /// </summary>
    private static double HowSolid(string page, string id)
    {
        Match rule = Regex.Match(
            page,
            "^" + Regex.Escape(id) + @" \{[^}]*\}",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        Assert.True(rule.Success, $"The page has no rule for {id}.");

        Match alpha = Regex.Match(
            rule.Value,
            @"background: rgba\(\s*\d+,\s*\d+,\s*\d+,\s*([0-9.]+)\)",
            RegexOptions.RightToLeft | RegexOptions.CultureInvariant);

        Assert.True(alpha.Success, $"{id} names no see-through background.");

        return double.Parse(alpha.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
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
