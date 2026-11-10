using System.Globalization;

namespace DiscWright.Core;

/// <summary>
/// The drives, asked the usual way, with every answer written in the log.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The question nothing could answer on 2026-11-10.</strong> A set
/// stopped at disc 2 saying there was no disc writer, and Erase a disc said
/// the same thing twenty minutes later, on a machine with two. What Windows
/// had listed, and how long it took to list it, was nowhere. Here it is in
/// one line per ask: every drive, what it is holding, and how long the asking
/// took when it took long enough to notice.
/// </para>
/// <para>
/// <strong>An answer the same as the last one is not written again.</strong>
/// The settling asks once a second for up to fifteen seconds while it waits,
/// and fifteen identical lines say less than one line and how long it lasted.
/// A slow ask is always written, because slowness is the news.
/// </para>
/// </remarks>
/// <param name="drives">The drives, asked the real way.</param>
/// <param name="log">Where the answers go.</param>
/// <param name="now">The clock, so that a test can make an ask slow.</param>
public sealed class TheDrivesWrittenDown(IFindRecorders drives, IKeepALog log, Func<DateTime>? now = null)
    : IFindRecorders
{
    /// <summary>How long an ask has to take before how long is written down.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromSeconds(1);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly Lock _oneAtATime = new();
    private string? _lastSaid;

    /// <inheritdoc />
    public IReadOnlyList<Recorder> All()
    {
        DateTime started = _now();
        IReadOnlyList<Recorder> found = drives.All();
        TimeSpan took = _now() - started;

        string said = InWords(found);
        bool slow = took >= Slow;

        lock (_oneAtATime)
        {
            if (said == _lastSaid && !slow)
            {
                return found;
            }

            _lastSaid = said;
        }

        log.Write(slow
            ? $"{said} (took {took.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s)"
            : said);

        return found;
    }

    /// <summary>The whole answer, in one line.</summary>
    public static string InWords(IReadOnlyList<Recorder> found) =>
        found.Count == 0
            ? "Drives: Windows listed none."
            : "Drives: " + string.Join("; ", found.Select(About)) + ".";

    private static string About(Recorder drive)
    {
        string who = string.IsNullOrEmpty(drive.Drive) ? drive.Name : $"{drive.Drive} {drive.Name}";

        if (drive.NothingInIt)
        {
            return $"{who}, nothing in it";
        }

        if (drive.WhyNot is string why)
        {
            return $"{who}, did not answer ({why})";
        }

        return drive.Blank
            ? $"{who}, blank {drive.Holding}, {drive.FreeSectors.ToString("N0", CultureInfo.InvariantCulture)} sectors free"
            : $"{who}, {drive.Holding} with something on it";
    }
}
