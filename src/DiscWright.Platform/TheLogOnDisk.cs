using System.Globalization;
using System.Text;
using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// The log, as one plain text file a day in a folder of its own.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Small by construction.</strong> The owner asked whether it would
/// take much room, and the answer is written into the code rather than hoped
/// for: a fortnight of files at most, five megabytes between them at most,
/// and a day that reaches the limit on its own says so once and stops. A
/// night of burning a set is a few hundred lines, around fifty kilobytes.
/// </para>
/// <para>
/// Plain text, one line each, the time first, so it reads in Notepad and a
/// person can find the minute they remember.
/// </para>
/// <para>
/// Nothing in it leaves the machine. It is somewhere to look, not something
/// that reports.
/// </para>
/// </remarks>
/// <param name="folder">Where the files go, made when the first line is written.</param>
/// <param name="now">What the time is, handed in so a test can be any day it likes.</param>
public sealed class TheLogOnDisk(string folder, Func<DateTime>? now = null) : IKeepALog
{
    /// <summary>How many days of files are kept, today's included.</summary>
    public const int DaysKept = 14;

    /// <summary>The most every file in the folder may add up to.</summary>
    public const long AtMostBytes = 5L * 1024 * 1024;

    /// <summary>
    /// The most one day may take: half, so that today and the days before it
    /// can never add up to more than <see cref="AtMostBytes"/> between them.
    /// </summary>
    public const long ADayAtMost = AtMostBytes / 2;

    /// <summary>What the last line of a day that filled the log says.</summary>
    public const string Full = "The log is full for today, so nothing more is written until tomorrow.";

    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);
    private readonly Lock _oneAtATime = new();
    private DateTime? _tidiedOn;
    private bool _fullToday;

    /// <summary>The folder every user's log lives in on a real machine.</summary>
    /// <param name="package">
    /// The package family name of a copy from the Microsoft Store, or nothing
    /// for an installed or portable one.
    /// </param>
    /// <remarks>
    /// A packaged copy's writes into AppData are redirected to a private place
    /// that only it can see, so "Open the log" would hand Explorer or Notepad a
    /// folder they find empty. So a packaged copy writes its log into the
    /// package's own folder, LocalState, which is where Windows keeps a
    /// package's data in the open and every program can read it.
    /// </remarks>
    public static string ForThisUser(string? package = null) =>
        package is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscWright",
                "logs")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                package,
                "LocalState",
                "logs");

    /// <summary>Where the folder is, for the link that opens it.</summary>
    public string Folder => folder;

    /// <summary>Today's file, whether or not anything has been written to it yet.</summary>
    public string Today => FileFor(_now());

    /// <inheritdoc />
    public void Write(string line)
    {
        lock (_oneAtATime)
        {
            try
            {
                DateTime at = _now();

                if (_tidiedOn != at.Date)
                {
                    Directory.CreateDirectory(folder);
                    Tidy(at);
                    _tidiedOn = at.Date;
                    _fullToday = false;
                }

                if (_fullToday)
                {
                    return;
                }

                string path = FileFor(at);
                string text = At(at) + OnOneLine(line) + Environment.NewLine;

                long already = File.Exists(path) ? new FileInfo(path).Length : 0;

                // The room for the closing line is held back, so the day that
                // fills up still ends inside its share.
                if (already + Encoding.UTF8.GetByteCount(text) > ADayAtMost - 256)
                {
                    _fullToday = true;
                    text = At(at) + Full + Environment.NewLine;
                }

                File.AppendAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
            {
                // A line that cannot be written is a line lost, and nothing
                // more. The burn it describes carries on.
            }
        }
    }

    /// <summary>
    /// Drops the days past the fortnight, then the oldest of what is left
    /// until the folder fits.
    /// </summary>
    /// <remarks>
    /// Once a day, on the first line of the day, which is cheap enough that it
    /// needs no timer and frequent enough that the folder never outgrows it.
    /// Only files this class named are touched: anything else somebody puts
    /// in the folder is theirs.
    /// </remarks>
    private void Tidy(DateTime at)
    {
        List<(FileInfo File, DateTime Day)> days = [];

        foreach (FileInfo each in new DirectoryInfo(folder).GetFiles("????-??-??.txt"))
        {
            if (DateTime.TryParseExact(
                Path.GetFileNameWithoutExtension(each.Name), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
            {
                days.Add((each, day));
            }
        }

        DateTime oldestKept = at.Date.AddDays(-(DaysKept - 1));

        foreach ((FileInfo file, DateTime day) in days.Where(each => each.Day < oldestKept).ToList())
        {
            file.Delete();
            days.Remove((file, day));
        }

        // The days before today, which share the other half.
        long total = days.Where(each => each.Day != at.Date).Sum(each => each.File.Length);

        foreach ((FileInfo file, DateTime day) in days.Where(each => each.Day != at.Date).OrderBy(each => each.Day))
        {
            if (total <= ADayAtMost)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    private string FileFor(DateTime at) =>
        Path.Combine(folder, at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt");

    private static string At(DateTime at) => at.ToString("HH:mm:ss.fff  ", CultureInfo.InvariantCulture);

    /// <summary>
    /// A line is a line: a refusal's sentence can carry a newline, and a log
    /// read by the minute must not have entries that start without a time.
    /// </summary>
    private static string OnOneLine(string line) =>
        line.Replace("\r\n", " / ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace("\n", " / ", StringComparison.Ordinal);
}
