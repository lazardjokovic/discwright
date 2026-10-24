using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Erasing a disc, and nothing else: no image, no burn, no set.
/// </summary>
/// <remarks>
/// <para>
/// Somebody with a drawer of used rewritables wants them blank again, and
/// until there was a button for it the only way to erase one was to be half
/// way through a burn and be asked.
/// </para>
/// <para>
/// <strong>Nothing tested any of it.</strong> A mutation pass found sixteen
/// faults in this one method that the whole suite went green on, which is
/// every line of it: the sentences, every announcement at both ends, and the
/// look at the drives afterwards that decides whether the button stays alive.
/// There was no test because driving it needs a drive with a used disc in it,
/// and now the questions it asks live where a test can answer them.
/// </para>
/// </remarks>
public class ErasingADiscOnItsOwn
{
    [Fact]
    public async Task Refuses_when_there_is_nothing_to_erase()
    {
        MakeADisc window = AWindow(new OneBlankDisc());

        await window.EraseADisc(NobodyIsAsked);

        // Which drives were asked, because "there is nothing to erase" about a
        // machine with drives in it is a sentence somebody will argue with.
        Refusal why = Assert.IsType<Refusal>(window.ItWentWrong);
        Assert.Contains("E:", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Erases_the_disc_somebody_picked_and_says_so()
    {
        AnEraserThatWorks eraser = new();
        List<string> saying = [];
        MakeADisc window = AWindow(new OneUsedRewritable(), eraser);

        // Locked at both ends, because the drive's progress reports arrive on
        // the thread pool and a list written from two threads at once loses
        // entries or throws. This test passed on its own every time and
        // failed a full suite under load.
        window.PropertyChanged += (sender, about) =>
        {
            if (about.PropertyName == nameof(MakeADisc.Doing) && window.Doing.Length > 0)
            {
                lock (saying)
                {
                    saying.Add(window.Doing);
                }
            }
        };

        // Heard the way the window hears it: on one thread, in order. Posted
        // to the pool, a late report from the drive could land on top of
        // "D: is blank again" between its announcement and this listener
        // reading it, which is what happened once in a full suite.
        await OnTheWindowsThread.Run(() => window.EraseADisc(could => Task.FromResult<Recorder?>(could[0])));

        List<string> said;

        lock (saying)
        {
            said = [.. saying];
        }

        Assert.Equal<IEnumerable<string>>(["D:"], eraser.Blanked);
        Assert.Null(window.ItWentWrong);

        // Which disc, in which drive, and then that it is done. Erasing takes
        // most of a minute, and a window that says nothing for that long looks
        // like a window that has died.
        Assert.Contains("erasing the CD-RW in D:", said);
        Assert.Contains("D: is blank again", said);
    }

    [Fact]
    public async Task Moves_the_bar_while_it_erases()
    {
        List<double> howFar = [];
        MakeADisc window = null!;

        // Waited for inside the erase, because Progress<T> hands the report to
        // the thread pool and the window stops listening the moment the erase
        // is over. A real erase takes most of a minute; this one takes no time
        // at all, so the report would arrive after nobody was left to hear it.
        AnEraserThatWorks eraser = new(untilHeard: () =>
        {
            lock (howFar)
            {
                return howFar.Contains(0.5);
            }
        });

        window = AWindow(new OneUsedRewritable(), eraser);

        window.PropertyChanged += (sender, about) =>
        {
            if (about.PropertyName == nameof(MakeADisc.HowFar))
            {
                lock (howFar)
                {
                    howFar.Add(window.HowFar);
                }
            }
        };

        await OnTheWindowsThread.Run(() => window.EraseADisc(could => Task.FromResult<Recorder?>(could[0])));

        List<double> far;

        lock (howFar)
        {
            far = [.. howFar];
        }

        // The drive hands over a fraction as well as a sentence. Thrown away,
        // the bar sits at nothing for the whole half minute while the words
        // count up beside it, which reads as a program that has stopped.
        Assert.Contains(0.5, far);

        // And it ends full, over the line saying the disc is blank again.
        Assert.Equal(1, far[^1]);
    }

    [Fact]
    public async Task Erases_nothing_when_somebody_closes_the_question()
    {
        AnEraserThatWorks eraser = new();
        MakeADisc window = AWindow(new OneUsedRewritable(), eraser);

        await window.EraseADisc(could => Task.FromResult<Recorder?>(null));

        // Closing the question is an answer. Whatever is on that disc is still
        // on it, and there is nothing to tell anybody.
        Assert.Empty(eraser.Blanked);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Says_why_when_the_erase_will_not_work()
    {
        MakeADisc window = AWindow(new OneUsedRewritable(), new AnEraserThatRefuses());

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        Refusal why = Assert.IsType<Refusal>(window.ItWentWrong);
        Assert.Contains("let go of", why.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turns_something_nobody_expected_into_an_answer()
    {
        MakeADisc window = AWindow(new OneUsedRewritable(), new AnEraserThatThrows());

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // Letting it out turns into raw exception text in a message box, which
        // reads as a crash. This is the sentence Lazar got instead.
        Refusal why = Assert.IsType<Refusal>(window.ItWentWrong);
        Assert.Equal("Something went wrong while erasing the disc", why.Headline);
        Assert.Contains("backing class factory", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turns_a_drive_that_will_not_answer_into_an_answer_too()
    {
        MakeADisc window = AWindow(new ADriveThatWillNotAnswer(), new AnEraserThatWorks());

        await window.EraseADisc(NobodyIsAsked);

        // Asking the drives what they are holding is a pile of COM calls, and
        // it used to sit outside the catch-all: the box arrived before a single
        // thing had been erased, from the asking rather than from the erase the
        // catch-all was written for.
        Refusal why = Assert.IsType<Refusal>(window.ItWentWrong);
        Assert.Equal("Something went wrong while erasing the disc", why.Headline);
        Assert.Contains("backing class factory", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_that_it_has_started_and_that_it_has_stopped()
    {
        List<string> announced = [];
        MakeADisc window = AWindow(new OneUsedRewritable(), new AnEraserThatWorks());

        // Locked, because the drive's own progress reports arrive on the
        // thread pool: Progress<T> posts them, and a list written from two
        // threads at once loses entries or throws. It took a full suite under
        // load to show, having passed on its own every time.
        window.PropertyChanged += (sender, about) =>
        {
            lock (announced)
            {
                announced.Add(about.PropertyName ?? string.Empty);
            }
        };

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        List<string> said;

        lock (announced)
        {
            said = [.. announced];
        }

        // Four buttons hang off this, and a window nobody tells about keeps
        // whatever it read the first time: Remove and Start again stay dead for
        // the rest of the run, over a program that is doing nothing.
        foreach (string about in (string[])
        [
            nameof(MakeADisc.IsWorking),
            nameof(MakeADisc.NothingIsRunning),
            nameof(MakeADisc.CanStartAgain),
            nameof(MakeADisc.CanTakeSomethingOff)
        ])
        {
            Assert.True(
                said.Count(each => each == about) >= 2,
                $"{about} was not said both at the start and at the end");
        }

        Assert.True(window.NothingIsRunning);
    }

    [Fact]
    public async Task Looks_at_the_drives_again_once_the_disc_is_blank()
    {
        // The disc really is blank afterwards, the way a drive that erased it
        // would answer the next time it is asked.
        ADiscThatIsBlankOnceErased drives = new();
        MakeADisc window = AWindow(drives, new AnEraserThatWorks(() => drives.Erased = true));

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // There is nothing left to erase, so the button that offers it is dead
        // again. Without the look it stays lit over a drawer of blanks, and
        // pressing it says there is nothing to erase.
        Assert.False(window.CanErase);
        Assert.True(drives.AskedAgain, "it never asked the drives what they hold now");
    }

    [Fact]
    public async Task Offers_every_disc_an_erase_would_help_and_no_others()
    {
        IReadOnlyList<Recorder>? offered = null;
        MakeADisc window = AWindow(new OneOfEach());

        await window.EraseADisc(could =>
        {
            offered = could;
            return Task.FromResult<Recorder?>(null);
        });

        // A pressed disc cannot be erased however much somebody wishes it
        // could, and a blank one is a minute spent to achieve nothing.
        Assert.Equal<IEnumerable<string>>(["D:"], offered!.Select(each => each.Drive));
    }

    // ------------------------------------------------------------- the rig

    private static Task<Recorder?> NobodyIsAsked(IReadOnlyList<Recorder> could) =>
        throw new InvalidOperationException("It should not have had to ask which disc to erase.");

    private static MakeADisc AWindow(IFindRecorders drives, IEraseDiscs? eraser = null) =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            drives,
            new ABurnerThatDoesNothing(),
            eraser ?? new AnEraserThatRefuses(),
            new ADiskWithNothingOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesThatRead(),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

    private sealed class OneBlankDisc : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id-E", "E:", "A Drive", 2, true, 360_000, [750])];
    }

    private sealed class OneUsedRewritable : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id-D", "D:", "A Drive", 3, false, 0, [750])];
    }

    /// <summary>A used rewritable, a blank and a disc that was pressed.</summary>
    private sealed class OneOfEach : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 3, false, 0, [750]),
            new("id-E", "E:", "Another Drive", 2, true, 360_000, [750]),
            new("id-F", "F:", "A Reader", 1, false, 0, [])
        ];
    }

    private sealed class ADiscThatIsBlankOnceErased : IFindRecorders
    {
        internal bool Erased { get; set; }

        internal bool AskedAgain { get; private set; }

        public IReadOnlyList<Recorder> All()
        {
            if (Erased)
            {
                AskedAgain = true;
            }

            return
            [
                Erased
                    ? new Recorder("id-D", "D:", "A Drive", 3, true, 360_000, [750])
                    : new Recorder("id-D", "D:", "A Drive", 3, false, 0, [750])
            ];
        }
    }

    private sealed class ADriveThatWillNotAnswer : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            throw new InvalidOperationException(
                "Attempt has been made to use a COM object that does not have a backing class factory.");
    }

    /// <param name="andSoTheDiscIsBlank">What erasing does to the drives.</param>
    /// <param name="untilHeard">
    /// What has to be true before the erase is over, for a test about the
    /// reporting. A real erase takes most of a minute, which is all the time
    /// in the world for a report to be delivered; this one is instant.
    /// </param>
    private sealed class AnEraserThatWorks(
        Action? andSoTheDiscIsBlank = null, Func<bool>? untilHeard = null) : IEraseDiscs
    {
        internal List<string> Blanked { get; } = [];

        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct)
        {
            Blanked.Add(disc.Drive);
            progress?.Report(new EraseProgress("erasing", 0.5));

            if (untilHeard is not null)
            {
                // Delivering while it waits when it is on the thread that
                // delivers, which under OnTheWindowsThread it is.
                Assert.True(OnTheWindowsThread.WaitUntil(untilHeard), "the erase reported and nothing heard it");
            }

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

    private sealed class AnEraserThatThrows : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            throw new InvalidOperationException(
                "Attempt has been made to use a COM object that does not have a backing class factory.");
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(Refusals.ThereIsNothingToPutOnADisc());
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(Refusals.WindowsCouldNotMakeTheImage("nothing here makes images"));
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class ADiskWithNothingOnIt : IFileSystem
    {
        public bool FolderExists(string path) => false;

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }

    private sealed class PicturesThatRead : IImages
    {
        public ImageFacts Read(string path) => new(true, 512, 512, null);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }
}
