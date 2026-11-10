using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// A solid colour behind the menu instead of a picture, discwright#98: "a
/// solid color option by choosing a hex value".
/// </summary>
/// <remarks>
/// No picture and no colour is itself a look, the menu's own dark stage, so
/// this is strictly a third choice. A picture and a colour can never both
/// be chosen, and nothing that is not exactly a colour may reach the disc's
/// own markup, however it got into a project file.
/// </remarks>
public class AColourBehindTheMenu
{
    [Theory]
    [InlineData("1B3A4B", "#1b3a4b")]
    [InlineData("#1b3a4b", "#1b3a4b")]
    [InlineData("  #1B3a4B  ", "#1b3a4b")]
    [InlineData("FFFFFF", "#ffffff")]
    public void A_colour_is_six_hex_figures_kept_lowercase_with_its_hash_on(string typed, string kept) =>
        Assert.Equal(kept, SolidColour.Tidied(typed));

    [Theory]
    [InlineData("abc")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("12345g")]
    [InlineData("##aabbcc")]
    [InlineData("teal")]
    [InlineData("#aabbcc00")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_colour(string? typed)
    {
        Assert.Null(SolidColour.Tidied(typed));
        Assert.False(SolidColour.Fits(typed));
    }

    [Fact]
    public void Typing_a_colour_dresses_the_disc_and_takes_the_picture_off()
    {
        MakeADisc screen = TheScreen();
        screen.Background = @"C:\art\back.png";

        screen.BackgroundColourText = "1B3A4B";

        Assert.Equal("#1b3a4b", screen.BackgroundColour);
        Assert.Null(screen.Background);
        Assert.Equal("#1b3a4b", screen.AsAProject().Artwork?.BackgroundColour);
    }

    [Fact]
    public void Choosing_a_picture_takes_the_colour_off_and_empties_the_box()
    {
        MakeADisc screen = TheScreen();
        screen.BackgroundColourText = "#1B3A4B";

        screen.Background = @"C:\art\back.png";

        Assert.Null(screen.BackgroundColour);
        Assert.Equal(string.Empty, screen.BackgroundColourText);
        Assert.Equal(@"C:\art\back.png", screen.Background);
    }

    [Fact]
    public void Half_typed_text_says_so_and_dresses_nothing()
    {
        MakeADisc screen = TheScreen();

        screen.BackgroundColourText = "1B3A";

        Assert.Null(screen.BackgroundColour);
        Assert.NotNull(screen.WhatTheColourSays);

        screen.BackgroundColourText = "1B3A4B";

        Assert.Null(screen.WhatTheColourSays);
        Assert.Equal("#1b3a4b", screen.BackgroundColour);
    }

    [Fact]
    public void Every_colour_offered_is_a_colour()
    {
        MakeADisc screen = TheScreen();

        Assert.True(screen.TheColoursOffered.Count >= 4, "a swatch row of fewer than four is a poor offer");
        Assert.Equal(screen.TheColoursOffered.Count, screen.TheColoursOffered.Distinct(StringComparer.Ordinal).Count());

        foreach (string offered in screen.TheColoursOffered)
        {
            Assert.Equal(offered, SolidColour.Tidied(offered));
        }
    }

    [Fact]
    public void The_window_hears_about_the_colour_when_it_changes()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.BackgroundColourText = "#1B3A4B";

        Assert.Contains(nameof(MakeADisc.BackgroundColour), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.BackgroundColourText), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhatTheColourSays), told, StringComparer.Ordinal);
    }

    [Fact]
    public void The_menu_wears_the_colour_as_its_backdrop()
    {
        string page = MenuWriter.MenuPage(AMenu() with { BackdropColour = "#1b3a4b" });

        Assert.Contains("background: #1b3a4b;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picture_wins_when_a_file_somebody_edited_carries_both()
    {
        string page = MenuWriter.MenuPage(
            AMenu() with { BackdropFile = "background.png", BackdropColour = "#1b3a4b" });

        Assert.Contains("background-image", page, StringComparison.Ordinal);
        Assert.DoesNotContain("background: #1b3a4b;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_that_is_not_a_colour_reaches_the_pages_own_markup()
    {
        // The colour is written into a style rule on the disc's page, so
        // this is the gate that keeps a hand edited project file from
        // riding into the menu's markup.
        string page = MenuWriter.MenuPage(
            AMenu() with { BackdropColour = "red; } body { background: url(evil)" });

        Assert.DoesNotContain("evil", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_project_carries_the_colour_and_a_doctored_one_cannot()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.BackgroundColourText = "#1B3A4B";

        string written = TheProjectAsJson.Of(
            screen.AsAProject(), "2.0.0", "DVD5", @"C:\out", DateTimeOffset.UtcNow);

        Assert.Contains("\"BgColour\": \"#1b3a4b\"", written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;
        Assert.Equal("#1b3a4b", back.Artwork.BackgroundColour);

        // And a file edited into nonsense yields nothing rather than a side.
        string doctored = written.Replace("#1b3a4b", "url(evil)", StringComparison.Ordinal);
        OpenedProject refusedQuietly = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(doctored)).Value;
        Assert.Null(refusedQuietly.Artwork.BackgroundColour);
    }

    [Fact]
    public void An_opened_project_fills_the_box_so_saving_again_keeps_it()
    {
        MakeADisc screen = TheScreen();

        screen.OpenThis(new Reopened(
            new DiscProject(
                "OPENED",
                [AGame("Hollow Knight")],
                new DiscOptions(Menu: true, Checksums: false, LegacyFileSystems: false, CutFiles: false),
                Artwork: new DiscArtwork(BackgroundColour: "#1b3a4b"),
                Extras: null,
                AddOns: []),
            null,
            null,
            [],
            []));

        Assert.Equal("#1b3a4b", screen.BackgroundColourText);
        Assert.Equal("#1b3a4b", screen.BackgroundColour);
    }

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
