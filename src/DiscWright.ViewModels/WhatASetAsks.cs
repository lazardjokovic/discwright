using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// A question with two answers, where neither is a refusal, worded here so a
/// test can read which answer is the one the button offers.
/// </summary>
/// <remarks>
/// <para>
/// The window draws one of these as a dialog with two buttons: the likely
/// one wears the accent and answers to Enter, the other is grey. Which answer
/// is which was decided inside the window, where nothing could read it, and
/// two of the questions a set asks had them the wrong way round: the dialog
/// between discs put the accent and Enter on <em>Stop here</em>, and the one
/// about carrying on put them on <em>Start again from disc 1</em>. Both put
/// the emphasis on walking away from a set somebody was in the middle of.
/// </para>
/// <para>
/// <strong>The likely answer is the one that carries on.</strong> Somebody
/// who has just labelled disc 1 and reached for a blank is pressing Enter to
/// go on, and somebody whose note says three discs are already written and
/// checked is far more often finishing the set than making a second copy.
/// The other way is still one click, and still perfectly reasonable.
/// </para>
/// </remarks>
/// <param name="Headline">One line, saying what is being asked.</param>
/// <param name="Because">Why, and what each answer costs.</param>
/// <param name="TheLikelyOne">The answer nearly everybody wants, so it is the button.</param>
/// <param name="TheOtherWay">The other answer, still reasonable.</param>
public sealed record AQuestion(string Headline, string Because, string TheLikelyOne, string TheOtherWay);

/// <summary>The two questions a set asks on its way through.</summary>
public static class WhatASetAsks
{
    /// <summary>
    /// Between discs: the one just written is out, and the next blank is
    /// wanted. The likely answer is that it is in.
    /// </summary>
    /// <param name="next">Which disc is wanted, and of how many.</param>
    /// <param name="drive">The drive it goes in.</param>
    public static AQuestion ForTheNextBlank(WhichDisc.OfASet next, string drive) =>
        new(
            $"Disc {next.Number - 1} is done. Put a blank in for {next.InWords}.",
            $"Take disc {next.Number - 1} out of {drive}, label it, and put a blank disc in. The set is not "
                + "finished until every disc is written, so stopping here leaves it half made.",
            "The blank is in",
            "Stop here");

    /// <summary>
    /// At the start, when the note beside the discs says some of them are
    /// already written. The likely answer is to carry on from the first one
    /// that is not.
    /// </summary>
    /// <param name="from">The disc to carry on from.</param>
    /// <param name="ofDiscs">How many the set has.</param>
    public static AQuestion WhetherToCarryOn(int from, int ofDiscs) =>
        new(
            from == 2
                ? "Disc 1 of this set is already written."
                : $"Discs 1 to {from - 1} of this set are already written.",
            $"DiscWright can carry on from disc {from} of {ofDiscs}, or start again from disc 1 and "
                + "write the whole set. It knows about them because it checked each one as it was "
                + "written, not because it can see them now.",
            $"Carry on from disc {from}",
            "Start again from disc 1");
}
