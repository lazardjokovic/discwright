using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// Waits, after a disc is written and its tray shut again, for the drive to
/// read it back in, and says how long that is taking while it does.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Found on an older laptop on 2026-11-12.</strong> After two of three
/// burns the window sat for seven minutes and then ten saying "flushing the
/// drive's buffer", the last thing the burn had reported. The burn was over.
/// In those minutes the disc was ejected, the tray was shut again, by the
/// drive or by a hand answering a box that asked for it, and the drive read
/// the disc back in, and neither the window nor the log said which of those
/// the time went on.
/// </para>
/// <para>
/// So the wait for the drive is said, with the time ticking, in words the
/// log keeps once and then once more when the disc is back, with how long it
/// took. The question about the tray is logged where every question is.
/// </para>
/// <para>
/// <strong>The drive is never asked on the window's thread.</strong> The
/// question can take as long as the drive takes to answer, which can be
/// minutes, so a real drive answers from the pool and the time goes on
/// ticking beside it. It used to be asked in the window, in a loop of sixty tries
/// meant to be a minute, and every try could block for as long as the drive
/// liked.
/// </para>
/// <para>
/// Gives up after <see cref="HowLongADriveGets.AtMost"/>, which for this is
/// a quarter of an hour, and answers nothing, which the caller reports as a
/// disc that could not be checked.
/// </para>
/// </remarks>
/// <param name="tray">What can say whether the drive has read the disc.</param>
/// <param name="patience">How long between asking, how long in all, and the clock.</param>
public sealed class WaitingForTheDiscToComeBack(IWorkTheTray tray, HowLongADriveGets patience)
{
    /// <summary>How long a real drive is given to read a disc back in.</summary>
    public static readonly TimeSpan AQuarterOfAnHour = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long it took for the drive to read the disc, or nothing when it
    /// had not after as long as it is given.
    /// </summary>
    /// <param name="drive">The drive the disc is in.</param>
    /// <param name="say">Where the wait is said, already worded for this disc.</param>
    /// <param name="ct">Stopping the whole run.</param>
    public async Task<TimeSpan?> For(Recorder drive, Action<string> say, CancellationToken ct)
    {
        DateTimeOffset started = patience.Now();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Task<bool> looking = tray.HasReadTheDisc(drive, ct);

            // The answer can be minutes away, so the clock goes on being said
            // beside it rather than after it.
            while (!looking.IsCompleted)
            {
                TimeSpan sofar = patience.Now() - started;

                if (sofar >= patience.AtMost)
                {
                    return null;
                }

                say(Waiting(drive, sofar));
                await Task.WhenAny(looking, patience.Waiting(patience.Between, ct));
                ct.ThrowIfCancellationRequested();
            }

            TimeSpan took = patience.Now() - started;

            if (await looking)
            {
                say(Back(drive, took));
                return took;
            }

            say(Waiting(drive, took));
            await patience.Waiting(patience.Between, ct);

            if (patience.Now() - started >= patience.AtMost)
            {
                return null;
            }
        }
    }

    /// <summary>What is said while the drive reads the disc in.</summary>
    public static string Waiting(Recorder drive, TimeSpan sofar) =>
        $"waiting for {drive.Drive} to read the disc back in, {BurnProgress.Clock((int)sofar.TotalSeconds)}";

    /// <summary>What is said once it has, which the log keeps with how long it took.</summary>
    public static string Back(Recorder drive, TimeSpan took) =>
        $"{drive.Drive} has read the disc back in after {BurnProgress.Clock((int)took.TotalSeconds)}";
}
