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
/// <summary>A size chosen out of the blanks somebody has.</summary>
/// <param name="Medium">The size.</param>
/// <param name="Discs">How many of it the disc or the set takes.</param>
/// <param name="Owned">How many of it they have.</param>
public sealed record AChoiceFromTheBlanks(Medium Medium, int Discs, int Owned)
{
    /// <summary>Why this one, for the line under the size.</summary>
    public string InWords =>
        (Discs == 1 ? "1" : Discs.ToString(System.Globalization.CultureInfo.InvariantCulture))
        + $" of your {Owned} {Medium.Key}, which wastes the least of the blanks you have.";
}

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
    /// <summary>
    /// The size that wastes least out of the blanks somebody has, or nothing
    /// when none of what they have holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// discwright#98: "If a game is 800MB, for the app to suggest 2 x CD if
    /// you have them available instead of 1 x DVD." Each size they own any
    /// of is planned, as a build would plan it, and the ones that need more
    /// discs than they have are out. Of the rest, the one whose discs come
    /// to the least room wins, because the room not used is the waste, and
    /// between two that come to the same, the one with fewer discs to swap.
    /// </para>
    /// <para>
    /// Planned and never added up, for the reason the smallest is: a file
    /// too big for a size, or one that has to be cut, is the plan's to say.
    /// </para>
    /// </remarks>
    public static AChoiceFromTheBlanks? FromTheBlanksOwned(
        DiscProject project, Func<string, long> sizeOf, BlanksOwned blanks) =>
        Media.All
            // Only to save planning a size there are none of. The count
            // below would turn it away just the same.
            .Where(size => blanks.Of(size) > 0)
            .Select(size => WhatWouldBeMade.For(project, size, sizeOf) is Outcome<PlannedBuild>.Ok(PlannedBuild planned)
                ? new AChoiceFromTheBlanks(size, planned.Discs, blanks.Of(size))
                : null)
            .Where(each => each is not null && each.Discs <= each.Owned)
            .OrderBy(each => each!.Discs * each.Medium.Capacity)
            .ThenBy(each => each!.Discs)
            .FirstOrDefault();

    public static Medium For(DiscProject project, Func<string, long> sizeOf) =>
        TheSmallestThatHoldsItOnOneDisc(project, sizeOf) ?? Media.All[^1];
}
