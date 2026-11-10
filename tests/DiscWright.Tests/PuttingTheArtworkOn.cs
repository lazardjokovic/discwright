using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Tests;

public class PuttingTheArtworkOn
{
    [Fact]
    public async Task Turns_a_picture_into_the_icon_This_PC_shows()
    {
        RecordingImages drew = new();

        await Run(drew, new RecordingWriter(), new DiscArtwork(IconPath: @"C:\art\icon.png"));

        // Named after the disc, not "disc.ico". Explorer caches an icon by
        // its path, so one fixed name means the second disc into a drive
        // letter wears the first one's picture.
        Assert.Equal((@"C:\art\icon.png", @"C:\stage\HOLLOWKNIGHT.ico"), Assert.Single(drew.Icons));
    }

    [Fact]
    public async Task Writes_the_icon_once_and_leaves_no_second_copy()
    {
        RecordingImages drew = new();
        RecordingWriter wrote = new();

        await Run(drew, wrote, new DiscArtwork(IconPath: @"C:\art\icon.png"));

        // v1 writes a second copy beside the menu, on the grounds that an
        // HTA will not resolve an icon path climbing out of the page's own
        // folder. That was checked by rendering such a page and it is not
        // true here: it wears it. A second copy would be another third of
        // a megabyte on every disc for nothing.
        Assert.Single(drew.Icons);
        Assert.Empty(wrote.Copies);
    }

    [Fact]
    public async Task Fits_the_background_to_the_menu_rather_than_to_the_picture()
    {
        RecordingImages drew = new();

        await Run(drew, new RecordingWriter(), new DiscArtwork(BackgroundPath: @"C:\art\bg.jpg"));

        RecordingImages.Background made = Assert.Single(drew.Backgrounds);

        // The menu's own size. A background made to any other size is a
        // background the menu will stretch again at the far end.
        Assert.Equal(760, made.Width);
        Assert.Equal(480, made.Height);
        Assert.Equal(@"C:\stage\AUTORUN\background.png", made.Destination);
    }

    [Fact]
    public async Task Crops_by_default_and_stretches_only_when_asked()
    {
        RecordingImages cropped = new();
        await Run(cropped, new RecordingWriter(), new DiscArtwork(BackgroundPath: @"C:\art\bg.jpg"));
        Assert.False(Assert.Single(cropped.Backgrounds).AsIs);

        RecordingImages stretched = new();
        await Run(stretched, new RecordingWriter(), new DiscArtwork(BackgroundPath: @"C:\art\bg.jpg", BackgroundAsIs: true));
        Assert.True(Assert.Single(stretched.Backgrounds).AsIs);
    }

    [Fact]
    public async Task Leaves_the_background_off_a_disc_with_no_menu()
    {
        RecordingImages drew = new();

        await Run(drew, new RecordingWriter(), new DiscArtwork(BackgroundPath: @"C:\art\bg.jpg"), menu: false);

        // It would be a file nothing ever opens.
        Assert.Empty(drew.Backgrounds);
    }

    [Fact]
    public async Task Carries_the_printable_artwork_as_ordinary_pictures()
    {
        RecordingWriter wrote = new();

        await Run(new RecordingImages(), wrote,
            new DiscArtwork(CoverPath: @"C:\art\sleeve.png", DiscFacePath: @"C:\art\face.jpg"));

        // Copied exactly as given, at whatever size they were given. Resizing
        // them would be deciding what somebody's printer can do, years early.
        Assert.Equal(
            [@"C:\stage\ARTWORK\cover.png", @"C:\stage\ARTWORK\disc face.jpg"],
            wrote.Copies.Select(copy => copy.To));
    }

    [Fact]
    public async Task Tells_whoever_finds_the_disc_how_to_print_it()
    {
        RecordingWriter wrote = new();

        await Run(new RecordingImages(), wrote, new DiscArtwork(CoverPath: @"C:\art\sleeve.png"));

        string readme = wrote.Texts.Single(text => text.Path.EndsWith("READ ME.txt", StringComparison.Ordinal)).Text;

        // The ten year promise made real: this folder needs a printer, and
        // nothing else. Written for somebody who has never heard of DiscWright,
        // because in ten years that is who is reading it.
        Assert.Contains("HOLLOW KNIGHT", readme, StringComparison.Ordinal);
        Assert.Contains("Nothing here needs the program that made this disc", readme, StringComparison.Ordinal);
        Assert.Contains("at their own size", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Puts_the_printable_folder_on_without_being_asked()
    {
        RecordingWriter wrote = new();

        await Run(new RecordingImages(), wrote, new DiscArtwork(CoverPath: @"C:\art\sleeve.png"));

        // There is no asking any more. A cover and a label are small, and a
        // disc carrying them is one somebody can reprint the sleeve from in
        // ten years without still having the folder it was built from.
        // Making it a setting meant the usual answer was no, because the
        // usual answer to a setting nobody understands is to leave it alone.
        Assert.NotEmpty(wrote.Copies);
    }

    [Fact]
    public void Has_nothing_to_do_for_a_disc_with_no_pictures()
    {
        Assert.False(new CopyTheArtworkStep(new RecordingImages()).AppliesTo(Context(new RecordingWriter(), null)));
        Assert.False(new CopyTheArtworkStep(new RecordingImages()).AppliesTo(Context(new RecordingWriter(), new DiscArtwork())));
    }

    // ------------------------------------------------------------------ helpers

    private static Task Run(
        IImages images,
        IStageWriter writer,
        DiscArtwork artwork,
        bool menu = true) =>
        new CopyTheArtworkStep(images).Run(
            Context(writer, artwork, menu), CancellationToken.None);

    private static BuildContext Context(
        IStageWriter writer,
        DiscArtwork? artwork,
        bool menu = true)
    {
        PlannedDisc disc = new(1, [], 0);

        return new BuildContext(
            new DiscProject(
                "HOLLOW KNIGHT",
                [],
                new DiscOptions(Menu: menu),
                artwork),
            new DiscLayout([disc], 1_000_000, false),
            disc,
            @"C:\stage",
            writer);
    }

    /// <summary>Draws nothing and remembers what it was asked to draw.</summary>
    private sealed class RecordingImages : IImages
    {
        public List<(string Source, string Destination)> Icons { get; } = [];

        public List<Background> Backgrounds { get; } = [];

        public ImageFacts Read(string path) => new(true, 1920, 1080, null);

        public void WriteIcon(string source, string destination) => Icons.Add((source, destination));

        public void WriteBackground(string source, string destination, int width, int height, bool asIs) =>
            Backgrounds.Add(new Background(source, destination, width, height, asIs));

        public sealed record Background(string Source, string Destination, int Width, int Height, bool AsIs);
    }
}
