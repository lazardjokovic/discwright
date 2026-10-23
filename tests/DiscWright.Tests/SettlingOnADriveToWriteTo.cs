using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Settling on a drive before anything is written: waiting for one that has
/// not said what is in it yet, asking which when there is a choice to make,
/// and offering to erase the disc that is in the way.
/// </summary>
/// <remarks>
/// <para>
/// All of this used to be private to <see cref="MakeADisc"/>, where the only
/// way in was to burn something. A mutation pass said what that cost:
/// <strong>the offer to erase the disc in a drive somebody had already picked
/// was reached by nothing at all</strong>, and that is the branch somebody
/// meets when the second disc of a set turns out to be a used one. So is the
/// giving up on a drive that never answers, which no test could reach without
/// sitting through fifteen real seconds.
/// </para>
/// <para>
/// Which is why the clock is handed in. Every wait in here happens instantly
/// and the drive is asked exactly as many times as it would really be asked.
/// </para>
/// </remarks>
public class SettlingOnADriveToWriteTo
{
    [Fact]
    public async Task Takes_the_one_ready_drive_without_asking_anybody()
    {
        Settled settled = await Settling(new OneBlankDisc("E:"))
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        // Asking somebody to pick the only drive they own is a decision shaped
        // like a chore.
        Assert.Equal("E:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
    }

    [Fact]
    public async Task Asks_which_drive_when_two_are_ready_and_takes_the_answer()
    {
        IReadOnlyList<Recorder>? offered = null;

        Settled settled = await Settling(new TwoBlankDiscs()).OnWhicheverIsReady(
            ASmallImage,
            ready =>
            {
                offered = ready;
                return Task.FromResult<Recorder?>(ready[1]);
            },
            NeverAsksAboutErasing);

        // Both of them, because guessing between two drives is how a disc ends
        // up written in the wrong one and the one in the other drive is often
        // somebody's only blank.
        Assert.Equal<IEnumerable<string>>(["D:", "E:"], offered!.Select(each => each.Drive));
        Assert.Equal("E:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
    }

    [Fact]
    public async Task Being_told_none_of_the_drives_is_not_a_failure()
    {
        Settled settled = await Settling(new TwoBlankDiscs()).OnWhicheverIsReady(
            ASmallImage, ready => Task.FromResult<Recorder?>(null), NeverAsksAboutErasing);

        // A question answered with no is a decision. A box afterwards saying
        // no drive was chosen is a window arguing with somebody.
        Assert.IsType<Settled.NobodyChose>(settled);
    }

    [Fact]
    public async Task Never_asks_which_drive_about_the_one_it_was_told_to_use()
    {
        Settled settled = await Settling(new TwoBlankDiscs())
            .OnThatOne("D:", ASmallImage, NeverAsksAboutErasing);

        // Two blanks in two drives, and no question, because the drive is
        // already settled: this is how every disc of a set after the first
        // arrives here.
        Assert.Equal("D:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
    }

    [Fact]
    public async Task Says_why_when_the_drive_it_was_told_to_use_is_not_there()
    {
        Settled settled = await Settling(new OneBlankDisc("E:"))
            .OnThatOne("D:", ASmallImage, NeverAsksAboutErasing);

        Assert.Contains("D:", Assert.IsType<Settled.No>(settled).Why.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_there_is_no_writer_when_the_machine_has_none()
    {
        Settled settled = await Settling(new NoDrivesAtAll())
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        Assert.IsType<Settled.No>(settled);
    }

    [Fact]
    public async Task Says_why_when_the_disc_in_the_drive_is_too_small_for_it()
    {
        // One sector free, and a megabyte to write into it.
        Settled settled = await Settling(new OneBlankDisc("E:", sectors: 1))
            .OnWhicheverIsReady(1024 * 1024, NeverAsksWhichDrive, NeverAsksAboutErasing);

        Assert.Contains(
            "E:",
            Assert.IsType<Settled.No>(settled).Why.Headline,
            StringComparison.Ordinal);
    }

    // ------------------------------------------- the disc that is in the way

    [Fact]
    public async Task Offers_to_erase_the_disc_in_the_way_and_tries_that_drive_again()
    {
        ADiscThatIsBlankOnceErased drives = new("D:");
        AnEraserThatWorks eraser = new(() => drives.Erased = true);

        Settled settled = await Settling(drives, eraser).OnWhicheverIsReady(
            ASmallImage, NeverAsksWhichDrive, could => Task.FromResult<Recorder?>(could[0]));

        // Erasing changes the answer, so the answer is asked for again.
        Assert.Equal("D:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
        Assert.Equal<IEnumerable<string>>(["D:"], eraser.Blanked);
    }

    [Fact]
    public async Task Offers_to_erase_the_disc_in_the_drive_it_was_told_to_use()
    {
        ADiscThatIsBlankOnceErased drives = new("D:");
        AnEraserThatWorks eraser = new(() => drives.Erased = true);

        Settled settled = await Settling(drives, eraser).OnThatOne(
            "D:", ASmallImage, could => Task.FromResult<Recorder?>(could[0]));

        // This is the one Lazar hit: a used disc put in for the second disc of
        // a set, where the drive is already settled and nothing was asking
        // which. Nothing reached it before this test existed.
        Assert.Equal("D:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
        Assert.Equal<IEnumerable<string>>(["D:"], eraser.Blanked);
    }

    [Fact]
    public async Task Never_offers_an_erase_while_there_is_a_blank_to_write_to()
    {
        Settled settled = await Settling(new OneBlankAndOneUsedRewritable())
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        // A blank in one drive and somebody's used rewritable in the other.
        // Taking the blank is the whole answer, and a box offering to wipe the
        // other disc is a box offering to destroy something for no reason.
        Assert.Equal("D:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
    }

    [Fact]
    public async Task Moves_the_bar_while_it_erases()
    {
        List<double> far = [];
        ADiscThatIsBlankOnceErased drives = new("D:");

        await Settling(
            drives,
            new AnEraserThatWorks(() => drives.Erased = true),
            (doing, how) => far.Add(how)).OnWhicheverIsReady(
            ASmallImage, NeverAsksWhichDrive, could => Task.FromResult<Recorder?>(could[0]));

        // The eraser is handed a fraction as well as a sentence, and throwing
        // it away leaves the bar at nothing for the whole half minute while
        // the words count up beside it, which reads as a program that has
        // stopped.
        Assert.Contains(0.5, far);
    }

    [Fact]
    public async Task Offers_only_the_disc_in_the_drive_it_was_told_about()
    {
        IReadOnlyList<Recorder>? offered = null;

        await Settling(new TwoUsedRewritables()).OnThatOne(
            "E:",
            ASmallImage,
            could =>
            {
                offered = could;
                return Task.FromResult<Recorder?>(null);
            });

        // Offering to wipe the disc in the other drive is offering to wipe a
        // disc nothing is being written to.
        Assert.Equal<IEnumerable<string>>(["E:"], offered!.Select(each => each.Drive));
    }

    [Fact]
    public async Task Offers_both_discs_when_either_drive_would_do()
    {
        IReadOnlyList<Recorder>? offered = null;

        await Settling(new TwoUsedRewritables()).OnWhicheverIsReady(
            ASmallImage,
            NeverAsksWhichDrive,
            could =>
            {
                offered = could;
                return Task.FromResult<Recorder?>(null);
            });

        Assert.Equal<IEnumerable<string>>(["D:", "E:"], offered!.Select(each => each.Drive));
    }

    [Fact]
    public async Task Declining_the_erase_leaves_the_refusal_that_says_the_disc_is_in_the_way()
    {
        Settled settled = await Settling(new OneUsedRewritable("D:")).OnWhicheverIsReady(
            ASmallImage, NeverAsksWhichDrive, could => Task.FromResult<Recorder?>(null));

        // Not silence, which is what being told no about a drive gets. The
        // disc in the way is still in the way, and the refusal is what says
        // so.
        Assert.Contains(
            "has something on it",
            Assert.IsType<Settled.No>(settled).Why.Headline,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offers_the_erase_once_and_never_twice()
    {
        AnEraserThatWorks eraser = new();
        int offers = 0;

        // A disc that is still not blank after being erased, which is a drive
        // that said it worked and did not.
        Settled settled = await Settling(new OneUsedRewritable("D:"), eraser).OnWhicheverIsReady(
            ASmallImage,
            NeverAsksWhichDrive,
            could =>
            {
                offers++;

                // Nothing the second time, so that something which does ask
                // twice ends and is told off rather than going round for ever.
                return Task.FromResult<Recorder?>(offers == 1 ? could[0] : null);
            });

        // Asking again would be a loop with somebody's evening in it.
        Assert.Equal(1, offers);
        Assert.Single(eraser.Blanked);
        Assert.IsType<Settled.No>(settled);
    }

    [Fact]
    public async Task Says_why_when_the_erase_itself_will_not_work()
    {
        Settled settled = await Settling(new OneUsedRewritable("D:"), new AnEraserThatRefuses())
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, could => Task.FromResult<Recorder?>(could[0]));

        // The erase's own refusal, not the one about the disc being in the
        // way: what went wrong is the erasing.
        Assert.Contains(
            "let go of",
            Assert.IsType<Settled.No>(settled).Why.Headline,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_which_disc_in_which_drive_while_it_erases()
    {
        List<string> said = [];
        ADiscThatIsBlankOnceErased drives = new("D:");

        await Settling(
            drives,
            new AnEraserThatWorks(() => drives.Erased = true),
            (doing, far) => said.Add(doing)).OnWhicheverIsReady(
            ASmallImage, NeverAsksWhichDrive, could => Task.FromResult<Recorder?>(could[0]));

        // Erasing a disc takes most of a minute. A window that says nothing
        // for that long looks like a window that has died.
        Assert.Contains("erasing the CD-RW in D:", said);
    }

    // ----------------------------------- a drive that has not answered yet

    [Fact]
    public async Task Waits_for_a_drive_that_has_not_said_what_is_in_it_yet()
    {
        ADriveSlowToAnswer drives = new(quietFor: 3);
        AClockThatMovesItself clock = new();

        Settled settled = await Settling(drives, patience: clock.Patience)
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        // A disc that has just been pushed in is still being read, and asking
        // then throws rather than answering. Taking the first silence for an
        // answer is how somebody got a dead end instead of a question.
        Assert.Equal("D:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
        Assert.Equal(4, drives.Asked);
        Assert.Equal(3, clock.Waits);
    }

    [Fact]
    public async Task Gives_up_on_a_drive_that_never_says_anything()
    {
        ADriveSlowToAnswer drives = new(quietFor: int.MaxValue);
        AClockThatMovesItself clock = new();

        Settled settled = await Settling(drives, patience: clock.Patience)
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        // Fifteen seconds, a second at a time, and then the silence is the
        // answer: once before the waiting starts and fifteen times inside it.
        Assert.Equal(16, drives.Asked);
        Assert.Equal(15, clock.Waits);
        Assert.IsType<Settled.No>(settled);
    }

    [Fact]
    public async Task Says_it_is_waiting_while_it_waits()
    {
        List<string> said = [];
        AClockThatMovesItself clock = new();

        await Settling(new ADriveSlowToAnswer(quietFor: 2), say: (doing, far) => said.Add(doing),
            patience: clock.Patience)
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        Assert.Contains("waiting for the drive", said);
    }

    [Fact]
    public async Task Says_nothing_at_all_when_every_drive_answers_at_once()
    {
        List<string> said = [];

        await Settling(new OneBlankDisc("E:"), say: (doing, far) => said.Add(doing))
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        // The ordinary case is a drive with a blank in it, and it must be
        // silent. A line about waiting over a thing that did not wait is a
        // window describing something that did not happen.
        Assert.Empty(said);
    }

    [Fact]
    public async Task Waits_when_any_one_of_them_has_not_answered()
    {
        AClockThatMovesItself clock = new();

        Settled settled = await Settling(new OneReadyAndOneStillThinking(), patience: clock.Patience)
            .OnWhicheverIsReady(ASmallImage, NeverAsksWhichDrive, NeverAsksAboutErasing);

        // One drive answered and the other has not, and what the quiet one is
        // holding decides whether there is a choice to make. Taking the ready
        // one straight away is deciding that question by not asking it.
        Assert.True(clock.Waits > 0, "it never waited");
        Assert.IsType<Settled.ThisOne>(settled);
    }

    [Fact]
    public async Task Waits_only_for_the_drive_it_was_told_about()
    {
        AClockThatMovesItself clock = new();

        Settled settled = await Settling(new OneReadyAndOneStillThinking(), patience: clock.Patience)
            .OnThatOne("D:", ASmallImage, NeverAsksAboutErasing);

        // D: has answered. Whatever the other drive is doing is nothing to do
        // with the disc going in this one.
        Assert.Equal(0, clock.Waits);
        Assert.Equal("D:", Assert.IsType<Settled.ThisOne>(settled).Drive.Drive);
    }

    [Fact]
    public void A_real_drive_gets_fifteen_seconds_and_is_asked_every_second()
    {
        // Measured on the ASUS writer here: a drive asked the moment a tray
        // shuts can take ten seconds to answer. The numbers are asserted so
        // that moving them is a thing somebody chose to do.
        Assert.Equal(TimeSpan.FromSeconds(1), HowLongADriveGets.WhatARealOneNeeds.Between);
        Assert.Equal(TimeSpan.FromSeconds(15), HowLongADriveGets.WhatARealOneNeeds.AtMost);
    }

    // ------------------------------------------------------------- the rig

    /// <summary>Something small enough for any blank in these tests.</summary>
    private const long ASmallImage = 1000;

    private static SettlingOnADrive Settling(
        IFindRecorders drives,
        IEraseDiscs? eraser = null,
        Action<string, double>? say = null,
        HowLongADriveGets? patience = null) =>
        new(drives,
            eraser ?? new AnEraserThatRefuses(),
            say ?? ((doing, far) => { }),
            patience ?? new AClockThatMovesItself().Patience);

    private static Task<Recorder?> NeverAsksWhichDrive(IReadOnlyList<Recorder> ready) =>
        throw new InvalidOperationException("It should not have had to ask which drive.");

    private static Task<Recorder?> NeverAsksAboutErasing(IReadOnlyList<Recorder> could) =>
        throw new InvalidOperationException("It should not have had to ask about erasing.");

    private sealed class NoDrivesAtAll : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class OneBlankDisc(string drive, long sectors = 360_000) : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id" + drive, drive, "A Drive", 2, true, sectors, [750])];
    }

    private sealed class TwoBlankDiscs : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 2, true, 360_000, [750]),
            new("id-E", "E:", "Another Drive", 2, true, 360_000, [750])
        ];
    }

    /// <summary>A CD-RW with something on it, which erasing would fix.</summary>
    private sealed class OneUsedRewritable(string drive) : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id" + drive, drive, "A Drive", 3, false, 0, [750])];
    }

    /// <summary>A blank in one drive and somebody's used rewritable in the other.</summary>
    private sealed class OneBlankAndOneUsedRewritable : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 2, true, 360_000, [750]),
            new("id-E", "E:", "Another Drive", 3, false, 0, [750])
        ];
    }

    private sealed class TwoUsedRewritables : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 3, false, 0, [750]),
            new("id-E", "E:", "Another Drive", 3, false, 0, [750])
        ];
    }

    /// <summary>
    /// A used rewritable that really does come back blank, the way a drive
    /// that erased it would answer the next time it is asked.
    /// </summary>
    private sealed class ADiscThatIsBlankOnceErased(string drive) : IFindRecorders
    {
        internal bool Erased { get; set; }

        public IReadOnlyList<Recorder> All() =>
        [
            Erased
                ? new Recorder("id" + drive, drive, "A Drive", 3, true, 360_000, [750])
                : new Recorder("id" + drive, drive, "A Drive", 3, false, 0, [750])
        ];
    }

    /// <summary>
    /// A drive that says nothing for the first few asks, the way a real one
    /// does while it is still reading what was just pushed in.
    /// </summary>
    private sealed class ADriveSlowToAnswer(int quietFor) : IFindRecorders
    {
        internal int Asked { get; private set; }

        public IReadOnlyList<Recorder> All()
        {
            Asked++;

            return Asked <= quietFor
                ? [new("id-D", "D:", "A Drive", 0, false, 0, [], "the device is not ready")]
                : [new("id-D", "D:", "A Drive", 2, true, 360_000, [750])];
        }
    }

    private sealed class OneReadyAndOneStillThinking : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 2, true, 360_000, [750]),
            new("id-E", "E:", "Another Drive", 0, false, 0, [], "the device is not ready")
        ];
    }

    /// <param name="andSoTheDiscIsBlank">
    /// What erasing does to the drives, for a fake that answers differently
    /// once its disc has been wiped.
    /// </param>
    private sealed class AnEraserThatWorks(Action? andSoTheDiscIsBlank = null) : IEraseDiscs
    {
        internal List<string> Blanked { get; } = [];

        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct)
        {
            Blanked.Add(disc.Drive);
            progress?.Report(new EraseProgress("erasing", 0.5));
            andSoTheDiscIsBlank?.Invoke();

            return Task.FromResult<Outcome<string>>("blank again");
        }
    }

    private sealed class AnEraserThatRefuses : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.WindowsLetGoOfTheDrive(disc.Drive, "it stopped answering"));
    }
}

/// <summary>
/// A clock that moves itself, so that waiting for a drive costs no time at
/// all.
/// </summary>
/// <remarks>
/// <para>
/// Every wait advances it by exactly as long as the wait asked for, so the
/// drive is asked the same number of times it would really be asked and the
/// giving up happens at the same place. Two tests of a slow drive used to sit
/// through three real seconds each for want of this.
/// </para>
/// <para>
/// Not nested in a test class, because the window's own tests of a set want it
/// too: a view model built with this in its machine walks a set without
/// waiting for anything.
/// </para>
/// </remarks>
internal sealed class AClockThatMovesItself
{
    /// <summary>How many times something waited on it.</summary>
    internal int Waits { get; private set; }

    internal HowLongADriveGets Patience => new(
        HowLongADriveGets.WhatARealOneNeeds.Between,
        HowLongADriveGets.WhatARealOneNeeds.AtMost,
        (how, ct) =>
        {
            Waits++;
            _now += how;

            return Task.CompletedTask;
        },
        () => _now);

    private DateTimeOffset _now = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
}
