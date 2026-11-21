using DiscWright.Staging;

namespace DiscWright.ViewModels;

/// <summary>
/// The question a preview of a set asks before it opens any windows.
/// </summary>
/// <remarks>
/// <para>
/// A set previews as one menu per disc, because every disc carries a menu
/// of its own and the preview's whole claim is showing the real thing. But
/// the windows land exactly on top of one another, and the same surprise
/// has now caught the owner from both directions: on 2026-10-24 he looked
/// at disc 1 of 2 and took it for the whole answer, and on 2026-11-09 the
/// crowd of windows read to him as a bug. A hover note on the button was
/// the first cure and it did not carry.
/// </para>
/// <para>
/// So the set asks first, his design: one window is the likely answer, all
/// of them is one click away, and a single disc has nothing to ask about.
/// </para>
/// </remarks>
public static class WhatAPreviewAsks
{
    /// <summary>
    /// The question for a set of that many discs, or nothing for one disc,
    /// which opens its one window unasked.
    /// </summary>
    /// <summary>
    /// What a preview asks before it opens anything, from what the build
    /// would make and how many menus were written, or nothing when there is
    /// one menu and nothing to decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// More than one menu is not always a set. Games that do not all fit on
    /// one disc get a disc each, and each disc's menu is for its own game:
    /// no menu lists them all. The question said "this is a set of 3 discs"
    /// and "one window opens disc 1" all the same, and the owner, on 21
    /// November 2026, with several games on the window, chose one window
    /// expecting the menu with all his games and got one game's Install,
    /// "not sure which disc it is".
    /// </para>
    /// <para>
    /// So it says which it is, names the game the one window is for, and
    /// says how to get them all on one menu, which is the thing he was
    /// looking for.
    /// </para>
    /// </remarks>
    public static AQuestion? For(PlannedBuild? planned, int menus)
    {
        if (menus < 2)
        {
            return null;
        }

        if (planned is not { Outputs.Count: > 1 } several)
        {
            return ForASetOf(menus);
        }

        int games = several.Outputs.Count;
        string first = several.Outputs[0].Games.Count > 0 ? several.Outputs[0].Games[0].Name : several.Outputs[0].Label;

        return new AQuestion(
            several.Discs == games
                ? $"These {games} games do not fit on one disc, so each gets a disc and a menu of its own."
                : $"These {games} games do not fit on one disc, so they go on {several.Discs} discs, each with a menu of its own.",
            $"No menu lists them all: each disc's menu is for the game on it. One window opens the first, {first}. "
                + "To have every game on one menu, choose a size of disc that holds them all.",
            "Open one window",
            menus == 2 ? "Open both" : $"Open all {menus}");
    }

    public static AQuestion? ForASetOf(int discs) =>
        discs < 2
            ? null
            : new AQuestion(
                $"This is a set of {discs} discs, and every disc has a menu of its own.",
                "One window opens disc 1, the menu somebody meets first. Every disc carries the "
                    + "manual and the extras, so nothing is missed. Opening every disc shows the whole set at once.",
                "Open one window",
                discs == 2 ? "Open both" : $"Open all {discs}");
}
