using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Saving the recipe without building anything.
/// </summary>
/// <remarks>
/// Asked for in discwright#98: keeping a project to come back to used to
/// mean building the whole thing first, minutes of discs for a file of
/// recipe. The recipe is the same <c>discproject.json</c> a build leaves at
/// the top of the output folder, written the same way to the same place, so
/// a saved project and a built one are opened identically.
/// </remarks>
public class SavingItForLater
{
    [Fact]
    public void Writes_the_recipe_through_the_builder_exactly_as_the_window_stands()
    {
        ABuilderThatKeepsTheRecipe builder = new();
        MakeADisc screen = TheScreen(builder);

        screen.Add(AGame("Hollow Knight"));
        screen.Label = "HOLLOW KNIGHT";
        screen.Medium = Media.Dvd5;
        screen.OutputFolder = @"C:\out\hollow";

        screen.SaveItForLater();

        Assert.NotNull(builder.Saved);
        (DiscProject project, Medium medium, string folder) = builder.Saved.Value;
        Assert.Equal("HOLLOW KNIGHT", project.Label);
        Assert.Single(project.Games);
        Assert.Equal(Media.Dvd5.Key, medium.Key);
        Assert.Equal(@"C:\out\hollow", folder);
    }

    [Fact]
    public void Has_nothing_to_save_on_an_empty_window()
    {
        ABuilderThatKeepsTheRecipe builder = new();
        MakeADisc screen = TheScreen(builder);
        screen.OutputFolder = @"C:\out";

        Assert.False(screen.CanSaveItForLater);

        // Pressed anyway, the way a test can and a person cannot, it still
        // declines: the guard is the method's own, not only the button's.
        screen.SaveItForLater();

        Assert.Null(builder.Saved);
        Assert.Null(screen.HowTheSaveWent);
    }

    [Fact]
    public void Has_nowhere_to_save_without_a_folder()
    {
        // The recipe lands where the discs would, so with nowhere named
        // there is nowhere to save. A refusal after the press would be the
        // window's only button that complains instead of greying.
        ABuilderThatKeepsTheRecipe builder = new();
        MakeADisc screen = TheScreen(builder);

        screen.Add(AGame("Hollow Knight"));
        screen.OutputFolder = "   ";

        Assert.False(screen.CanSaveItForLater);

        screen.SaveItForLater();

        Assert.Null(builder.Saved);
    }

    [Fact]
    public void Says_where_it_went_and_announces_everything_that_moved()
    {
        ABuilderThatKeepsTheRecipe builder = new();
        MakeADisc screen = TheScreen(builder);
        List<string> changed = [];

        screen.Add(AGame("Hollow Knight"));
        screen.OutputFolder = @"C:\out\hollow";
        screen.PropertyChanged += (_, moved) => changed.Add(moved.PropertyName!);

        screen.SaveItForLater();

        Assert.NotNull(screen.HowTheSaveWent);
        Assert.Contains(@"C:\out\hollow", screen.HowTheSaveWent, StringComparison.Ordinal);
        Assert.Contains(nameof(MakeADisc.HowTheSaveWent), changed, StringComparer.Ordinal);
    }

    [Fact]
    public void The_saved_line_goes_when_the_window_is_cleared()
    {
        ABuilderThatKeepsTheRecipe builder = new();
        MakeADisc screen = TheScreen(builder);

        screen.Add(AGame("Hollow Knight"));
        screen.OutputFolder = @"C:\out\hollow";
        screen.SaveItForLater();
        Assert.NotNull(screen.HowTheSaveWent);

        screen.StartAgain();

        // The sentence was about a project that is no longer on the window.
        Assert.Null(screen.HowTheSaveWent);
    }

    [Fact]
    public void The_real_builder_writes_the_same_file_a_build_leaves_behind()
    {
        RecordingWriter writer = new();
        DiscBuilder builder = new(
            new ADiskWithNothingOnIt(), writer, new PicturesThatRead(), new NoHashing(),
            "2.0.0-test", () => new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

        builder.SaveTheRecipe(
            new DiscProject("HOLLOW KNIGHT", [AGame("Hollow Knight")], new DiscOptions()),
            Media.Dvd5,
            @"C:\out\hollow");

        // The folder first, because a build has made it long before the
        // recipe is written and a save is the one path where nothing has.
        Assert.Contains(@"C:\out\hollow", writer.Folders);

        RecordingWriter.Written recipe = Assert.Single(writer.Texts);
        Assert.Equal(System.IO.Path.Combine(@"C:\out\hollow", TheProjectAsJson.FileName), recipe.Path);
        Assert.Contains("HOLLOW KNIGHT", recipe.Text, StringComparison.Ordinal);
        Assert.Contains("2.0.0-test", recipe.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_saved_into_a_nameless_folder()
    {
        // The screen never offers this, and the builder still refuses to
        // write a recipe into the root of nowhere, because a guard that only
        // exists on one side of an interface is a guard half the callers do
        // not have.
        RecordingWriter writer = new();
        DiscBuilder builder = new(
            new ADiskWithNothingOnIt(), writer, new PicturesThatRead(), new NoHashing(), "x");

        builder.SaveTheRecipe(
            new DiscProject("A", [AGame("A Game")], new DiscOptions()), Media.Cd, string.Empty);

        Assert.Empty(writer.Folders);
        Assert.Empty(writer.Texts);
    }

    // ------------------------------------------------------------- helpers

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    private static MakeADisc TheScreen(IBuildDiscs builder) =>
        new(new TheMachine(
            new NoEncoding(),
            builder,
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
            _ => 1000));

    private sealed class ABuilderThatKeepsTheRecipe : IBuildDiscs
    {
        public (DiscProject Project, Medium Medium, string Folder)? Saved { get; private set; }

        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));

        public void SaveTheRecipe(DiscProject project, Medium medium, string outputFolder) =>
            Saved = (project, medium, outputFolder);
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
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
