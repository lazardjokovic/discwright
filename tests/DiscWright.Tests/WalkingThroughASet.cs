using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Writing a whole set, one disc at a time, in order.
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

        await window.BurnTheSet(ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother);

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
            });

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
            (number, of, drive) => Task.FromResult(number < 3));

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

        await window.BurnTheSet(ThreeImages, OnlyOneDrive, NeverErases, Closes, AlwaysAnother);

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
            AlwaysAnother);

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

        await window.BurnTheSet([], OnlyOneDrive, NeverErases, Closes, AlwaysAnother);

        Assert.Null(window.HowTheSetWent);
    }

    // ------------------------------------------------------------------ helpers

    private static readonly string[] ThreeImages =
        [@"C:\out\A SET D1.iso", @"C:\out\A SET D2.iso", @"C:\out\A SET D3.iso"];

    private static Task<Recorder?> OnlyOneDrive(IReadOnlyList<Recorder> ready) =>
        throw new InvalidOperationException("It should not have had to ask which drive.");

    private static Task<Recorder?> NeverErases(IReadOnlyList<Recorder> could) =>
        throw new InvalidOperationException("It should not have had to ask about erasing.");

    private static Task<bool> Closes(Recorder drive) => Task.FromResult(true);

    private static Task<bool> AlwaysAnother(int number, int of, Recorder drive) => Task.FromResult(true);

    private static async Task<MakeADisc> ASetOf(
        int discs,
        IBurnDiscs burner,
        bool checksOut = true,
        IFindRecorders? drives = null)
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
            _ => 1000));

        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Big Game"));

        await window.MakeIt();

        return window;
    }

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
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
}
