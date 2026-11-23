namespace DiscWright.Core;

/// <summary>
/// How large the menu's heading and buttons are written.
/// </summary>
/// <remarks>
/// xniwo, discwright#98, 23 November 2026: some fonts of his own came out
/// "quite small", and he asked to adjust the size "slightly", "up to the
/// limit of its confined space". So four steps and no free number: the
/// largest is as large as a button's height and the list's width allow, and
/// a word too long for it is squeezed as any long word is.
/// </remarks>
public enum MenuTextSize
{
    AsItIs,

    Smaller,

    Larger,

    Largest,
}

/// <summary>One step of the size, in words and in pixels.</summary>
/// <param name="Which">The step.</param>
/// <param name="InWords">What the list in the dialog says.</param>
/// <param name="ButtonPixels">A button's words. 11 is the menu as it always was.</param>
/// <param name="HeadingPixels">The heading's. 15 is the menu as it always was.</param>
public sealed record AMenuTextSize(MenuTextSize Which, string InWords, int ButtonPixels, int HeadingPixels)
{
    /// <summary>
    /// The height of a line of the heading: 20 as it always was, and room
    /// for the letters when they are larger, so a heading of two lines does
    /// not write over itself.
    /// </summary>
    public int HeadingLinePixels => Math.Max(20, HeadingPixels + 5);

    public override string ToString() => InWords;
}

public static class MenuTextSizes
{
    /// <summary>
    /// In the order the list shows them. The largest is 14 because a button
    /// is 38 high and its list 196 wide: at 14, bold and in capitals, a
    /// button still holds a word of a dozen letters before it is squeezed.
    /// </summary>
    public static IReadOnlyList<AMenuTextSize> All { get; } =
    [
        new(MenuTextSize.Smaller, "Smaller", 10, 13),
        new(MenuTextSize.AsItIs, "As it is", 11, 15),
        new(MenuTextSize.Larger, "Larger", 13, 17),
        new(MenuTextSize.Largest, "Largest", 14, 19),
    ];

    public static AMenuTextSize For(MenuTextSize which) => All.First(each => each.Which == which);

    /// <summary>
    /// The step a word names, by the list's words or the step's own name,
    /// or nothing for any other word.
    /// </summary>
    public static MenuTextSize? Named(string? word)
    {
        string wanted = (word ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

        foreach (AMenuTextSize each in All)
        {
            if (string.Equals(each.InWords.Replace(" ", string.Empty, StringComparison.Ordinal), wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(each.Which.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return each.Which;
            }
        }

        return null;
    }
}
