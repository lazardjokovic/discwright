using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What somebody watching one disc being written sees, from pressing the
/// button to the disc being read back.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These are the lines a snapshot cannot see.</strong>
/// <see cref="TheWindowChecksItself"/> reads every property before and after
/// each step and catches anything that moved without being announced, which is
/// most of this window. It cannot catch what a burn says while it is running,
/// because by the time the step is over the line is empty again, the bar is
/// back at nothing and the window is no longer working. A mutation pass found
/// ten faults in here that the whole suite was green on, every one of them the
/// window going quiet or saying the wrong thing for the two minutes somebody
/// is standing there watching it.
/// </para>
/// <para>
/// So this listens while it happens, the way the tests for a whole set do.
/// </para>
/// </remarks>
public class BurningOneDiscAndWatchingIt
{
    [Fact]
    public async Task Says_what_it_is_doing_from_the_drives_to_the_disc_coming_back()
    {
        AWindowWatched watched = new();

        await watched.Burn();

        // The order is the whole account of what happened: it looked at the
        // drives, it wrote, it waited for the disc to come back, and it read
        // it. The drive's own words go through untouched, which is why the
        // middle line carries how long it thinks is left.
        Assert.Equal("looking at the drives", watched.Said[0]);
        Assert.Contains("writing, about 1 min 0 sec left", watched.Said);
        Assert.Contains("waiting for the disc to come back", watched.Said);

        Assert.True(
            watched.Said.IndexOf("waiting for the disc to come back")
                > watched.Said.IndexOf("writing, about 1 min 0 sec left"),
            "it waited for the disc before it had written it: " + string.Join(" | ", watched.Said));

        // And then the reading back, file by file, with the count in every
        // line. Not in a fixed order: Progress<T> hands each report to the
        // thread pool, so two files hashed in no time can be heard either way
        // round. What matters is that every one of them was said.
        Assert.Contains("checking the disc, 0%, file 1 of 2: setup_a_game.exe", watched.Said);
        Assert.Contains("checking the disc, 50%, file 2 of 2: A DISC.iso", watched.Said);
    }

    [Fact]
    public async Task Says_it_is_waiting_for_the_drive_to_read_the_disc_and_for_how_long()
    {
        // A drive that takes three asks, a second apart, to read the disc it
        // has just written. An older laptop's drive took minutes, and the
        // window went on saying the last thing the burn had said.
        AWindowWatched watched = new(discNotReadFor: 3);

        await watched.Burn();

        Assert.Contains("waiting for E: to read the disc back in, 0 sec", watched.Said);
        Assert.Contains("waiting for E: to read the disc back in, 2 sec", watched.Said);
        Assert.Contains("E: has read the disc back in after 3 sec", watched.Said);

        // In that order, after the burn and before the check.
        int waited = watched.Said.IndexOf("waiting for E: to read the disc back in, 0 sec");
        int back = watched.Said.IndexOf("E: has read the disc back in after 3 sec");
        int checking = watched.Said.FindIndex(said => said.StartsWith("checking the disc", StringComparison.Ordinal));
        Assert.True(
            watched.Said.IndexOf("waiting for the disc to come back") < waited && waited < back && back < checking,
            string.Join(" | ", watched.Said));

        Assert.True(watched.Window.HowTheDiscCameOut?.Good, "the disc that came back was not checked as good");
    }

    [Fact]
    public async Task Gives_up_on_a_drive_that_never_reads_the_disc_and_says_it_was_not_checked()
    {
        AWindowWatched watched = new(discNotReadFor: -1);

        await watched.Burn();

        // A quarter of an hour of the fake clock, said the whole way.
        Assert.Contains("waiting for E: to read the disc back in, 14 min 59 sec", watched.Said);
        Assert.Null(watched.Window.HowTheDiscCameOut);
        Assert.Equal(Refusals.TheDiscWasNotChecked("E:").Because, watched.Window.ItWentWrong?.Because);
    }

    [Fact]
    public async Task Moves_the_bar_with_what_the_drive_says()
    {
        AWindowWatched watched = new();

        await watched.Burn();

        // The drive hands over a fraction as well as a sentence. Thrown away,
        // the bar sits at nothing through the whole burn while the words count
        // up beside it, which is how a working program comes to look stuck.
        Assert.Contains(0.5, watched.Far);
    }

    [Fact]
    public async Task Says_what_the_drive_said_about_its_own_reporting()
    {
        AWindowWatched watched = new();

        await watched.Burn();

        // Said every time, even when it worked, because a drive that would not
        // take a listener and a drive that took one and said nothing both
        // leave a bar that never moved, and they are different faults.
        Assert.Contains(nameof(MakeADisc.AboutTheImage), watched.WhatWasAnnounced());
        Assert.Contains("reported", watched.Window.AboutTheImage ?? string.Empty, StringComparison.Ordinal);

        // And said as the writing finishes, not at the end of the whole job.
        // The end of a burn is not the end of a burn: the disc is ejected,
        // pushed back in and read file by file, which on a full disc is
        // minutes. The note belongs on screen for those minutes, where
        // somebody can read it, rather than arriving with the verdict.
        //
        // The announcement in the finally would cover this one in a test that
        // only asked whether it was ever said, and the two are a disc check
        // apart.
        List<string> happened;

        lock (watched.Happened)
        {
            happened = [.. watched.Happened];
        }

        // Between the writing and the waiting, which is the only window where
        // it can have been said about this burn. A burn starts by clearing it
        // and announcing the clearing, so asking whether it was ever
        // announced is answered by that and proves nothing.
        int wrote = happened.IndexOf("said: writing, about 1 min 0 sec left");
        int waited = happened.IndexOf("said: waiting for the disc to come back");

        Assert.True(wrote >= 0 && waited > wrote, string.Join(" | ", happened));

        Assert.Contains(
            "told: " + nameof(MakeADisc.AboutTheImage),
            happened.GetRange(wrote, waited - wrote));
    }

    [Fact]
    public async Task Leaves_nothing_running_and_says_it_both_ways()
    {
        AWindowWatched watched = new();

        await watched.Burn();

        Assert.True(watched.Window.NothingIsRunning);
        Assert.Equal(string.Empty, watched.Window.Doing);
        Assert.Equal(0, watched.Window.HowFar);

        // Twice over: once when it started and once when it stopped. A window
        // told only at the end has every button dead for the whole burn and
        // then lit afterwards, which is right by accident.
        Assert.True(
            watched.WhatWasAnnounced().Count(each => each == nameof(MakeADisc.IsWorking)) >= 2,
            "it did not say both that it had started and that it had stopped");

        Assert.Contains(nameof(MakeADisc.HowTheDiscCameOut), watched.WhatWasAnnounced());

        // And the line the window keeps, which follows it.
        Assert.Contains(nameof(MakeADisc.HowTheBurnWent), watched.WhatWasAnnounced());
    }

    [Fact]
    public async Task Says_how_the_disc_came_out()
    {
        AWindowWatched watched = new();

        await watched.Burn();

        DiscCheck how = Assert.IsType<DiscCheck>(watched.Window.HowTheDiscCameOut);
        Assert.True(how.Good, how.InWords);
        Assert.Null(watched.Window.ItWentWrong);
    }

    [Fact]
    public async Task The_windows_own_thread_is_not_the_one_that_carries_the_files()
    {
        // The staging copy is synchronous by design, so wherever the build
        // runs is a thread that stops answering for the whole copy. Run on
        // the window's own thread, pressing Build froze the window: Starting
        // was set and never painted, and the first thing anybody saw was the
        // image writer's report seconds later, watched on 2026-11-09.
        //
        // Under OnTheWindowsThread.Run this thread is the window's thread,
        // and it is busy delivering until the build is over, so the builder
        // can only have run here by running inline, which is exactly the
        // freeze. No deadline is needed: a builder that runs inline still
        // finishes, it just answers with this thread's own id.
        AWindowWatched watched = new();
        int pressing = Environment.CurrentManagedThreadId;

        await OnTheWindowsThread.Run(() => watched.Window.MakeIt());

        Assert.NotNull(watched.BuildRanOn);
        Assert.NotEqual(pressing, watched.BuildRanOn);
    }

    [Fact]
    public async Task Writes_nothing_while_something_else_is_already_running()
    {
        // A build that tries to burn while it is building, which is the only
        // way to be inside the window while it is busy. The window's own
        // buttons are dead at that moment; this is the guard behind them.
        AWindowWatched watched = new();
        bool burnedDuring = true;

        watched.WhileBuilding = () =>
        {
            watched.Window.BurnIt(
                @"C:\out\A DISC.iso", TheFirstDrive, NothingToErase, TheTrayShuts).GetAwaiter().GetResult();

            burnedDuring = watched.Burner.Wrote.Count > 0;
        };

        await watched.Window.MakeIt();

        Assert.False(burnedDuring, "it burned a disc in the middle of building one");
    }

    // ------------------------------------- the small things nothing presses

    [Fact]
    public void Takes_a_path_of_nothing_but_spaces_for_no_path_at_all()
    {
        MakeADisc window = new AWindowWatched().Window;

        // A picker answers with an empty string when somebody clears it, and
        // somewhere between the dialog and here it can arrive as spaces. Kept,
        // it becomes a manual the build cannot find.
        window.Manual = "   ";
        Assert.Null(window.Manual);

        window.Music = string.Empty;
        Assert.Null(window.Music);

        window.DiscIcon = "  ";
        Assert.Null(window.DiscIcon);
    }

    [Fact]
    public void Remembers_that_the_background_is_to_be_stretched()
    {
        MakeADisc window = new AWindowWatched().Window;

        window.BackgroundAsIs = true;
        Assert.True(window.BackgroundAsIs);

        window.BackgroundAsIs = false;
        Assert.False(window.BackgroundAsIs);
    }

    [Fact]
    public void Says_why_a_picture_cannot_be_used_the_moment_it_is_picked()
    {
        AWindowWatched watched = new(picturesRefuse: "Windows cannot read that file.");

        Assert.False(watched.Window.ThatPictureIsUsable(@"C:\art\broken.png"));
        Assert.Equal("Windows cannot read that file.", watched.Window.WhyNotThatPicture);

        // Said, and not only known. The line under the picker reads whatever
        // it read the first time until something tells it otherwise, which is
        // how a file Windows cannot decode sailed through the whole window and
        // failed at the end as "A generic error occurred in GDI+".
        Assert.Contains(nameof(MakeADisc.WhyNotThatPicture), watched.WhatWasAnnounced());
    }

    [Fact]
    public void Says_a_readable_picture_is_fine_and_says_so_too()
    {
        AWindowWatched watched = new();

        Assert.True(watched.Window.ThatPictureIsUsable(@"C:\art\icon.png"));
        Assert.Null(watched.Window.WhyNotThatPicture);
        Assert.Contains(nameof(MakeADisc.WhyNotThatPicture), watched.WhatWasAnnounced());
    }

    // ------------------------------------------------------------- the rig

    private static Task<Recorder?> TheFirstDrive(IReadOnlyList<Recorder> ready) =>
        Task.FromResult(ready.Count == 0 ? null : ready[0]);

    private static Task<Recorder?> NothingToErase(IReadOnlyList<Recorder> could) =>
        Task.FromResult<Recorder?>(null);

    private static Task<bool> TheTrayShuts(Recorder drive) => Task.FromResult(true);

    /// <summary>
    /// A window with a game on it, a disc built, and everything it says while
    /// it works written down in the order it said it.
    /// </summary>
    private sealed class AWindowWatched
    {
        internal AWindowWatched(string? picturesRefuse = null, int discNotReadFor = 0)
        {
            Window = new MakeADisc(new TheMachine(
                new NoEncoding(),
                new ABuilderThatMakesADisc(this),
                new ImagesThatWork(),
                new OneBlankDisc(),
                Burner,
                new AnEraserThatRefuses(),
                new ADiskThatMatchesWhatWasBuilt(),
                new NoHashing(),
                new NothingIsRemembered(),
                new ATrayThatShuts { NotReadFor = discNotReadFor },
                new NothingIsReleased(),
                new NoPreview(),
                new PicturesThatRead { Refuse = picturesRefuse },
                "2.0.0",
                _ => 1000,
                Patience: new AClockThatMovesItself().Patience));

            // So the burner can wait for its own report to be heard before it
            // carries on.
            Burner.Watching = this;

            Window.PropertyChanged += (sender, about) =>
            {
                // Everything in here is written under a lock, because the
                // drive's own progress reports arrive on the thread pool and a
                // list written from two threads at once loses entries or
                // throws.
                lock (Announced)
                {
                    Announced.Add(about.PropertyName ?? string.Empty);
                }

                lock (Happened)
                {
                    Happened.Add("told: " + about.PropertyName);

                    if (about.PropertyName == nameof(MakeADisc.Doing) && Window.Doing.Length > 0)
                    {
                        Happened.Add("said: " + Window.Doing);
                    }
                }

                if (about.PropertyName == nameof(MakeADisc.Doing) && Window.Doing.Length > 0)
                {
                    lock (Said)
                    {
                        Said.Add(Window.Doing);
                    }
                }

                if (about.PropertyName == nameof(MakeADisc.HowFar))
                {
                    lock (Far)
                    {
                        Far.Add(Window.HowFar);
                    }
                }
            };

            Window.OutputFolder = @"C:\out";
            Window.Add(AGame());
            Window.Label = "A DISC";
        }

        internal MakeADisc Window { get; }

        internal ABurnerThatSaysWhatItIsDoing Burner { get; } = new();

        internal List<string> Said { get; } = [];

        internal List<double> Far { get; } = [];

        internal List<string> Announced { get; } = [];

        /// <summary>What was announced, as a list nothing else is writing to.</summary>
        internal List<string> WhatWasAnnounced()
        {
            lock (Announced)
            {
                return [.. Announced];
            }
        }

        /// <summary>
        /// Everything said and everything announced, in one list in the order
        /// it happened, for the questions that are about when rather than
        /// whether.
        /// </summary>
        internal List<string> Happened { get; } = [];

        /// <summary>Something to do in the middle of a build, or nothing.</summary>
        internal Action? WhileBuilding { get; set; }

        /// <summary>Which thread the builder found itself on, once it ran.</summary>
        internal int? BuildRanOn { get; set; }

        /// <summary>Builds the disc and then burns it, watching all of it.</summary>
        internal async Task Burn()
        {
            await Window.MakeIt();

            lock (Said)
            {
                Said.Clear();
            }

            lock (Far)
            {
                Far.Clear();
            }

            lock (Announced)
            {
                Announced.Clear();
            }

            lock (Happened)
            {
                Happened.Clear();
            }

            // Heard the way the window hears it: on one thread, in order.
            //
            // Progress<T> posts each report to the thread that started the
            // burn when that thread has a context, which in the app is the
            // thread the window is drawn on, so the last report of a check
            // always lands before the burn's own ending does. A test thread
            // has no context, so the reports went to the pool, and the last
            // one could land after the burn had finished and be dropped as a
            // report about nothing. It did, once in six runs of the suite, on
            // 2026-10-24: "checking the disc, 2 of 2" was never said.
            await OnTheWindowsThread.Run(() => Window.BurnIt(Window.TheImages[0], TheFirstDrive, NothingToErase, TheTrayShuts));
        }

        /// <summary>Whether the bar has been told about this fraction yet.</summary>
        internal bool Heard(double far)
        {
            lock (Far)
            {
                return Far.Contains(far);
            }
        }

        /// <summary>
        /// Waits for the bar to be told about this fraction, delivering the
        /// window's reports meanwhile.
        /// </summary>
        /// <remarks>
        /// The burner calls this from the thread the burn runs on, which is
        /// also the thread that delivers the reports, so waiting without
        /// delivering would wait for ever.
        /// </remarks>
        internal bool WaitToHear(double far) => OnTheWindowsThread.WaitUntil(() => Heard(far));
    }

    private static GameFolder AGame()
    {
        ListedFile only = new("setup_a_game.exe", 1000);

        return new GameFolder(
            @"C:\games\a game", GameKind.GogInstaller, "A Game", "A Game", only, [only], [], null);
    }

    /// <summary>
    /// A burner that reports the way a drive does, and waits for each report
    /// to be heard before it goes on.
    /// </summary>
    /// <remarks>
    /// Progress&lt;T&gt; hands every report to the thread pool. A real burn
    /// takes two minutes, which is all the time in the world for one to be
    /// delivered; this one is instant, so without the wait the window would
    /// have stopped listening before the bar ever moved.
    /// </remarks>
    private sealed class ABurnerThatSaysWhatItIsDoing : IBurnDiscs
    {
        internal List<string> Wrote { get; } = [];

        internal AWindowWatched? Watching { get; set; }

        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct)
        {
            Wrote.Add(imagePath);

            Report(progress, new BurnProgress("writing", 0.5, 60));

            return Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 47, 3, true));
        }

        private void Report(IProgress<BurnProgress>? progress, BurnProgress step)
        {
            progress?.Report(step);

            if (Watching is null || step.HowFar is not double far)
            {
                return;
            }

            // Delivering while it waits, because the report is posted to the
            // very thread this is running on.
            Assert.True(Watching.WaitToHear(far), $"the burn said \"{step.Doing}\" and nothing heard it");
        }
    }

    private sealed class ABuilderThatMakesADisc(AWindowWatched watched) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            watched.BuildRanOn = Environment.CurrentManagedThreadId;
            watched.WhileBuilding?.Invoke();

            return Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
                [new BuiltDisc(project.Label, 1, 1, System.IO.Path.Combine(outputFolder, "disc 1"), 1000)]));
        }
    }

    private sealed class ImagesThatWork : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 1024, null));
    }

    private sealed class OneBlankDisc : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id-E", "E:", "A Drive", 2, true, 360_000, [750])];
    }

    private sealed class AnEraserThatRefuses : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.WindowsLetGoOfTheDrive(disc.Drive, "nothing to erase here"));
    }

    private sealed class ADiskThatMatchesWhatWasBuilt : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public FolderListing List(string path) =>
            new(path,
            [
                new ListedFile("setup_a_game.exe", 1000),
                new ListedFile("A DISC.iso", 1000)
            ]);
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        private int _asked;

        /// <summary>How many times it says the disc is not read yet; below nought, for ever.</summary>
        internal int NotReadFor { get; init; }

        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;

        public Task<bool> HasReadTheDisc(Recorder drive, CancellationToken ct) =>
            Task.FromResult(NotReadFor >= 0 && Interlocked.Increment(ref _asked) > NotReadFor);
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
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
}
