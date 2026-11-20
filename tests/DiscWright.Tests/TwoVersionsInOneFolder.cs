using DiscWright.Core;
using DiscWright.Core.Recognisers;

namespace DiscWright.Tests;

/// <summary>
/// A folder holding two versions of one game with a set of DLC for each,
/// which is how GOG Galaxy saves a game that has more than one edition.
/// </summary>
/// <remarks>
/// <para>
/// The names and sizes are a real download's, Metro Exodus Gold as it landed
/// on 2026-11-20: 45 files in one folder, the game at 1.0.8.39 and the
/// Enhanced Edition at 3.0.8.39, with Sam's Story and The Two Colonels once
/// for each. Every test of this before it had been written around files
/// made up to be easy.
/// </para>
/// <para>
/// The Enhanced Edition was taken for the game, rightly, and then the other
/// edition and both of its DLC were offered as add-ons to it, ticked.
/// </para>
/// </remarks>
public class TwoVersionsInOneFolder
{
    private const string TheEnhancedOne = "setup_metro_exodus_3.0.8.39_(64bit)_(62413).exe";
    private const string TheFirstOne = "setup_metro_exodus_1.0.8.39_(64bit)_(62346).exe";

    // ------------------------------------------------------ what a name says

    [Theory]
    [InlineData("setup_metro_exodus_3.0.8.39_(64bit)_(62413).exe", "metro_exodus", "3.0.8.39", "62413")]
    [InlineData("setup_metro_exodus_-_sams_story_1.0.8.39_(64bit)_(62346).exe", "metro_exodus_-_sams_story", "1.0.8.39", "62346")]
    [InlineData("setup_hollow_knight_1.5.12620_(64bit)_(89718).exe", "hollow_knight", "1.5.12620", "89718")]
    [InlineData("setup_the_witcher_enhanced_edition_directors_cut_1.5_(cs)_gog_0.2_(77554).exe", "the_witcher_enhanced_edition_directors_cut", "1.5", "77554")]
    [InlineData("SETUP_Fallout_2_1.02_(12345).EXE", "fallout_2", "1.02", "12345")]
    [InlineData("setup_alan_wake_v1.07.exe", "alan_wake", "v1.07", null)]
    [InlineData("setup_a_game.exe", "a_game", null, null)]
    [InlineData("setup_quiet_orchard_1.4_(60000).exe", "quiet_orchard", "1.4", "60000")]
    public void An_installers_name_says_what_it_is_which_version_and_which_build(
        string file, string product, string? version, string? build) =>
        Assert.Equal(new AGogInstallerName(product, version, build), AGogInstallerName.Of(file));

    [Theory]
    [InlineData("patch_hollow_knight_1.5.12459_to_1.5.12618_(89712).exe")]
    [InlineData("setup_metro_exodus_3.0.8.39_(64bit)_(62413)-1.bin")]
    [InlineData("setup_.exe")]
    [InlineData("readme.txt")]
    [InlineData("")]
    public void What_is_not_named_as_GOG_names_an_installer_says_nothing(string file) =>
        Assert.Null(AGogInstallerName.Of(file));

    [Fact]
    public void A_DLC_is_built_on_its_game_and_a_game_is_built_on_nothing()
    {
        AGogInstallerName game = AGogInstallerName.Of(TheEnhancedOne)!;
        AGogInstallerName dlc = AGogInstallerName.Of("setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe")!;

        Assert.True(dlc.IsBuiltOn(game));
        Assert.False(game.IsBuiltOn(dlc));
        Assert.False(game.IsBuiltOn(game));

        // A name that only begins with the same letters is another game.
        Assert.False(AGogInstallerName.Of("setup_metro_exodusx_1.0_(1).exe")!.IsBuiltOn(game));
    }

    [Fact]
    public void Two_builds_of_one_product_are_two_versions_and_nothing_else_is()
    {
        AGogInstallerName enhanced = AGogInstallerName.Of(TheEnhancedOne)!;
        AGogInstallerName first = AGogInstallerName.Of(TheFirstOne)!;

        Assert.True(first.IsAnotherBuildOf(enhanced));
        Assert.True(enhanced.IsAnotherBuildOf(first));
        Assert.False(enhanced.IsAnotherBuildOf(enhanced));
        Assert.False(AGogInstallerName.Of("setup_metro_exodus_-_sams_story_1.0.8.39_(64bit)_(62346).exe")!.IsAnotherBuildOf(enhanced));

        // With no build to go by there is nothing to tell them apart with.
        Assert.False(AGogInstallerName.Of("setup_metro_exodus_1.0.exe")!.IsAnotherBuildOf(enhanced));
    }

    // -------------------------------------------------------- which is the game

    [Fact]
    public void The_real_folder_gives_the_enhanced_edition_with_all_of_its_parts()
    {
        GameFolder game = Recognised(TheRealFolder());

        Assert.Equal(TheEnhancedOne, game.Installer?.Name);
        Assert.Equal(19, game.Files.Count);
        Assert.All(game.Files, file => Assert.Contains("_(62413)", file.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void A_DLC_bigger_than_its_game_is_still_not_the_game()
    {
        // The limit left open when the heaviest installer was made the
        // game: weight alone cannot tell a big DLC from a small game. The
        // name can, whenever GOG named the DLC after the game.
        FolderListing folder = new(@"C:\games\Small Game",
        [
            new("setup_small_game_1.0_(100).exe", 200_000_000),
            new("setup_small_game_-_the_big_expansion_1.0_(100).exe", 1_000_000),
            new("setup_small_game_-_the_big_expansion_1.0_(100)-1.bin", 4_000_000_000),
            new("setup_small_game_-_the_big_expansion_1.0_(100)-2.bin", 4_000_000_000),
        ]);

        GameFolder game = Recognised(folder);

        Assert.Equal("setup_small_game_1.0_(100).exe", game.Installer?.Name);
        Assert.Single(game.Files);

        LeftBehind expansion = Assert.Single(WhatWasLeftBehind.In(folder, game));

        Assert.Equal("setup_small_game_-_the_big_expansion_1.0_(100).exe", expansion.File.Name);
        Assert.Equal(2, expansion.Parts?.Count);
        Assert.Null(expansion.ForAnotherVersion);
        Assert.True(expansion.WantedAgainst(game.Installer!.Name));
    }

    [Fact]
    public void Two_games_with_nothing_in_common_are_still_weighed_as_they_were()
    {
        FolderListing folder = new(@"C:\games\Two",
        [
            new("setup_harbour_tales_1.0_(80000).exe", 30_000_000),
            new("setup_quiet_orchard_1.4_(60000).exe", 24_000_000),
        ]);

        Assert.Equal("setup_harbour_tales_1.0_(80000).exe", Recognised(folder).Installer?.Name);
    }

    // ------------------------------------------------ what goes with which game

    [Fact]
    public void In_the_real_folder_only_what_was_built_for_the_game_on_the_disc_starts_ticked()
    {
        FolderListing folder = TheRealFolder();
        GameFolder game = Recognised(folder);

        IReadOnlyList<LeftBehind> left = WhatWasLeftBehind.In(folder, game);

        Assert.Equal(
            [
                ("setup_metro_exodus_-_sams_story_1.0.8.39_(64bit)_(62346).exe", 2, false),
                ("setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe", 2, true),
                ("setup_metro_exodus_-_the_two_colonels_1.0.8.39_(64bit)_(62346).exe", 0, false),
                ("setup_metro_exodus_-_the_two_colonels_3.0.8.39_(64bit)_(62413).exe", 0, true),
                (TheFirstOne, 17, false),
            ],
            left.Select(each => (each.File.Name, each.Parts?.Count ?? 0, each.WantedAgainst(game.Installer!.Name))));

        // And nothing is lost from the question: every file in the folder
        // is the game's, one of these rows, or a part of one.
        Assert.Equal(45, game.Files.Count + left.Sum(each => each.WithItsParts.Count));
    }

    [Fact]
    public void What_is_left_off_says_why_in_words_that_name_the_other_version()
    {
        FolderListing folder = TheRealFolder();
        GameFolder game = Recognised(folder);
        IReadOnlyList<LeftBehind> left = WhatWasLeftBehind.In(folder, game);

        string theOtherGame = left.Single(each => each.File.Name == TheFirstOne).ExplainedAgainst(game.Installer!.Name);
        string itsDlc = left.Single(each => each.File.Name.Contains("sams_story_1.0.8.39", StringComparison.Ordinal))
            .ExplainedAgainst(game.Installer.Name);
        string ours = left.Single(each => each.File.Name.Contains("sams_story_3.0.8.39", StringComparison.Ordinal))
            .ExplainedAgainst(game.Installer.Name);

        Assert.Equal(
            "Another version of the game itself, 1.0.8.39. A disc carries one version, so it starts left off."
                + " Its 17 parts go with it.",
            theOtherGame);
        Assert.Equal(
            "Goes with another version of this game, 1.0.8.39, whose installer is in this folder too, "
                + "so it starts left off. Its 2 parts go with it.",
            itsDlc);
        Assert.StartsWith("Looks like something that installs onto the game", ours, StringComparison.Ordinal);
        Assert.EndsWith("Its 2 parts go with it.", ours, StringComparison.Ordinal);
    }

    [Fact]
    public void A_DLC_of_another_build_is_ordinary_when_the_folder_holds_one_game()
    {
        // GOG does not rebuild every DLC each time the game is patched, so
        // a build that differs says nothing by itself. What gives it away is
        // a second installer of the game itself.
        FolderListing folder = new(@"C:\games\One",
        [
            new("setup_a_game_2.0_(500).exe", 9_000_000),
            new("setup_a_game_-_an_expansion_1.3_(321).exe", 1_000_000),
        ]);
        GameFolder game = Recognised(folder);

        LeftBehind expansion = Assert.Single(WhatWasLeftBehind.In(folder, game));

        Assert.Null(expansion.ForAnotherVersion);
        Assert.True(expansion.WantedAgainst(game.Installer!.Name));
    }

    [Fact]
    public void Something_unrelated_in_the_folder_is_never_called_another_version()
    {
        FolderListing folder = new(@"C:\games\Mixed",
        [
            new("setup_a_game_2.0_(500).exe", 9_000_000),
            new("setup_a_game_1.0_(400).exe", 8_000_000),
            new("setup_something_else_1.0_(400).exe", 1_000_000),
            new("manual.pdf", 1_000),
        ]);
        GameFolder game = Recognised(folder);
        IReadOnlyList<LeftBehind> left = WhatWasLeftBehind.In(folder, game);

        Assert.Equal("1.0", left.Single(each => each.File.Name == "setup_a_game_1.0_(400).exe").ForAnotherVersion);
        Assert.Null(left.Single(each => each.File.Name == "setup_something_else_1.0_(400).exe").ForAnotherVersion);
        Assert.Null(left.Single(each => each.File.Name == "manual.pdf").ForAnotherVersion);
    }

    [Fact]
    public void A_folder_with_one_version_is_asked_about_exactly_as_before()
    {
        FolderListing folder = new(@"C:\games\Plain",
        [
            new("setup_a_game_2.0_(500).exe", 1_000_000),
            new("setup_a_game_2.0_(500)-1.bin", 4_000_000_000),
            new("setup_a_game_-_soundtrack_2.0_(500).exe", 90_000_000),
            new("patch_a_game_1.9_to_2.0_(499).exe", 5_000_000),
        ]);
        GameFolder game = Recognised(folder);

        Assert.All(WhatWasLeftBehind.In(folder, game), each => Assert.Null(each.ForAnotherVersion));
    }

    // ---------------------------------------------------------------- helpers

    private static GameFolder Recognised(FolderListing folder) =>
        Assert.IsType<Outcome<GameFolder>.Ok>(new GogDownloadRecogniser(new NoFacts()).Recognise(folder)).Value;

    /// <summary>
    /// The 45 files of the real download, by name and by size in bytes,
    /// in the order the folder lists them.
    /// </summary>
    private static FolderListing TheRealFolder()
    {
        // Two bytes under 4 GiB, which is where GOG cut every full part
        // of this download.
        const long Whole = 4_294_967_294;

        List<ListedFile> files =
        [
            new("setup_metro_exodus_-_sams_story_1.0.8.39_(64bit)_(62346).exe", 864_096),
            new("setup_metro_exodus_-_sams_story_1.0.8.39_(64bit)_(62346)-1.bin", 4_294_113_278),
            new("setup_metro_exodus_-_sams_story_1.0.8.39_(64bit)_(62346)-2.bin", 2_430_804_168),
            new("setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe", 851_568),
            new("setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413)-1.bin", 4_294_125_566),
            new("setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413)-2.bin", 2_342_969_651),
            new("setup_metro_exodus_-_the_two_colonels_1.0.8.39_(64bit)_(62346).exe", 1_244_369_488),
            new("setup_metro_exodus_-_the_two_colonels_3.0.8.39_(64bit)_(62413).exe", 989_653_728),
            new(TheFirstOne, 1_708_896),
        ];

        files.Add(new("setup_metro_exodus_1.0.8.39_(64bit)_(62346)-1.bin", 4_293_268_990));
        files.AddRange(Enumerable.Range(2, 15).Select(part =>
            new ListedFile($"setup_metro_exodus_1.0.8.39_(64bit)_(62346)-{part}.bin", Whole)));
        files.Add(new("setup_metro_exodus_1.0.8.39_(64bit)_(62346)-17.bin", 95_259_079));

        files.Add(new(TheEnhancedOne, 1_723_472));
        files.Add(new("setup_metro_exodus_3.0.8.39_(64bit)_(62413)-1.bin", 4_293_254_654));
        files.AddRange(Enumerable.Range(2, 16).Select(part =>
            new ListedFile($"setup_metro_exodus_3.0.8.39_(64bit)_(62413)-{part}.bin", Whole)));
        files.Add(new("setup_metro_exodus_3.0.8.39_(64bit)_(62413)-18.bin", 691_988_506));

        Assert.Equal(45, files.Count);

        // 158.1 GB, which Explorer shows as 147 GB.
        Assert.Equal(158_118_941_254, files.Sum(file => file.Bytes));

        return new FolderListing(
            @"C:\Program Files (x86)\GOG Galaxy\Games\Offline Installers\metro_exodus", files);
    }

    private sealed class NoFacts : IFileFacts
    {
        public string? ProductNameOf(string path) => null;
    }
}
