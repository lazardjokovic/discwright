using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The blank discs somebody has, discwright#98, the first thing asked for in
/// it: "you tell the application how many empty cd-r, dvd, dvd-9, bluray,
/// bluray dual or bluray 100gb discs you have in total, so that a match
/// relating to what you have is given which wastes less space. If a game is
/// 800MB, for the app to suggest 2 x CD if you have them available instead
/// of 1 x DVD."
/// </summary>
/// <remarks>
/// Settled with the owner on 2026-11-20: the size is suggested out of what
/// is owned, and a blank comes off the count when a disc is burned, never
/// when one is only built. One set of mixed sizes is not attempted. A burn
/// taking one off is <see cref="BurningOneDiscAndWatchingIt"/>'s.
/// </remarks>
public class TheBlanksSomebodyHas
{
    private const long Mb = 1024 * 1024;
    private const long Gb = 1024 * Mb;

    // --------------------------------------------------------------- the count

    [Fact]
    public void Nobody_has_any_until_they_say()
    {
        Assert.False(BlanksOwned.None.IsAnything);
        Assert.All(Media.All, size => Assert.Equal(0, BlanksOwned.None.Of(size)));
        Assert.Equal(string.Empty, BlanksOwned.None.AsText());
    }

    [Fact]
    public void A_count_is_kept_for_each_size_and_changing_one_leaves_the_rest()
    {
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 10).With(Media.Dvd5, 25).With(Media.Cd, 7);

        Assert.True(owned.IsAnything);
        Assert.Equal(7, owned.Of(Media.Cd));
        Assert.Equal(25, owned.Of(Media.Dvd5));
        Assert.Equal(0, owned.Of(Media.Bd25));

        // The one it was made from is as it was.
        Assert.False(BlanksOwned.None.IsAnything);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(9999, 9999)]
    [InlineData(123456, 9999)]
    public void A_count_is_never_less_than_none_or_more_than_anybody_has(int said, int kept) =>
        Assert.Equal(kept, BlanksOwned.None.With(Media.Cd, said).Of(Media.Cd));

    [Fact]
    public void None_of_a_size_is_the_same_as_never_having_said()
    {
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 3).With(Media.Cd, 0);

        Assert.False(owned.IsAnything);
        Assert.Equal(string.Empty, owned.AsText());
    }

    [Fact]
    public void Spending_takes_them_off_and_stops_at_none()
    {
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 2).With(Media.Dvd5, 5);

        Assert.Equal(1, owned.Spending(Media.Cd).Of(Media.Cd));
        Assert.Equal(0, owned.Spending(Media.Cd, 2).Of(Media.Cd));
        Assert.Equal(0, owned.Spending(Media.Cd, 9).Of(Media.Cd));
        Assert.Equal(0, owned.Spending(Media.Bd25).Of(Media.Bd25));
        Assert.Equal(2, owned.Spending(Media.Cd, -3).Of(Media.Cd));
        Assert.Equal(5, owned.Spending(Media.Cd, 2).Of(Media.Dvd5));
    }

    [Fact]
    public void It_is_written_as_one_line_in_the_order_the_window_lists_the_sizes_and_read_back_the_same()
    {
        BlanksOwned owned = BlanksOwned.None.With(Media.BdXl, 2).With(Media.Cd, 10).With(Media.Dvd9, 1);

        Assert.Equal("CD=10;DVD9=1;BDXL=2", owned.AsText());

        BlanksOwned back = BlanksOwned.FromText(owned.AsText());

        Assert.All(Media.All, size => Assert.Equal(owned.Of(size), back.Of(size)));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("nonsense", "")]
    [InlineData("CD=ten;DVD5=5", "DVD5=5")]
    [InlineData("cd=3 ; dvd5 = 4", "CD=3;DVD5=4")]
    [InlineData("CD=3\r\nDVD5=4\n", "CD=3;DVD5=4")]
    [InlineData("FLOPPY=40;CD=1", "CD=1")]
    [InlineData("CD=-4;BD25=2", "BD25=2")]
    [InlineData("CD=1;CD=6", "CD=6")]
    [InlineData("CD=999999", "CD=9999")]
    [InlineData("=5;CD=;=;;", "")]
    public void A_file_somebody_edited_costs_the_parts_that_are_wrong_and_nothing_else(string? text, string means) =>
        Assert.Equal(means, BlanksOwned.FromText(text).AsText());

    // ------------------------------------------------------- choosing from them

    [Fact]
    public void Two_CDs_are_suggested_for_800_MB_where_one_DVD_would_also_do()
    {
        // The example in the request, as it was written. Two files, so the
        // set needs no file cut to make it.
        DiscProject project = AProjectOf(AGame("First", 400 * Mb), AGame("Second", 400 * Mb));
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 10).With(Media.Dvd5, 10);

        AChoiceFromTheBlanks? chosen = ChoosingASize.FromTheBlanksOwned(project, _ => 1000, owned);

        Assert.Equal(new AChoiceFromTheBlanks(Media.Cd, 2, 10), chosen);
        Assert.Equal("2 of your 10 CD, which wastes the least of the blanks you have.", chosen!.InWords);

        // Without the count it is one DVD, as it always was.
        Assert.Equal(Media.Dvd5, ChoosingASize.TheSmallestThatHoldsItOnOneDisc(project, _ => 1000));
    }

    [Fact]
    public void A_size_there_are_not_enough_of_is_out()
    {
        DiscProject project = AProjectOf(AGame("First", 400 * Mb), AGame("Second", 400 * Mb));

        // One CD will not hold two CDs' worth.
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 1).With(Media.Dvd5, 10);

        Assert.Equal(new AChoiceFromTheBlanks(Media.Dvd5, 1, 10), ChoosingASize.FromTheBlanksOwned(project, _ => 1000, owned));
    }

    [Fact]
    public void Only_sizes_somebody_has_are_ever_chosen()
    {
        // 100 MB fits a CD, and there are none: it goes on a Blu-ray,
        // because that is what is on the shelf.
        DiscProject project = AProjectOf(AGame("Small", 100 * Mb));
        BlanksOwned owned = BlanksOwned.None.With(Media.Bd25, 3);

        Assert.Equal(new AChoiceFromTheBlanks(Media.Bd25, 1, 3), ChoosingASize.FromTheBlanksOwned(project, _ => 1000, owned));
    }

    [Fact]
    public void Between_two_that_waste_the_same_it_is_the_one_with_fewer_discs_to_swap()
    {
        // Thirteen CDs and two DVD5s are not the same room, so this is held
        // with what the rule is: least room first, and only then fewer discs.
        DiscProject project = AProjectOf(AGame("One", 3 * Gb), AGame("Two", 3 * Gb));
        BlanksOwned owned = BlanksOwned.None.With(Media.Dvd5, 5).With(Media.Dvd9, 5).With(Media.Bd25, 5);

        AChoiceFromTheBlanks chosen = ChoosingASize.FromTheBlanksOwned(project, _ => 1000, owned)!;

        // One DVD9 at 7.95 is less room than two DVD5 at 8.74, and far less
        // than a Blu-ray.
        Assert.Equal(Media.Dvd9, chosen.Medium);
        Assert.Equal(1, chosen.Discs);
    }

    [Fact]
    public void A_file_too_big_for_a_size_is_the_plans_to_say_and_that_size_is_out()
    {
        // One file of 800 MB cannot go on CDs without being cut, so with
        // cutting off it is the DVD however many CDs there are.
        DiscProject project = AProjectOf(AGame("OneFile", 800 * Mb));
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 50).With(Media.Dvd5, 1);

        Assert.Equal(Media.Dvd5, ChoosingASize.FromTheBlanksOwned(project, _ => 1000, owned)?.Medium);

        // With cutting on, the CDs can take it.
        DiscProject cut = project with { Options = new DiscOptions(CutFiles: true) };

        Assert.Equal(Media.Cd, ChoosingASize.FromTheBlanksOwned(cut, _ => 1000, owned)?.Medium);
    }

    [Fact]
    public void When_nothing_owned_holds_it_there_is_no_choice_from_the_blanks()
    {
        DiscProject project = AProjectOf(AGame("Huge", 30 * Gb));
        BlanksOwned owned = BlanksOwned.None.With(Media.Cd, 3).With(Media.Dvd5, 2);

        Assert.Null(ChoosingASize.FromTheBlanksOwned(project, _ => 1000, owned));
        Assert.Null(ChoosingASize.FromTheBlanksOwned(project, _ => 1000, BlanksOwned.None));
    }

    [Theory]
    [InlineData(1, 1, "1 of your 1 CD, which wastes the least of the blanks you have.")]
    [InlineData(3, 40, "3 of your 40 CD, which wastes the least of the blanks you have.")]
    public void The_line_says_how_many_of_how_many(int discs, int owned, string says) =>
        Assert.Equal(says, new AChoiceFromTheBlanks(Media.Cd, discs, owned).InWords);

    // --------------------------------------------------------------- the screen

    [Fact]
    public void The_window_has_a_box_for_every_size_and_they_start_empty()
    {
        MakeADisc screen = TheScreen(new Paper(null));

        Assert.Equal(Media.All, screen.TheBlanks.Select(each => each.Medium));
        Assert.Equal(["CD", "DVD5", "DVD9", "BD25", "BD50", "BDXL"], screen.TheBlanks.Select(each => each.Name));
        Assert.All(screen.TheBlanks, each => Assert.Equal(string.Empty, each.Typed));
        Assert.Equal(6, screen.TheBlanks.Select(each => each.BoxId).Distinct().Count());
        Assert.All(screen.TheBlanks, each => Assert.Contains(each.Medium.Name, each.InFull, StringComparison.Ordinal));
        Assert.Equal(string.Empty, screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void What_was_kept_is_in_the_boxes_the_next_time()
    {
        MakeADisc screen = TheScreen(new Paper("CD=10;BD25=2"));

        Assert.Equal(["10", "", "", "2", "", ""], screen.TheBlanks.Select(each => each.Typed));
    }

    [Fact]
    public void Typing_a_count_writes_it_down_at_once()
    {
        Paper paper = new(null);
        MakeADisc screen = TheScreen(paper);

        screen.TheBlanks[0].Typed = "12";
        screen.TheBlanks[1].Typed = "3";

        Assert.Equal("CD=12;DVD5=3", paper.Text);

        // Emptied, it is none, and the box stays as it was left.
        screen.TheBlanks[0].Typed = string.Empty;

        Assert.Equal("DVD5=3", paper.Text);
        Assert.Equal(string.Empty, screen.TheBlanks[0].Typed);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData(" ")]
    public void What_is_not_a_count_is_none_and_the_box_keeps_what_was_typed(string typed)
    {
        Paper paper = new("CD=4");
        MakeADisc screen = TheScreen(paper);

        screen.TheBlanks[0].Typed = typed;

        Assert.Equal(string.Empty, paper.Text);
        Assert.Equal(typed, screen.TheBlanks[0].Typed);
    }

    [Fact]
    public void With_blanks_counted_the_size_chosen_for_me_is_out_of_them()
    {
        MakeADisc screen = TheScreen(new Paper("CD=10;DVD5=10"));
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));

        screen.ChooseTheSizeForMe = true;

        Assert.Equal(Media.Cd, screen.Medium);
        Assert.Equal("2 of your 10 CD, which wastes the least of the blanks you have.", screen.WhyThatSize);
        Assert.Equal("You have 10 blank CD.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void With_none_counted_it_is_the_smallest_as_it_always_was_and_nothing_is_said_of_blanks()
    {
        MakeADisc screen = TheScreen(new Paper(null));
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));

        screen.ChooseTheSizeForMe = true;

        Assert.Equal(Media.Dvd5, screen.Medium);
        Assert.Equal("The smallest that holds all of it on one disc.", screen.WhyThatSize);
        Assert.Equal(string.Empty, screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void Counting_a_blank_moves_a_size_that_is_being_chosen_for_me()
    {
        MakeADisc screen = TheScreen(new Paper(null));
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));
        screen.ChooseTheSizeForMe = true;
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.TheBlanks[0].Typed = "5";

        Assert.Equal(Media.Cd, screen.Medium);
        Assert.Contains(nameof(MakeADisc.WhyThatSize), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhatThereIsOfThatSize), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told, StringComparer.Ordinal);
    }

    [Fact]
    public void A_size_somebody_chose_themselves_is_never_moved_by_the_count()
    {
        MakeADisc screen = TheScreen(new Paper(null));
        screen.Add(AGame("Small", 100 * Mb));
        screen.Medium = Media.Bd50;

        screen.TheBlanks[0].Typed = "50";

        Assert.Equal(Media.Bd50, screen.Medium);
        Assert.Equal("You have no blank BD50 counted.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void When_no_blank_owned_holds_it_the_size_is_the_smallest_and_the_line_says_why()
    {
        MakeADisc screen = TheScreen(new Paper("CD=3"));
        screen.Add(AGame("Big", 3 * Gb));

        screen.ChooseTheSizeForMe = true;

        Assert.Equal(Media.Dvd5, screen.Medium);
        Assert.Equal(
            "None of the blanks you have holds it. The smallest that holds all of it on one disc.",
            screen.WhyThatSize);
        Assert.Equal("You have no blank DVD5 counted.", screen.WhatThereIsOfThatSize);
    }

    [Theory]
    [InlineData("CD=1", "You have 1 blank CD.")]
    [InlineData("CD=40", "You have 40 blank CD.")]
    [InlineData("DVD5=2", "You have no blank CD counted.")]
    public void The_line_under_the_size_says_how_many_of_it_there_are(string kept, string says)
    {
        MakeADisc screen = TheScreen(new Paper(kept));
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.Medium = Media.Cd;

        Assert.Equal(says, screen.WhatThereIsOfThatSize);
        Assert.Contains(nameof(MakeADisc.WhatThereIsOfThatSize), told, StringComparer.Ordinal);
    }

    [Fact]
    public void A_size_chosen_by_hand_that_takes_more_discs_than_there_are_says_so()
    {
        // discwright#98, 3 October: a warning when a backup would run out
        // of the discs counted. Two files of 400 MB are two CDs, and there
        // is one.
        MakeADisc screen = TheScreen(new Paper("CD=1;DVD5=4"));
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.Medium = Media.Cd;

        Assert.Equal("This takes 2 CD and you have 1 counted.", screen.WhatThereIsOfThatSize);
        Assert.Contains(nameof(MakeADisc.WhatThereIsOfThatSize), told, StringComparer.Ordinal);

        // Enough of them, and it is the plain count again.
        screen.TheBlanks[0].Typed = "2";
        Assert.Equal("You have 2 blank CD.", screen.WhatThereIsOfThatSize);

        // And one more game than there are discs for says so again.
        screen.Add(AGame("Third", 400 * Mb));
        Assert.Equal("This takes 3 CD and you have 2 counted.", screen.WhatThereIsOfThatSize);

        screen.Remove(screen.Games[2]);
        Assert.Equal("You have 2 blank CD.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void The_window_hears_when_only_what_is_on_it_changed()
    {
        // Nothing about the size or the count moved: a game was added, and
        // the line has to be drawn again all the same.
        MakeADisc screen = TheScreen(new Paper("CD=1"));
        screen.Medium = Media.Cd;
        screen.Add(AGame("First", 400 * Mb));

        // What a window would have read each time it was told to look. The
        // name alone proves nothing: the line is said once before the plan,
        // when it still has nothing to be short of.
        List<string> read = [];
        screen.PropertyChanged += (_, moved) =>
        {
            if (moved.PropertyName == nameof(MakeADisc.WhatThereIsOfThatSize))
            {
                read.Add(screen.WhatThereIsOfThatSize);
            }
        };

        screen.Add(AGame("Second", 400 * Mb));

        Assert.Equal("This takes 2 CD and you have 1 counted.", screen.WhatThereIsOfThatSize);
        Assert.NotEmpty(read);
        Assert.Equal("This takes 2 CD and you have 1 counted.", read[^1]);
    }

    [Fact]
    public void With_every_game_taken_off_again_nothing_is_short_any_more()
    {
        MakeADisc screen = TheScreen(new Paper("CD=1"));
        screen.Medium = Media.Cd;
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));
        Assert.Equal("This takes 2 CD and you have 1 counted.", screen.WhatThereIsOfThatSize);

        screen.Remove(screen.Games[1]);
        screen.Remove(screen.Games[0]);

        Assert.Equal("You have 1 blank CD.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void A_disc_with_no_name_has_no_plan_and_so_nothing_to_be_short_of()
    {
        // The count is the plan's. With the name rubbed out nothing is
        // planned, and a number left from the plan before would be a guess.
        MakeADisc screen = TheScreen(new Paper("CD=1"));
        screen.Medium = Media.Cd;
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));
        Assert.Equal("This takes 2 CD and you have 1 counted.", screen.WhatThereIsOfThatSize);

        screen.Label = string.Empty;

        Assert.Equal("You have 1 blank CD.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void A_size_with_none_counted_says_none_and_not_how_many_short()
    {
        MakeADisc screen = TheScreen(new Paper("DVD5=4"));
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));

        screen.Medium = Media.Cd;

        Assert.Equal("You have no blank CD counted.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void With_nothing_on_the_window_there_is_nothing_to_be_short_of()
    {
        MakeADisc screen = TheScreen(new Paper("CD=1"));
        screen.Medium = Media.Cd;

        Assert.Equal("You have 1 blank CD.", screen.WhatThereIsOfThatSize);
    }

    [Fact]
    public void The_count_is_not_part_of_a_project()
    {
        // About the shelf beside the person, not about a disc: a project
        // opened on another machine must not bring somebody else's shelf.
        MakeADisc screen = TheScreen(new Paper("CD=10"));
        screen.Add(AGame("Small", 100 * Mb));

        string saved = TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "CD", @"C:\out", DateTimeOffset.UtcNow);

        Assert.DoesNotContain("Blank", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CD=10", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void A_machine_with_nowhere_to_keep_it_still_counts_until_it_closes()
    {
        MakeADisc screen = TheScreen(null);
        screen.Add(AGame("First", 400 * Mb));
        screen.Add(AGame("Second", 400 * Mb));
        screen.ChooseTheSizeForMe = true;

        screen.TheBlanks[0].Typed = "9";

        Assert.Equal(Media.Cd, screen.Medium);
    }

    // --------------------------------------------------------------- the window

    [Fact]
    public void The_window_has_the_boxes_in_More_options_and_the_line_under_the_size()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement window = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        System.Xml.Linq.XElement boxes = Assert.Single(window.Descendants(xaml + "ItemsControl"), each => Id(each) == "TheBlanks");

        Assert.Equal("{Binding TheBlanks}", (string?)boxes.Attribute("ItemsSource"));
        Assert.Equal("{Binding NothingIsRunning}", (string?)boxes.Attribute("IsEnabled"));
        Assert.Contains(boxes.Ancestors(xaml + "Expander"), each => (string?)each.Attribute("Header") == "More options");

        System.Xml.Linq.XElement box = Assert.Single(boxes.Descendants(xaml + "TextBox"));

        // Three to a row and every name the same width, so the boxes stand
        // in columns. In a row that wrapped where it liked they stood
        // wherever their names ended, which the owner saw on the recording.
        Assert.Equal("3", (string?)Assert.Single(boxes.Descendants(xaml + "UniformGrid")).Attribute("Columns"));
        Assert.Empty(boxes.Descendants(xaml + "WrapPanel"));
        System.Xml.Linq.XElement[] columns = [.. boxes.Descendants(xaml + "ColumnDefinition")];

        Assert.Equal(["45", "*"], columns.Select(each => (string?)each.Attribute("Width")));

        // And no width of their own that the column may not have. Three
        // names and three boxes at a fixed width came to more than the
        // column holds once it grows a scrollbar, and the last box of each
        // row lost its right edge. A box takes what its name leaves of a
        // third of the width, and no more than it needs.
        Assert.Equal("46", (string?)columns[1].Attribute("MaxWidth"));
        Assert.Null(box.Attribute("Width"));
        Assert.Null(Assert.Single(boxes.Descendants(xaml + "UniformGrid")).Attribute("HorizontalAlignment"));
        Assert.Empty(boxes.Descendants(xaml + "StackPanel"));

        Assert.Equal("{Binding Typed, UpdateSourceTrigger=PropertyChanged}", (string?)box.Attribute("Text"));

        // Every box is the same template, so each answers to an id of its
        // own and says its whole name to a reader.
        Assert.Equal("{Binding BoxId}", Id(box));
        Assert.Equal("{Binding InFull}", (string?)box.Attribute("AutomationProperties.Name"));

        System.Xml.Linq.XElement line = Assert.Single(window.Descendants(xaml + "TextBlock"), each => Id(each) == "WhatThereIsOfThatSize");

        Assert.Equal("{Binding WhatThereIsOfThatSize}", (string?)line.Attribute("Text"));
        Assert.Equal(
            "{Binding WhatThereIsOfThatSize, Converter={StaticResource WhenAnything}}",
            (string?)line.Attribute("Visibility"));

        List<string?> order = [.. window.Descendants().Select(Id).Where(each => each is "WhyThatSize" or "WhatThereIsOfThatSize" or "CutFiles")];

        Assert.Equal(["WhyThatSize", "WhatThereIsOfThatSize", "CutFiles"], order);
    }

    // ---------------------------------------------------------------- helpers

    private static DiscProject AProjectOf(params GameFolder[] games) =>
        new("A DISC", games, new DiscOptions());

    private static GameFolder AGame(string name, long bytes = 1000)
    {
        ListedFile only = new($"setup_{name}.exe", bytes);

        return new GameFolder(
            @"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    private static MakeADisc TheScreen(Paper? paper) =>
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
            new PicturesOfOneSize(512, 512),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience,
            Blanks: paper));

    private sealed class Paper(string? text) : IKeepTheBlanks
    {
        public string? Text { get; private set; } = text;

        public string? Recalled() => Text;

        public void Keep(string kept) => Text = kept;
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
