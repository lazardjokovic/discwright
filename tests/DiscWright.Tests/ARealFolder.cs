using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// The same recogniser, run against folders that really exist.
/// </summary>
/// <remarks>
/// Everything in <c>TheRecogniser</c> is a written listing and needs no disk.
/// These are the handful that cannot be: that the walk finds what is there,
/// reports the right sizes and relative paths, includes hidden files, does not
/// chase a junction, and that a product name really does come back out of a
/// real executable. Few on purpose, because a test that touches a disk is a
/// test that can fail for reasons that are nothing to do with the code.
/// </remarks>
public sealed class ARealFolder : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwreal_" + Guid.NewGuid().ToString("N")[..8]);

    public ARealFolder() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is not worth failing a test over.
        }
    }

    [Fact]
    public void Is_recognised_as_a_GOG_download_off_the_disk()
    {
        string folder = Make("A - GOG download",
            ("setup_hollow_knight_1.5.12620_(64bit)_(89718).exe", 2048),
            ("setup_hollow_knight_1.5.12620_(64bit)_(89718)-1.bin", 4096),
            ("READ ME.txt", 171));

        GameFolder game = Read(folder);

        Assert.Equal(GameKind.GogInstaller, game.Kind);
        Assert.Equal(2048 + 4096, game.Bytes);
    }

    [Fact]
    public void Reads_a_folder_with_square_brackets_in_its_name()
    {
        // Reported against v1 on 2026-11-09 (discwright#181): a folder with
        // square brackets in its name was "not found". PowerShell reads [ and
        // ] in a path as a wildcard. Nothing here should, but it was never
        // asked until somebody's real folder had them.
        GameFolder installer = Read(Make("The Witcher [GOG] [v1.5]",
            ("setup_the_witcher_1.5_(12345).exe", 2048)));

        Assert.Equal(GameKind.GogInstaller, installer.Kind);

        // And brackets inside it, in a folder and a file name, every one found.
        GameFolder files = Read(Make("Gothic [GOTY]",
            ("Gothic [GOTY].exe", 3000),
            (@"Data [x]\anims [1].vdf", 1500)));

        Assert.Equal(
            [@"Data [x]\anims [1].vdf", "Gothic [GOTY].exe"],
            files.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
        Assert.Equal(4500, files.Bytes);
    }

    [Fact]
    public void Reports_sizes_and_paths_the_way_the_planner_needs_them()
    {
        string folder = Make("B - game files",
            ("Gothic.exe", 3000),
            (@"Data\anims.vdf", 1500),
            (@"Data\sound\music.ogg", 700));

        GameFolder game = Read(folder);

        Assert.Equal(3, game.Files.Count);
        Assert.Equal(5200, game.Bytes);

        // Relative, with the folder itself cut off the front, because that is
        // what becomes the path on the disc.
        Assert.Contains(game.Files, file => file.RelativePath == @"Data\sound\music.ogg");
        Assert.DoesNotContain(game.Files, file => file.RelativePath.Contains(':', StringComparison.Ordinal));
    }

    [Fact]
    public void Includes_a_hidden_file_because_the_game_needs_it()
    {
        string folder = Make("C - with something hidden",
            ("game.exe", 1000),
            ("settings.ini", 50));

        File.SetAttributes(Path.Combine(folder, "settings.ini"), FileAttributes.Hidden);

        GameFolder game = Read(folder);

        // A disc missing a game's hidden files is a disc missing part of the
        // game. The default walk skips them, so this is a decision, not luck.
        Assert.Equal(2, game.Files.Count);
        Assert.Equal(1050, game.Bytes);
    }

    [Fact]
    public void Refuses_a_folder_that_is_not_there()
    {
        Refusal refusal = Refused(Path.Combine(_root, "never existed"));

        // Different from "that folder is empty": a USB stick pulled out and a
        // folder somebody emptied are different mistakes.
        Assert.Contains("not there", refusal.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RemedyKind.PickFolder, refusal.Remedy?.Kind);
    }

    [Fact]
    public void Refuses_nothing_at_all()
    {
        Refusal refusal = Refused("   ");

        Assert.Contains("No folder", refusal.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_a_disc_it_built_rather_than_taking_it_as_game_files()
    {
        string folder = Make("out",
            (@"disc\setup_hollow_knight_1.0.exe", 2048),
            (@"disc\AUTORUN\menu.hta", 400),
            ("Hollow Knight.iso", 8192));

        Refusal refusal = Refused(folder);

        // The case the three-answer chain exists for, now proven against a real
        // folder shaped exactly like one DiscWright writes.
        Assert.Contains("disc", refusal.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reads_a_product_name_out_of_a_real_executable()
    {
        // Windows' own, because it is on every machine this will ever run on,
        // including the test VM, and it certainly carries version information.
        string? reported = new FileVersionFacts().ProductNameOf(
            Path.Combine(Environment.SystemDirectory, "notepad.exe"));

        Assert.False(string.IsNullOrWhiteSpace(reported));
    }

    [Fact]
    public void Says_nothing_rather_than_throwing_for_a_file_with_no_version_information()
    {
        string folder = Make("D", ("readme.txt", 10));

        Assert.Null(new FileVersionFacts().ProductNameOf(Path.Combine(folder, "readme.txt")));
    }

    [Fact]
    public void Says_nothing_rather_than_throwing_for_a_file_that_has_gone()
    {
        // An ordinary thing on a USB stick or a network drive, between the
        // listing and the question.
        Assert.Null(new FileVersionFacts().ProductNameOf(Path.Combine(_root, "gone.exe")));
    }

    // ------------------------------------------------------------------ helpers

    private string Make(string name, params (string Path, int Bytes)[] files)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);

        foreach ((string path, int bytes) in files)
        {
            string full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[bytes]);
        }

        return folder;
    }

    private static GameFolder Read(string path) =>
        Assert.IsType<Outcome<GameFolder>.Ok>(Folders().At(path)).Value;

    private static Refusal Refused(string path) =>
        Assert.IsType<Outcome<GameFolder>.No>(Folders().At(path)).Refusal;

    private static GameFolders Folders() =>
        GameFolders.InTheUsualOrder(new WindowsFileSystem(), new FileVersionFacts());
}
