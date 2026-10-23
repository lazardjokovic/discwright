using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The tree of what goes on the disc, and taking things off it.
/// </summary>
/// <remarks>
/// All of it without a window. The tree is a view model and the ticks are its
/// own state, so what a tick does to the disc can be asked directly.
/// </remarks>
public class TheTreeOfWhatGoesOnIt
{
    [Fact]
    public void Shows_the_folders_a_game_already_describes_rather_than_a_flat_list()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        // Shown flat, an installed game is three hundred rows of nothing.
        // In the order they were found, which is the order the folder has.
        Assert.Equal(["setup.exe", "data", "readme.txt"], tree.Inside.Select(each => each.Name));

        AThingOnTheDisc data = tree.Inside.Single(each => each.Name == "data");
        Assert.Equal(["textures"], data.Inside.Select(each => each.Name));
        Assert.Equal(["skin.pak"], data.Inside.Single().Inside.Select(each => each.Name));
    }

    // ------------------------------------------------- what hovering says

    [Fact]
    public void A_name_too_long_for_the_row_can_be_read_on_hover()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        // The row has a fixed width and trims what will not fit, so the only
        // place a long name can be read in full is here.
        AThingOnTheDisc installer = tree.Inside.Single(each => each.Name == "setup.exe");

        // One line, because the path says nothing the name has not said.
        Assert.Equal("setup.exe", installer.InFull);
    }

    [Fact]
    public void Hovering_a_file_in_a_folder_says_where_in_the_game_it_is()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        AThingOnTheDisc skin = tree.Inside.Single(each => each.Name == "data")
            .Inside.Single().Inside.Single();

        Assert.Equal("skin.pak" + (char)10 + @"data\textures\skin.pak", skin.InFull);
    }

    [Fact]
    public void Hovering_an_add_on_says_what_the_file_is_really_called()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), [APatch()]);

        AThingOnTheDisc patch = tree.Inside
            .Single(each => each.Name == TheTreeOfWhatGoesOn.AddOnsAreUnder)
            .Inside.Single();

        // The case that needs this most. The row shows a tidy version of
        // what the patch calls itself, and the file on disk is called
        // something else entirely, which is the name somebody looking for
        // it in the folder will see.
        Assert.Equal(
            "Update 1.5.12618" + (char)10 + @"C:\game\patch_a_game_1.5.12459_to_1.5.12618.exe",
            patch.InFull);
    }

    [Fact]
    public void Hovering_a_game_says_which_folder_it_came_from()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        Assert.Equal("A Game" + (char)10 + @"C:\game", tree.InFull);
    }

    [Fact]
    public void A_folder_knows_where_it_is_rather_than_where_the_game_is()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        AThingOnTheDisc data = tree.Inside.Single(each => each.Name == "data");
        AThingOnTheDisc textures = data.Inside.Single();

        // Every folder used to be handed the whole game folder, so hovering
        // "data" and hovering "textures" inside it said the same thing, and
        // neither of them said where it was.
        Assert.Equal("data", data.ItsPath);
        Assert.Equal(@"data\textures", textures.ItsPath);

        // And a folder whose path is its own name has nothing more to add.
        Assert.Equal("data", data.InFull);
    }

    /// <summary>A patch found beside the game, as the window takes one on.</summary>
    private static AddOn APatch() => new(
        BelongsTo: 0,
        Path: @"C:\game\patch_a_game_1.5.12459_to_1.5.12618.exe",
        Name: "Update 1.5.12618",
        Bytes: 500);

    [Fact]
    public void A_folder_is_as_big_as_everything_under_it()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        // Worked out rather than added up as rows arrive: a folder is put
        // under its parent before anything goes in it, so a running total
        // taken at that moment is a total of nothing.
        Assert.Equal(300, tree.Inside.Single(each => each.Name == "data").Bytes);
        Assert.Equal(1300, tree.Bytes);
    }

    [Fact]
    public void Says_which_row_is_the_installer_and_which_is_an_add_on()
    {
        GameFolder game = AGameWithFolders();
        AddOn[] addOns = [new(0, @"C:\game\patch.exe", "Patch", 70)];

        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(game, addOns);

        Assert.Equal(
            TheTreeOfWhatGoesOn.TheInstaller,
            tree.Inside.Single(each => each.Name == "setup.exe").What);

        AThingOnTheDisc under = tree.Inside.Single(each => each.Name == TheTreeOfWhatGoesOn.AddOnsAreUnder);
        Assert.Equal(TheTreeOfWhatGoesOn.AnAddOn, under.Inside.Single(each => each.Name == "Patch").What);
    }

    [Fact]
    public void Taking_a_file_off_takes_it_out_of_what_will_be_written()
    {
        GameFolder game = AGameWithFolders();
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(game, []);

        tree.Inside.Single(each => each.Name == "readme.txt").IsOn = false;

        GameFolder chosen = tree.AsChosen(game);

        // Everything downstream reads the file list: the size, the refusals,
        // how many discs it takes, and what the builder writes. So a game
        // carrying fewer files is the whole of leaving a file off.
        Assert.DoesNotContain("readme.txt", chosen.Files.Select(file => file.RelativePath));
        Assert.Equal(game.Bytes - 100, chosen.Bytes);
    }

    [Fact]
    public void Taking_a_folder_off_takes_everything_under_it()
    {
        GameFolder game = AGameWithFolders();
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(game, []);

        tree.Inside.Single(each => each.Name == "data").IsOn = false;

        Assert.DoesNotContain(@"data\textures\skin.pak", tree.AsChosen(game).Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void A_folder_with_some_of_it_left_out_is_neither_on_nor_off()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(ADeeperGame(), []);
        AThingOnTheDisc data = tree.Inside.Single(each => each.Name == "data");

        Assert.True(data.IsOn);

        data.Inside.First().IsOn = false;

        // Saying it was either would be a lie about what will be written.
        Assert.Null(data.IsOn);
        Assert.Null(tree.IsOn);

        data.Inside.Last().IsOn = false;
        Assert.False(data.IsOn);
    }

    [Fact]
    public void Clicking_a_half_full_folder_turns_all_of_it_on()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(ADeeperGame(), []);
        AThingOnTheDisc data = tree.Inside.Single(each => each.Name == "data");

        data.Inside.First().IsOn = false;
        Assert.Null(data.IsOn);

        // A box with no answer is what a mixed folder shows, never something
        // anybody sets, so the click has to mean one of the two.
        data.IsOn = null;

        Assert.True(data.IsOn);
        Assert.All(data.Inside, each => Assert.True(each.IsOn));
    }

    [Fact]
    public void Turning_a_row_back_on_puts_the_same_file_back()
    {
        GameFolder game = AGameWithFolders();
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(game, []);
        AThingOnTheDisc readme = tree.Inside.Single(each => each.Name == "readme.txt");

        readme.IsOn = false;
        readme.IsOn = true;

        // The tree holds everything that was found and never forgets a row
        // that was turned off.
        Assert.Equal(game.Files.Count, tree.AsChosen(game).Files.Count);
        Assert.Equal(game.Bytes, tree.AsChosen(game).Bytes);
    }

    [Fact]
    public void Leaving_the_installer_off_is_allowed_and_clears_it()
    {
        GameFolder game = AGameWithFolders();
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(game, []);

        tree.Inside.Single(each => each.What == TheTreeOfWhatGoesOn.TheInstaller).IsOn = false;

        GameFolder chosen = tree.AsChosen(game);

        // Allowed, because this window refuses by explaining rather than by
        // greying things out, and somebody making a disc of just the extras
        // has a reason nobody here can guess.
        //
        // Cleared rather than left pointing at a file nobody put on the disc:
        // the menu copes with a game that has no installer by writing no
        // Install button, which is honest. A path to a missing file is not.
        Assert.Null(chosen.Installer);
        Assert.DoesNotContain("setup.exe", chosen.Files.Select(file => file.RelativePath));
        Assert.True(tree.ItsInstallerIsOff());
    }

    [Fact]
    public void An_add_on_left_off_is_not_mistaken_for_a_file()
    {
        GameFolder game = AGameWithFolders();
        AddOn[] addOns = [new(0, @"C:\game\patch.exe", "Patch", 70)];
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(game, addOns);

        AThingOnTheDisc under = tree.Inside.Single(each => each.Name == TheTreeOfWhatGoesOn.AddOnsAreUnder);
        under.Inside.Single().IsOn = false;

        // Add-ons are not in the game's own listing, so a tick on one must
        // reach the add-on list and must not quietly drop a file as well.
        Assert.Empty(tree.AddOnsChosen(addOns));
        Assert.Equal(game.Files.Count, tree.AsChosen(game).Files.Count);
    }

    [Fact]
    public void Says_when_a_tick_changes_so_the_window_can_work_it_out_again()
    {
        int told = 0;
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), [], () => told++);

        tree.Inside.Single(each => each.Name == "readme.txt").IsOn = false;

        // Without this the size, the refusals and the disc count would all go
        // on describing files that are no longer going to be written.
        Assert.Equal(1, told);
    }

    // --------------------------------------------- the sizes read down

    [Fact]
    public void Every_row_knows_how_far_the_tree_has_pushed_it_in()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        AThingOnTheDisc data = tree.Inside.Single(each => each.Name == "data");
        AThingOnTheDisc textures = data.Inside.Single();
        AThingOnTheDisc skin = textures.Inside.Single();

        // A game is the top of its own tree, whatever else is on the window.
        Assert.Equal(0, tree.HowDeep);
        Assert.Equal(1, data.HowDeep);
        Assert.Equal(2, textures.HowDeep);
        Assert.Equal(3, skin.HowDeep);
    }

    [Fact]
    public void Every_size_starts_at_the_same_place_however_deep_the_row_is()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        AThingOnTheDisc[] everyDepth =
        [
            tree,
            tree.Inside.Single(each => each.Name == "data"),
            tree.Inside.Single(each => each.Name == "data").Inside.Single(),
            tree.Inside.Single(each => each.Name == "data").Inside.Single().Inside.Single()
        ];

        // This is the whole point of the property, said as the window sees it:
        // wherever a row has been pushed to, the name gives that much back, so
        // what follows the name begins in one column and the figures can be
        // read down. Four rows on the window at four depths meant four columns
        // of numbers and no way to compare any of them.
        double[] whereTheNameEnds =
        [
            .. everyDepth.Select(row => (row.HowDeep * 19) + row.HowWideTheNameIs)
        ];

        Assert.Equal([250, 250, 250, 250], whereTheNameEnds);
    }

    [Fact]
    public void A_name_keeps_some_room_however_deep_the_row_is()
    {
        AThingOnTheDisc bottom = new("bottom", 10, null, "bottom");
        AThingOnTheDisc deep = bottom;
        List<AThingOnTheDisc> everyRow = [bottom];

        for (int above = 0; above < 20; above++)
        {
            deep = new AThingOnTheDisc($"folder {above}", 0, null, $"folder {above}").Holding(deep);
            everyRow.Add(deep);
        }

        // Twenty folders deep the subtraction has gone through nothing and out
        // the other side, and a width of less than nothing is not a width WPF
        // will take. Past thirteen levels the sizes stop lining up, which is
        // the small loss. The window falling over is not.
        Assert.Equal(20, bottom.HowDeep);
        Assert.Equal(60, bottom.HowWideTheNameIs);
        Assert.All(
            everyRow,
            row => Assert.True(row.HowWideTheNameIs > 0, $"{row.Name} was given {row.HowWideTheNameIs}"));
    }


    // --------------------------------------- what the whole list comes to

    [Fact]
    public void Says_what_the_whole_list_comes_to()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        // 900, 300 and 100 bytes, and three files to say it about. There was
        // nowhere on the window that said what the lot came to, so somebody
        // with four games had to add them up by eye.
        Assert.Equal("1.3 KB in 3 files", new[] { tree }.WhatItAllComesTo());
    }

    [Fact]
    public void Says_one_file_rather_than_1_files()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithOneFile(), []);

        Assert.Equal("900 bytes in one file", new[] { tree }.WhatItAllComesTo());
    }

    [Fact]
    public void Says_nothing_at_all_when_every_row_is_turned_off()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);
        tree.IsOn = false;

        // Everything turned off is a thing somebody has just done on purpose,
        // and the line under this one already says so.
        Assert.Equal(string.Empty, new[] { tree }.WhatItAllComesTo());
    }

    [Fact]
    public void Counts_only_what_is_still_ticked()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);
        tree.Inside.Single(each => each.Name == "readme.txt").IsOn = false;

        // A total that counted rows somebody had just turned off would be
        // worse than no total at all.
        Assert.Equal("1.2 KB in 2 files", new[] { tree }.WhatItAllComesTo());
    }

    // ------------------------------------------------ what is left off it

    [Fact]
    public void Says_nothing_about_what_is_left_off_when_nothing_is()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);

        Assert.Null(new[] { tree }.WhatIsTurnedOff());
        Assert.Null(new[] { tree }.WhatTakingThoseOffWouldSay());
    }

    [Fact]
    public void Says_what_is_left_off_and_how_much_of_it()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);
        tree.Inside.Single(each => each.Name == "readme.txt").IsOn = false;

        Assert.Equal("Leaving off 1 thing, 100 bytes.", new[] { tree }.WhatIsTurnedOff());
        Assert.Equal("Remove the 1 thing left off", new[] { tree }.WhatTakingThoseOffWouldSay());
    }

    [Fact]
    public void Counts_everything_left_off_wherever_it_sits_in_the_tree()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(AGameWithFolders(), []);
        tree.Inside.Single(each => each.Name == "readme.txt").IsOn = false;
        tree.Inside.Single(each => each.Name == "setup.exe").IsOn = false;

        // Rows scattered down a tree, some of them inside folders that are
        // shut, which is why the button says how many it will take.
        Assert.Equal("Leaving off 2 things, 1000 bytes.", new[] { tree }.WhatIsTurnedOff());
        Assert.Equal("Remove the 2 things left off", new[] { tree }.WhatTakingThoseOffWouldSay());
    }

    [Fact]
    public void Counts_the_files_in_a_folder_somebody_turned_off_and_not_the_folder()
    {
        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(ADeeperGame(), []);
        tree.Inside.Single(each => each.Name == "data").IsOn = false;

        // Two things, not three. Unticking a folder unticks what is in it, and
        // what comes off the disc is the two files: the folder is a way of
        // showing them rather than a thing anybody put there.
        Assert.Equal("Leaving off 2 things, 300 bytes.", new[] { tree }.WhatIsTurnedOff());
    }

    // ------------------------------------------------------------------ helpers

    private static GameFolder AGameWithFolders() => new(
        Path: @"C:\game",
        Kind: GameKind.GogInstaller,
        Name: "A Game",
        MatchName: "A Game",
        Installer: new ListedFile("setup.exe", 900),
        Files:
        [
            new ListedFile("setup.exe", 900),
            new ListedFile(@"data\textures\skin.pak", 300),
            new ListedFile("readme.txt", 100)
        ],
        LauncherCandidates: [],
        Warning: null);

    private static GameFolder ADeeperGame() => new(
        Path: @"C:\game",
        Kind: GameKind.GogInstaller,
        Name: "A Game",
        MatchName: "A Game",
        Installer: null,
        Files:
        [
            new ListedFile(@"data\one.pak", 100),
            new ListedFile(@"data\two.pak", 200)
        ],
        LauncherCandidates: [],
        Warning: null);

    private static GameFolder AGameWithOneFile() => new(
        Path: @"C:\game",
        Kind: GameKind.GogInstaller,
        Name: "A Game",
        MatchName: "A Game",
        Installer: new ListedFile("setup.exe", 900),
        Files: [new ListedFile("setup.exe", 900)],
        LauncherCandidates: [],
        Warning: null);
}
