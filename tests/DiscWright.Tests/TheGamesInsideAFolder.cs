using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Pointing at the folder that holds the downloads rather than at a download.
/// </summary>
public class TheGamesInsideAFolder
{
    [Fact]
    public void Finds_nothing_in_a_download_itself()
    {
        // It holds an installer, so it is a download rather than a folder of
        // them, and the recognisers have it already.
        Assert.Empty(GamesInside.Of(AFolderOf(
            "setup_hollow_knight.exe",
            "setup_hollow_knight-1.bin")));
    }

    [Fact]
    public void Finds_nothing_in_a_download_that_also_has_one_in_a_subfolder()
    {
        // A download with a bonus installer tucked inside it is still one
        // download. Holding an installer of its own settles it, whatever else
        // is further down.
        Assert.Empty(GamesInside.Of(AFolderOf(
            "setup_a_game.exe",
            @"extras\setup_a_game_soundtrack.exe")));
    }

    [Fact]
    public void Finds_the_downloads_somebody_keeps_in_one_place()
    {
        IReadOnlyList<string> inside = GamesInside.Of(AFolderOf(
            @"Hollow Knight\setup_hollow_knight.exe",
            @"Hollow Knight\setup_hollow_knight-1.bin",
            @"Gothic\setup_gothic.exe",
            @"Arcanum\setup_arcanum.exe"));

        Assert.Equal(["Arcanum", "Gothic", "Hollow Knight"], inside);
    }

    [Fact]
    public void Finds_them_a_folder_further_down_as_well()
    {
        // Which is how the testing kit files them, and how somebody who tidies
        // their downloads into GAMES will have them.
        IReadOnlyList<string> inside = GamesInside.Of(AFolderOf(
            @"GAMES\GOG1\setup_game_1.exe",
            @"GAMES\GOG2\setup_game_2.exe",
            @"GAMES\GOG3\setup_game_3.exe"));

        Assert.Equal([@"GAMES\GOG1", @"GAMES\GOG2", @"GAMES\GOG3"], inside);
    }

    [Fact]
    public void Does_not_go_down_for_ever()
    {
        // Pointing at a drive should not walk the drive.
        Assert.Empty(GamesInside.Of(AFolderOf(@"a\b\c\d\setup_buried.exe")));
    }

    [Fact]
    public void Counts_a_download_once_however_many_installers_are_in_it()
    {
        IReadOnlyList<string> inside = GamesInside.Of(AFolderOf(
            @"The Witcher\setup_the_witcher.exe",
            @"The Witcher\setup_the_witcher_soundtrack.exe"));

        Assert.Equal(["The Witcher"], inside);
    }

    [Fact]
    public void Treats_a_download_with_an_installer_in_a_subfolder_as_one_download()
    {
        IReadOnlyList<string> inside = GamesInside.Of(AFolderOf(
            @"A Game\setup_a_game.exe",
            @"A Game\extras\setup_a_game_bonus.exe"));

        // Still one game, not a game with a game inside it.
        Assert.Equal(["A Game"], inside);
    }

    [Fact]
    public void Minds_only_things_that_look_like_installers()
    {
        Assert.Empty(GamesInside.Of(AFolderOf(
            @"Some Folder\readme.txt",
            @"Some Folder\game.exe",
            @"Another\setup_notes.txt")));
    }

    private static FolderListing AFolderOf(params string[] paths) =>
        new(@"C:\downloads", [.. paths.Select(path => new ListedFile(path, 1000))]);
}

/// <summary>
/// Whether something installs onto a game rather than being one.
/// </summary>
public class WhetherItLooksLikeAnAddOn
{
    [Theory]
    [InlineData("patch_the_witcher_2_3.3_to_3.4_(21034).exe")]
    [InlineData("setup_grim_fandango_remastered_soundtrack_2.0.0.9_(31045).exe")]
    [InlineData("setup_the_witcher_artbook.exe")]
    [InlineData("setup_a_game_wallpapers.exe")]
    [InlineData("setup_a_game_avatars.exe")]
    public void Says_so_for_the_things_that_install_onto_a_game(string name)
    {
        Assert.True(AddOn.LooksLikeOne(name));
    }

    [Theory]
    [InlineData("setup_hollow_knight_1.5.12620_(64bit)_(89718).exe")]
    [InlineData("setup_gothic_2_gold_2.0.0.9.exe")]
    [InlineData("setup_the_witcher_2_3.3_(21034).exe")]
    public void Says_nothing_about_an_ordinary_game(string name)
    {
        // The one that matters: a patch is "patch_x_to_y", and a game that
        // merely has a version in its name is not one.
        Assert.False(AddOn.LooksLikeOne(name));
    }
}
