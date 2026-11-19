namespace DiscWright.Core;

/// <summary>
/// How a picture is made to cover the menu, which is 760 by 480 and almost
/// never the picture's own shape.
/// </summary>
/// <remarks>
/// The four of discwright#98, 15 November: "fill (doesn't show whole image
/// but fills the screen and keeps ratio), fit (keeps ratio and doesn't lose
/// image, may show black border), stretch (fills screen, ignoring ratio) and
/// tile (to repeat the image to fill the window without changing its size)".
/// The first and third were always here, as a tick box.
/// </remarks>
public enum BackgroundFit
{
    /// <summary>Grown or shrunk until it covers the menu, the overflow cropped evenly.</summary>
    Fill,

    /// <summary>Pulled to the menu's shape, all of it showing, out of proportion.</summary>
    Stretch,

    /// <summary>Grown or shrunk until all of it is inside the menu, the rest left dark.</summary>
    Fit,

    /// <summary>Repeated at its own size from the top left.</summary>
    Tile,
}

/// <summary>One way to fit, as a list shows it.</summary>
/// <param name="How">The way itself, which is what gets saved.</param>
/// <param name="InWords">What the list shows for it.</param>
/// <param name="WhatItDoes">A sentence for under the list, once it is chosen.</param>
public sealed record AWayToFit(BackgroundFit How, string InWords, string WhatItDoes)
{
    /// <summary>What a list shows when it is given no template.</summary>
    public override string ToString() => InWords;
}

/// <summary>Every way to fit, in the order the list offers them.</summary>
public static class WaysToFit
{
    /// <summary>
    /// Fill first, because keeping the proportions and covering the menu is
    /// almost always what somebody wants from cover art or a screenshot.
    /// </summary>
    public static IReadOnlyList<AWayToFit> All { get; } =
    [
        new(BackgroundFit.Fill, "Fill and crop",
            "Covers the menu and keeps the proportions. What does not fit is cropped evenly."),
        new(BackgroundFit.Fit, "Fit inside",
            "Shows the whole picture and keeps the proportions. Where it does not reach, the menu is its own dark."),
        new(BackgroundFit.Stretch, "Stretch",
            "Covers the menu with the whole picture, pulled out of proportion."),
        new(BackgroundFit.Tile, "Tile",
            "Repeats the picture at its own size, from the top left."),
    ];

    /// <summary>The row of the list that stands for one way.</summary>
    public static AWayToFit For(BackgroundFit how) => All.First(each => each.How == how);

    /// <summary>
    /// The way a word names, whatever its capitals, or nothing for a word
    /// that names none. Never a number: Enum.Parse reads "3" as Tile, and a
    /// file is only ever written with the words.
    /// </summary>
    public static BackgroundFit? Named(string? word) =>
        All.Select(each => (BackgroundFit?)each.How)
            .FirstOrDefault(each => string.Equals(each.ToString(), word?.Trim(), StringComparison.OrdinalIgnoreCase));
}
