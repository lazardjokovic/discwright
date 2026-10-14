using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Tests;

public class CopyingTheFiles
{
    [Fact]
    public async Task Copies_what_is_on_this_disc_and_nothing_else()
    {
        RecordingWriter wrote = new();

        await Copy(wrote, OneDisc(
            Whole("setup_game.exe", @"C:\gog\setup_game.exe", 800),
            Whole("setup_game-1.bin", @"C:\gog\setup_game-1.bin", 1200)));

        Assert.Equal(
            [@"C:\stage\setup_game.exe", @"C:\stage\setup_game-1.bin"],
            wrote.Copies.Select(copy => copy.To));
    }

    [Fact]
    public async Task Keeps_a_file_where_it_sat_inside_its_folder()
    {
        RecordingWriter wrote = new();

        await Copy(wrote, OneDisc(Whole(@"Data\sound\music.ogg", @"C:\game\Data\sound\music.ogg", 700)));

        // A GOG download has no shape to keep. A folder of game files does, and
        // the folders have to be made before anything lands in them.
        Assert.Equal(@"C:\stage\Data\sound\music.ogg", Assert.Single(wrote.Copies).To);
        Assert.Contains(@"C:\stage\Data\sound", wrote.Folders);
    }

    [Fact]
    public async Task Asks_for_a_piece_of_a_file_that_is_too_big_for_one_disc()
    {
        RecordingWriter wrote = new();

        await Copy(wrote, OneDisc(
            new PlannedFile("huge.bin.dwpart02", @"C:\game\huge.bin", 10, new FilePart(2, 3, 10, 25))));

        RecordingWriter.Copied piece = Assert.Single(wrote.Copies);

        // Read out of the original at the right place. The source is the
        // person's own download and is never touched.
        Assert.Equal(@"C:\game\huge.bin", piece.From);
        Assert.Equal(@"C:\stage\huge.bin.dwpart02", piece.To);
        Assert.Equal(10, piece.Offset);
        Assert.Equal(10, piece.Bytes);
    }

    [Fact]
    public async Task Copies_a_whole_file_as_a_whole_file()
    {
        RecordingWriter wrote = new();

        await Copy(wrote, OneDisc(Whole("readme.txt", @"C:\game\readme.txt", 10)));

        // Not as a piece that happens to start at nothing and run to the end.
        Assert.Null(Assert.Single(wrote.Copies).Bytes);
    }

    [Fact]
    public void Has_nothing_to_do_for_a_disc_that_carries_only_the_extras()
    {
        // In a set the manual and the extras can need a disc to themselves.
        // That disc still gets a menu and an autorun, so this is not an error,
        // it is a step that does not apply and is never counted.
        Assert.False(new CopyTheFilesStep().AppliesTo(Context(new RecordingWriter(), OneDisc())));
    }

    [Fact]
    public async Task Stops_part_way_through_when_it_is_cancelled()
    {
        using CancellationTokenSource cancelling = new();
        RecordingWriter wrote = new();
        cancelling.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CopyTheFilesStep().Run(
                Context(wrote, OneDisc(Whole("a.bin", @"C:\a.bin", 1))), cancelling.Token));

        // Checked before each file rather than once at the start: a twenty
        // gigabyte disc is one step, and Cancel has to mean something during it.
        Assert.Empty(wrote.Copies);
    }

    // ------------------------------------------------------------------ helpers

    private static PlannedFile Whole(string relative, string source, long bytes) =>
        new(relative, source, bytes, null);

    private static PlannedDisc OneDisc(params PlannedFile[] files) =>
        new(1, files, files.Sum(file => file.Bytes));

    private static BuildContext Context(IStageWriter writer, PlannedDisc disc) =>
        new(new DiscProject("TEST", [], new DiscOptions()),
            new DiscLayout([disc], 1_000_000, 0),
            disc,
            @"C:\stage",
            writer);

    private static Task Copy(IStageWriter writer, PlannedDisc disc) =>
        new CopyTheFilesStep().Run(Context(writer, disc), CancellationToken.None);
}
