using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// What a folder held that the game did not take.
/// </summary>
public class TheFilesLeftBehind
{
    [Fact]
    public void Finds_nothing_when_the_game_took_everything()
    {
        FolderListing folder = AFolderOf("setup_game.exe", "setup_game-1.bin");
        GameFolder game = AGameTaking(folder, "setup_game.exe", "setup_game-1.bin");

        Assert.Empty(WhatWasLeftBehind.In(folder, game));
    }

    [Fact]
    public void Finds_the_patch_and_the_readme_a_download_came_with()
    {
        FolderListing folder = AFolderOf(
            "setup_the_witcher_2_3.3_(21034).exe",
            "setup_the_witcher_2_3.3_(21034)-1.bin",
            "patch_the_witcher_2_3.3_to_3.4_(21034).exe",
            "READ ME.txt");

        GameFolder game = AGameTaking(folder,
            "setup_the_witcher_2_3.3_(21034).exe",
            "setup_the_witcher_2_3.3_(21034)-1.bin");

        IReadOnlyList<LeftBehind> left = WhatWasLeftBehind.In(folder, game);

        // Somebody pointed at a folder with four files in it and got a disc
        // with two, with no sign that two had gone.
        Assert.Equal(2, left.Count);
        Assert.Equal(["patch_the_witcher_2_3.3_to_3.4_(21034).exe", "READ ME.txt"], left.Select(each => each.Name));
    }

    [Theory]
    [InlineData("patch_the_witcher_2_3.3_to_3.4_(21034).exe", LeftBehindKind.LooksLikeAnAddOn)]
    [InlineData("setup_the_witcher_soundtrack.exe", LeftBehindKind.LooksLikeAnAddOn)]
    [InlineData("READ ME.txt", LeftBehindKind.LooksLikeSomethingToKeep)]
    [InlineData("manual.pdf", LeftBehindKind.LooksLikeSomethingToKeep)]
    [InlineData("cover.jpg", LeftBehindKind.LooksLikeSomethingToKeep)]
    public void Guesses_what_each_one_is(string name, LeftBehindKind expected)
    {
        FolderListing folder = AFolderOf("setup_game.exe", name);
        GameFolder game = AGameTaking(folder, "setup_game.exe");

        // A guess, and only a guess: it decides what the window suggests,
        // never what happens. An executable beside a game is nearly always
        // something that installs onto the game.
        Assert.Equal(expected, Assert.Single(WhatWasLeftBehind.In(folder, game)).Kind);
    }

    [Fact]
    public void Names_an_add_on_the_way_an_add_on_is_named()
    {
        FolderListing folder = AFolderOf("setup_game.exe", "patch_hollow_knight_1.5.12620_to_1.5.12700.exe");
        GameFolder game = AGameTaking(folder, "setup_game.exe");

        // Not its file name. Every GOG patch reports the name of the game it
        // patches, so the version it moves to is the only thing that tells two
        // of them apart.
        Assert.Equal("Update 1.5.12700", Assert.Single(WhatWasLeftBehind.In(folder, game)).AsAnAddOn);
    }

    [Fact]
    public void Puts_what_somebody_kept_back_into_the_game()
    {
        FolderListing folder = AFolderOf("setup_game.exe", "READ ME.txt", "manual.pdf");
        GameFolder game = AGameTaking(folder, "setup_game.exe");

        IReadOnlyList<LeftBehind> left = WhatWasLeftBehind.In(folder, game);
        GameFolder with = WhatWasLeftBehind.Taking(game, left);

        // Part of the game's own files rather than anything new, so they land
        // in its folder on the disc, get planned with it, get cut with it and
        // appear in the checksum list like everything else.
        Assert.Equal(["setup_game.exe", "READ ME.txt", "manual.pdf"], with.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void Leaves_the_game_alone_when_nothing_was_kept()
    {
        FolderListing folder = AFolderOf("setup_game.exe", "READ ME.txt");
        GameFolder game = AGameTaking(folder, "setup_game.exe");

        Assert.Equal(game.Files, WhatWasLeftBehind.Taking(game, []).Files);
    }

    [Fact]
    public void Does_not_mind_what_case_the_folder_listed_things_in()
    {
        FolderListing folder = AFolderOf("SETUP_GAME.EXE", "READ ME.txt");
        GameFolder game = AGameTaking(folder, "setup_game.exe");

        // Windows does not tell these apart, so neither does this: the game
        // would otherwise be offered back as a file it left behind.
        Assert.Equal("READ ME.txt", Assert.Single(WhatWasLeftBehind.In(folder, game)).Name);
    }

    // ------------------------------------------------------------------ helpers

    private static FolderListing AFolderOf(params string[] names) =>
        new(@"C:\games\a game", [.. names.Select(name => new ListedFile(name, 100))]);

    private static GameFolder AGameTaking(FolderListing folder, params string[] names)
    {
        List<ListedFile> taken = [.. names.Select(name => new ListedFile(name, 100))];

        return new GameFolder(folder.Path, GameKind.GogInstaller, "a game", "a game", taken[0], taken, [], null);
    }
}
