using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The wait between a disc being written and its check starting, which an
/// older laptop spent minutes in while the window said nothing new.
/// </summary>
public class WaitingForADiscToComeBack
{
    private static readonly Recorder TheDrive = new("id-E", "E:", "A Drive", 2, false, 0, [750]);

    [Fact]
    public async Task Answers_straight_away_for_a_drive_that_has_the_disc_already()
    {
        List<string> said = [];
        AClockThatMovesItself clock = new();

        TimeSpan? took = await Waiting(new ADrive(notReadFor: 0), clock).For(TheDrive, said.Add, CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, took);
        Assert.Equal(0, clock.Waits);
        Assert.Equal(["E: has read the disc back in after 0 sec"], said);
    }

    [Fact]
    public async Task Says_the_time_while_it_waits_and_how_long_it_took()
    {
        List<string> said = [];
        ADrive drive = new(notReadFor: 2);

        TimeSpan? took = await Waiting(drive, new AClockThatMovesItself()).For(TheDrive, said.Add, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(2), took);
        Assert.Equal(3, drive.Asked);
        Assert.Equal(
            [
                "waiting for E: to read the disc back in, 0 sec",
                "waiting for E: to read the disc back in, 1 sec",
                "E: has read the disc back in after 2 sec"
            ],
            said);
    }

    [Fact]
    public async Task Gives_up_after_as_long_as_it_is_given()
    {
        List<string> said = [];
        ADrive drive = new(notReadFor: -1);

        TimeSpan? took = await Waiting(drive, new AClockThatMovesItself()).For(TheDrive, said.Add, CancellationToken.None);

        Assert.Null(took);

        // A second apart for a quarter of an hour, and not one ask more.
        Assert.Equal(900, drive.Asked);
        Assert.Equal("waiting for E: to read the disc back in, 14 min 59 sec", said[^1]);
    }

    [Fact]
    public async Task Goes_on_saying_the_time_while_the_drive_takes_its_time_to_answer()
    {
        // The drive's answer itself is what takes the minutes: one ask that
        // does not come back. The time is said beside it, so the window does
        // not freeze on the last thing it said.
        List<string> said = [];
        using ManualResetEventSlim answer = new(false);
        ADrive drive = new(notReadFor: 0) { Holding = answer };

        TimeSpan? took = await Waiting(drive, new AClockThatMovesItself()).For(TheDrive, said.Add, CancellationToken.None);

        answer.Set();

        // Still unanswered at the end of a quarter of an hour, so given up on,
        // having said every second of it.
        Assert.Null(took);
        Assert.Equal(1, drive.Asked);
        Assert.Equal(900, said.Count);
        Assert.Equal("waiting for E: to read the disc back in, 7 min 3 sec", said[423]);
    }

    [Fact]
    public async Task Stops_when_the_run_is_stopped()
    {
        using CancellationTokenSource stop = new();
        List<string> said = [];
        ADrive drive = new(notReadFor: -1);

        Task<TimeSpan?> waiting = Waiting(drive, new AClockThatMovesItself()).For(
            TheDrive,
            line =>
            {
                said.Add(line);

                if (said.Count == 3)
                {
                    stop.Cancel();
                }
            },
            stop.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(3, said.Count);
    }

    [Fact]
    public void The_question_about_the_tray_reaches_the_log()
    {
        // It was a bare message box, the one question the log never heard,
        // so ten minutes between a burn and its check could not be told apart
        // from a drive that was slow. Every question through WorthAsking is
        // written down with its answer.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        int start = code.IndexOf("private Task<bool> ShutTheTray(Recorder drive)", StringComparison.Ordinal);
        Assert.True(start >= 0, "ShutTheTray is not where it was");

        int end = code.IndexOf("private ", start + 10, StringComparison.Ordinal);
        string shutTheTray = code[start..end];

        Assert.Contains("WorthAsking.Ask(", shutTheTray, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", shutTheTray, StringComparison.Ordinal);

        // And the window no longer waits for the drive itself, on its own
        // thread, where a slow drive froze it.
        Assert.DoesNotContain("Directory.EnumerateFileSystemEntries", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_drive_is_given_a_quarter_of_an_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), WaitingForTheDiscToComeBack.AQuarterOfAnHour);
    }

    // ------------------------------------------------------------- the rig

    private static WaitingForTheDiscToComeBack Waiting(IWorkTheTray drive, AClockThatMovesItself clock) =>
        new(drive, clock.Patience with { AtMost = WaitingForTheDiscToComeBack.AQuarterOfAnHour });

    /// <summary>A drive that has not read the disc for so many asks, or never.</summary>
    private sealed class ADrive(int notReadFor) : IWorkTheTray
    {
        private int _asked;

        internal int Asked => _asked;

        /// <summary>Something the answer waits on, for a drive slow to answer at all.</summary>
        internal ManualResetEventSlim? Holding { get; init; }

        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;

        public Task<bool> HasReadTheDisc(Recorder drive, CancellationToken ct)
        {
            int asked = Interlocked.Increment(ref _asked);

            // A drive slow to answer at all answers from the pool, the way a
            // real one does, and only once it is let go.
            return Holding is ManualResetEventSlim held
                ? Task.Run(() =>
                {
                    held.Wait(CancellationToken.None);
                    return notReadFor >= 0 && asked > notReadFor;
                })
                : Task.FromResult(notReadFor >= 0 && asked > notReadFor);
        }
    }
}
