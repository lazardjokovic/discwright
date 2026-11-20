namespace DiscWright.Core;

/// <summary>
/// The face the menu is written in.
/// </summary>
/// <remarks>
/// <para>
/// discwright#98: "more menu themes (layout / font / font colour)". A short
/// list, and every face on it came with Windows itself as far back as
/// Windows 2000. The menu is read off the disc on whatever machine the disc
/// is put in, years from now, and cannot bring a font with it: a face that
/// came with Office or arrived with Windows 10 would be somebody else's
/// fallback on the machine that matters.
/// </para>
/// <para>
/// Tahoma first, because it is what every disc has been written in and what
/// a file that names none means.
/// </para>
/// </remarks>
public enum MenuFont
{
    /// <summary>Plain and narrow, and what the menu has always worn.</summary>
    Tahoma,

    /// <summary>Wide and open, drawn to be read small on a screen.</summary>
    Verdana,

    /// <summary>Rounder, with a little character.</summary>
    TrebuchetMs,

    /// <summary>A book face with feet, for a story or a period piece.</summary>
    Georgia,

    /// <summary>A newspaper face, older and narrower than Georgia.</summary>
    TimesNewRoman,

    /// <summary>A typewriter, every letter the same width.</summary>
    CourierNew,
}

/// <summary>One face, as a list shows it and as a page asks for it.</summary>
/// <param name="Which">The face itself, which is what gets saved.</param>
/// <param name="InWords">What the list shows, which is the face's own name.</param>
/// <param name="AsThePageAsksForIt">
/// What goes into the page's style: the face, then one like it that is on
/// everything, then the kind of face, so a machine without the first still
/// draws something of the same sort.
/// </param>
public sealed record AMenuFont(MenuFont Which, string InWords, string AsThePageAsksForIt)
{
    /// <summary>The name a window draws a row of the list in.</summary>
    public string Family => InWords;

    /// <summary>What a list shows when it is given no template.</summary>
    public override string ToString() => InWords;
}

/// <summary>Every face the menu can wear, in the order the list offers them.</summary>
public static class MenuFonts
{
    public static IReadOnlyList<AMenuFont> All { get; } =
    [
        new(MenuFont.Tahoma, "Tahoma", "Tahoma, sans-serif"),
        new(MenuFont.Verdana, "Verdana", "Verdana, Tahoma, sans-serif"),
        new(MenuFont.TrebuchetMs, "Trebuchet MS", "\"Trebuchet MS\", Tahoma, sans-serif"),
        new(MenuFont.Georgia, "Georgia", "Georgia, \"Times New Roman\", serif"),
        new(MenuFont.TimesNewRoman, "Times New Roman", "\"Times New Roman\", serif"),
        new(MenuFont.CourierNew, "Courier New", "\"Courier New\", monospace"),
    ];

    /// <summary>The row of the list that stands for one face.</summary>
    public static AMenuFont For(MenuFont which) => All.First(each => each.Which == which);

    /// <summary>
    /// The face a word names, by its name on the list or by the name it is
    /// saved under, whatever its capitals, or nothing for a word that names
    /// none. Never a number, which Enum.Parse would take.
    /// </summary>
    public static MenuFont? Named(string? word)
    {
        string wanted = (word ?? string.Empty).Trim();

        foreach (AMenuFont each in All)
        {
            if (string.Equals(each.InWords, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(each.Which.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return each.Which;
            }
        }

        return null;
    }
}
