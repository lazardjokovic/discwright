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
    public static AQuestion? ForASetOf(int discs) =>
        discs < 2
            ? null
            : new AQuestion(
                $"This is a set of {discs} discs, and every disc has a menu of its own.",
                "One window opens disc 1, the menu somebody meets first, which carries the manual "
                    + "and the extras. Opening every disc shows the whole set at once.",
                "Open one window",
                discs == 2 ? "Open both" : $"Open all {discs}");
}
