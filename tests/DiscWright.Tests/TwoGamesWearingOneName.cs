using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The notice about two games answering to the same name, discwright#98.
/// </summary>
/// <remarks>
/// Two rows reading alike on the window become two menu buttons reading
/// alike on the burned disc. A notice and never a refusal, in exactly the
/// words of the ask: "Not to forbid it if there's no potential for
/// problems, but just to give notice."
/// </remarks>
public class TwoGamesWearingOneName
{
    [Fact]
    public void Says_nothing_while_every_game_answers_to_its_own_name()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.Add(AGame("Celeste"));

        Assert.Null(screen.WhoWearsTheSameName);
    }

    [Fact]
    public void Names_the_name_two_games_wear_and_counts_them()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.Add(AGame("Hollow Knight", @"C:\games\two"));

        Assert.NotNull(screen.WhoWearsTheSameName);
        Assert.Contains("2 games", screen.WhoWearsTheSameName, StringComparison.Ordinal);
        Assert.Contains("Hollow Knight", screen.WhoWearsTheSameName, StringComparison.Ordinal);
    }

    [Fact]
    public void Spelling_a_name_in_a_different_case_does_not_hide_the_match()
    {
        // The menu does not care about case and neither does the person
        // reading two buttons, so neither may this.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.Add(AGame("HOLLOW KNIGHT", @"C:\games\two"));

        Assert.NotNull(screen.WhoWearsTheSameName);
    }

    [Fact]
    public void Every_shared_name_is_named_when_there_are_several()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.Add(AGame("Hollow Knight", @"C:\games\two"));
        screen.Add(AGame("Celeste", @"C:\games\three"));
        screen.Add(AGame("Celeste", @"C:\games\four"));

        Assert.Contains("Hollow Knight", screen.WhoWearsTheSameName, StringComparison.Ordinal);
        Assert.Contains("Celeste", screen.WhoWearsTheSameName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_notice_goes_when_one_of_the_two_comes_off()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.Add(AGame("Hollow Knight", @"C:\games\two"));
        Assert.NotNull(screen.WhoWearsTheSameName);

        screen.TakeOff(screen.WhatGoesOnIt[1]);

        Assert.Null(screen.WhoWearsTheSameName);
    }

    [Fact]
    public void The_window_hears_about_it_when_the_list_changes()
    {
        MakeADisc screen = TheScreen();
        List<string> changed = [];
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.PropertyChanged += (_, moved) => changed.Add(moved.PropertyName!);

        screen.Add(AGame("Hollow Knight", @"C:\games\two"));

        Assert.Contains(nameof(MakeADisc.WhoWearsTheSameName), changed, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------- helpers

    private static GameFolder AGame(string name, string path = @"C:\games\somewhere")
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(path, GameKind.GogInstaller, name, name, only, [only], [], null);
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
