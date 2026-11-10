using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The line about what an opened project named and could not bring back,
/// discwright#98.
/// </summary>
/// <remarks>
/// The games that did not come back are the one part of a project that
/// cannot sit on the window as rows, so a line under the list is their only
/// trace. The news used to arrive in a box, and a box is gone at OK while
/// the games are still gone an hour later.
/// </remarks>
public class TheNewsAfterOpening
{
    [Fact]
    public void Says_nothing_when_everything_came_back()
    {
        MakeADisc screen = TheScreen();

        screen.OpenThis(Opened());

        Assert.Null(screen.WhatDidNotComeBack);
    }

    [Fact]
    public void Counts_and_names_what_is_gone()
    {
        MakeADisc screen = TheScreen();

        screen.OpenThis(Opened(gone: [@"C:\games\one", @"C:\games\two"]));

        Assert.NotNull(screen.WhatDidNotComeBack);
        Assert.Contains("2 things", screen.WhatDidNotComeBack, StringComparison.Ordinal);
        Assert.Contains(@"C:\games\one", screen.WhatDidNotComeBack, StringComparison.Ordinal);
        Assert.Contains(@"C:\games\two", screen.WhatDidNotComeBack, StringComparison.Ordinal);
    }

    [Fact]
    public void Speaks_of_one_missing_thing_in_the_singular()
    {
        MakeADisc screen = TheScreen();

        screen.OpenThis(Opened(gone: [@"C:\games\one"]));

        Assert.Contains(
            "1 thing this project points at is not where it left it",
            screen.WhatDidNotComeBack,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Caps_the_names_at_five_and_counts_the_rest()
    {
        MakeADisc screen = TheScreen();
        string[] gone = [.. Enumerable.Range(1, 7).Select(at => @"C:\games\" + at)];

        screen.OpenThis(Opened(gone: gone));

        Assert.Contains(@"C:\games\5", screen.WhatDidNotComeBack, StringComparison.Ordinal);
        Assert.Contains("and 2 more", screen.WhatDidNotComeBack, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\games\6", screen.WhatDidNotComeBack, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_the_folders_that_could_not_be_read()
    {
        MakeADisc screen = TheScreen();

        screen.OpenThis(Opened(trouble:
        [
            Refusals.SomethingNobodyExpected("reading the folder", "its installer is gone"),
            Refusals.SomethingNobodyExpected("reading the folder", "its files are locked")
        ]));

        Assert.Contains(
            "2 folders are still there but could not be read",
            screen.WhatDidNotComeBack,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_hears_about_it_when_a_project_opens()
    {
        MakeADisc screen = TheScreen();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.OpenThis(Opened(gone: [@"C:\games\one"]));

        Assert.Contains(nameof(MakeADisc.WhatDidNotComeBack), told, StringComparer.Ordinal);
    }

    [Fact]
    public void Starting_a_new_disc_takes_the_line_down()
    {
        MakeADisc screen = TheScreen();
        screen.OpenThis(Opened(gone: [@"C:\games\one"]));
        Assert.NotNull(screen.WhatDidNotComeBack);

        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);
        screen.StartAgain();

        Assert.Null(screen.WhatDidNotComeBack);
        Assert.Contains(nameof(MakeADisc.WhatDidNotComeBack), told, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Stays_through_a_build_because_that_disc_is_short_of_it()
    {
        MakeADisc screen = TheScreen();
        screen.OpenThis(Opened(gone: [@"C:\games\one"]));
        screen.OutputFolder = @"C:\out";

        await screen.MakeIt();

        // A disc built while something is missing is a disc built without
        // it, which is exactly when the line is worth reading again.
        Assert.NotNull(screen.WhatDidNotComeBack);
    }

    // ------------------------------------------------------------- helpers

    private static Reopened Opened(
        IReadOnlyList<string>? gone = null, IReadOnlyList<Refusal>? trouble = null) =>
        new(new DiscProject(
                "OPENED",
                [AGame("Still Here")],
                new DiscOptions(Menu: true, Checksums: false, LegacyFileSystems: false, CutFiles: false),
                Artwork: null,
                Extras: null,
                AddOns: []),
            null,
            null,
            gone ?? [],
            trouble ?? []);

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(
            @"C:\games\somewhere", GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    private static MakeADisc TheScreen() =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
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

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
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
