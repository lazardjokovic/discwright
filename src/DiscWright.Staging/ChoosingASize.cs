using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// The size of disc that suits what is going on it, for somebody who would
/// rather not pick one, discwright#98, 12 November: "'What size of disc' in
/// DW2 is missing an 'Automatically Choose' option, so it can suggest what
/// disc it thinks would be a best fit".
/// </summary>
/// <remarks>
/// Planned for real on each size rather than added up, because what a size
/// holds is the whole plan: the menu and the artwork take room, and the
/// extras may be what does not fit.
/// </remarks>
public static class ChoosingASize
{
    /// <summary>
    /// The smallest size that holds all of it on a single disc, or nothing
    /// when no size does.
    /// </summary>
    public static Medium? TheSmallestThatHoldsItOnOneDisc(DiscProject project, Func<string, long> sizeOf) =>
        Media.All.FirstOrDefault(size =>
            WhatWouldBeMade.For(project, size, sizeOf) is Outcome<PlannedBuild>.Ok(PlannedBuild planned)
            && planned.Discs == 1);

    /// <summary>
    /// The size to aim at: the smallest that holds it on one disc, and when
    /// none does the largest there is, which takes the fewest.
    /// </summary>
    public static Medium For(DiscProject project, Func<string, long> sizeOf) =>
        TheSmallestThatHoldsItOnOneDisc(project, sizeOf) ?? Media.All[^1];
}
