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
        Assert.Contains("Discs 1 to 2 are written", went.InWords, StringComparison.Ordinal);
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
        Assert.Contains("Nothing was written", went.InWords, StringComparison.Ordinal);
        Assert.Contains("stopped at disc 1 of 3", went.InWords, StringComparison.Ordinal);
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

    private static async Task<MakeADisc> ASetOf(
        int discs,
        IBurnDiscs burner,
        bool checksOut = true,
        IFindRecorders? drives = null,
        IKeepTheSetsPlace? note = null,
        Func<string, long>? howBig = null)
    {
        MakeADisc window = new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatMakesASetOf(discs),
            new ImagesThatWork(),
            drives ?? new OneBlankDrive(),
            burner,
            new AnEraserThatWorks(),
            checksOut ? new ADiskThatMatches() : new ADiskThatDoesNot(),
            new NoHashing(),
            note ?? new ANoteInMemory(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            "2.0.0",
            howBig ?? (_ => 1000)));

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
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<string>? progress, CancellationToken ct) =>
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
        public Outcome<string> Write(DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }
}
