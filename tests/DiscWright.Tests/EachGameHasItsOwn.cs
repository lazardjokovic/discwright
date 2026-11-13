using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// Each game's own manual and extras, on the disc beside the game they belong
/// to. Asked for by xniwo on discwright#98 and on Reddit, and approved by the
/// owner on 2026-11-13.
/// </summary>
public sealed class EachGameHasItsOwn : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwown_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;

    public EachGameHasItsOwn()
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
    public async Task A_lone_game_with_only_a_manual_keeps_the_root_and_its_manual_sits_in_Manual()
    {
        GameFolder game = AGame("Hollow Knight", 500) with
        {
            Own = [new GameExtra(AFile("Hollow Knight manual.pdf", 400), GameExtraKind.Manual)]
        };

        BuiltDisc disc = Assert.Single((await Build(OnADisc(game))).Discs);

        // Manual belongs to nothing else at the root, so the game keeps it.
        Assert.True(File.Exists(Path.Combine(disc.Folder, "setup_hollow_knight.exe")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Manual", "Hollow Knight manual.pdf")));
    }

    [Fact]
    public async Task A_lone_game_with_extras_of_its_own_takes_a_folder_so_they_never_meet_the_discs_Extras()
    {
        string artbook = AFolder("Artbook", ("page 1.png", 100), (@"sketches\page 2.png", 200));
        GameFolder game = AGame("The Witcher", 500) with { Own = [new GameExtra(artbook, GameExtraKind.Extra)] };
        string discManual = AFile("disc manual.pdf", 50);

        BuiltDisc disc = Assert.Single(
            (await Build(OnADisc(game) with { Extras = new DiscExtras(ManualPath: discManual) })).Discs);

        // The disc's own Extras stays at the root, and the game's are in its
        // folder: two folders called Extras at the root would have had one
        // land on top of the other.
        Assert.True(File.Exists(Path.Combine(disc.Folder, "Extras", "disc manual.pdf")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "The Witcher", "setup_the_witcher.exe")));

        // A folder goes whole, under its own name.
        Assert.True(File.Exists(Path.Combine(disc.Folder, "The Witcher", "Extras", "Artbook", "page 1.png")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "The Witcher", "Extras", "Artbook", "sketches", "page 2.png")));
    }

    [Fact]
    public async Task Each_game_on_a_shared_disc_gets_its_own_and_nobody_elses()
    {
        GameFolder witcher = AGame("The Witcher", 500) with
        {
            Own = [new GameExtra(AFile("witcher manual.pdf", 100), GameExtraKind.Manual)]
        };
        GameFolder knight = AGame("Hollow Knight", 500) with
        {
            Own = [new GameExtra(AFile("soundtrack.zip", 100), GameExtraKind.Extra)]
        };

        BuiltDisc disc = Assert.Single((await Build(OnADisc(witcher, knight))).Discs);

        Assert.True(File.Exists(Path.Combine(disc.Folder, "01 - The Witcher", "Manual", "witcher manual.pdf")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "02 - Hollow Knight", "Extras", "soundtrack.zip")));
        Assert.False(Directory.Exists(Path.Combine(disc.Folder, "01 - The Witcher", "Extras")));
        Assert.False(Directory.Exists(Path.Combine(disc.Folder, "02 - Hollow Knight", "Manual")));
    }

    [Fact]
    public async Task Rides_every_disc_of_its_set()
    {
        GameFolder game = AGame("Big", 4000, parts: 3) with
        {
            Own = [new GameExtra(AFile("manual.pdf", 400), GameExtraKind.Manual)]
        };

        BuiltDiscs built = await Build(OnADisc(game), capacity: Overhead + 6000);

        Assert.True(built.Discs.Count > 1);

        // The owner's rule from #167, agreed again for these on 2026-11-13:
        // every disc of the set carries them and pays their room, so no
        // disc's menu offers a Manual with nothing behind it.
        Assert.All(built.Discs, disc =>
            Assert.True(File.Exists(Path.Combine(disc.Folder, "Manual", "manual.pdf")), $"disc {disc.Number} has no manual"));
    }

    [Fact]
    public async Task Counts_against_every_disc()
    {
        GameFolder game = AGame("Game", 4000) with
        {
            Own = [new GameExtra(AFile("an enormous manual.pdf", 4000), GameExtraKind.Manual)]
        };

        // The game alone would fit. Beside its manual it does not, and
        // without cutting the build is refused rather than squeezed.
        Outcome<BuiltDiscs> built = await Builder().Build(
            OnADisc(game), new Medium("TEST", "a test disc", Overhead + 5000), _out, null, CancellationToken.None);

        Assert.IsType<Outcome<BuiltDiscs>.No>(built);
    }

    [Fact]
    public async Task A_game_with_a_disc_of_its_own_pays_for_nobody_elses_extras()
    {
        // Two games that cannot share a disc get one each. The first one's
        // artbook goes on the first one's disc and must not take room from
        // the second, which fits its disc only without it.
        GameFolder first = AGame("First", 4000) with
        {
            Own = [new GameExtra(AFile("artbook.zip", 600), GameExtraKind.Extra)]
        };
        GameFolder second = AGame("Second", 4500);

        BuiltDiscs built = await Build(OnADisc(first, second), capacity: Overhead + 5000);

        Assert.Equal(2, built.Discs.Count);
        Assert.True(File.Exists(Path.Combine(built.Discs[0].Folder, "First", "Extras", "artbook.zip"))
            || File.Exists(Path.Combine(built.Discs[0].Folder, "Extras", "artbook.zip")));
        Assert.Empty(Directory.EnumerateFiles(built.Discs[1].Folder, "artbook.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Hashes_them_like_everything_else()
    {
        GameFolder game = AGame("Hollow Knight", 500) with
        {
            Own = [new GameExtra(AFile("Hollow Knight manual.pdf", 300), GameExtraKind.Manual)]
        };

        BuiltDisc disc = Assert.Single((await Build(OnADisc(game))).Discs);

        Assert.Contains(
            "Manual/Hollow Knight manual.pdf",
            File.ReadAllText(Path.Combine(disc.Folder, "checksums.sha256")).Replace((char)92, '/'),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_menu_looks_for_the_game_where_the_build_put_it()
    {
        string artbook = AFolder("Artbook", ("page.png", 100));
        GameFolder game = AGame("The Witcher", 500) with { Own = [new GameExtra(artbook, GameExtraKind.Extra)] };

        BuiltDisc disc = Assert.Single((await Build(OnADisc(game))).Discs);

        // Its extras moved the game into a folder of its own, and the menu's
        // Install has to follow it there or it runs nothing.
        Assert.Contains(
            "The Witcher/setup_the_witcher.exe",
            File.ReadAllText(Path.Combine(disc.Folder, "AUTORUN", "menu.hta")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Lands_inside_the_games_folder_under_its_own_name()
    {
        GameExtra manual = new(@"D:\goodies\The Witcher - manual.pdf", GameExtraKind.Manual);
        GameExtra artbook = new(@"D:\goodies\Artbook\", GameExtraKind.Extra);

        Assert.Equal(Path.Combine("01 - The Witcher", "Manual", "The Witcher - manual.pdf"), manual.OnTheDisc("01 - The Witcher"));
        Assert.Equal(Path.Combine("Manual", "The Witcher - manual.pdf"), manual.OnTheDisc(string.Empty));

        // A folder chosen with a slash on the end still has a name.
        Assert.Equal(Path.Combine("01 - The Witcher", "Extras", "Artbook"), artbook.OnTheDisc("01 - The Witcher"));
    }

    // ------------------------------------------------------------------ helpers

    private const long Overhead = 2L * 1024 * 1024;

    private static DiscProject OnADisc(params GameFolder[] games) =>
        new("A DISC", games, new DiscOptions());

    private async Task<BuiltDiscs> Build(DiscProject project, long capacity = Overhead + 1_000_000)
    {
        Outcome<BuiltDiscs> built = await Builder().Build(
            project, new Medium("TEST", "a test disc", capacity), _out, null, CancellationToken.None);

        return Assert.IsType<Outcome<BuiltDiscs>.Ok>(built).Value;
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
        string path = Path.Combine(_root, "goodies", name);
        Write(path, bytes);
        return path;
    }

    private string AFolder(string name, params (string Path, int Bytes)[] inside)
    {
        string folder = Path.Combine(_root, "goodies", name);
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
