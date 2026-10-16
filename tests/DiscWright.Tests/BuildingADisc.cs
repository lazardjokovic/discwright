using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// Whole builds, from a folder of files to a disc folder you could open.
/// </summary>
/// <remarks>
/// These run against real folders on a real disk, because the thing being
/// tested is the assembly of every part: planning, recognising, copying,
/// naming, the menu, the artwork and the checksums, in an order where several
/// of them depend on the one before. A written listing cannot say whether the
/// disc that came out is a disc.
/// </remarks>
public sealed class BuildingADisc : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwbuild_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;

    public BuildingADisc()
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

    // ---------------------------------------------------------------- one disc

    [Fact]
    public async Task Makes_one_disc_that_has_everything_on_it()
    {
        GameFolder game = AGogDownload("Hollow Knight", 4000);

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        Assert.Equal(1, disc.OfDiscs);
        Assert.Equal(Path.Combine(_out, "disc"), disc.Folder);

        // Everything a disc is: the game, its name, its menu, its proof.
        Assert.True(File.Exists(On(disc, "setup_hollow_knight.exe")));
        Assert.True(File.Exists(On(disc, "autorun.inf")));
        Assert.True(File.Exists(On(disc, @"AUTORUN\menu.hta")));
        Assert.True(File.Exists(On(disc, @"AUTORUN\menu.js")));
        Assert.True(File.Exists(On(disc, "checksums.sha256")));
    }

    [Fact]
    public async Task Writes_a_checksum_list_that_covers_every_file_it_wrote()
    {
        GameFolder game = AGogDownload("Hollow Knight", 2000);

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        string[] listed =
        [
            .. File.ReadAllLines(On(disc, "checksums.sha256"))
                .Where(line => !line.StartsWith('#'))
                .Select(line => line[(line.IndexOf(" *", StringComparison.Ordinal) + 2)..])
        ];

        string[] onTheDisc =
        [
            .. Directory.EnumerateFiles(disc.Folder, "*", SearchOption.AllDirectories)
                .Select(file => file[(disc.Folder.Length + 1)..].Replace('\\', '/'))
                .Where(file => file != "checksums.sha256")
        ];

        // The step runs last for this reason: everything every other step wrote
        // has to be in here, or the disc carries proof of only part of itself.
        Assert.Equal(onTheDisc.OrderBy(name => name, StringComparer.Ordinal), listed.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Writes_hashes_that_are_actually_right()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        Sha256Hasher hasher = new();

        foreach (string line in File.ReadAllLines(On(disc, "checksums.sha256")).Where(line => !line.StartsWith('#')))
        {
            string[] halves = line.Split(" *", 2);
            string file = Path.Combine(disc.Folder, halves[1].Replace('/', '\\'));

            Assert.Equal(halves[0], hasher.Sha256(file, CancellationToken.None));
        }
    }

    // ---------------------------------------------------------------- a set

    [Fact]
    public async Task Makes_a_numbered_set_when_one_game_needs_more_than_one_disc()
    {
        GameFolder game = AGogDownload("Big Game", 4000, parts: 3);

        BuiltDiscs built = await Build(Project(game), capacity: Overhead + 6000);

        Assert.True(built.Discs.Count > 1);
        Assert.All(built.Discs, disc => Assert.Equal(built.Discs.Count, disc.OfDiscs));

        // "disc D1", "disc D2", which is what v1 writes and what anybody who
        // has one of these folders already expects.
        Assert.Equal(Path.Combine(_out, "disc D1"), built.Discs[0].Folder);
        Assert.Equal(Path.Combine(_out, "disc D2"), built.Discs[1].Folder);
    }

    [Fact]
    public async Task Gives_every_disc_of_a_set_its_own_menu_and_proof()
    {
        GameFolder game = AGogDownload("Big Game", 4000, parts: 3);

        BuiltDiscs built = await Build(Project(game), capacity: Overhead + 6000);

        // Any disc of a set can answer for itself. Somebody who finds disc 2 in
        // a drawer gets a disc, not a fragment.
        Assert.All(built.Discs, disc =>
        {
            Assert.True(File.Exists(On(disc, "autorun.inf")));
            Assert.True(File.Exists(On(disc, @"AUTORUN\menu.hta")));
            Assert.True(File.Exists(On(disc, "checksums.sha256")));
        });
    }

    [Fact]
    public async Task Numbers_the_discs_of_a_set_in_their_names()
    {
        GameFolder game = AGogDownload("Big Game", 4000, parts: 3);

        BuiltDiscs built = await Build(Project(game), capacity: Overhead + 6000);

        Assert.Contains("label=HOLLOW KNIGHT 1", File.ReadAllText(On(built.Discs[0], "autorun.inf")), StringComparison.Ordinal);
        Assert.Contains("label=HOLLOW KNIGHT 2", File.ReadAllText(On(built.Discs[1], "autorun.inf")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gives_every_disc_of_a_set_the_same_file_describing_the_whole_set()
    {
        GameFolder game = AGogDownload("Big Game", 4000, parts: 3);

        BuiltDiscs built = await Build(Project(game), capacity: Overhead + 6000);

        Assert.True(built.Discs.Count > 1);

        string[] written =
        [
            .. built.Discs.Select(disc => File.ReadAllText(On(disc, "Disc set.txt")))
        ];

        // Every disc carries one, and every one of them lists every file on
        // every disc, so any single disc can say what the whole set should be.
        Assert.All(written, file => Assert.Contains("DiscWright disc set", file, StringComparison.Ordinal));
        Assert.All(written, file => Assert.Contains("setup_big_game.exe", file, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gives_every_disc_of_a_set_the_same_set_id()
    {
        GameFolder game = AGogDownload("Big Game", 4000, parts: 3);

        BuiltDiscs built = await Build(Project(game), capacity: Overhead + 6000);

        string[] ids =
        [
            .. built.Discs
                .Select(disc => File.ReadAllLines(On(disc, "Disc set.txt"))
                    .First(line => line.Contains("Set id", StringComparison.Ordinal)))
        ];

        // Two discs that disagree about it were built from different sources,
        // which is the one thing a failed join cannot tell you.
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task Writes_no_set_file_for_a_disc_that_is_not_part_of_a_set()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        // Telling somebody that one disc of one is all there is would be noise
        // on every disc that never needed it.
        Assert.False(File.Exists(On(disc, "Disc set.txt")));
    }

    [Fact]
    public async Task Cuts_a_file_too_big_for_any_disc_when_it_is_allowed_to()
    {
        GameFolder game = AGogDownload("Huge", 9000);

        BuiltDiscs built = await Build(Project(game, cut: true), capacity: Overhead + 5000);

        Assert.True(built.Discs.Count > 1);
        Assert.True(File.Exists(On(built.Discs[0], "setup_huge.exe.dwpart01")));
        Assert.True(File.Exists(On(built.Discs[1], "setup_huge.exe.dwpart02")));
    }

    [Fact]
    public async Task Refuses_a_file_too_big_for_any_disc_and_writes_nothing()
    {
        GameFolder game = AGogDownload("Huge", 9000);

        Refusal refused = await Refuse(Project(game), capacity: Overhead + 5000);

        Assert.Contains("setup_huge.exe", refused.Because, StringComparison.Ordinal);

        // Nothing half made, left where somebody might burn it.
        Assert.Empty(Directory.EnumerateFileSystemEntries(_out));
    }

    // ---------------------------------------------------------------- games

    [Fact]
    public async Task Puts_several_games_on_one_disc_when_they_fit()
    {
        BuiltDiscs built = await Build(Project(AGogDownload("One", 500), AGogDownload("Two", 500)));

        BuiltDisc disc = Assert.Single(built.Discs);

        // Numbered folders, because two games are perfectly entitled to both
        // contain a Data folder.
        Assert.True(File.Exists(On(disc, @"01 - One\setup_one.exe")));
        Assert.True(File.Exists(On(disc, @"02 - Two\setup_two.exe")));
    }

    [Fact]
    public async Task Gives_several_games_a_disc_each_when_they_do_not_fit()
    {
        // 6000 bytes of game into 5000 bytes of room, while either game alone
        // fits comfortably. The first version of this asked for 6000 bytes of
        // room, which the two games fit EXACTLY, so one disc was the right
        // answer and the test was the thing that was wrong.
        BuiltDiscs built = await Build(
            Project(AGogDownload("One", 3000), AGogDownload("Two", 3000)),
            capacity: Overhead + 5000);

        // A numbered set is a promise that disc 2 continues disc 1, and that is
        // a lie for two unrelated games. A disc each, each named after its own
        // game, and neither of them numbered.
        Assert.Equal(2, built.Discs.Count);
        Assert.All(built.Discs, disc => Assert.Equal(1, disc.OfDiscs));
        Assert.Equal(["One", "Two"], built.Discs.Select(disc => disc.Label));
        Assert.Equal(Path.Combine(_out, "One", "disc"), built.Discs[0].Folder);
    }

    [Fact]
    public async Task Names_a_compilation_after_the_disc_and_not_after_one_game()
    {
        BuiltDiscs built = await Build(Project(AGogDownload("One", 500), AGogDownload("Two", 500)));

        string menu = File.ReadAllText(On(built.Discs[0], @"AUTORUN\menu.hta"));

        // Somebody looking at a compilation called "One" would reasonably
        // conclude the other game was not on it.
        Assert.Contains("HOLLOW KNIGHT", menu, StringComparison.Ordinal);

        // Each game is an object now, not just a name, because the chooser has
        // to know what to run as well as what to call it.
        Assert.Contains("name: \"One\"", menu, StringComparison.Ordinal);
        Assert.Contains("name: \"Two\"", menu, StringComparison.Ordinal);
        Assert.Contains("01 - One/setup_one.exe", menu, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Puts_an_add_on_beside_the_game_it_belongs_to()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);
        string patch = APatch("patch_hollow_knight_1.5.12620_to_1.5.12700.exe", 200);

        DiscProject project = Project(game) with
        {
            AddOns = [new AddOn(0, patch, AddOn.NameFor(patch), 200)]
        };

        BuiltDisc disc = Assert.Single((await Build(project)).Discs);

        Assert.True(File.Exists(On(disc, @"Add-ons\patch_hollow_knight_1.5.12620_to_1.5.12700.exe")));
    }

    [Fact]
    public async Task Keeps_each_game_s_add_ons_with_that_game()
    {
        GameFolder one = AGogDownload("One", 400);
        GameFolder two = AGogDownload("Two", 400);
        string forOne = APatch("patch_one_1_to_2.exe", 100);
        string forTwo = APatch("setup_two_soundtrack.exe", 100);

        DiscProject project = Project(one, two) with
        {
            AddOns = [new AddOn(0, forOne, AddOn.NameFor(forOne), 100), new AddOn(1, forTwo, AddOn.NameFor(forTwo), 100)]
        };

        BuiltDisc disc = Assert.Single((await Build(project)).Discs);

        // A disc holding four games keeps each one's patches with it, rather
        // than in one pile at the root where nobody can tell which belongs to
        // what.
        Assert.True(File.Exists(On(disc, @"01 - One\Add-ons\patch_one_1_to_2.exe")));
        Assert.True(File.Exists(On(disc, @"02 - Two\Add-ons\setup_two_soundtrack.exe")));
    }

    [Fact]
    public async Task Offers_an_add_on_under_its_game_and_dead_until_the_game_is_there()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);
        string patch = APatch("patch_hollow_knight_1.5.12620_to_1.5.12700.exe", 200);

        DiscProject project = Project(game) with
        {
            AddOns = [new AddOn(0, patch, AddOn.NameFor(patch), 200)]
        };

        BuiltDisc disc = Assert.Single((await Build(project)).Discs);
        string menu = File.ReadAllText(On(disc, @"AUTORUN\menu.hta"));

        // Named after the version it moves to, pointed at where it landed, and
        // the menu knows it cannot be pressed until the game is installed.
        Assert.Contains("name: \"Update 1.5.12700\"", menu, StringComparison.Ordinal);
        Assert.Contains("Add-ons/patch_hollow_knight", menu, StringComparison.Ordinal);
        Assert.Contains("canRunAddOnYet", menu, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_the_whole_shape_of_a_folder_of_game_files()
    {
        GameFolder game = AFolderOfGameFiles("Gothic");

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        Assert.True(File.Exists(On(disc, "Gothic.exe")));
        Assert.True(File.Exists(On(disc, @"Data\anims.vdf")));
    }

    // ---------------------------------------------------------------- options

    [Fact]
    public async Task Puts_a_way_in_at_the_top_of_the_disc_beside_the_files()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        // Where somebody looking at the disc in Explorer will actually see it.
        Assert.True(File.Exists(On(disc, "Start Here.hta")));
    }

    [Fact]
    public async Task Leaves_the_menu_off_when_it_was_not_asked_for()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);

        BuiltDisc disc = Assert.Single((await Build(Project(game) with { Options = new DiscOptions(Menu: false) })).Discs);

        // The files are still there and the disc still has its name. It simply
        // opens in Explorer.
        Assert.False(Directory.Exists(On(disc, "AUTORUN")));
        Assert.True(File.Exists(On(disc, "autorun.inf")));
        Assert.DoesNotContain("shellexecute", File.ReadAllText(On(disc, "autorun.inf")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_the_checksums_off_when_they_were_not_asked_for()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);

        BuiltDisc disc = Assert.Single((await Build(Project(game) with { Options = new DiscOptions(Checksums: false) })).Discs);

        Assert.False(File.Exists(On(disc, "checksums.sha256")));
    }

    [Fact]
    public async Task Puts_the_printable_artwork_on_without_being_asked()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);
        DiscArtwork artwork = new(IconPath: APicture("icon", 256, 256), BackgroundPath: APicture("bg", 1920, 1080),
            CoverPath: APicture("cover", 1200, 1700));

        BuiltDisc disc = Assert.Single((await Build(Project(game) with
        {
            Artwork = artwork,
            Options = new DiscOptions()
        })).Discs);

        // The cover and the disc face are there to be PRINTED, and they are
        // carried on the disc as well, always. There used to be a setting for
        // it and the usual answer was no, because the usual answer to a
        // setting nobody understands is to leave it alone. A disc that has
        // them is one somebody can reprint the sleeve from in ten years.
        Assert.True(Directory.Exists(On(disc, "ARTWORK")));

        // The icon and the background are not that. They are what the disc
        // wears and what the menu is drawn on, so they go on regardless.
        Assert.True(File.Exists(On(disc, "disc.ico")));
        Assert.True(File.Exists(On(disc, @"AUTORUN\background.png")));
    }

    [Fact]
    public async Task Puts_the_artwork_on_when_there_is_any()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);
        DiscArtwork artwork = new(IconPath: APicture("icon", 256, 256), BackgroundPath: APicture("bg", 1920, 1080),
            CoverPath: APicture("cover", 1200, 1700));

        BuiltDisc disc = Assert.Single((await Build(Project(game) with { Artwork = artwork })).Discs);

        Assert.True(File.Exists(On(disc, "disc.ico")));
        Assert.True(File.Exists(On(disc, @"AUTORUN\background.png")));
        Assert.True(File.Exists(On(disc, @"ARTWORK\cover.png")));
        Assert.True(File.Exists(On(disc, @"ARTWORK\READ ME.txt")));
    }

    // ---------------------------------------------------------------- building twice

    [Fact]
    public async Task Clears_out_what_a_previous_build_left_behind()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);

        await Build(Project(game));

        string stale = Path.Combine(_out, "disc", "left over from last time.bin");
        File.WriteAllText(stale, "x");

        BuiltDisc disc = Assert.Single((await Build(Project(game))).Discs);

        // Left there, it would be hashed into this build's checksum list and
        // burned onto the disc, where it is now correct and permanent.
        Assert.False(File.Exists(stale));
        Assert.DoesNotContain("left over", File.ReadAllText(On(disc, "checksums.sha256")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_which_disc_and_which_step_as_it_goes()
    {
        GameFolder game = AGogDownload("Big Game", 4000, parts: 3);
        List<string> said = [];

        await Build(Project(game), capacity: Overhead + 6000, progress: new Watching(said.Add));

        Assert.Contains(said, line => line.StartsWith("disc 1 of ", StringComparison.Ordinal));
        Assert.Contains(said, line => line.StartsWith("disc 2 of ", StringComparison.Ordinal));
        Assert.Contains(said, line => line.Contains("copying the files", StringComparison.Ordinal));
        Assert.Contains(said, line => line.Contains("checking every file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Says_nothing_about_discs_when_there_is_only_one()
    {
        GameFolder game = AGogDownload("Hollow Knight", 500);
        List<string> said = [];

        await Build(Project(game), progress: new Watching(said.Add));

        // Saying "disc 1 of 1" to somebody making one disc is noise, and noise
        // in a progress line is what makes people stop reading it.
        Assert.All(said, line => Assert.DoesNotContain("disc 1 of 1", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_project_with_no_games_at_all()
    {
        Refusal refused = await Refuse(new DiscProject("EMPTY", [], new DiscOptions()));

        Assert.Contains("nothing", refused.Headline, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// What the menu, the autorun and the checksum list take off every disc
    /// before any of the game goes on it.
    /// </summary>
    /// <remarks>
    /// Written out here rather than hidden in a round number, because the first
    /// version of these tests asked for a disc of a megabyte, which is SMALLER
    /// THAN THE OVERHEAD, and every one of them was correctly refused. A test
    /// capacity now says what it is: room for the game, plus what the disc
    /// spends on being a disc.
    /// </remarks>
    private const long Overhead = 2L * 1024 * 1024;

    private static DiscProject Project(params GameFolder[] games) =>
        new("HOLLOW KNIGHT", games, new DiscOptions());

    private static DiscProject Project(GameFolder game, bool cut) =>
        new("HOLLOW KNIGHT", [game], new DiscOptions(CutFiles: cut));

    private async Task<BuiltDiscs> Build(
        DiscProject project,
        long capacity = Overhead + 1_000_000,
        IProgress<BuildProgress>? progress = null)
    {
        Outcome<BuiltDiscs> built = await Builder().Build(
            project, new Medium("TEST", "a test disc", capacity), _out, progress, CancellationToken.None);

        return Assert.IsType<Outcome<BuiltDiscs>.Ok>(built).Value;
    }

    private async Task<Refusal> Refuse(DiscProject project, long capacity = Overhead + 1_000_000)
    {
        Outcome<BuiltDiscs> built = await Builder().Build(
            project, new Medium("TEST", "a test disc", capacity), _out, null, CancellationToken.None);

        return Assert.IsType<Outcome<BuiltDiscs>.No>(built).Refusal;
    }

    private static DiscBuilder Builder() =>
        new(new WindowsFileSystem(), new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher());

    private static string On(BuiltDisc disc, string relative) => Path.Combine(disc.Folder, relative);

    /// <summary>A GOG download: one installer, and its numbered parts.</summary>
    private GameFolder AGogDownload(string name, int bytes, int parts = 0)
    {
        string tidied = name.ToLowerInvariant().Replace(' ', '_');
        string folder = Path.Combine(_root, "src", tidied);
        Directory.CreateDirectory(folder);

        List<ListedFile> listed = [];

        Make(folder, $"setup_{tidied}.exe", bytes);
        listed.Add(new ListedFile($"setup_{tidied}.exe", bytes));

        for (int part = 1; part <= parts; part++)
        {
            Make(folder, $"setup_{tidied}-{part}.bin", bytes);
            listed.Add(new ListedFile($"setup_{tidied}-{part}.bin", bytes));
        }

        return new GameFolder(folder, GameKind.GogInstaller, name, name, listed[0], listed, [], null);
    }

    private GameFolder AFolderOfGameFiles(string name)
    {
        string folder = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(folder);
        Make(folder, $"{name}.exe", 300);
        Make(folder, @"Data\anims.vdf", 200);

        ListedFile[] listed = [new($"{name}.exe", 300), new(@"Data\anims.vdf", 200)];

        return new GameFolder(folder, GameKind.FilesWithLauncher, name, name, null, listed, [listed[0]], null);
    }

    private string APatch(string name, int bytes)
    {
        string path = Path.Combine(_root, "addons", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        byte[] contents = new byte[bytes];
        Random.Shared.NextBytes(contents);
        File.WriteAllBytes(path, contents);

        return path;
    }

    private static void Make(string folder, string relative, int bytes)
    {
        string full = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        byte[] contents = new byte[bytes];
        Random.Shared.NextBytes(contents);
        File.WriteAllBytes(full, contents);
    }

    private string APicture(string name, int width, int height)
    {
        string path = Path.Combine(_root, "src", name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using System.Drawing.Bitmap bitmap = new(width, height);
        using System.Drawing.Graphics drawing = System.Drawing.Graphics.FromImage(bitmap);
        drawing.Clear(System.Drawing.Color.SlateGray);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        return path;
    }

    private sealed class Watching(Action<string> say) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value) => say(value.ToString());
    }
}
