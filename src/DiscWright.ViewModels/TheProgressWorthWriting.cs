using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscWright.ViewModels;

/// <summary>
/// Which of the window's progress lines go in the log.
/// </summary>
/// <remarks>
/// <para>
/// Every new stage, and within a stage every new tenth of its percentage. A
/// burn or a check reports hundreds of times, and a line per report buries the
/// log; a line per stage was what it wrote until 2026-11-11.
/// </para>
/// <para>
/// That was not enough. The owner watched the check of a two disc set sit at
/// 0% and then jump, and the log could not say whether the number had climbed
/// in between or stood still: "checking the disc, 0%, file 5 of 41" was its
/// only line for the two and a half minutes of a 600 MB file, because 1% to
/// 87% of the same file was the same stage. A line at 10%, 20% and so on
/// answers that from the log next time, for a few lines a stage.
/// </para>
/// </remarks>
public sealed partial class TheProgressWorthWriting
{
    private string? _lastShape;
    private int _lastTenth = -1;

    /// <summary>Whether this line is worth a line in the log, remembering it if it is.</summary>
    public bool Worth(string doing)
    {
        if (doing.Length == 0)
        {
            return false;
        }

        string shape = ShapeOf(doing);
        int tenth = TenthOf(doing);

        if (shape == _lastShape && tenth <= _lastTenth)
        {
            return false;
        }

        _lastShape = shape;
        _lastTenth = tenth;
        return true;
    }

    /// <summary>
    /// A progress line with its numbers taken out, so that 45% and 46% of the
    /// same stage are the same line and the next stage is a new one.
    /// </summary>
    public static string ShapeOf(string doing) => Numbers().Replace(doing, "#");

    /// <summary>Which tenth of the way the line's percentage is, or -1 when it has none.</summary>
    public static int TenthOf(string doing)
    {
        Match percent = Percent().Match(doing);

        return percent.Success
            ? int.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture) / 10
            : -1;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex Percent();
}
