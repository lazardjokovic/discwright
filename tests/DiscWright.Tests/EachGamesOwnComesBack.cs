using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// A game's own manual and extras, saved in the project file and opened again.
/// </summary>
public sealed class EachGamesOwnComesBack : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dw-ownback-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Writes_each_games_own_and_reads_them_back_with_their_kinds()
    {
        DiscProject project = AProject(
            new GameExtra(@"C:\goodies\manual.pdf", GameExtraKind.Manual),
            new GameExtra(@"C:\goodies\Artbook", GameExtraKind.Extra),
            new GameExtra(@"C:\goodies\handbuch.pdf", GameExtraKind.Manual));

        OpenedProject back = Written(project);

        ProjectEntry game = Assert.Single(back.Games);
        Assert.Equal(
            [
                new GameExtra(@"C:\goodies\manual.pdf", GameExtraKind.Manual),
                new GameExtra(@"C:\goodies\handbuch.pdf", GameExtraKind.Manual),
                new GameExtra(@"C:\goodies\Artbook", GameExtraKind.Extra)
            ],
            game.OwnOrNone);

        // An add-on is not a game and has none of its own.
        Assert.All(back.Entries.Where(entry => entry.IsAnAddOn), entry => Assert.Empty(entry.OwnOrNone));
    }

    [Fact]
    public void Leaves_the_old_Manual_and_Extras_fields_empty_for_v1()
    {
        string json = TheProjectAsJson.Of(
            AProject(new GameExtra(@"C:\goodies\manual.pdf", GameExtraKind.Manual)),
            "2.0.4", "CD", @"F:\out", DateTimeOffset.UnixEpoch);

        // v1 read Manual and Extras as one path each, so a list there would
        // be misread. The game's own go under names of their own.
        using System.Text.Json.JsonDocument read = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement game = read.RootElement.GetProperty("Games")[0];

        Assert.Equal(System.Text.Json.JsonValueKind.Null, game.GetProperty("Manual").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, game.GetProperty("Extras").ValueKind);
        Assert.Equal(@"C:\goodies\manual.pdf", game.GetProperty("OwnManuals")[0].GetString());
    }

    [Fact]
    public void A_file_older_than_this_reads_as_no_manual_and_no_extras_of_the_games_own()
    {
        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(
            """{ "Label": "A DISC", "Games": [{ "Folder": "C:\\games\\Old", "Manual": null, "Extras": null }] }""")).Value;

        Assert.Empty(Assert.Single(back.Games).OwnOrNone);
    }

    [Fact]
    public void Reopening_keeps_what_is_still_there_and_names_what_has_gone()
    {
        string game = AGameFolder("Hollow Knight", "setup_hollow_knight.exe");
        string manual = Path.Combine(_root, "goodies", "manual.pdf");
        string artbook = Path.Combine(_root, "goodies", "Artbook");
        string moved = Path.Combine(_root, "goodies", "moved.zip");
        Directory.CreateDirectory(artbook);
        File.WriteAllText(manual, "a manual");
        File.WriteAllText(Path.Combine(artbook, "page.png"), "twelve bytes");

        OpenedProject opened = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(TheProjectAsJson.Of(
            new DiscProject("A DISC",
            [
                new GameFolder(game, GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", null, [], [], null,
                    [
                        new GameExtra(manual, GameExtraKind.Manual),
                        new GameExtra(artbook, GameExtraKind.Extra),
                        new GameExtra(moved, GameExtraKind.Extra)
                    ])
            ], new DiscOptions()),
            "2.0.4", "CD", @"F:\out", DateTimeOffset.UnixEpoch))).Value;

        Reopened back = ReopenAProject.From(
            opened,
            GameFolderReader.InTheUsualOrder(new NoFacts()),
            new Platform.WindowsFileSystem(),
            path => File.Exists(path) ? new FileInfo(path).Length : 0);

        // A folder counts as much as a file, and a moved one is said rather
        // than dropped in silence, the way a moved game folder is.
        Assert.Equal(
            [new GameExtra(manual, GameExtraKind.Manual, 8), new GameExtra(artbook, GameExtraKind.Extra, 12)],
            Assert.Single(back.Project.Games).OwnOrNone);
        Assert.Equal([moved], back.Gone);
    }

    // ------------------------------------------------------------------ helpers

    private static OpenedProject Written(DiscProject project)
    {
        string json = TheProjectAsJson.Of(project, "2.0.4", "CD", @"F:\out", DateTimeOffset.UnixEpoch);

        return Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(json)).Value;
    }

    private static DiscProject AProject(params GameExtra[] own) => new(
        Label: "A BIG GAME",
        Games:
        [
            new GameFolder(
                Path: @"C:\games\A Big Game",
                Kind: GameKind.GogInstaller,
                Name: "A Big Game",
                MatchName: "A Big Game",
                Installer: new ListedFile("setup_a_big_game.exe", 900),
                Files: [new ListedFile("setup_a_big_game.exe", 900)],
                LauncherCandidates: [],
                Warning: null,
                Own: own)
        ],
        Options: new DiscOptions(Menu: true, Checksums: true),
        AddOns: [new AddOn(0, @"C:\games\A Big Game\patch.exe", "A Patch", 70)]);

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
