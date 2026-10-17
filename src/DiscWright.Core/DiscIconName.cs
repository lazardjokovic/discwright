using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DiscWright.Core;

/// <summary>
/// What a disc's icon file is called, which is never the same thing twice.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every disc used to ship its icon as <c>disc.ico</c>, and that is a
/// bug with a long tail.</strong> Explorer caches icon bitmaps keyed by path,
/// so <c>E:\disc.ico</c> was the same cache key for every disc that ever went
/// through that drive letter. Put a new disc in and Explorer would happily
/// redraw the previous disc's icon without re-reading the file, and the only
/// way back was clearing the icon cache, which nobody outside this program
/// knows to do. The taskbar caches by path too, so the menu window wore the
/// wrong icon for the same reason.
/// </para>
/// <para>
/// Naming the icon after the disc gives each one its own key. v1 learned this
/// and fixed it; the rewrite reintroduced the constant name and so brought the
/// bug back with it.
/// </para>
/// </remarks>
public static class DiscIconName
{
    /// <summary>What an icon file is called at the end.</summary>
    public const string Extension = ".ico";

    /// <summary>
    /// How long a name may get before it is cut.
    /// </summary>
    /// <remarks>
    /// Not a limit anything imposes. It is short enough to stay readable in a
    /// listing and long enough that two discs named after the same series are
    /// still told apart.
    /// </remarks>
    private const int LongestName = 40;

    /// <summary>
    /// The icon file name for one disc.
    /// </summary>
    /// <param name="label">The project's label, as it was typed.</param>
    /// <param name="discNumber">Which disc of the set this is, from one.</param>
    /// <param name="ofDiscs">How many discs there are. One means no number.</param>
    public static string For(string? label, int discNumber = 1, int ofDiscs = 1)
    {
        // THE RULE THE VOLUME NAME ALREADY PAID FOR. A disc in a set reserves
        // room for its number BEFORE the name is cut, not after. Cut
        // "A VERY LONG NAME INDEED" to forty and then add the number and the
        // name is forty-one; cut it to forty afterwards and the number comes
        // off the end, so every disc of the set is back to one shared cache
        // key and the whole point of this is lost.
        //
        // v1 appends the number to the label before any of this and so has
        // exactly that hole. It has never been hit because v1's labels are
        // short.
        string ending = ofDiscs > 1
            ? discNumber.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        string plain = OnlyLettersAndDigits(FoldedToAscii(label ?? string.Empty));

        if (plain.Length == 0)
        {
            // A label with no Latin letters or digits in it at all: Cyrillic,
            // Greek, Japanese, or a name made of punctuation. Falling back to
            // a constant here would put every such disc back on one shared
            // cache key, which is the thing being fixed, so the label is
            // hashed instead and two Japanese discs in one drive letter still
            // get different file names.
            plain = "disc" + ShortHashOf(label ?? string.Empty);
        }

        int room = Math.Max(1, LongestName - ending.Length);

        if (plain.Length > room)
        {
            plain = plain[..room];
        }

        return plain + ending + Extension;
    }

    /// <summary>
    /// Whether a name at the root of a disc is the name this disc's own icon
    /// would be written under.
    /// </summary>
    /// <remarks>
    /// Only the single disc case, because the question is asked of loose
    /// extras before the discs have been planned and so before anything knows
    /// how many there will be. A set's icons end in a digit and a loose file
    /// named for one of those would still be overwritten. It is a narrow hole
    /// and closing it means knowing the layout here, which would mean planning
    /// twice.
    /// </remarks>
    public static bool Claims(string name, string? label) =>
        string.Equals(name, For(label), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An accented letter reduced to the letter under it, rather than deleted.
    /// </summary>
    /// <remarks>
    /// Folding happens BEFORE the stripping below, or an accented letter is
    /// thrown away outright instead of reduced. v1 found this the hard way:
    /// the Polish spelling of "Wiedzmin" came out "Wiedmin", because the one
    /// accented letter was not in A to Z and so was simply dropped.
    /// </remarks>
    private static string FoldedToAscii(string text)
    {
        StringBuilder folded = new(text.Length);

        foreach (char character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(character);
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// A to Z, a to z and nought to nine, and nothing else at all.
    /// </summary>
    /// <remarks>
    /// Severe on purpose. This name is written into autorun.inf, which Windows
    /// reads in the system codepage rather than as UTF-8, and it is read back
    /// off an ISO 9660 disc by whatever is in the drive in ten years. A name
    /// made of the twenty-six letters survives all of that; a name with a
    /// space or an apostrophe in it is a name something eventually mangles.
    /// </remarks>
    private static string OnlyLettersAndDigits(string text) =>
        new([.. text.Where(char.IsAsciiLetterOrDigit)]);

    /// <summary>Eight hex characters of the label, to tell two of them apart.</summary>
    /// <remarks>
    /// SHA-256 rather than the MD5 v1 uses. Nothing here is a security
    /// question, so either would do the job, but MD5 is a warning in a build
    /// that treats warnings as errors and there is no reason to argue with it.
    /// The names do not have to match v1's: this is a cache key, not an
    /// identity, and a disc built by either program is read by neither.
    /// </remarks>
    private static string ShortHashOf(string label) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..8];
}
