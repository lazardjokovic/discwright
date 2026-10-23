using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Writing a whole set, one disc at a time, in order, and picking one up again
/// where it was left.
/// </summary>
/// <remarks>
/// A set is only a set once every disc is written, and the things most likely
/// to go wrong are burning them out of order and losing count.
/// </remarks>
public class WalkingThroughASet
{
    [Fact]
    public async Task Writes_every_disc_in_order()
    {
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        Assert.Equal([@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso", @"C:\out\A SET D3.iso"], burner.Wrote);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.True(went.Whole);
        Assert.Contains("All 3 discs", went.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asks_for_a_blank_between_discs_and_not_before_the_first()
    {
        List<int> askedFor = [];
        MakeADisc window = await ASetOf(3, new ABurnerThatRemembers());

        await window.BurnTheSet(
            ThreeImages,
            OnlyOneDrive,
            NeverErases,
            Closes,
            (number, of, drive) =>
            {
                askedFor.Add(number);
                return Task.FromResult(true);
            },
            NeverAsksToCarryOn);

        // Disc 1 goes in the drive that was already settled; it is discs 2 and
        // 3 that need somebody to swap the written one out.
        Assert.Equal([2, 3], askedFor);
    }

    [Fact]
    public async Task Stops_where_it_is_when_nobody_puts_the_next_blank_in()
    {
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes,
            (number, of, drive) => Task.FromResult(number < 3),
            NeverAsksToCarryOn);

        // A decision, not a failure, and it says exactly where to carry on.
        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.False(went.Whole);
        Assert.Equal(2, went.Written);
        Assert.Equal(3, went.CarryOnFrom);
        Assert.Contains(
            "Discs 1 to 2 are written and checked. It stopped at disc 3 of 3: nobody put the next blank in",
            went.InWords,
            StringComparison.Ordinal);

        Assert.Equal(2, burner.Wrote.Count);
    }

    [Fact]
    public async Task Stops_when_a_disc_does_not_read_back()
    {
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner, checksOut: false);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Disc 2 is no use at all if disc 1 came out wrong, and carrying on
        // would spend the rest of somebody's blanks on a broken set.
        Assert.Single(burner.Wrote);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Equal(0, went.Written);

        // Not "nothing was written", which is what it used to say and is
        // false here: a disc was written and then failed its check, and the
        // blank is spent whatever the sentence says.
        Assert.Contains("No disc of the set is finished", went.InWords, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing was written", went.InWords, StringComparison.Ordinal);
        Assert.Contains("stopped at disc 1 of 3", went.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_out_loud_that_a_disc_could_not_be_checked()
    {
        // The staging folder has gone and the disc carries no list, so there
        // is nothing to compare the disc with. The disc is written and that is
        // all anybody knows, which is not the same as a disc that failed.
        List<string> announced = [];
        MakeADisc window = await ASetOf(2, new ABurnerThatRemembers(), disk: new ADiskWithNothingLeftBesideIt());

        window.PropertyChanged += (sender, about) => announced.Add(about.PropertyName ?? string.Empty);

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Said, and not only decided. One half of this used to set the answer
        // and tell nobody, so a window showing the previous disc's result went
        // on showing it over a disc nothing had looked at. Nothing caught it
        // because nothing in the suite can press Burn.
        Assert.Contains(nameof(MakeADisc.HowTheDiscCameOut), announced);
        Assert.Null(window.HowTheDiscCameOut);

        Refusal why = Assert.IsType<Refusal>(window.ItWentWrong);
        Assert.Equal("The disc was written but not checked", why.Headline);
    }

    [Fact]
    public async Task Never_keeps_one_discs_answer_over_a_disc_nothing_could_check()
    {
        // Disc 1 was built, burned and read back clean. Disc 2's folder has
        // gone, so there is nothing to compare it with.
        MakeADisc window = await ASetOf(
            2, new ABurnerThatRemembers(), disk: new ADiskThatKeptOnlyTheFirstDiscsFolder());

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Leaving disc 1's answer in place puts a tick beside a disc nothing
        // ever looked at, which is the worst way for this program to be wrong.
        Assert.Null(window.HowTheDiscCameOut);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Equal(1, went.Written);
    }

    // ------------------------------------- what somebody watching it sees

    [Fact]
    public async Task Says_which_disc_of_how_many_at_every_step()
    {
        MakeADisc window = await ASetOf(2, new ABurnerThatRemembers());
        List<string> said = [];

        window.PropertyChanged += (sender, about) =>
        {
            if (about.PropertyName == nameof(MakeADisc.Doing) && window.Doing.Length > 0)
            {
                said.Add(window.Doing);
            }
        };

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Which disc, and which of how many, in every line. Half an hour of
        // watching a bar with no idea how many discs are left is the thing
        // these sentences exist to prevent.
        Assert.Contains("disc 1 of 2", said);
        Assert.Contains("waiting for a blank disc for disc 2 of 2", said);
        Assert.Contains("disc 2 of 2", said);
        Assert.All(said, line => Assert.Contains(" of 2", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Leaves_nothing_running_when_the_set_is_finished()
    {
        MakeADisc window = await ASetOf(2, new ABurnerThatRemembers());
        List<string> announced = [];

        window.PropertyChanged += (sender, about) => announced.Add(about.PropertyName ?? string.Empty);

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // A window left saying it is working is a window with every button
        // dead and a bar part way across, over a set that finished.
        Assert.True(window.NothingIsRunning);
        Assert.Equal(string.Empty, window.Doing);
        Assert.Equal(0, window.HowFar);

        // Said, and not only true. Twice over: once when it started and once
        // when it stopped.
        Assert.Contains(nameof(MakeADisc.HowTheSetWent), announced);
        Assert.True(
            announced.Count(each => each == nameof(MakeADisc.IsWorking)) >= 2,
            "it did not say both that it had started and that it had stopped");
    }

    [Fact]
    public async Task Stops_before_the_next_disc_when_somebody_cancels()
    {
        using CancellationTokenSource stop = new();
        ABurnerThatRemembers burner = new();

        // The note is left the moment a disc has been written and read back
        // clean, which is the one place in the walk where a disc is finished
        // and the next has not begun.
        MakeADisc window = await ASetOf(3, burner, note: new ANoteThatCancels(stop));

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn, stop.Token);

        // One blank spent, not two. Somebody who presses stop between discs
        // has stopped it, and a disc written after that is a disc they did not
        // ask for.
        Assert.Single(burner.Wrote);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Equal(1, went.Written);
        Assert.Contains("It stopped at disc 2 of 3: it was stopped", went.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_so_when_nobody_picks_a_drive()
    {
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner, drives: new TwoDrives());

        await window.BurnTheSet(
            ThreeImages,
            ready => Task.FromResult<Recorder?>(null),
            NeverErases,
            Closes,
            AlwaysAnother,
            NeverAsksToCarryOn);

        // Two blanks in two drives and somebody who closed the question. That
        // is a decision rather than a fault, and nothing was written.
        Assert.Empty(burner.Wrote);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Contains("It stopped at disc 1 of 3: no drive was chosen", went.InWords, StringComparison.Ordinal);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Writes_the_whole_set_to_the_one_drive_it_settled_on()
    {
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner, drives: new TwoDrives());

        int asked = 0;

        await window.BurnTheSet(
            ThreeImages,
            ready =>
            {
                asked++;
                return Task.FromResult<Recorder?>(ready.First(each => each.Drive == "E:"));
            },
            NeverErases,
            Closes,
            AlwaysAnother,
            NeverAsksToCarryOn);

        // A set that wanders between two writers is a set whose discs were
        // written by two different lasers at two different speeds.
        Assert.Equal(3, burner.Onto.Count);
        Assert.All(burner.Onto, drive => Assert.Equal("E:", drive));

        // And asked once, before the first disc. Asking again for disc 2 is
        // both how a set wanders and how somebody is made to answer the same
        // question three times.
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Says_nothing_happened_when_there_are_no_images()
    {
        MakeADisc window = await ASetOf(3, new ABurnerThatRemembers());

        await window.BurnTheSet(
            [], OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        Assert.Null(window.HowTheSetWent);
    }

    // ------------------------------------------------- coming back to a set

    [Fact]
    public async Task Keeps_the_place_after_every_disc_that_reads_back()
    {
        ANoteInMemory note = new();
        MakeADisc window = await ASetOf(3, new ABurnerThatRemembers(), note: note);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Once per disc, not once at the end: the run that needs this is the
        // one that never reaches the end.
        Assert.Equal(3, note.Left);

        TheNoteBesideTheDiscs kept = Assert.IsType<TheNoteBesideTheDiscs>(TheNoteBesideTheDiscs.Read(note.Note));
        Assert.Equal(3, kept.Written);
        Assert.Equal("A SET", kept.Label);
    }

    [Fact]
    public async Task Keeps_nothing_for_a_disc_that_did_not_read_back()
    {
        ANoteInMemory note = new();
        MakeADisc window = await ASetOf(3, new ABurnerThatRemembers(), checksOut: false, note: note);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Written is not the same as readable, and a note that counted a
        // coaster as done would send somebody home with a broken set.
        Assert.Equal(0, note.Left);
        Assert.Null(note.Note);
    }

    [Fact]
    public async Task Carries_on_from_the_disc_after_the_last_one_written()
    {
        ABurnerThatRemembers burner = new();
        ANoteInMemory note = new(NoteSaying(2, 3));
        MakeADisc window = await ASetOf(3, burner, note: note);

        int askedAbout = 0;

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother,
            (number, of) =>
            {
                askedAbout = number;
                return Task.FromResult(true);
            });

        // Disc 3 and nothing else. Discs 1 and 2 are already good, and writing
        // them again costs blanks and twenty minutes.
        Assert.Equal([@"C:\out\A SET D3.iso"], burner.Wrote);
        Assert.Equal(3, askedAbout);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.True(went.Whole);
        Assert.Equal(3, went.Written);
        Assert.Equal(2, went.BroughtForward);
    }

    [Fact]
    public async Task Does_not_make_anybody_choose_a_drive_before_being_asked_about_carrying_on()
    {
        ANoteInMemory note = new(NoteSaying(2, 3));
        MakeADisc window = await ASetOf(3, new ABurnerThatRemembers(), note: note, drives: new TwoDrives());

        List<string> order = [];

        await window.BurnTheSet(
            ThreeImages,
            ready =>
            {
                order.Add("drive");
                return Task.FromResult<Recorder?>(ready[0]);
            },
            NeverErases,
            Closes,
            AlwaysAnother,
            (number, of) =>
            {
                order.Add("carry on");
                return Task.FromResult(true);
            });

        // Somebody about to carry on from disc 3 should not be answering
        // questions about drives first.
        Assert.Equal(["carry on", "drive"], order);
    }

    [Fact]
    public async Task Does_not_ask_for_a_blank_before_the_first_disc_it_carries_on_with()
    {
        List<int> askedFor = [];
        MakeADisc window = await ASetOf(
            3, new ABurnerThatRemembers(), note: new ANoteInMemory(NoteSaying(2, 3)));

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes,
            (number, of, drive) =>
            {
                askedFor.Add(number);
                return Task.FromResult(true);
            },
            (number, of) => Task.FromResult(true));

        // Disc 3 goes into the drive that was just settled, exactly as disc 1
        // would have. Asking for a blank first would be asking somebody to
        // swap a disc that is not in there yet.
        Assert.Empty(askedFor);
    }

    [Fact]
    public async Task Looks_at_the_disc_it_is_about_to_write_and_not_the_first_one()
    {
        ABurnerThatRemembers burner = new();

        // Disc 1 is far too big for what is in the drive. Disc 3 fits. Coming
        // back to write disc 3 must not be refused over disc 1's size.
        long HowBig(string image) => image.Contains("D1", StringComparison.Ordinal) ? 10_000_000 : 1000;

        // The note has to agree with the images beside it, sizes and all, or
        // it would rightly be ignored and prove nothing.
        string note = new TheNoteBesideTheDiscs(
            "A SET",
            [.. ThreeImages.Select(each => new NotedImage(System.IO.Path.GetFileName(each), HowBig(each)))],
            2).AsText();

        MakeADisc window = await ASetOf(
            3,
            burner,
            note: new ANoteInMemory(note),
            drives: new OneSmallDrive(),
            howBig: HowBig);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother,
            (number, of) => Task.FromResult(true));

        Assert.Equal([@"C:\out\A SET D3.iso"], burner.Wrote);
        Assert.True(Assert.IsType<TheSetSoFar>(window.HowTheSetWent).Whole);
    }

    [Fact]
    public async Task Starts_again_when_that_is_what_was_asked()
    {
        ABurnerThatRemembers burner = new();
        ANoteInMemory note = new(NoteSaying(2, 3));
        MakeADisc window = await ASetOf(3, burner, note: note);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother,
            (number, of) => Task.FromResult(false));

        // The discs may have been thrown away, or somebody may want a second
        // copy. Both are ordinary, so the answer is theirs.
        Assert.Equal(3, burner.Wrote.Count);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Equal(0, went.BroughtForward);
    }

    [Fact]
    public async Task Does_not_ask_about_carrying_on_when_there_is_no_note()
    {
        MakeADisc window = await ASetOf(3, new ABurnerThatRemembers());

        // NeverAsksToCarryOn throws if it is called at all.
        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        Assert.True(Assert.IsType<TheSetSoFar>(window.HowTheSetWent).Whole);
    }

    [Fact]
    public async Task Ignores_a_note_that_is_not_about_these_discs()
    {
        ABurnerThatRemembers burner = new();

        // The same set, built again: same names, different sizes. The discs on
        // the shelf are of something else now.
        string rebuilt = new TheNoteBesideTheDiscs(
            "A SET",
            [.. ThreeImages.Select(each => new NotedImage(System.IO.Path.GetFileName(each), 999))],
            2).AsText();

        MakeADisc window = await ASetOf(3, burner, note: new ANoteInMemory(rebuilt));

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Ignoring a good note costs one disc. Trusting a stale one puts
        // somebody else's disc 1 in the set.
        Assert.Equal(3, burner.Wrote.Count);
    }

    [Fact]
    public async Task Does_not_offer_to_carry_on_with_a_set_that_is_finished()
    {
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner, note: new ANoteInMemory(NoteSaying(3, 3)));

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        // Pressing burn on a finished set is how a second copy gets made, and
        // being asked whether to carry on from disc 4 of 3 would be nonsense.
        Assert.Equal(3, burner.Wrote.Count);
    }

    [Fact]
    public async Task Says_which_discs_were_written_just_now_and_which_earlier()
    {
        MakeADisc window = await ASetOf(
            3, new ABurnerThatRemembers(), note: new ANoteInMemory(NoteSaying(2, 3)));

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother,
            (number, of) => Task.FromResult(true));

        // Claiming every file was checked, in one breath, about discs this run
        // never saw would be claiming more than it knows.
        Assert.Equal(
            "All 3 discs are written and checked. Disc 3 was written just now, and discs 1 to 2 earlier.",
            Assert.IsType<TheSetSoFar>(window.HowTheSetWent).InWords);
    }

    // ------------------------------------------------------------------ helpers

    // ------------------------------- when it goes wrong half way through

    [Fact]
    public async Task Stops_when_the_tray_will_not_shut_on_a_disc_in_the_middle()
    {
        // The one branch that really happens on this machine: the Blu-ray
        // writer at E: cannot shut its own tray, so a set burned there waits
        // for a hand after every disc, and a hand that never comes has to
        // stop the set rather than carry on to disc 3 over an unchecked 2.
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(3, burner);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, ShutsUntilDisc(2), AlwaysAnother, NeverAsksToCarryOn);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);

        Assert.False(went.Whole);
        Assert.Equal(1, went.Written);
        Assert.Contains("could not be read back", went.InWords, StringComparison.Ordinal);

        // Disc 2 was written and never checked, so the set stops there rather
        // than spending a third blank on a set that is already broken.
        Assert.Equal(2, burner.Wrote.Count);
        Assert.NotNull(window.ItWentWrong);
    }

    [Fact]
    public async Task Says_it_was_stopped_when_somebody_stops_it()
    {
        using CancellationTokenSource stopping = new();
        ABurnerThatStopsEverything burner = new(stopping);
        MakeADisc window = await ASetOf(3, burner);

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn,
            stopping.Token);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);

        // Stopped is not the same as broken, and a set that was stopped has
        // to say so rather than name a disc as the thing that went wrong.
        Assert.False(went.Whole);
        Assert.Contains("stopped", went.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offers_to_erase_what_is_in_the_drive_when_the_next_blank_is_not_blank()
    {
        // Nobody put a blank in, but what is in there is a rewritable with
        // something on it. Offered, never done without being asked.
        ADriveHoldingAUsedDisc drive = new();
        ABurnerThatRemembers burner = new();
        AnEraserThatCounts eraser = new(drive);

        MakeADisc window = await ASetOf(2, burner, drives: drive, eraser: eraser);

        await window.BurnTheSet(
            [.. ThreeImages.Take(2)], OnlyOneDrive, ErasesWhatIsOffered, Closes,
            AlwaysAnother, NeverAsksToCarryOn);

        Assert.Equal(1, eraser.Erased);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.True(went.Whole);
    }

    [Fact]
    public async Task Stops_when_nothing_usable_can_be_got_into_the_drive()
    {
        // The same drive, and this time nobody wants it erased either.
        ADriveHoldingAUsedDisc drive = new();
        ABurnerThatRemembers burner = new();

        MakeADisc window = await ASetOf(2, burner, drives: drive);

        await window.BurnTheSet(
            [.. ThreeImages.Take(2)], OnlyOneDrive, SaysNoToErasing, Closes,
            AlwaysAnother, NeverAsksToCarryOn);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);

        Assert.False(went.Whole);
        Assert.Equal(0, went.Written);
        Assert.Empty(burner.Wrote);
    }

    [Fact]
    public async Task Says_which_disc_it_is_on_while_it_writes_one()
    {
        // The line somebody watches for a quarter of an hour. It carries the
        // disc number as well as the step, because "writing" on its own says
        // nothing about how far through a set of three it is.
        List<string> said = [];
        ABurnerThatReportsItsProgress burner = new(said);
        MakeADisc window = await ASetOf(3, burner);
        window.PropertyChanged += (_, what) =>
        {
            if (what.PropertyName == nameof(MakeADisc.Doing))
            {
                lock (said) { said.Add(window.Doing); }
            }
        };

        await window.BurnTheSet(
            ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother, NeverAsksToCarryOn);

        lock (said)
        {
            Assert.Contains(said, each => each.Contains("disc 1 of 3: ", StringComparison.Ordinal));
            Assert.Contains(said, each => each.Contains("disc 3 of 3: ", StringComparison.Ordinal));
        }

        // And never a disc that is not in the set.
        lock (said)
        {
            Assert.DoesNotContain(said, each => each.Contains("disc 4 of 3", StringComparison.Ordinal));
            Assert.DoesNotContain(said, each => each.Contains("disc 0 of 3", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------- the fakes for those

    /// <summary>A tray that shuts until the disc given, and then will not.</summary>
    private static Func<Recorder, Task<bool>> ShutsUntilDisc(int which)
    {
        int shut = 0;

        return _ => Task.FromResult(++shut < which);
    }

    private static Task<Recorder?> ErasesWhatIsOffered(IReadOnlyList<Recorder> could) =>
        Task.FromResult<Recorder?>(could.Count > 0 ? could[0] : null);

    /// <summary>
    /// Asked, and said no. NeverErases is an assertion that the question was
    /// not put at all, which is a different thing.
    /// </summary>
    private static Task<Recorder?> SaysNoToErasing(IReadOnlyList<Recorder> could) =>
        Task.FromResult<Recorder?>(null);

    /// <summary>A drive holding a rewritable with something already on it.</summary>
    private sealed class ADriveHoldingAUsedDisc : IFindRecorders
    {
        private bool _erased;

        internal void Wiped() => _erased = true;

        public IReadOnlyList<Recorder> All() =>
            [new("id-E", "E:", "A Drive", 3, _erased, _erased ? 360_000 : 0, [750])];
    }

    private sealed class AnEraserThatCounts(ADriveHoldingAUsedDisc drive) : IEraseDiscs
    {
        internal int Erased { get; private set; }

        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct)
        {
            Erased++;
            drive.Wiped();

            return Task.FromResult<Outcome<string>>("blank");
        }
    }

    private sealed class ABurnerThatStopsEverything(CancellationTokenSource stopping) : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct)
        {
            stopping.Cancel();
            ct.ThrowIfCancellationRequested();

            return Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
        }
    }

    /// <summary>
    /// Reports its progress, and does not finish until the report has been
    /// heard.
    /// </summary>
    /// <remarks>
    /// Progress&lt;T&gt; hands its callback to another thread, and Say drops
    /// anything that arrives once the burn is over, so a report made on the
    /// way out is simply lost. Which thread wins is the machine's business:
    /// this passed here every time and failed every other run on the test VM,
    /// which is the kind of test that teaches people to press run again.
    ///
    /// So the burn does not end until what it said has been heard.
    /// </remarks>
    private sealed class ABurnerThatReportsItsProgress(List<string> said) : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct)
        {
            Report(progress, new BurnProgress("writing", 0.5, 90));
            Report(progress, new BurnProgress("closing the disc", 0.9, 10));

            return Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
        }

        private void Report(IProgress<BurnProgress>? progress, BurnProgress step)
        {
            if (progress is null)
            {
                return;
            }

            int before;
            lock (said) { before = said.Count; }

            progress.Report(step);

            DateTime until = DateTime.UtcNow.AddSeconds(10);

            while (DateTime.UtcNow < until)
            {
                lock (said)
                {
                    if (said.Count > before)
                    {
                        return;
                    }
                }

                Thread.Sleep(1);
            }

            throw new InvalidOperationException($"The burn said \"{step.Doing}\" and nothing heard it.");
        }
    }

    private static readonly string[] ThreeImages =
        [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso", @"C:\out\A SET D3.iso"];

    /// <summary>A note left beside the discs saying how many are done.</summary>
    private static string NoteSaying(int written, int ofDiscs) =>
        new TheNoteBesideTheDiscs(
            "A SET",
            [.. ThreeImages.Take(ofDiscs).Select(each => new NotedImage(System.IO.Path.GetFileName(each), 1000))],
            written).AsText();

    private static Task<Recorder?> OnlyOneDrive(IReadOnlyList<Recorder> ready) =>
        throw new InvalidOperationException("It should not have had to ask which drive.");

    private static Task<Recorder?> NeverErases(IReadOnlyList<Recorder> could) =>
        throw new InvalidOperationException("It should not have had to ask about erasing.");

    private static Task<bool> Closes(Recorder drive) => Task.FromResult(true);

    private static Task<bool> AlwaysAnother(int number, int of, Recorder drive) => Task.FromResult(true);

    private static Task<bool> NeverAsksToCarryOn(int number, int of) =>
        throw new InvalidOperationException("It should not have had to ask about carrying on.");

    /// <summary>
    /// A blank for the first disc and somebody's used rewritable for the
    /// second, which is the drawer most people are burning out of.
    /// </summary>
    private sealed class ADriveWithAUsedDiscForTheSecond : IFindRecorders
    {
        internal void TheDiscWasJustSwapped() => _used = true;

        private bool _used;

        public IReadOnlyList<Recorder> All() =>
        [
            _used
                ? new Recorder("id-D", "D:", "A Drive", 3, false, 0, [750])
                : new Recorder("id-D", "D:", "A Drive", 2, true, 360_000, [750])
        ];
    }

    /// <summary>
    /// One drive that says nothing for the first few asks after the disc is
    /// swapped, the way a real one does while it is still reading what was
    /// just pushed in.
    /// </summary>
    private sealed class ADriveSlowToReadTheNextDisc(int quietAfter, bool blankWhenRead) : IFindRecorders
    {
        internal int Asked { get; private set; }

        internal void TheDiscWasJustSwapped() => _quiet = quietAfter;

        private int _quiet;

        public IReadOnlyList<Recorder> All()
        {
            Asked++;

            if (_quiet > 0)
            {
                _quiet--;
                return [new("id-D", "D:", "A Drive", 0, false, 0, [], "the device is not ready")];
            }

            return [new("id-D", "D:", "A Drive", 3, blankWhenRead, blankWhenRead ? 359_847 : 0, [750])];
        }
    }

    private static async Task<MakeADisc> ASetOf(
        int discs,
        IBurnDiscs burner,
        bool checksOut = true,
        IFileSystem? disk = null,
        IFindRecorders? drives = null,
        IKeepTheSetsPlace? note = null,
        Func<string, long>? howBig = null,
        IEraseDiscs? eraser = null,
        AClockThatMovesItself? clock = null)
    {
        MakeADisc window = new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatMakesASetOf(discs),
            new ImagesThatWork(),
            drives ?? new OneBlankDrive(),
            burner,
            eraser ?? new AnEraserThatWorks(),
            disk ?? (checksOut ? new ADiskThatMatches() : new ADiskThatDoesNot()),
            new NoHashing(),
            note ?? new ANoteInMemory(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesThatRead(),
            "2.0.0",
            howBig ?? (_ => 1000),

            // A clock that moves itself, so a drive that is slow to read the
            // disc just pushed in is waited for without the suite sitting
            // through the seconds. The two tests below used to spend three
            // real ones each.
            Patience: (clock ?? new AClockThatMovesItself()).Patience));

        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Big Game"));
        window.Label = "A SET";

        await window.MakeIt();

        return window;
    }

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    /// <summary>
    /// A note that stops the set the moment a disc is finished, which is
    /// somebody pressing stop between discs.
    /// </summary>
    private sealed class ANoteThatCancels(CancellationTokenSource stop) : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text) => stop.Cancel();
    }

    /// <summary>
    /// The note, kept in memory instead of beside the discs.
    /// </summary>
    private sealed class ANoteInMemory(string? says = null) : IKeepTheSetsPlace
    {
        internal string? Note { get; private set; } = says;

        internal int Left { get; private set; }

        public string? WhatItSays(string folder) => Note;

        public void Leave(string folder, string text)
        {
            Note = text;
            Left++;
        }
    }

    private sealed class ABuilderThatMakesASetOf(int discs) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
            [
                .. Enumerable.Range(1, discs).Select(number => new BuiltDisc(
                    project.Label, number, discs, System.IO.Path.Combine(outputFolder, $"disc D{number}"), 1000))
            ]));
    }

    private sealed class ABurnerThatRemembers : IBurnDiscs
    {
        internal List<string> Wrote { get; } = [];

        internal List<string> Onto { get; } = [];

        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct)
        {
            Wrote.Add(imagePath);
            Onto.Add(onto.Drive);

            return Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 47, 16, true));
        }
    }

    private sealed class ImagesThatWork : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 1024, null));
    }

    private sealed class AnEraserThatWorks : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>("blank again");
    }

    private sealed class OneBlankDrive : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id-E", "E:", "A Drive", 2, true, 360_000, [750])];
    }

    /// <summary>A drive with a disc in it that almost nothing would fit on.</summary>
    private sealed class OneSmallDrive : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id-E", "E:", "A Drive", 2, true, 10, [750])];
    }

    private sealed class TwoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 2, true, 360_000, [750]),
            new("id-E", "E:", "Another Drive", 2, true, 360_000, [750])
        ];
    }

    /// <summary>
    /// A disc that can be read and a staging folder that has gone, which is
    /// what a project looks like a week after it was built.
    /// </summary>
    private sealed class ADiskWithNothingLeftBesideIt : IFileSystem
    {
        public bool FolderExists(string path) => path.StartsWith("E:", StringComparison.OrdinalIgnoreCase);

        public FolderListing List(string path) => new(path, []);
    }

    /// <summary>
    /// The first disc's folder still there and the second one's gone, which is
    /// a set finished over two evenings with a tidy up in between.
    /// </summary>
    private sealed class ADiskThatKeptOnlyTheFirstDiscsFolder : IFileSystem
    {
        public bool FolderExists(string path) =>
            !path.EndsWith("disc D2", StringComparison.OrdinalIgnoreCase);

        public FolderListing List(string path) => new(path, [new ListedFile("setup_a_game.exe", 1000)]);
    }

    private sealed class ADiskThatMatches : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public FolderListing List(string path) => new(path, [new ListedFile("setup_a_game.exe", 1000)]);
    }

    private sealed class ADiskThatDoesNot : IFileSystem
    {
        public bool FolderExists(string path) => true;

        // The disc comes back with nothing on it, which is what a write that
        // reported success and produced a coaster looks like.
        public FolderListing List(string path) =>
            path.StartsWith("E:", StringComparison.OrdinalIgnoreCase)
                ? new FolderListing(path, [])
                : new FolderListing(path, [new ListedFile("setup_a_game.exe", 1000)]);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    /// <summary>Somewhere nothing has ever been published.</summary>
    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    /// <summary>A preview nobody looks at.</summary>
    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }

    /// <summary>Pictures that can all be read, which is the ordinary case.</summary>
    private sealed class PicturesThatRead : IImages
    {
        internal string? Refuse { get; init; }

        public ImageFacts Read(string path) =>
            Refuse is null ? new ImageFacts(true, 512, 512, null) : new ImageFacts(false, 0, 0, Refuse);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }


    [Fact]
    public async Task Says_which_disc_is_in_the_way_when_the_next_one_cannot_be_used()
    {
        // Disc 1 goes onto a blank. What goes in for disc 2 is a used
        // rewritable, and the offer to erase it is declined.
        ADriveWithAUsedDiscForTheSecond drives = new();
        ABurnerThatRemembers burner = new();
        MakeADisc window = await ASetOf(2, burner, drives: drives);

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive,
            could => Task.FromResult<Recorder?>(null),
            Closes,
            (number, of, drive) => { drives.TheDiscWasJustSwapped(); return Task.FromResult(true); },
            NeverAsksToCarryOn);

        // One disc written, and the sentence names the disc in the way rather
        // than saying the next disc could not be used, which tells somebody
        // standing at the drive nothing they can act on.
        Assert.Single(burner.Wrote);

        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Equal(1, went.Written);
        Assert.Contains(
            "It stopped at disc 2 of 2: The CD-RW in D: has something on it",
            went.InWords,
            StringComparison.Ordinal);
    }

    // ----------------------------------- a drive still reading the next disc

    [Fact]
    public async Task Waits_for_the_drive_to_read_the_disc_that_was_just_put_in()
    {
        ADriveSlowToReadTheNextDisc slow = new(quietAfter: 3, blankWhenRead: true);
        ABurnerThatRemembers burner = new();
        AClockThatMovesItself clock = new();
        MakeADisc window = await ASetOf(2, burner, drives: slow, clock: clock);

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive,
            NeverErases,
            Closes,
            (number, of, drive) => { slow.TheDiscWasJustSwapped(); return Task.FromResult(true); },
            NeverAsksToCarryOn);

        // The second disc is asked for the moment somebody says they have put
        // it in, and a drive takes seconds to read it. Taking the first
        // silence as an answer ends the set one disc short.
        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.True(went.Whole, $"the set stopped: {window.ItWentWrong?.Headline}");
        Assert.Equal([@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"], burner.Wrote);

        // Through the clock it was handed, which is the only proof that the
        // window waits with the machine's patience rather than with one of
        // its own. Ignoring it would leave the suite sitting through real
        // seconds and still passing.
        Assert.True(clock.Waits > 0, "nothing waited on the clock it was given");
    }

    [Fact]
    public async Task Still_offers_to_erase_the_next_disc_when_the_drive_was_slow_to_read_it()
    {
        ADriveSlowToReadTheNextDisc slow = new(quietAfter: 3, blankWhenRead: false);
        MakeADisc window = await ASetOf(2, new ABurnerThatRemembers(), drives: slow);

        bool offered = false;

        await window.BurnTheSet(
            [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso"],
            OnlyOneDrive,
            could => { offered = true; return Task.FromResult<Recorder?>(null); },
            Closes,
            (number, of, drive) => { slow.TheDiscWasJustSwapped(); return Task.FromResult(true); },
            NeverAsksToCarryOn);

        // This is the one Lazar hit: a used disc put in for disc 2 on purpose.
        // A drive that has not answered yet has no media type, so
        // ErasingWouldHelp is false, the offer to erase never appears, and the
        // answer is "the drive would not say what is in it" with nothing to do
        // about it.
        Assert.True(offered, "it never offered to erase the disc");
        Assert.DoesNotContain(
            "would not say",
            window.ItWentWrong?.Headline ?? string.Empty,
            StringComparison.Ordinal);

        // And the set's own sentence carries that refusal rather than a
        // general line about no drive being chosen. Somebody who declined to
        // erase a disc knows they declined; what they need told is that the
        // disc in the drive is the thing in the way.
        TheSetSoFar went = Assert.IsType<TheSetSoFar>(window.HowTheSetWent);
        Assert.Contains("The CD-RW in D: has something on it", went.InWords, StringComparison.Ordinal);
    }

}
