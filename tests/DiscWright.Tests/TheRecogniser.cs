using DiscWright.Core;
using DiscWright.Core.Recognisers;

namespace DiscWright.Tests;

/// <summary>
/// The cases are the source folders in the manual testing kit, written out as
/// listings rather than made as files. A recogniser only ever looks at names
/// and sizes, so a list says everything a folder on disk would, runs in
/// microseconds, and works the same on a machine that has never seen the kit.
/// </summary>
public class TheRecogniser
{
    [Fact]
    public void Knows_a_GOG_download_by_its_installer()
    {
        // Source folder A.
        GameFolder game = Read(Folder(@"C:\kit\A - GOG download (Hollow Knight)",
            ("setup_hollow_knight_1.5.12620_(64bit)_(89718).exe", 819_200),
            ("setup_hollow_knight_1.5.12620_(64bit)_(89718)-1.bin", 1_228_800),
            ("READ ME.txt", 171)));

        Assert.Equal(GameKind.GogInstaller, game.Kind);
        Assert.Equal("setup_hollow_knight_1.5.12620_(64bit)_(89718).exe", game.Installer?.Name);
    }

    [Fact]
    public void Puts_only_the_installer_and_its_own_parts_on_the_disc()
    {
        GameFolder game = Read(Folder(@"C:\kit\A",
            ("setup_hollow_knight_1.0.exe", 800),
            ("setup_hollow_knight_1.0-1.bin", 1_000),
            ("READ ME.txt", 100),
            ("cover.png", 200)));

        // The loose files in a GOG folder are not part of the download, and a
        // GOG download keeps none of its folder's shape.
        Assert.Equal(["setup_hollow_knight_1.0.exe", "setup_hollow_knight_1.0-1.bin"],
            game.Files.Select(file => file.Name));
    }

    [Fact]
    public void Does_not_take_the_parts_of_the_installer_next_to_it()
    {
        // A base game and its DLC in one folder, which is how GOG leaves them.
        // Matching every .bin swept the DLC's parts onto the disc as though they
        // belonged to the game, and the installer then failed.
        GameFolder game = Read(Folder(@"C:\kit\I",
            ("setup_grim_fandango_2.0.exe", 700_000),
            ("setup_grim_fandango_2.0-1.bin", 1_400_000),
            ("setup_grim_fandango_soundtrack_2.0.exe", 420_000),
            ("setup_grim_fandango_soundtrack_2.0-1.bin", 100_000)));

        Assert.DoesNotContain(game.Files, file => file.Name.Contains("soundtrack", StringComparison.Ordinal));
    }

    [Fact]
    public void Picks_the_largest_installer_and_says_so()
    {
        GameFolder game = Read(Folder(@"C:\kit\I",
            ("setup_grim_fandango_2.0.exe", 700_000),
            ("setup_grim_fandango_soundtrack_2.0.exe", 420_000)));

        Assert.Equal("setup_grim_fandango_2.0.exe", game.Installer?.Name);

        // Said out loud, so a wrong guess is visible before a disc is burned
        // rather than after.
        Assert.Contains("2 installers", game.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Warns_that_a_download_stopped_early()
    {
        GameFolder game = Read(Folder(@"C:\kit\A",
            ("setup_a_game_1.0.exe", 800),
            ("setup_a_game_1.0-1.bin", 1_000),
            ("setup_a_game_1.0-3.bin", 1_000)));

        // Part 2 never arrived. Built, this makes a clean looking ISO that only
        // fails when somebody runs the installer off the burned disc, which is
        // the worst possible place to find out.
        Assert.Contains("INCOMPLETE", game.Warning, StringComparison.Ordinal);
        Assert.Contains("-2", game.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_nothing_about_a_game_that_simply_has_no_parts()
    {
        GameFolder game = Read(Folder(@"C:\kit\A", ("setup_a_small_game_1.0.exe", 900)));

        // Plenty of GOG downloads are one file. Only a gap in a sequence is wrong.
        Assert.Null(game.Warning);
    }

    [Fact]
    public void Prefers_the_name_the_installer_reports_about_itself()
    {
        GameFolder game = Read(
            Folder(@"C:\kit\A", ("setup_the_witcher_enhanced_edition_1.5_(cs).exe", 900)),
            reports: "The Witcher: Enhanced Edition");

        Assert.Equal("The Witcher: Enhanced Edition", game.Name);
    }

    [Fact]
    public void Trims_the_padding_Inno_leaves_on_a_reported_name()
    {
        GameFolder game = Read(
            Folder(@"C:\kit\A", ("setup_a_game_1.0.exe", 900)),
            reports: "Hollow Knight                    ");

        // Untrimmed it leaks into folder names: "Hollow Knight            Disc".
        Assert.Equal("Hollow Knight", game.Name);
    }

    [Fact]
    public void Falls_back_to_tidying_up_the_file_name()
    {
        GameFolder game = Read(
            Folder(@"C:\kit\A", ("setup_hollow_knight_1.5.12620_(64bit)_(89718).exe", 900)),
            reports: null);

        // setup_ off the front, underscores to spaces, and everything from the
        // first number onwards taken away with the version and the build.
        Assert.Equal("hollow knight", game.Name);
    }

    [Fact]
    public void Refuses_a_disc_we_built_rather_than_accepting_it_as_game_files()
    {
        // Source folder picked by mistake: the output folder, not the download.
        Refusal refusal = Refused(Folder(@"C:\kit\out",
            (@"disc\setup_hollow_knight_1.0.exe", 800),
            (@"disc\AUTORUN\menu.hta", 40_000),
            ("Hollow Knight.iso", 3_801_088)));

        // The whole reason a recogniser can refuse rather than only pass. Left
        // to fall through, the next one takes it as an ordinary folder of game
        // files and the disc gets a disc inside it.
        Assert.Contains("disc", refusal.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RemedyKind.PickFolder, refusal.Remedy?.Kind);
    }

    [Fact]
    public void Knows_game_files_with_something_to_run()
    {
        // Source folder B.
        GameFolder game = Read(Folder(@"C:\kit\B - game files with an exe (Gothic)",
            ("Gothic.exe", 2_100_000),
            (@"Data\anims.vdf", 900_000),
            (@"Data\meshes.vdf", 600_000)));

        Assert.Equal(GameKind.FilesWithLauncher, game.Kind);
        Assert.Equal("Gothic.exe", game.LauncherCandidates[0].Name);
    }

    [Fact]
    public void Keeps_the_whole_shape_of_a_folder_of_game_files()
    {
        GameFolder game = Read(Folder(@"C:\kit\B",
            ("Gothic.exe", 2_100_000),
            (@"Data\anims.vdf", 900_000)));

        // Unlike a GOG download, here the shape IS the game, subfolders and all.
        Assert.Equal(2, game.Files.Count);
        Assert.Contains(game.Files, file => file.RelativePath == @"Data\anims.vdf");
    }

    [Fact]
    public void Leaves_out_the_executables_that_are_never_the_game()
    {
        // Source folder Q, which exists to be this test.
        GameFolder game = Read(Folder(@"C:\kit\Q - which exe starts the game",
            ("tyrian2000.exe", 2_100_000),
            ("setup.exe", 184_000),
            ("unins000.exe", 700_000),
            (@"DOSBOX\dosbox.exe", 3_800_000),
            (@"__redist\vcredist_x64.exe", 14_000_000),
            (@"__redist\DirectX\DXSETUP.exe", 500_000)));

        string[] offered = [.. game.LauncherCandidates.Select(file => file.Name)];

        Assert.DoesNotContain("unins000.exe", offered);
        Assert.DoesNotContain("vcredist_x64.exe", offered);
        Assert.DoesNotContain("DXSETUP.exe", offered);

        // The redistributable is the largest executable in that folder, so
        // without the exclusions it would be offered first.
        Assert.Equal("dosbox.exe", offered[0]);
    }

    [Fact]
    public void Never_picks_the_launcher_itself()
    {
        GameFolder game = Read(Folder(@"C:\kit\Q",
            ("tyrian2000.exe", 2_100_000),
            (@"DOSBOX\dosbox.exe", 3_800_000)));

        // Which executable starts a game is a question for the person. Guessed
        // wrong, the disc's only button does nothing, and nobody finds out until
        // the disc is burned.
        Assert.Equal(2, game.LauncherCandidates.Count);
    }

    [Fact]
    public void Accepts_a_folder_with_nothing_to_run_at_all()
    {
        // Source folder C. Not a failure: the menu offers the folder instead,
        // and the disc is still a disc.
        GameFolder game = Read(Folder(@"C:\kit\C - game files, NO installer (Arcanum)",
            ("arcanum.dat", 1_000_000),
            ("modules.dat", 40_000)));

        Assert.Equal(GameKind.FilesOnly, game.Kind);
        Assert.Empty(game.LauncherCandidates);
    }

    [Fact]
    public void Names_a_folder_of_game_files_after_the_folder()
    {
        GameFolder game = Read(Folder(@"C:\kit\Gothic II", ("Gothic2.exe", 900)));

        Assert.Equal("Gothic II", game.Name);
    }

    [Fact]
    public void Refuses_an_empty_folder()
    {
        Refusal refusal = Refused(Folder(@"C:\kit\nothing"));

        Assert.Contains("empty", refusal.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Asks_the_recognisers_in_the_order_it_was_given()
    {
        // A GOG download is also, strictly, a folder with files in it. If the
        // broader recogniser came first it would claim everything and no disc
        // would ever be built from an installer again.
        GameFolder game = Read(Folder(@"C:\kit\A",
            ("setup_a_game_1.0.exe", 900),
            ("readme.txt", 10)));

        Assert.Equal(GameKind.GogInstaller, game.Kind);
    }

    // ------------------------------------------------------------------ helpers

    private static FolderListing Folder(string path, params (string Path, long Bytes)[] files) =>
        new(path, [.. files.Select(file => new ListedFile(file.Path, file.Bytes))]);

    private static GameFolder Read(FolderListing folder, string? reports = null) =>
        Assert.IsType<Outcome<GameFolder>.Ok>(
            GameFolderReader.InTheUsualOrder(new FactsThatReport(reports)).Read(folder)).Value;

    private static Refusal Refused(FolderListing folder) =>
        Assert.IsType<Outcome<GameFolder>.No>(
            GameFolderReader.InTheUsualOrder(new FactsThatReport(null)).Read(folder)).Refusal;

    /// <summary>
    /// Stands in for reading a product name out of an executable, which is the
    /// one fact about a folder that a listing cannot carry.
    /// </summary>
    private sealed class FactsThatReport(string? name) : IFileFacts
    {
        public string? ProductNameOf(string path) => name;
    }
}
