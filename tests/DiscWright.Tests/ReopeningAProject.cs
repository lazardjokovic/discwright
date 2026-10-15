using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Turning what a project file says back into a disc that could be built.
/// </summary>
public class ReopeningAProject : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dw-reopen-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Reads_the_games_again_rather_than_trusting_the_file()
    {
        string game = AGameFolder("Hollow Knight", "setup_hollow_knight.exe");

        Reopened back = Reopen(AProjectNaming(game));

        Assert.True(back.WholeAgain);
        GameFolder found = Assert.Single(back.Project.Games);

        // Read off the disk, not out of the file: the file holds a path and
        // nothing else about what is in it.
        Assert.NotEmpty(found.Files);
        Assert.Equal(game, found.Path);
    }

    [Fact]
    public void Keeps_the_name_somebody_typed()
    {
        string game = AGameFolder("Hollow Knight", "setup_hollow_knight.exe");

        Reopened back = Reopen(AProjectWhoseGameIsCalled(game, "My Favourite Game"));

        Assert.Equal("My Favourite Game", Assert.Single(back.Project.Games).Name);
    }

    [Fact]
    public void Says_which_game_is_not_where_it_was_and_opens_the_rest()
    {
        string here = AGameFolder("Still Here", "setup_still_here.exe");
        string moved = Path.Combine(_root, "gone away");

        Reopened back = Reopen(AProjectNaming(here, moved));

        // The commonest thing that happens to a project a year later, and
        // refusing the whole disc over one game somebody moved would be a poor
        // answer to it.
        Assert.False(back.WholeAgain);
        Assert.True(back.AnythingLeft);
        Assert.Single(back.Project.Games);
        Assert.Equal([moved], back.Gone);
        Assert.Contains("not where it was", back.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_when_none_of_it_is_there_any_more()
    {
        Reopened back = Reopen(AProjectNaming(Path.Combine(_root, "one"), Path.Combine(_root, "two")));

        Assert.False(back.AnythingLeft);
        Assert.Contains("None of the 2", back.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public void Points_an_add_on_at_the_game_it_belonged_to()
    {
        string first = AGameFolder("First", "setup_first.exe");
        string second = AGameFolder("Second", "setup_second.exe");
        string patch = Path.Combine(second, "patch_second_1_to_2.exe");
        File.WriteAllText(patch, "a patch");

        // The add-on sits in the MIDDLE of the entries, which is where the two
        // ways of counting come apart: the file counts entries and a disc
        // counts games.
        OpenedProject opened = Open($$"""
            {
              "Label": "A SET",
              "Games": [
                { "Folder": {{Json(first)}}, "GameName": "First" },
                { "Folder": {{Json(second)}}, "Kind": "AddOn", "Parent": 2, "Setup": {{Json(patch)}} },
                { "Folder": {{Json(second)}}, "GameName": "Second" }
              ]
            }
            """);

        Reopened back = Reopen(opened);

        Assert.Equal(2, back.Project.Games.Count);

        AddOn attached = Assert.Single(back.Project.AddOnsOrNone);
        Assert.Equal(patch, attached.Path);

        // Entry 2 is game 1. Taking the entry number as the game number would
        // have attached it to nothing.
        Assert.Equal(1, attached.BelongsTo);
        Assert.Equal("Second", back.Project.Games[attached.BelongsTo].Name);
    }

    [Fact]
    public void Drops_an_add_on_whose_game_did_not_come_back()
    {
        string patch = Path.Combine(_root, "patch_gone_1_to_2.exe");
        Directory.CreateDirectory(_root);
        File.WriteAllText(patch, "a patch");

        OpenedProject opened = Open($$"""
            {
              "Games": [
                { "Folder": {{Json(Path.Combine(_root, "not here"))}}, "GameName": "Gone" },
                { "Folder": {{Json(_root)}}, "Kind": "AddOn", "Parent": 0, "Setup": {{Json(patch)}} }
              ]
            }
            """);

        Reopened back = Reopen(opened);

        // An add-on with no game is a button that does nothing.
        Assert.Empty(back.Project.AddOnsOrNone);
        Assert.Contains(patch, back.Gone);
    }

    [Fact]
    public void Brings_back_the_options_and_the_artwork_and_where_it_was_built()
    {
        string game = AGameFolder("A Game", "setup_a_game.exe");

        OpenedProject opened = Open($$"""
            {
              "Label": "A GAME",
              "Games": [{ "Folder": {{Json(game)}} }],
              "Menu": true, "Checksums": true, "LegacyFs": true, "CutFiles": true,
              "IconPath": "D:\\art\\icon.png", "BgPath": "D:\\art\\bg.png", "BgAsIs": true,
              "ManualPath": "D:\\manual.pdf", "ExtraItems": ["D:\\read me.txt"],
              "MusicFile": "D:\\tune.mp3",
              "MediaKey": "DVD9", "OutDir": "D:\\out"
            }
            """);

        Reopened back = Reopen(opened);

        Assert.Equal("A GAME", back.Project.Label);
        Assert.True(back.Project.Options.CutFiles);
        Assert.Equal(@"D:\art\icon.png", back.Project.Artwork?.IconPath);
        Assert.True(back.Project.Artwork?.BackgroundAsIs);
        Assert.Equal(@"D:\manual.pdf", back.Project.Extras?.ManualPath);
        Assert.Equal([@"D:\read me.txt"], back.Project.Extras?.Loose);
        Assert.Equal(Media.Dvd9, back.Medium);
        Assert.Equal(@"D:\out", back.OutputFolder);
    }

    // ------------------------------------------------------------------ helpers

    private static OpenedProject Open(string json) =>
        Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(json)).Value;

    private static string Json(string path) => System.Text.Json.JsonSerializer.Serialize(path);

    private static OpenedProject AProjectNaming(params string[] folders) =>
        Open($$"""
            { "Label": "A DISC", "Games": [{{string.Join(", ", folders.Select(f => $$"""{ "Folder": {{Json(f)}} }"""))}}] }
            """);

    private static OpenedProject AProjectWhoseGameIsCalled(string folder, string called) =>
        Open($$"""
            { "Label": "A DISC", "Games": [{ "Folder": {{Json(folder)}}, "GameName": {{Json(called)}} }] }
            """);

    private static Reopened Reopen(OpenedProject opened) =>
        ReopenAProject.From(
            opened,
            GameFolderReader.InTheUsualOrder(new NoFacts()),
            new Platform.WindowsFileSystem(),
            path => File.Exists(path) ? new FileInfo(path).Length : 0);

    private string AGameFolder(string name, string installer)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, installer), "an installer");
        File.WriteAllText(Path.Combine(folder, installer.Replace(".exe", "-1.bin")), "its parts");

        return folder;
    }

    private sealed class NoFacts : IFileFacts
    {
        public string? ProductNameOf(string path) => null;
    }
}
