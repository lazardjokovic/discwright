using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The two questions a set asks on its way through, and which answer each
/// one offers as the button.
/// </summary>
/// <remarks>
/// Worded in the screen rather than in the window so that this can be read.
/// Decided in the window, both had the accent and Enter on the answer that
/// walks away from a half-made set, and nothing could say so.
/// </remarks>
public class TheQuestionsASetAsks
{
    [Fact]
    public void Between_discs_the_likely_answer_is_that_the_blank_is_in()
    {
        AQuestion asked = WhatASetAsks.ForTheNextBlank(new WhichDisc.OfASet(2, 3), "E:");

        // Somebody who has just labelled disc 1 and reached for a blank is
        // pressing Enter to go on.
        Assert.Equal("The blank is in", asked.TheLikelyOne);
        Assert.Equal("Stop here", asked.TheOtherWay);
    }

    [Fact]
    public void Between_discs_it_says_which_disc_is_done_and_which_is_wanted()
    {
        AQuestion asked = WhatASetAsks.ForTheNextBlank(new WhichDisc.OfASet(3, 4), "D:");

        Assert.Equal("Disc 2 is done. Put a blank in for disc 3 of 4.", asked.Headline);
        Assert.Contains("Take disc 2 out of D:", asked.Because, StringComparison.Ordinal);
        Assert.Contains("stopping here leaves it half made", asked.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void Coming_back_to_a_set_the_likely_answer_is_to_carry_on()
    {
        AQuestion asked = WhatASetAsks.WhetherToCarryOn(3, 4);

        // A note saying two discs are written and checked is far more often
        // somebody finishing the set than making a second copy.
        Assert.Equal("Carry on from disc 3", asked.TheLikelyOne);
        Assert.Equal("Start again from disc 1", asked.TheOtherWay);
    }

    [Fact]
    public void Coming_back_it_says_how_many_are_already_written_in_a_persons_words()
    {
        Assert.Equal("Disc 1 of this set is already written.", WhatASetAsks.WhetherToCarryOn(2, 2).Headline);
        Assert.Equal("Discs 1 to 3 of this set are already written.", WhatASetAsks.WhetherToCarryOn(4, 5).Headline);
    }

    [Fact]
    public void Coming_back_it_says_why_it_believes_the_note_and_what_it_cannot_see()
    {
        AQuestion asked = WhatASetAsks.WhetherToCarryOn(2, 2);

        Assert.Contains("carry on from disc 2 of 2, or start again from disc 1", asked.Because, StringComparison.Ordinal);
        Assert.Contains("not because it can see them now", asked.Because, StringComparison.Ordinal);
    }

    // --------------------------------------- and the one a preview asks

    [Fact]
    public void A_preview_of_one_disc_asks_nothing_at_all()
    {
        // One disc, one menu, one window: there is nothing to decide.
        Assert.Null(WhatAPreviewAsks.ForASetOf(1));
        Assert.Null(WhatAPreviewAsks.ForASetOf(0));
    }

    [Fact]
    public void A_preview_of_a_set_offers_one_window_as_the_likely_answer()
    {
        AQuestion asked = Assert.IsType<AQuestion>(WhatAPreviewAsks.ForASetOf(3));

        // The owner's design on 2026-11-09, after the crowd of windows,
        // opened with no warning, read to him as a bug. The question says
        // one window misses nothing, because every disc carries the manual
        // and the extras since 2026-11-10.
        Assert.Equal("This is a set of 3 discs, and every disc has a menu of its own.", asked.Headline);
        Assert.Equal("Open one window", asked.TheLikelyOne);
        Assert.Equal("Open all 3", asked.TheOtherWay);
        Assert.Contains("Every disc carries the manual and the extras", asked.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void A_preview_of_two_discs_says_both_rather_than_all_2()
    {
        Assert.Equal("Open both", Assert.IsType<AQuestion>(WhatAPreviewAsks.ForASetOf(2)).TheOtherWay);
    }

    // ------------------- and games with a disc each, which are not a set
    //
    // The owner, 21 November 2026: several games on the window, one window
    // chosen, and one game's Install where he expected the menu with all of
    // them, "not sure which disc it is". The question had called it a set.

    private static MakeADisc GamesThatDoNotShareADisc(params string[] names)
    {
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = new Medium("TEST", "a test disc", (2L * 1024 * 1024) + 1500);

        foreach (string name in names)
        {
            screen.Add(TheMainWindowWithoutAWindow.AGame(name, 1000));
        }

        return screen;
    }

    [Fact]
    public void Games_with_a_disc_each_are_not_called_a_set_and_the_one_window_is_named()
    {
        MakeADisc screen = GamesThatDoNotShareADisc("Alan Wake", "Dead Space", "Hollow Knight");
        Assert.Equal("3 games, a disc each", screen.WhatItWillMake);

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatThePreviewAsks(3));

        Assert.Equal(
            "These 3 games do not fit on one disc, so each gets a disc and a menu of its own.",
            asked.Headline);
        Assert.DoesNotContain("set", asked.Headline + asked.Because, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No menu lists them all", asked.Because, StringComparison.Ordinal);
        Assert.Contains("One window opens the first, Alan Wake.", asked.Because, StringComparison.Ordinal);
        Assert.Contains("choose a size of disc that holds them all", asked.Because, StringComparison.Ordinal);
        Assert.Equal("Open one window", asked.TheLikelyOne);
        Assert.Equal("Open all 3", asked.TheOtherWay);
    }

    [Fact]
    public void Two_of_them_say_both_and_the_first_named_is_whichever_is_first_in_the_list()
    {
        MakeADisc screen = GamesThatDoNotShareADisc("Alan Wake", "Dead Space");
        screen.MoveTheGameTo(screen.WhatGoesOnIt[1], screen.WhatGoesOnIt[0], after: false);

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatThePreviewAsks(2));

        Assert.StartsWith("These 2 games do not fit on one disc", asked.Headline, StringComparison.Ordinal);
        Assert.Contains("One window opens the first, Dead Space.", asked.Because, StringComparison.Ordinal);
        Assert.Equal("Open both", asked.TheOtherWay);
    }

    [Fact]
    public void When_one_of_them_needs_discs_of_its_own_it_says_how_many_discs_in_all()
    {
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = new Medium("TEST", "a test disc", (2L * 1024 * 1024) + 1500);
        screen.Add(TheMainWindowWithoutAWindow.AGame("Small", 1000));
        screen.Add(TheMainWindowWithoutAWindow.AGame("Big", 1200, parts: 2));

        // Whatever the plan says it comes to, the question says the same.
        string made = screen.WhatItWillMake;
        Assert.StartsWith("2 games on ", made, StringComparison.Ordinal);
        int discs = int.Parse(made.Split(' ')[3], System.Globalization.CultureInfo.InvariantCulture);

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatThePreviewAsks(discs));

        Assert.Equal(
            $"These 2 games do not fit on one disc, so they go on {discs} discs, each with a menu of its own.",
            asked.Headline);
        Assert.Equal($"Open all {discs}", asked.TheOtherWay);
    }

    [Fact]
    public void A_set_is_still_called_a_set_and_one_menu_still_asks_nothing()
    {
        // One game too big for one disc: a real set, the question it always had.
        MakeADisc set = TheMainWindowWithoutAWindow.New();
        set.OutputFolder = @"C:\out";
        set.Medium = new Medium("TEST", "a test disc", (2L * 1024 * 1024) + 1500);
        set.Add(TheMainWindowWithoutAWindow.AGame("Big", 1200, parts: 4));
        Assert.StartsWith("a set of ", set.WhatItWillMake, StringComparison.Ordinal);

        Assert.Equal(WhatAPreviewAsks.ForASetOf(3), set.WhatThePreviewAsks(3));

        // Games with a disc each, and only one menu written: nothing to ask.
        Assert.Null(GamesThatDoNotShareADisc("One", "Two").WhatThePreviewAsks(1));

        // With nothing on the window there is no plan, and several menus are
        // asked about the way they always were.
        Assert.Equal(WhatAPreviewAsks.ForASetOf(2), TheMainWindowWithoutAWindow.New().WhatThePreviewAsks(2));
        Assert.Equal(WhatAPreviewAsks.ForASetOf(2), WhatAPreviewAsks.For(null, 2));
    }

    [Fact]
    public void With_the_games_taken_off_again_nothing_is_remembered_of_them()
    {
        // The plan is of what is on the window. With that gone, or with
        // the disc's name rubbed out so nothing is planned, a question
        // about games that are no longer there would be about nothing.
        MakeADisc screen = GamesThatDoNotShareADisc("Alan Wake", "Dead Space");
        Assert.StartsWith("These 2 games", screen.WhatThePreviewAsks(2)!.Headline, StringComparison.Ordinal);

        screen.Label = string.Empty;

        Assert.Equal(WhatAPreviewAsks.ForASetOf(2), screen.WhatThePreviewAsks(2));

        screen = GamesThatDoNotShareADisc("Alan Wake", "Dead Space");
        screen.Remove(screen.Games[1]);
        screen.Remove(screen.Games[0]);

        Assert.Equal(WhatAPreviewAsks.ForASetOf(2), screen.WhatThePreviewAsks(2));
    }

    [Fact]
    public void The_question_follows_the_size_chosen_when_the_games_come_to_fit()
    {
        MakeADisc screen = GamesThatDoNotShareADisc("Alan Wake", "Dead Space");
        Assert.StartsWith("These 2 games", screen.WhatThePreviewAsks(2)!.Headline, StringComparison.Ordinal);

        // A disc that holds both: one disc, one menu, nothing to ask about
        // games, and two menus would be asked about as a set again.
        screen.Medium = new Medium("TEST", "a bigger test disc", (2L * 1024 * 1024) + 5000);

        Assert.Equal("one disc", screen.WhatItWillMake);
        Assert.Equal(WhatAPreviewAsks.ForASetOf(2), screen.WhatThePreviewAsks(2));
    }

    [Fact]
    public void The_window_asks_it_before_any_menu_opens_and_one_window_means_disc_1()
    {
        // Read off disk like the markup guards, because nothing in this
        // project can reference the code behind. What is pinned is that
        // the question comes from the screen, and that the likely answer
        // narrows the windows to the first disc's rather than none.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("_screen.WhatThePreviewAsks(menus.Length) is AQuestion crowd && Ask(crowd)", code, StringComparison.Ordinal);
        Assert.Contains("menus = [menus[0]];", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_asks_them_in_the_screens_words_and_takes_the_likely_answer_as_yes()
    {
        // Nothing in this project can reference the code behind, so it is
        // read off disk, like the markup guards. What is pinned is that both
        // questions come from the screen and that the window no longer
        // negates the answer, which is how the two came to be backwards.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("WhatASetAsks.WhetherToCarryOn(", code, StringComparison.Ordinal);
        Assert.Contains("WhatASetAsks.ForTheNextBlank(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("!WorthAsking.Ask(", code[code.IndexOf("CarryOnFromDisc(int", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }
}
