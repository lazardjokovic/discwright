using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// The manual, the extras folder, the loose files and the music, which between
/// them are everything on a disc that is neither the game nor the artwork.
/// </summary>
public sealed class TheExtras : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwext_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;

    public TheExtras()
    {
        _out = Path.Combine(_root, "out");
        Directory.CreateDirectory(_out);
    }

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
    public async Task Puts_the_manual_in_Extras_under_its_own_name()
    {
        string manual = AFile("Hollow Knight manual.pdf", 400);

        BuiltDisc disc = Assert.Single((await Build(With(new DiscExtras(ManualPath: manual)))).Discs);

        // Keeps its own name: somebody looking for the manual in ten years is
        // looking for whatever it was called.
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Extras", "Hollow Knight manual.pdf")));
    }

    [Fact]
    public async Task Puts_the_contents_of_the_extras_folder_in_Extras_and_not_the_folder()
    {
        string folder = AFolder("stuff for the disc", ("concept art.png", 100), (@"wallpapers\2560.png", 200));

        BuiltDisc disc = Assert.Single((await Build(With(new DiscExtras(ExtrasFolderPath: folder)))).Discs);

        // Somebody picking a folder called "stuff for the disc" did not mean to
        // put a folder called "stuff for the disc" on the disc.
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Extras", "concept art.png")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Extras", "wallpapers", "2560.png")));
        Assert.False(Directory.Exists(Path.Combine(disc.Folder, "Extras", "stuff for the disc")));
    }

    [Fact]
    public async Task Puts_loose_files_at_the_root_exactly_as_they_are()
    {
        string patch = AFile("Community patch 1.3.exe", 300);
        string translation = AFolder("Fan translation", ("srpski.loc", 50));

        BuiltDisc disc = Assert.Single(
            (await Build(With(new DiscExtras(LooseItems: [patch, translation])))).Discs);

        // A loose folder keeps its own name, unlike the extras folder, because
        // this one was added as itself rather than as a container of things.
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Community patch 1.3.exe")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Fan translation", "srpski.loc")));
    }

    [Fact]
    public async Task Refuses_a_loose_file_whose_name_belongs_to_the_disc()
    {
        string theirs = AFile("autorun.inf", 10);

        Refusal refused = await Refuse(With(new DiscExtras(LooseItems: [theirs])));

        // Refused rather than renamed around. Quietly putting it somewhere else
        // is worse: they would burn a disc believing it holds a file it does
        // not hold, under a name they could find it by.
        Assert.Contains("autorun.inf", refused.Because, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_out));
    }

    [Theory]
    [InlineData("AUTORUN")]
    [InlineData("checksums.sha256")]
    [InlineData("disc.ico")]
    [InlineData("Extras")]
    [InlineData("setup_something.exe")]
    public void Knows_which_names_a_disc_keeps_for_itself(string name)
    {
        Assert.True(ReservedDiscNames.Claims(name));
    }

    [Theory]
    [InlineData("Community patch 1.3.exe")]
    [InlineData("my save from 2011.sav")]
    [InlineData("Fan translation")]
    public void Lets_an_ordinary_name_through(string name)
    {
        Assert.False(ReservedDiscNames.Claims(name));
    }

    [Fact]
    public async Task Puts_the_extras_on_one_disc_of_a_set_and_not_on_all_of_them()
    {
        string manual = AFile("manual.pdf", 400);

        BuiltDiscs built = await Build(
            With(new DiscExtras(ManualPath: manual), AGame("Big", 4000, parts: 3)),
            capacity: Overhead + 6000);

        Assert.True(built.Discs.Count > 1);

        // Copying a three gigabyte texture pack onto five discs costs five
        // times the room, so it rides the one disc with the slack for it.
        int carrying = built.Discs.Count(disc => Directory.Exists(Path.Combine(disc.Folder, "Extras")));

        Assert.Equal(1, carrying);
    }

    [Fact]
    public async Task Offers_a_Manual_button_only_on_the_disc_that_has_the_manual()
    {
        string manual = AFile("manual.pdf", 400);

        BuiltDiscs built = await Build(
            With(new DiscExtras(ManualPath: manual), AGame("Big", 4000, parts: 3)),
            capacity: Overhead + 6000);

        int offering = built.Discs.Count(disc =>
            File.ReadAllText(Path.Combine(disc.Folder, "AUTORUN", "menu.hta")).Contains("\"Manual\"", StringComparison.Ordinal));

        // A Manual button on a disc with no manual is worse than no button: it
        // is a promise the disc cannot keep, found by somebody who has already
        // burned it.
        Assert.Equal(1, offering);
    }

    [Fact]
    public async Task Offers_no_Manual_button_when_there_is_no_manual()
    {
        BuiltDisc disc = Assert.Single((await Build(With(null))).Discs);

        Assert.DoesNotContain(
            "\"Manual\"",
            File.ReadAllText(Path.Combine(disc.Folder, "AUTORUN", "menu.hta")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Puts_the_music_on_every_disc_because_every_disc_has_a_menu()
    {
        string music = AFile("Menu music.mp3", 2400);

        // Room for 10000 bytes, less the 2400 the music takes off EVERY disc,
        // leaves 7600 for four files of 4000. Asked for 6000 first, which left
        // 3600 and made every single file too big for a disc, and the build was
        // correctly refused: music is overhead, and overhead is room gone.
        BuiltDiscs built = await Build(
            With(new DiscExtras(MusicPath: music), AGame("Big", 4000, parts: 3)),
            capacity: Overhead + 10000);

        Assert.True(built.Discs.Count > 1);

        // Renamed to "music", keeping its extension, so a name with a quote or
        // a bracket in it never reaches the menu's script at all.
        Assert.All(built.Discs, disc =>
            Assert.True(File.Exists(Path.Combine(disc.Folder, "AUTORUN", "music.mp3"))));
    }

    [Fact]
    public async Task Counts_the_extras_against_the_disc_they_land_on()
    {
        // The manual is bigger than the room left after the game, so the two
        // cannot share a disc and the planner has to make another.
        string manual = AFile("an enormous manual.pdf", 4000);

        BuiltDiscs built = await Build(
            With(new DiscExtras(ManualPath: manual), AGame("Game", 4000)),
            capacity: Overhead + 5000);

        Assert.True(built.Discs.Count > 1, "The extras have to be weighed, or they go on a disc with no room for them.");
    }

    [Fact]
    public async Task Hashes_the_extras_like_everything_else()
    {
        string patch = AFile("Community patch 1.3.exe", 300);

        BuiltDisc disc = Assert.Single((await Build(With(new DiscExtras(LooseItems: [patch])))).Discs);

        Assert.Contains(
            "Community patch 1.3.exe",
            File.ReadAllText(Path.Combine(disc.Folder, "checksums.sha256")),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private const long Overhead = 2L * 1024 * 1024;

    private DiscProject With(DiscExtras? extras, GameFolder? game = null) =>
        new("HOLLOW KNIGHT", [game ?? AGame("Hollow Knight", 500)], new DiscOptions(), null, extras);

    private async Task<BuiltDiscs> Build(DiscProject project, long capacity = Overhead + 1_000_000)
    {
        Outcome<BuiltDiscs> built = await Builder().Build(
            project, new Medium("TEST", "a test disc", capacity), _out, null, CancellationToken.None);

        return Assert.IsType<Outcome<BuiltDiscs>.Ok>(built).Value;
    }

    private async Task<Refusal> Refuse(DiscProject project)
    {
        Outcome<BuiltDiscs> built = await Builder().Build(
            project, new Medium("TEST", "a test disc", Overhead + 1_000_000), _out, null, CancellationToken.None);

        return Assert.IsType<Outcome<BuiltDiscs>.No>(built).Refusal;
    }

    private static DiscBuilder Builder() =>
        new(new WindowsFileSystem(), new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher());

    private GameFolder AGame(string name, int bytes, int parts = 0)
    {
        string tidied = name.ToLowerInvariant().Replace(' ', '_');
        string folder = Path.Combine(_root, "src", tidied);
        Directory.CreateDirectory(folder);

        List<ListedFile> listed = [];
        Write(Path.Combine(folder, $"setup_{tidied}.exe"), bytes);
        listed.Add(new ListedFile($"setup_{tidied}.exe", bytes));

        for (int part = 1; part <= parts; part++)
        {
            Write(Path.Combine(folder, $"setup_{tidied}-{part}.bin"), bytes);
            listed.Add(new ListedFile($"setup_{tidied}-{part}.bin", bytes));
        }

        return new GameFolder(folder, GameKind.GogInstaller, name, name, listed[0], listed, [], null);
    }

    private string AFile(string name, int bytes)
    {
        string path = Path.Combine(_root, "extras", name);
        Write(path, bytes);
        return path;
    }

    private string AFolder(string name, params (string Path, int Bytes)[] inside)
    {
        string folder = Path.Combine(_root, "extras", name);
        Directory.CreateDirectory(folder);

        foreach ((string path, int bytes) in inside)
        {
            Write(Path.Combine(folder, path), bytes);
        }

        return folder;
    }

    private static void Write(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        byte[] contents = new byte[bytes];
        Random.Shared.NextBytes(contents);
        File.WriteAllBytes(path, contents);
    }
}
