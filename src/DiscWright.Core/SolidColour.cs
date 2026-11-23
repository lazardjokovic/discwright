namespace DiscWright.Core;

/// <summary>
/// A solid colour the menu can be drawn on instead of a picture,
/// discwright#98: "a solid color option by choosing a hex value".
/// </summary>
/// <remarks>
/// <para>
/// Six hex digits and nothing else, with the leading <c>#</c> optional on
/// the way in and always present on the way out. Shorthand like
/// <c>#abc</c>, names like <c>teal</c> and alpha channels are all refused,
/// because every form accepted here is a form promised for ever in project
/// files.
/// </para>
/// <para>
/// <strong>This is also the gate on what reaches the menu's markup.</strong>
/// The colour is written into a style rule on the disc's own page, so
/// nothing that is not exactly a colour may pass, however it got into the
/// project file.
/// </para>
/// </remarks>
public static class SolidColour
{
    /// <summary>Whether this names a colour at all.</summary>
    public static bool Fits(string? text) => Tidied(text) is not null;

    /// <summary>
    /// The colour as it is kept and written: lowercase, with its
    /// <c>#</c> on. Nothing, when the text does not name one.
    /// </summary>
    public static string? Tidied(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string bare = text.Trim();

        if (bare.StartsWith('#'))
        {
            bare = bare[1..];
        }

        if (bare.Length != 6)
        {
            return null;
        }

        foreach (char figure in bare)
        {
            if (!char.IsAsciiHexDigit(figure))
            {
                return null;
            }
        }

        return "#" + bare.ToLowerInvariant();
    }

    /// <summary>
    /// A colour as Windows hands one back: red in the lowest byte, then
    /// green, then blue, and the top byte nothing. As this program keeps it.
    /// </summary>
    public static string FromWindows(int colour) =>
        "#"
        + (colour & 0xFF).ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
        + ((colour >> 8) & 0xFF).ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
        + ((colour >> 16) & 0xFF).ToString("x2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A colour as Windows takes one, for its colour dialog to start at.
    /// Black for anything that is not a colour, where Windows starts anyway.
    /// </summary>
    public static int ToWindows(string? text)
    {
        string? colour = Tidied(text);

        if (colour is null)
        {
            return 0;
        }

        int red = Convert.ToInt32(colour.Substring(1, 2), 16);
        int green = Convert.ToInt32(colour.Substring(3, 2), 16);
        int blue = Convert.ToInt32(colour.Substring(5, 2), 16);

        return red | (green << 8) | (blue << 16);
    }
}
