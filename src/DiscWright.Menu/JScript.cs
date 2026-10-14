using System.Globalization;
using System.Text;

namespace DiscWright.Menu;

/// <summary>
/// Turning a value into something safe to put inside the menu's script.
/// </summary>
/// <remarks>
/// A game's name is written by somebody else and lands inside a JScript string
/// literal in an HTA. That is three different sets of rules at once, and
/// getting any of them wrong breaks a disc that is already burned.
/// </remarks>
public static class JScript
{
    /// <summary>
    /// A string, quoted and escaped, ready to be pasted into the menu.
    /// </summary>
    public static string Literal(string? value)
    {
        if (value is null)
        {
            return "\"\"";
        }

        StringBuilder safe = new(value.Length + 2);
        safe.Append('"');

        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    safe.Append("\\\\");
                    break;

                case '"':
                    safe.Append("\\\"");
                    break;

                // The one that is not about JScript at all. A name containing
                // "</script>" would END THE SCRIPT BLOCK, because the HTA is
                // parsed as markup before any of it is script, and the rest of
                // the menu would be shown as text. Escaping the angle bracket
                // means the parser never sees the tag while JScript still reads
                // the same character.
                case '<':
                    safe.Append("\\x3C");
                    break;

                case '>':
                    safe.Append("\\x3E");
                    break;

                case '&':
                    safe.Append("\\x26");
                    break;

                default:
                    // Control characters are removed rather than escaped. A
                    // newline inside a literal is an unterminated string, the
                    // whole HTA fails to parse, and the disc opens to nothing.
                    // The window cannot produce one, but a project file read
                    // off disk can carry one, and that is the path that reaches
                    // a build.
                    if (character < ' ' || character == '\u007F')
                    {
                        break;
                    }

                    safe.Append(character);
                    break;
            }
        }

        safe.Append('"');

        return safe.ToString();
    }

    /// <summary>A whole number, written the same way on every machine.</summary>
    /// <remarks>
    /// Invariant, unlike every size a person reads. This is code, and a
    /// thousands separator or a decimal comma in the middle of it would be a
    /// syntax error on a Serbian machine and nowhere else.
    /// </remarks>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Number(int)"/>
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A list of strings, as a JScript array.</summary>
    public static string ListOf(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(Literal)) + "]";
}
