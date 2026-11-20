using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Choosing which part of a picture the menu keeps when it is filled and
/// cropped, asked for on Reddit on 20 November 2026: "the option to select
/// the area that gets cropped for the menu image".
/// </summary>
/// <remarks>
/// The sum, the screen and the project file. What is drawn from it is
/// <see cref="TheRealIconWriter"/>'s, by the pixel. The dragging itself is a
/// pointer over a Border, which no test can reach, and is checked by hand.
/// </remarks>
public class ThePartOfTheBackgroundKept
{
    private const int MenuWide = 760;
    private const int MenuTall = 480;

    // ---------------------------------------------------------------- the sum

    [Fact]
    public void Nothing_chosen_keeps_the_middle_as_every_disc_did_before()
    {
        // A picture twice as wide as the menu's shape: half of it is kept,
        // and a quarter comes off each side.
        Kept part = KeptPart.TheMiddle.In(1520, 480, MenuWide, MenuTall);

        Assert.Equal(new Kept(380, 0, 760, 480), part);
        Assert.True(KeptPart.TheMiddle.IsTheMiddle);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.25, 190)]
    [InlineData(1.0, 760)]
    public void Across_slides_the_frame_from_the_left_edge_to_the_right(double across, double left)
    {
        Kept part = new KeptPart(Across: across).In(1520, 480, MenuWide, MenuTall);

        Assert.Equal(left, part.X, 6);
        Assert.Equal(760, part.Width, 6);
        Assert.Equal(0, part.Y, 6);
        Assert.Equal(480, part.Height, 6);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 480)]
    public void Down_slides_it_from_the_top_to_the_bottom_of_a_tall_picture(double down, double top)
    {
        Kept part = new KeptPart(Down: down).In(760, 960, MenuWide, MenuTall);

        Assert.Equal(top, part.Y, 6);
        Assert.Equal(480, part.Height, 6);
        Assert.Equal(0, part.X, 6);
        Assert.Equal(760, part.Width, 6);
    }

    [Fact]
    public void Zoomed_in_twice_it_keeps_half_as_much_each_way_and_can_move_both_ways()
    {
        // The menu's own shape: at no zoom there is no slack at all.
        Assert.Equal(new Kept(0, 0, 760, 480), new KeptPart(0, 0).In(760, 480, MenuWide, MenuTall));

        Assert.Equal(new Kept(190, 120, 380, 240), new KeptPart(Zoom: 2).In(760, 480, MenuWide, MenuTall));
        Assert.Equal(new Kept(0, 0, 380, 240), new KeptPart(0, 0, 2).In(760, 480, MenuWide, MenuTall));
        Assert.Equal(new Kept(380, 240, 380, 240), new KeptPart(1, 1, 2).In(760, 480, MenuWide, MenuTall));
    }

    [Theory]
    [InlineData(1520, 480)]
    [InlineData(300, 900)]
    [InlineData(64, 64)]
    [InlineData(4000, 3000)]
    public void What_is_kept_is_always_the_menus_shape_and_always_inside_the_picture(int wide, int tall)
    {
        foreach (double zoom in new[] { 1.0, 1.7, 4.0 })
        {
            foreach (double at in new[] { 0.0, 0.33, 1.0 })
            {
                Kept part = new KeptPart(at, 1 - at, zoom).In(wide, tall, MenuWide, MenuTall);

                // Its shape, or the picture would be pulled.
                Assert.Equal((double)MenuWide / MenuTall, part.Width / part.Height, 6);

                // Inside, or the menu's dark would show down one side.
                Assert.True(part.X >= -1e-9 && part.Y >= -1e-9, $"{part} starts outside {wide}x{tall}.");
                Assert.True(part.X + part.Width <= wide + 1e-9, $"{part} runs past the right of {wide}x{tall}.");
                Assert.True(part.Y + part.Height <= tall + 1e-9, $"{part} runs past the bottom of {wide}x{tall}.");
            }
        }
    }

    [Theory]
    [InlineData(-3.0, 9.0, 0.5, 0.0, 1.0, 1.0)]
    [InlineData(0.2, 0.8, 99.0, 0.2, 0.8, 4.0)]
    [InlineData(double.NaN, double.PositiveInfinity, double.NaN, 0.5, 0.5, 1.0)]
    public void Numbers_that_mean_nothing_are_brought_inside_what_they_can_mean(
        double across, double down, double zoom, double tidyAcross, double tidyDown, double tidyZoom)
    {
        Assert.Equal(new KeptPart(tidyAcross, tidyDown, tidyZoom), new KeptPart(across, down, zoom).Tidied());

        // And the sum never trusts that it was tidied first.
        Assert.Equal(
            new KeptPart(tidyAcross, tidyDown, tidyZoom).In(1520, 960, MenuWide, MenuTall),
            new KeptPart(across, down, zoom).In(1520, 960, MenuWide, MenuTall));
    }

    [Fact]
    public void A_picture_with_no_size_keeps_nothing_and_divides_by_nothing()
    {
        Assert.Equal(new Kept(0, 0, 0, 0), new KeptPart(0.3, 0.3, 2).In(0, 0, MenuWide, MenuTall));
        Assert.Equal(new Kept(0, 0, 1, 1), new Kept(0, 0, 0, 0).AsFractionsOf(0, 0));
        Assert.Equal(new KeptPart(0.3, 0.3, 2), new KeptPart(0.3, 0.3, 2).Dragged(0.5, 0.5, 0, 0, MenuWide, MenuTall));
    }

    [Fact]
    public void Dragging_the_picture_right_brings_its_left_into_the_frame()
    {
        // Twice the menu's width: the frame shows 760 of 1520, from 380.
        KeptPart dragged = KeptPart.TheMiddle.Dragged(0.25, 0, 1520, 480, MenuWide, MenuTall);

        // A quarter of the frame is 190 of the picture's pixels, so the
        // frame now starts at 190, a quarter of the way across the slack.
        Assert.Equal(190, dragged.In(1520, 480, MenuWide, MenuTall).X, 6);
        Assert.Equal(0.25, dragged.Across, 6);

        // And left again by the same puts it back.
        Assert.True(dragged.Dragged(-0.25, 0, 1520, 480, MenuWide, MenuTall).IsTheMiddle);
    }

    [Fact]
    public void A_drag_stops_at_the_pictures_edge()
    {
        KeptPart dragged = KeptPart.TheMiddle.Dragged(5, 0, 1520, 480, MenuWide, MenuTall);

        Assert.Equal(0, dragged.Across, 6);
        Assert.Equal(0, dragged.In(1520, 480, MenuWide, MenuTall).X, 6);

        Assert.Equal(1, KeptPart.TheMiddle.Dragged(-5, 0, 1520, 480, MenuWide, MenuTall).Across, 6);
    }

    [Fact]
    public void A_drag_the_way_there_is_no_more_picture_changes_nothing()
    {
        // Wide and exactly as tall as it needs to be: nothing to show up or down.
        KeptPart dragged = new KeptPart(0.3, 0.7).Dragged(0, 0.4, 1520, 480, MenuWide, MenuTall);

        Assert.Equal(new KeptPart(0.3, 0.7), dragged);
    }

    [Fact]
    public void Fractions_of_the_picture_are_what_a_thumbnail_of_any_size_can_show()
    {
        Kept part = new KeptPart(0, 0, 2).In(760, 480, MenuWide, MenuTall).AsFractionsOf(760, 480);

        Assert.Equal(new Kept(0, 0, 0.5, 0.5), part);
        Assert.Equal("0,0,0.5,0.5", part.ToString());
    }

    // ------------------------------------------------------------- the screen

    [Fact]
    public void There_is_a_part_to_choose_only_for_a_picture_filled_and_cropped()
    {
        MakeADisc screen = TheScreen(1520, 480);

        Assert.False(screen.TheBackgroundCanBeFramed);

        screen.Background = @"C:\art\back.png";
        Assert.True(screen.TheBackgroundCanBeFramed);

        foreach (BackgroundFit other in new[] { BackgroundFit.Fit, BackgroundFit.Stretch, BackgroundFit.Tile })
        {
            screen.HowTheBackgroundFits = WaysToFit.For(other);
            Assert.False(screen.TheBackgroundCanBeFramed, $"{other} offered a part to choose.");
        }

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Fill);
        Assert.True(screen.TheBackgroundCanBeFramed);

        screen.BackgroundColourText = "#1B3A4B";
        Assert.False(screen.TheBackgroundCanBeFramed);
    }

    [Fact]
    public void Zooming_and_sliding_reach_the_disc_and_the_frame_shows_the_same_part()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";

        Assert.False(screen.TheBackgroundIsFramed);
        Assert.Equal(new Kept(0.25, 0, 0.5, 1), screen.WhatTheMenuKeeps);

        screen.BackgroundZoom = 2;
        screen.BackgroundAcross = 0;
        screen.BackgroundDown = 1;

        Assert.Equal(new KeptPart(0, 1, 2), screen.AsAProject().Artwork!.Kept);
        Assert.True(screen.TheBackgroundIsFramed);
        Assert.Equal(2, screen.BackgroundZoom);
        Assert.Equal(0, screen.BackgroundAcross);
        Assert.Equal(1, screen.BackgroundDown);

        // The left quarter of the width, the bottom half of the height.
        Assert.Equal(new Kept(0, 0.5, 0.25, 0.5), screen.WhatTheMenuKeeps);
    }

    [Fact]
    public void Dragging_on_the_screen_uses_the_pictures_real_size()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";

        screen.DragTheBackground(0.25, 0);

        Assert.Equal(0.25, screen.BackgroundAcross, 6);
        Assert.Equal(0.125, screen.WhatTheMenuKeeps.X, 6);
    }

    [Fact]
    public void The_picture_is_measured_once_however_far_it_is_dragged()
    {
        PicturesOfOneSize pictures = new(1520, 480);
        MakeADisc screen = TheScreen(pictures);
        screen.Background = @"C:\art\back.png";
        int before = pictures.TimesAsked;

        for (int move = 0; move < 50; move++)
        {
            screen.DragTheBackground(0.001, 0);
            _ = screen.WhatTheMenuKeeps;
        }

        Assert.True(pictures.TimesAsked - before <= 1, $"Asked {pictures.TimesAsked - before} times in one drag.");
    }

    [Fact]
    public void Centre_puts_the_middle_back_and_leaves_nothing_behind()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";
        screen.BackgroundZoom = 3;
        screen.DragTheBackground(0.4, 0.4);

        screen.KeepTheMiddleOfTheBackground();

        Assert.False(screen.TheBackgroundIsFramed);
        Assert.Null(screen.AsAProject().Artwork!.Kept);
        Assert.Equal(1, screen.BackgroundZoom);
    }

    [Fact]
    public void Sliding_back_to_the_middle_by_hand_is_the_middle_too()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";
        screen.BackgroundAcross = 0.1;

        screen.BackgroundAcross = 0.5;

        Assert.Null(screen.AsAProject().Artwork!.Kept);
        Assert.False(screen.TheBackgroundIsFramed);
    }

    [Fact]
    public void Another_picture_starts_in_the_middle_and_the_same_one_chosen_again_does_not()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";
        screen.BackgroundZoom = 2;

        screen.Background = @"C:\art\back.png";
        Assert.Equal(2, screen.BackgroundZoom);

        screen.Background = @"C:\art\another.png";
        Assert.Equal(1, screen.BackgroundZoom);
        Assert.Null(screen.AsAProject().Artwork!.Kept);
    }

    [Fact]
    public void Clearing_the_picture_or_choosing_a_colour_takes_the_part_off()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";
        screen.BackgroundZoom = 2;

        screen.Background = null;
        Assert.Null(screen.AsAProject().Artwork);

        screen.Background = @"C:\art\back.png";
        screen.BackgroundZoom = 2;
        screen.BackgroundColourText = "#1B3A4B";

        Assert.Null(screen.AsAProject().Artwork!.Kept);
    }

    [Fact]
    public void Nothing_is_kept_where_there_is_nothing_to_frame()
    {
        MakeADisc screen = TheScreen(1520, 480);

        // No picture at all.
        screen.BackgroundZoom = 3;
        screen.DragTheBackground(0.5, 0.5);
        Assert.Null(screen.AsAProject().Artwork);

        // A picture, tiled.
        screen.Background = @"C:\art\back.png";
        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Tile);
        screen.BackgroundZoom = 3;
        screen.DragTheBackground(0.5, 0.5);

        Assert.Null(screen.AsAProject().Artwork!.Kept);
    }

    [Fact]
    public void A_part_chosen_waits_while_the_picture_is_fitted_another_way()
    {
        // Somebody trying Fit and coming back should find their framing.
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";
        screen.BackgroundAcross = 0.1;

        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Fit);
        screen.HowTheBackgroundFits = WaysToFit.For(BackgroundFit.Fill);

        Assert.Equal(0.1, screen.BackgroundAcross);
    }

    [Fact]
    public void A_picture_that_cannot_be_read_shows_all_of_itself_and_breaks_nothing()
    {
        MakeADisc screen = TheScreen(new PicturesOfOneSize(0, 0, readable: false));
        screen.Background = @"C:\art\broken.png";

        screen.DragTheBackground(0.3, 0.3);
        screen.BackgroundZoom = 2;

        Assert.Equal(new Kept(0, 0, 1, 1), screen.WhatTheMenuKeeps);
    }

    [Fact]
    public void The_window_hears_about_everything_the_frame_shows()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Background = @"C:\art\back.png";
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.DragTheBackground(0.1, 0);

        foreach (string named in new[]
                 {
                     nameof(MakeADisc.WhatTheMenuKeeps), nameof(MakeADisc.BackgroundAcross),
                     nameof(MakeADisc.BackgroundDown), nameof(MakeADisc.BackgroundZoom),
                     nameof(MakeADisc.TheBackgroundIsFramed), nameof(MakeADisc.TheBackgroundCanBeFramed),
                 })
        {
            Assert.Contains(named, told, StringComparer.Ordinal);
        }
    }

    // -------------------------------------------------------- the copy step

    [Fact]
    public void The_frame_is_the_shape_the_disc_is_drawn_at()
    {
        Assert.Equal(MenuWide, CopyTheArtworkStep.MenuWidth);
        Assert.Equal(MenuTall, CopyTheArtworkStep.MenuHeight);
    }

    // ------------------------------------------------------ the project file

    [Fact]
    public void A_saved_project_carries_the_part_and_comes_back_with_it()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";
        screen.BackgroundZoom = 2.5;
        screen.BackgroundAcross = 0.125;
        screen.BackgroundDown = 1;

        string written = Saved(screen);

        Assert.Contains("\"BgKept\"", written, StringComparison.Ordinal);
        Assert.Contains("\"Across\": 0.125", written, StringComparison.Ordinal);
        Assert.Contains("\"Down\": 1", written, StringComparison.Ordinal);
        Assert.Contains("\"Zoom\": 2.5", written, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        Assert.Equal(new KeptPart(0.125, 1, 2.5), back.Artwork.BackgroundKept);
    }

    [Fact]
    public void A_project_with_the_middle_kept_says_nothing_about_it()
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";

        string written = Saved(screen);

        Assert.DoesNotContain("BgKept", written, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value.Artwork.BackgroundKept);
    }

    [Theory]
    [InlineData("\"BgKept\": { \"Across\": 7, \"Down\": -2, \"Zoom\": 900 }", 1.0, 0.0, 4.0)]
    [InlineData("\"BgKept\": { \"Across\": \"left\", \"Zoom\": 2 }", 0.5, 0.5, 2.0)]
    [InlineData("\"BgKept\": { }", 0.5, 0.5, 1.0)]
    public void Numbers_typed_into_a_file_are_tidied_on_the_way_in(
        string doctored, double across, double down, double zoom)
    {
        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(Doctored(doctored))).Value;

        Assert.Equal(new KeptPart(across, down, zoom), back.Artwork.BackgroundKept);
    }

    [Theory]
    [InlineData("\"BgKept\": \"the left\"")]
    [InlineData("\"BgKept\": 3")]
    [InlineData("\"BgKept\": null")]
    public void Something_that_is_not_three_numbers_is_the_middle(string doctored)
    {
        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(Doctored(doctored))).Value;

        Assert.Null(back.Artwork.BackgroundKept);
    }

    [Fact]
    public void An_opened_project_shows_its_part_in_the_frame()
    {
        MakeADisc screen = TheScreen(1520, 480);

        screen.OpenThis(new Reopened(
            new DiscProject(
                "OPENED",
                [AGame("Hollow Knight")],
                new DiscOptions(),
                Artwork: new DiscArtwork(BackgroundPath: @"C:\art\back.png").Keeping(new KeptPart(0, 0.5, 2)),
                Extras: null,
                AddOns: []),
            null,
            null,
            [],
            []));

        Assert.True(screen.TheBackgroundIsFramed);
        Assert.Equal(2, screen.BackgroundZoom);
        Assert.Equal(new Kept(0, 0.25, 0.25, 0.5), screen.WhatTheMenuKeeps);
    }

    // ------------------------------------------------------------- the dialog

    [Fact]
    public void The_dialog_has_a_frame_a_slider_and_a_way_back_to_the_middle()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        System.Xml.Linq.XElement brush = Assert.Single(dialog.Descendants(xaml + "ImageBrush"));

        Assert.Equal("{Binding WhatTheMenuKeeps, Converter={StaticResource AsARect}}", (string?)brush.Attribute("Viewbox"));
        Assert.Equal("RelativeToBoundingBox", (string?)brush.Attribute("ViewboxUnits"));

        // Fill, because the part kept is the frame's shape already. Anything
        // else would crop it a second time.
        Assert.Equal("Fill", (string?)brush.Attribute("Stretch"));

        System.Xml.Linq.XElement slider = Assert.Single(
            dialog.Descendants(xaml + "Slider"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "BackgroundZoom");

        Assert.Equal("{Binding BackgroundZoom}", (string?)slider.Attribute("Value"));
        Assert.Equal("1", (string?)slider.Attribute("Minimum"));
        Assert.Equal("{x:Static core:KeptPart.MostZoom}", (string?)slider.Attribute("Maximum"));
        Assert.False(string.IsNullOrWhiteSpace((string?)slider.Attribute("AutomationProperties.Name")));

        System.Xml.Linq.XElement centre = Assert.Single(
            dialog.Descendants(xaml + "Button"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "CentreTheBackground");

        Assert.Equal("{Binding TheBackgroundIsFramed}", (string?)centre.Attribute("IsEnabled"));

        // The frame's shape is the menu's, a quarter each way, inside a
        // margin of 2 and an edge of 1 on every side.
        System.Xml.Linq.XElement holder = brush.Ancestors(xaml + "Border").Last();

        Assert.Equal((MenuWide / 4) + 6, (int)holder.Attribute("Width")!);
        Assert.Equal((MenuTall / 4) + 6, (int)holder.Attribute("Height")!);
    }

    // ---------------------------------------------------------------- helpers

    private static string Saved(MakeADisc screen) =>
        TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "DVD5", @"C:\out", DateTimeOffset.UtcNow);

    /// <summary>A real saved project with one more thing written into it.</summary>
    private static string Doctored(string with)
    {
        MakeADisc screen = TheScreen(1520, 480);
        screen.Add(AGame("Hollow Knight"));
        screen.Background = @"C:\art\back.png";

        string written = Saved(screen).TrimEnd();

        Assert.EndsWith("}", written, StringComparison.Ordinal);

        return written[..^1].TrimEnd() + ",\n  " + with + "\n}";
    }

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(
            @"C:\games\somewhere", GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    private static MakeADisc TheScreen(int wide, int tall) => TheScreen(new PicturesOfOneSize(wide, tall));

    private static MakeADisc TheScreen(PicturesOfOneSize pictures) =>
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
            pictures,
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

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
}
