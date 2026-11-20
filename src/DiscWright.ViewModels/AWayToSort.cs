namespace DiscWright.ViewModels;

/// <summary>
/// A way to put the games in order.
/// </summary>
/// <remarks>
/// discwright#98, 6 and 12 November: "to even be able to sort the list by
/// column, by clicking on Name, Size", and "sorted in alphabetical order or
/// by size". By name was all there was until the owner asked, on the
/// recording of 2026-11-20, for a list of ways.
/// </remarks>
public enum HowToSort
{
    /// <summary>By name, A first. A number in a name is read as a number.</summary>
    ByNameAToZ,

    /// <summary>By name, Z first.</summary>
    ByNameZToA,

    /// <summary>By how much each takes on the disc, the biggest first.</summary>
    BiggestFirst,

    /// <summary>The smallest first.</summary>
    SmallestFirst,
}

/// <summary>One way to sort, as a list shows it.</summary>
/// <param name="How">The way itself.</param>
/// <param name="InWords">What the list shows for it.</param>
public sealed record AWayToSort(HowToSort How, string InWords)
{
    /// <summary>Every way, in the order the list offers them.</summary>
    public static IReadOnlyList<AWayToSort> All { get; } =
    [
        new(HowToSort.ByNameAToZ, "By name, A to Z"),
        new(HowToSort.ByNameZToA, "By name, Z to A"),
        new(HowToSort.BiggestFirst, "By size, biggest first"),
        new(HowToSort.SmallestFirst, "By size, smallest first"),
    ];

    /// <summary>What a list shows when it is given no template.</summary>
    public override string ToString() => InWords;
}
