using System.Globalization;
using System.Text;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// The system ANSI codepage, which is what AutoRun reads a disc's name in.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not <c>Encoding.Default</c>.</strong> In Windows PowerShell that is
/// the ANSI codepage, which is why v1 writes <c>autorun.inf</c> with it and
/// says so. In .NET it is <strong>UTF-8</strong>, so the same line here would
/// compile, run, look right in an editor, and write a file AutoRun reads
/// wrongly: a byte order mark it does not understand, followed by multi-byte
/// characters it will read as pairs of accented letters.
/// </para>
/// <para>
/// Reaching the real codepage needs <c>CodePagesEncodingProvider</c> registered
/// first. On .NET 10 that provider ships in the framework and an explicit
/// package reference for it is refused as redundant, but registering it is
/// still required: without that call <c>GetEncoding(1252)</c> throws, because
/// only UTF-8 and ASCII are there to begin with. Both machines here are 1252,
/// where "Uber Alles" survives intact and Cyrillic and Japanese become
/// question marks.
/// </para>
/// </remarks>
public sealed class WindowsAutorunEncoding : IAutorunEncoding
{
    private static readonly Encoding Ansi = FindTheAnsiCodepage();

    /// <summary>
    /// The encoding to write <c>autorun.inf</c> in. Never UTF-8: ASCII turned
    /// every accented character into a literal question mark, so "Uber Alles"
    /// reached Explorer as "?ber Alles", while this carries the Latin-1 accents
    /// and the typographic dashes GOG titles are full of.
    /// </summary>
    public static Encoding ForAutorunFiles => Ansi;

    public string AsAutorunWillSeeIt(string text) => Ansi.GetString(Ansi.GetBytes(text));

    private static Encoding FindTheAnsiCodepage()
    {
        // Registered once. Without it, GetEncoding for anything but UTF-8 and
        // ASCII throws on .NET, which is the trap this whole class exists for.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        int codepage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

        try
        {
            // Best-fit mapping on the way out, deliberately. A Polish z-acute
            // then arrives as a plain z rather than as a question mark, which
            // is a better disc than a worse one.
            return Encoding.GetEncoding(codepage);
        }
        catch (NotSupportedException)
        {
            return Encoding.GetEncoding(1252);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(1252);
        }
    }
}
