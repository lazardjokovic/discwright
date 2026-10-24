using System.Text;

namespace DiscWright.Core;

/// <summary>
/// Undoes the one encoding mistake other people's files carry: UTF-8 bytes
/// read as if they were Latin-1, so that "®" arrives as "Â®" and "é" as "Ã©".
/// </summary>
/// <remarks>
/// <para>
/// Found on the first real set, 2026-10-24. The installer was Windows' own
/// Character Map padded out, and its product name came off the disc as
/// "MicrosoftÂ® WindowsÂ® Operating System", on the menu, in its title bar
/// and in the set file. That is not our reading: the untouched copy in
/// System32 reports the same, because Microsoft built the resource that way.
/// Other people's installers do the same thing, and the name an installer
/// reports is the one thing on a disc that was typed by somebody else.
/// </para>
/// <para>
/// The test is strict so that it cannot fire on a real name. Every character
/// outside ASCII has to fit in one byte, and those bytes have to decode as
/// well formed UTF-8 to something different. "Café" has an é with nothing
/// after it, which is not well formed, and stays as it is. "STAR WARS™" has
/// a character that does not fit in a byte, and stays as it is.
/// </para>
/// </remarks>
public static class Mojibake
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>The text with the double encoding undone, or the text as it was.</summary>
    public static string Undone(string text)
    {
        bool anyOutsideAscii = false;

        foreach (char character in text)
        {
            if (character > 0xFF)
            {
                return text;
            }

            anyOutsideAscii |= character > 0x7F;
        }

        if (!anyOutsideAscii)
        {
            return text;
        }

        try
        {
            string decoded = StrictUtf8.GetString(Encoding.Latin1.GetBytes(text));

            return decoded.Length < text.Length ? decoded : text;
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }
}
