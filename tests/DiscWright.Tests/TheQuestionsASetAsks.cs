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

    [Fact]
    public void The_window_asks_it_before_any_menu_opens_and_one_window_means_disc_1()
    {
        // Read off disk like the markup guards, because nothing in this
        // project can reference the code behind. What is pinned is that
        // the question comes from the screen, and that the likely answer
        // narrows the windows to the first disc's rather than none.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("WhatAPreviewAsks.ForASetOf(menus.Length)", code, StringComparison.Ordinal);
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
