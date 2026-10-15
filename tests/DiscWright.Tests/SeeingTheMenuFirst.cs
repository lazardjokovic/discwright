using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Looking at the menu a disc would get, before making one.
/// </summary>
/// <remarks>
/// The thing worth proving is not that a preview appears. It is that the
/// preview is <strong>the same menu</strong>, because a preview written by a
/// second piece of code drifts from the disc, and the day it drifts is the day
/// somebody burns a disc because the preview looked right.
/// </remarks>
public class SeeingTheMenuFirst
{
    [Fact]
    public async Task Writes_the_same_menu_the_disc_would_get()
    {
        RecordingWriter previewed = new();
        RecordingWriter burned = new();

        APreviewOfTheMenu preview = new(previewed, new PicturesThatRead());
        preview.Write(AProject(), Media.Dvd5, _ => 1000, @"C:\temp\look");

        await new WriteTheMenuStep().Run(ADiscBeingMade(burned), CancellationToken.None);

        string onTheDisc = burned.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        string inThePreview = previewed.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // One difference, and it is the one that has to differ.
        Assert.Equal(
            onTheDisc.Replace("singleinstance=\"yes\"", "singleinstance=\"no\"", StringComparison.Ordinal),
            inThePreview);
    }

    [Fact]
    public void Lets_a_second_preview_open_beside_the_first()
    {
        RecordingWriter wrote = new();

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(AProject(), Media.Dvd5, _ => 1000, @"C:\temp\look");

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // A disc insists on being the only one of itself open, so putting the
        // same disc in twice refocuses rather than opening a second window.
        // A preview must not: it would refocus the stale window left over from
        // previewing something else a minute ago, and show the wrong games.
        Assert.Contains("singleinstance=\"no\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_the_disc_rather_than_a_menu_drawn_on_nothing()
    {
        RecordingWriter wrote = new();
        PicturesThatRead pictures = new();

        DiscProject withArt = AProject() with
        {
            Artwork = new DiscArtwork(IconPath: @"C:\art\icon.png", BackgroundPath: @"C:\art\bg.png"),
            Extras = new DiscExtras(MusicPath: @"C:\sound\theme.mp3")
        };

        new APreviewOfTheMenu(wrote, pictures).Write(withArt, Media.Dvd5, _ => 1000, @"C:\temp\look");

        // A preview that leaves the artwork out shows a menu drawn on
        // nothing, which is the one thing somebody choosing a background most
        // wants to look at.
        //
        // The icon and the background go through the pictures port rather than
        // the writer, because turning a photograph into an .ico is the
        // operating system's job and not this program's.
        Assert.Contains(pictures.Icons, path => path.EndsWith("disc.ico", StringComparison.Ordinal));
        Assert.Contains(
            pictures.Backgrounds, path => path.EndsWith(@"AUTORUN\background.png", StringComparison.Ordinal));

        // The music is a plain copy.
        Assert.Contains(wrote.Everything, path => path.EndsWith(@"AUTORUN\music.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void Says_where_it_put_it()
    {
        Outcome<string> went = new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead())
            .Write(AProject(), Media.Dvd5, _ => 1000, @"C:\temp\look");

        string where = Assert.IsType<Outcome<string>.Ok>(went).Value;

        Assert.Equal(@"C:\temp\look\AUTORUN\menu.hta", where);
    }

    [Fact]
    public void Writes_the_logic_beside_it_or_the_menu_does_nothing()
    {
        RecordingWriter wrote = new();

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(AProject(), Media.Dvd5, _ => 1000, @"C:\temp\look");

        // The page is a shell around menu.js. Without it the preview opens to
        // an empty window, which is a worse lie than no preview at all.
        Assert.Contains(
            wrote.Texts,
            text => text.Path.EndsWith(@"AUTORUN\menu.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Shows_the_first_disc_of_a_set()
    {
        RecordingWriter wrote = new();

        // Two games that cannot share a CD, so they get a disc each.
        DiscProject big = AProject() with
        {
            Games = [AGame("Hollow Knight"), AGame("Grim Fandango")]
        };

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(big, Media.Cd, _ => 600L * 1024 * 1024, @"C:\temp\look");

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // Whichever game lands on disc one is what somebody is previewing, and
        // which that is was decided by planning rather than by the order they
        // were added.
        Assert.Contains("Hollow Knight", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_rather_than_previewing_nothing()
    {
        Outcome<string> went = new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead())
            .Write(new DiscProject("EMPTY", [], new DiscOptions()), Media.Dvd5, _ => 1000, @"C:\temp\look");

        Refusal refused = Assert.IsType<Outcome<string>.No>(went).Refusal;

        Assert.Contains("nothing", refused.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passes_on_the_refusal_rather_than_inventing_one()
    {
        // A file bigger than any disc is refused at planning, and previewing
        // has to say the same thing building would say rather than a vaguer
        // version of it.
        ListedFile huge = new("setup_enormous.exe", 9L * 1024 * 1024 * 1024);

        DiscProject enormous = AProject() with
        {
            Games = [new GameFolder(@"C:\games\x", GameKind.GogInstaller, "Enormous", "Enormous", huge, [huge], [], null)]
        };

        Outcome<string> went = new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead())
            .Write(enormous, Media.Dvd5, _ => huge.Bytes, @"C:\temp\look");

        Assert.IsType<Outcome<string>.No>(went);
    }

    [Fact]
    public void Tells_the_window_when_previewing_becomes_possible()
    {
        MakeADisc screen = TheScreen();
        List<string> changed = [];
        screen.PropertyChanged += (_, what) => changed.Add(what.PropertyName ?? string.Empty);

        Assert.False(screen.CanPreview);

        screen.Add(AGame("Hollow Knight"));

        // A property nobody is told about keeps whatever the window read out
        // of it the first time, which here is a button that is dead for ever.
        // The real window found this; reading the property in a test does not.
        Assert.True(screen.CanPreview);
        Assert.Contains(nameof(MakeADisc.CanPreview), changed, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------- helpers

    private static DiscProject AProject() =>
        new("HOLLOW KNIGHT", [AGame("Hollow Knight")], new DiscOptions());

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    /// <summary>The same project, as the builder would hand it to the step.</summary>
    private static BuildContext ADiscBeingMade(IStageWriter writer)
    {
        DiscProject project = AProject();
        PlannedDisc disc = new(1, [], 0);

        return new BuildContext(project, new DiscLayout([disc], Media.Dvd5.Capacity, 1), disc, @"C:\temp\look", writer);
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
            new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead()),
            new PicturesThatRead(),
            "2.0.0",
            _ => 1000));

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

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<string>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>("blank");
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

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    /// <summary>Pictures that can all be read, which is the ordinary case.</summary>
    private sealed class PicturesThatRead : IImages
    {
        internal string? Refuse { get; init; }

        public ImageFacts Read(string path) =>
            Refuse is null ? new ImageFacts(true, 512, 512, null) : new ImageFacts(false, 0, 0, Refuse);

        internal List<string> Icons { get; } = [];

        internal List<string> Backgrounds { get; } = [];

        public void WriteIcon(string source, string destination) => Icons.Add(destination);

        public void WriteBackground(string source, string destination, int width, int height, bool asIs) =>
            Backgrounds.Add(destination);
    }
}
