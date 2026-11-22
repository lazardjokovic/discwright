using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What the main window says, and offers, when the games on it do not fit on
/// one disc: that they share a set, which of them land on which disc, and
/// the other way, a disc for each.
/// </summary>
/// <remarks>
/// The owner, 21 November 2026, with three games that came to five discs:
/// "it should be 3". Sets of several games were taken out once because a
/// packer chose which games shared a disc without a word. So the window
/// says it, before anything is built, and the order is the list's.
/// </remarks>
public class SeveralGamesOnTheWindow
{
    [Fact]
    public void Games_that_do_not_fit_on_one_disc_share_a_set_and_the_window_says_which_land_on_which_disc()
    {
        MakeADisc screen = With(2500, ("One", 1000, 0), ("Two", 1000, 0), ("Three", 1000, 0));

        Assert.Equal("3 games on a set of 2 discs", screen.WhatItWillMake);
        Assert.Equal("Disc 1: One, Two. Disc 2: Three. They go on in the order of the list.", screen.WhereEachGameGoes);
        Assert.True(screen.ADiscForEachGameIsAChoice);
        Assert.False(screen.ADiscForEachGame);
        Assert.True(screen.CanMakeIt);
        Assert.Null(screen.WhyNot);
    }

    [Fact]
    public void A_game_that_runs_on_to_the_next_disc_is_named_on_both()
    {
        // Three files of 1000 on a disc of 2500: two, and then the third
        // with the next game beside it.
        MakeADisc screen = With(2500, ("Big", 1000, 2), ("Small", 1000, 0));

        Assert.Equal("2 games on a set of 2 discs", screen.WhatItWillMake);
        Assert.Equal("Disc 1: Big. Disc 2: Big, Small. They go on in the order of the list.", screen.WhereEachGameGoes);
    }

    [Fact]
    public void Moving_a_game_in_the_list_moves_it_on_the_discs_and_the_line_follows()
    {
        MakeADisc screen = With(2500, ("One", 1000, 0), ("Two", 1000, 0), ("Three", 1000, 0));
        List<string?> told = [];
        screen.PropertyChanged += (_, changed) => told.Add(changed.PropertyName);

        screen.MoveTheGameTo(screen.WhatGoesOnIt[2], screen.WhatGoesOnIt[0], after: false);

        Assert.Equal("Disc 1: Three, One. Disc 2: Two. They go on in the order of the list.", screen.WhereEachGameGoes);
        Assert.Contains(nameof(MakeADisc.WhereEachGameGoes), told);
    }

    [Fact]
    public void Ticked_each_game_gets_a_disc_of_its_own_and_unticked_the_set_is_back()
    {
        MakeADisc screen = With(2500, ("One", 1000, 0), ("Two", 1000, 0), ("Three", 1000, 0));
        List<string?> told = [];
        screen.PropertyChanged += (_, changed) => told.Add(changed.PropertyName);

        screen.ADiscForEachGame = true;

        Assert.Equal("3 games, a disc each", screen.WhatItWillMake);
        Assert.Equal(string.Empty, screen.WhereEachGameGoes);
        Assert.True(screen.ADiscForEachGameIsAChoice);
        Assert.Contains(nameof(MakeADisc.ADiscForEachGame), told);
        Assert.Contains(nameof(MakeADisc.WhatItWillMake), told);
        Assert.Contains(nameof(MakeADisc.WhereEachGameGoes), told);

        screen.ADiscForEachGame = false;

        Assert.Equal("3 games on a set of 2 discs", screen.WhatItWillMake);
        Assert.StartsWith("Disc 1: One, Two.", screen.WhereEachGameGoes, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disc_each_takes_more_discs_where_a_game_runs_over_and_the_window_counts_them()
    {
        MakeADisc screen = With(2500, ("Big", 1000, 2), ("Small", 1000, 0));
        Assert.Equal("2 games on a set of 2 discs", screen.WhatItWillMake);

        screen.ADiscForEachGame = true;

        // Big is a set of two by itself, and Small a disc beside them.
        Assert.Equal("2 games on 3 discs", screen.WhatItWillMake);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_one_game_or_games_that_fit_on_one_disc_there_is_nothing_to_choose_and_nothing_to_say(bool oneGame)
    {
        MakeADisc screen = oneGame
            ? With(1500, ("Big", 1000, 2))
            : With(5000, ("One", 1000, 0), ("Two", 1000, 0));

        Assert.Equal(oneGame ? "a set of 3 discs" : "one disc", screen.WhatItWillMake);
        Assert.Equal(string.Empty, screen.WhereEachGameGoes);
        Assert.False(screen.ADiscForEachGameIsAChoice);
    }

    [Fact]
    public void While_it_is_ticked_the_box_stays_so_there_is_a_way_back_out_of_it()
    {
        MakeADisc screen = With(1500, ("One", 1000, 0), ("Two", 1000, 0));
        screen.ADiscForEachGame = true;
        Assert.Equal("2 games, a disc each", screen.WhatItWillMake);

        // A disc that holds both. Nothing is left for the box to decide,
        // and it stays because it is ticked.
        screen.Medium = ADisc(5000);

        Assert.Equal("one disc", screen.WhatItWillMake);
        Assert.True(screen.ADiscForEachGameIsAChoice);

        screen.ADiscForEachGame = false;

        Assert.False(screen.ADiscForEachGameIsAChoice);

        // And it goes with the games, ticked or not.
        screen.ADiscForEachGame = true;
        screen.Remove(screen.Games[1]);

        Assert.False(screen.ADiscForEachGameIsAChoice);
    }

    [Fact]
    public void A_plan_that_is_refused_keeps_the_box_because_the_other_way_may_not_be_refused()
    {
        // A file bigger than the disc, and nothing may be cut.
        MakeADisc screen = With(1500, ("One", 1000, 0), ("Vast", 2000, 0));

        Assert.NotNull(screen.WhyNot);
        Assert.Equal(string.Empty, screen.WhatItWillMake);
        Assert.Equal(string.Empty, screen.WhereEachGameGoes);
        Assert.True(screen.ADiscForEachGameIsAChoice);

        // One game refused has no other way.
        MakeADisc alone = With(1500, ("Vast", 2000, 0));

        Assert.NotNull(alone.WhyNot);
        Assert.False(alone.ADiscForEachGameIsAChoice);
    }

    [Fact]
    public void With_no_games_or_no_name_nothing_is_said()
    {
        MakeADisc empty = TheMainWindowWithoutAWindow.New();

        Assert.Equal(string.Empty, empty.WhereEachGameGoes);
        Assert.False(empty.ADiscForEachGameIsAChoice);

        MakeADisc unnamed = With(1500, ("One", 1000, 0), ("Two", 1000, 0));
        unnamed.Label = string.Empty;

        Assert.Equal(string.Empty, unnamed.WhereEachGameGoes);
        Assert.False(unnamed.ADiscForEachGameIsAChoice);

        // Taken off again, the line goes with them.
        MakeADisc emptied = With(1500, ("One", 1000, 0), ("Two", 1000, 0));
        Assert.NotEqual(string.Empty, emptied.WhereEachGameGoes);

        emptied.Remove(emptied.Games[1]);
        emptied.Remove(emptied.Games[0]);

        Assert.Equal(string.Empty, emptied.WhereEachGameGoes);
        Assert.False(emptied.ADiscForEachGameIsAChoice);
    }

    [Fact]
    public void A_bigger_disc_that_holds_them_all_takes_the_line_and_the_box_away()
    {
        MakeADisc screen = With(1500, ("One", 1000, 0), ("Two", 1000, 0));
        Assert.Equal("Disc 1: One. Disc 2: Two. They go on in the order of the list.", screen.WhereEachGameGoes);

        screen.Medium = ADisc(5000);

        Assert.Equal("one disc", screen.WhatItWillMake);
        Assert.Equal(string.Empty, screen.WhereEachGameGoes);
        Assert.False(screen.ADiscForEachGameIsAChoice);
    }

    [Fact]
    public void How_many_discs_it_needs_is_the_sets_and_is_what_the_blanks_are_counted_against()
    {
        MakeADisc screen = With(2500, ("One", 1000, 0), ("Two", 1000, 0), ("Three", 1000, 0));

        Assert.StartsWith("Needs 2 discs.", screen.WhatSplittingWouldDo, StringComparison.Ordinal);

        screen.ADiscForEachGame = true;

        Assert.StartsWith("Needs 3 discs.", screen.WhatSplittingWouldDo, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_before_a_preview_is_about_a_set_whose_every_menu_lists_every_game()
    {
        MakeADisc screen = With(1500, ("Alan Wake", 1000, 0), ("Dead Space", 1000, 0), ("Hollow Knight", 1000, 0));

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatThePreviewAsks(3));

        Assert.Equal("These 3 games go on a set of 3 discs, and every disc's menu lists all of them.", asked.Headline);
        Assert.StartsWith("One window opens disc 1, the menu somebody meets first.", asked.Because, StringComparison.Ordinal);
        Assert.Contains("A game's button says which disc it is on", asked.Because, StringComparison.Ordinal);

        // Not what a set of one game is told, which is that every disc
        // carries the manual and the extras. Here a game's own are on one.
        Assert.DoesNotContain("Every disc carries the manual", asked.Because, StringComparison.Ordinal);
        Assert.DoesNotContain("No menu lists them all", asked.Because, StringComparison.Ordinal);
        Assert.Equal("Open one window", asked.TheLikelyOne);
        Assert.Equal("Open all 3", asked.TheOtherWay);

        Assert.Equal("Open both", With(1500, ("One", 1000, 0), ("Two", 1000, 0)).WhatThePreviewAsks(2)!.TheOtherWay);

        // Ticked, it is the other question, and it says how to get back.
        screen.ADiscForEachGame = true;

        AQuestion each = Assert.IsType<AQuestion>(screen.WhatThePreviewAsks(3));

        Assert.Equal("These 3 games do not fit on one disc, and each was given a disc and a menu of its own.", each.Headline);
        Assert.Contains("No menu lists them all", each.Because, StringComparison.Ordinal);
        Assert.Contains("Give each game a disc of its own", each.Because, StringComparison.Ordinal);

        // One game with a set to itself is asked what it always was.
        Assert.Equal(WhatAPreviewAsks.ForASetOf(3), With(1500, ("Big", 1000, 2)).WhatThePreviewAsks(3));

        // And one menu is no question at all.
        Assert.Null(screen.WhatThePreviewAsks(1));
        Assert.Null(WhatAPreviewAsks.ForASetSeveralGamesShare(3, 1));
    }

    [Fact]
    public void The_window_shows_the_line_and_offers_the_box_only_while_there_is_something_to_choose()
    {
        // Read off the markup, as the other guards of the window are: the
        // line is there only when it has something to say, and the box only
        // while it has something to decide, and neither during a run.
        string markup = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml"));

        int line = markup.IndexOf("AutomationProperties.AutomationId=\"WhereEachGameGoes\"", StringComparison.Ordinal);
        int box = markup.IndexOf("AutomationProperties.AutomationId=\"ADiscForEachGame\"", StringComparison.Ordinal);
        int cut = markup.IndexOf("AutomationProperties.AutomationId=\"WhatSplittingWouldDo\"", StringComparison.Ordinal);
        int drive = markup.IndexOf("AutomationProperties.AutomationId=\"WhichDrive\"", StringComparison.Ordinal);

        // Under how many discs it needs, and above which drive.
        Assert.True(cut > 0 && cut < line && line < box && box < drive, $"{cut} {line} {box} {drive}");

        string theLine = markup[markup.LastIndexOf("<TextBlock", line, StringComparison.Ordinal)..line];
        string theBox = markup[markup.LastIndexOf("<CheckBox", box, StringComparison.Ordinal)..box];

        Assert.Contains("Text=\"{Binding WhereEachGameGoes}\"", theLine, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding WhereEachGameGoes, Converter={StaticResource WhenAnything}}\"", theLine, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", theLine, StringComparison.Ordinal);

        Assert.Contains("Content=\"Give each game a disc of its own\"", theBox, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding ADiscForEachGame}\"", theBox, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding NothingIsRunning}\"", theBox, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding ADiscForEachGameIsAChoice, Converter={StaticResource WhenSo}}\"", theBox, StringComparison.Ordinal);
    }

    [Fact]
    public void The_empty_list_says_each_game_needs_a_folder_and_that_their_parent_brings_them_all()
    {
        System.Xml.Linq.XElement nothingYet = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml"))
            .Descendants()
            .Single(each => (string?)each.Attribute("AutomationProperties.AutomationId") == "NothingYet");

        string says = (string)nothingYet.Attribute("Text")!;

        // What it always said, then a line left empty, then what a folder
        // is to this program, in the owner's words.
        Assert.StartsWith("Nothing yet. Add the folder a game was downloaded into", says, StringComparison.Ordinal);
        Assert.EndsWith(
            "\n\nEach game needs a folder of its own. If those folders sit in a parent folder, "
                + "add the parent and every game in it comes in at once.",
            says,
            StringComparison.Ordinal);

        // Only while there is nothing on the list, and it wraps.
        Assert.Equal("{Binding Games.Count, Converter={StaticResource WhenNone}}", (string?)nothingYet.Attribute("Visibility"));
        Assert.Equal("Wrap", (string?)nothingYet.Attribute("TextWrapping"));
    }

    private static Medium ADisc(long room) => new("TEST", "a test disc", (2L * 1024 * 1024) + room);

    /// <summary>The window with a disc of that much room and those games, each of a file of that size and so many more like it.</summary>
    private static MakeADisc With(long room, params (string Name, int Bytes, int MoreFiles)[] games)
    {
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = ADisc(room);

        foreach ((string name, int bytes, int more) in games)
        {
            screen.Add(more > 0
                ? TheMainWindowWithoutAWindow.AGame(name, bytes, parts: more)
                : TheMainWindowWithoutAWindow.AGame(name, bytes));
        }

        return screen;
    }
}
