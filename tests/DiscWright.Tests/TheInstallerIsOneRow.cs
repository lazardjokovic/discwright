using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// A game's installer and its numbered parts are one row in the list, with
/// one tick, at the size of all of it.
/// </summary>
/// <remarks>
/// The owner, on a recording of 21 November 2026, with a game that came in
/// parts on the window: "what's the point of all these others, why are they
/// on the disc", the installer itself reading 1.6 MB beside rows of 4 GB.
/// They are the game. And each had a tick of its own, so one could be left
/// off, which makes a disc whose game cannot install.
/// </remarks>
public class TheInstallerIsOneRow
{
    private const int Each = 1_000_000;

    private static MakeADisc WithAGameInParts(int parts, out GameFolder game)
    {
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        game = TheMainWindowWithoutAWindow.AGame("witcher", Each, parts);
        screen.Add(game);

        return screen;
    }

    private static string[] WhatGoesOn(MakeADisc screen, int game = 0) =>
        [.. screen.AsAProject().Games[game].Files.Select(file => file.RelativePath)];

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void An_installer_and_its_parts_are_one_row_at_the_size_of_all_of_it(int parts)
    {
        MakeADisc screen = WithAGameInParts(parts, out _);

        AThingOnTheDisc row = Assert.Single(screen.WhatGoesOnIt[0].EveryThing());

        Assert.Equal("setup_witcher.exe", row.Name);
        Assert.Equal(TheTreeOfWhatGoesOn.TheInstaller, row.What);
        Assert.Equal((long)Each * (parts + 1), row.Bytes);
        Assert.Equal(parts + 1, row.Files);
        Assert.False(row.HasThingsInside);

        // And the game is as big as it was, and holds as many files.
        Assert.Equal((long)Each * (parts + 1), screen.WhatGoesOnIt[0].Bytes);
        Assert.Equal(parts + 1, screen.WhatGoesOnIt[0].Files);
        Assert.Equal(parts + 1, WhatGoesOn(screen).Length);
    }

    [Fact]
    public void Pointed_at_it_says_how_many_parts_and_that_they_are_the_game()
    {
        Assert.EndsWith(
            "With its 1 part, which holds the game itself. They go on the disc together.",
            Assert.Single(WithAGameInParts(1, out _).WhatGoesOnIt[0].EveryThing()).InFull,
            StringComparison.Ordinal);

        Assert.EndsWith(
            "With its 3 parts, which hold the game itself. They go on the disc together.",
            Assert.Single(WithAGameInParts(3, out _).WhatGoesOnIt[0].EveryThing()).InFull,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_installer_in_one_piece_is_the_row_it_always_was()
    {
        MakeADisc screen = WithAGameInParts(0, out _);

        AThingOnTheDisc row = Assert.Single(screen.WhatGoesOnIt[0].EveryThing());

        Assert.Equal(TheTreeOfWhatGoesOn.TheInstaller, row.What);
        Assert.Equal(Each, row.Bytes);
        Assert.Equal(1, row.Files);
        Assert.DoesNotContain("part", row.InFull, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_total_counts_every_part_though_the_list_shows_one_row()
    {
        MakeADisc screen = WithAGameInParts(3, out _);

        Assert.Equal("3.8 MB in 4 files", screen.WhatIsOnIt);
    }

    [Fact]
    public void An_add_ons_parts_are_counted_in_the_total_as_the_games_are()
    {
        // An add-on has been one row with its parts since it was first
        // listed, and counted as one file whatever came with it: a game in
        // three files with a DLC in three was "4 files", seen in the real
        // window the day the game's own parts became one row.
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(
            TheMainWindowWithoutAWindow.AGame("witcher", Each, parts: 2),
            [
                new LeftBehind(
                    new ListedFile("setup_witcher_dlc.exe", Each),
                    LeftBehindKind.LooksLikeAnAddOn,
                    Parts: [new ListedFile("setup_witcher_dlc-1.bin", Each), new ListedFile("setup_witcher_dlc-2.bin", Each)]),
                new LeftBehind(new ListedFile("patch_witcher_1_to_2.exe", Each), LeftBehindKind.LooksLikeAnAddOn),
            ]);

        AThingOnTheDisc[] addOns =
            [.. screen.WhatGoesOnIt[0].EveryThing().Where(row => row.What == TheTreeOfWhatGoesOn.AnAddOn)];

        Assert.Equal([3, 1], addOns.Select(row => row.Files));
        Assert.Equal(7, screen.WhatGoesOnIt[0].Files);
        Assert.Equal("6.7 MB in 7 files", screen.WhatIsOnIt);
    }

    [Fact]
    public void Its_one_tick_is_for_all_of_it()
    {
        MakeADisc screen = WithAGameInParts(3, out GameFolder game);
        AThingOnTheDisc row = Assert.Single(screen.WhatGoesOnIt[0].EveryThing());

        Assert.Equal(game.Files.Select(file => file.RelativePath), WhatGoesOn(screen));

        // Off: none of it goes on. Parts on a disc with no installer to
        // read them are gigabytes of nothing.
        row.IsOn = false;

        Assert.Empty(WhatGoesOn(screen));
        Assert.Null(screen.AsAProject().Games[0].Installer);

        // On again, and every part is back, in the order they were in.
        row.IsOn = true;

        Assert.Equal(game.Files.Select(file => file.RelativePath), WhatGoesOn(screen));
        Assert.NotNull(screen.AsAProject().Games[0].Installer);
    }

    [Fact]
    public void A_file_beside_it_that_is_not_one_of_its_parts_keeps_a_row_and_a_tick_of_its_own()
    {
        // Same folder, and each close to a part's name without being one:
        // another stem, another extension, and a part of something else.
        ListedFile installer = new("setup_witcher.exe", Each);
        ListedFile part = new("setup_witcher-1.bin", Each);
        ListedFile[] others =
        [
            new("setup_witcher_dlc-1.bin", 10), new("setup_witcher-1.dat", 20), new("witcher-1.bin", 30),
            new("setup_witcher.bin", 40), new(@"deeper\setup_witcher-2.bin", 50),
        ];

        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(new GameFolder(
            @"C:\games\witcher", GameKind.GogInstaller, "witcher", "witcher", installer, [installer, part, .. others], [], null));

        AThingOnTheDisc[] rows = [.. screen.WhatGoesOnIt[0].EveryThing()];

        Assert.Equal(1 + others.Length, rows.Length);
        Assert.Equal(2, rows.Single(row => row.What == TheTreeOfWhatGoesOn.TheInstaller).Files);
        Assert.All(others, other => Assert.Contains(rows, row => row.ItsPath == other.RelativePath && row.Files == 1));

        // Each of them leaves alone, and the installer's part stays.
        rows.Single(row => row.ItsPath == "setup_witcher_dlc-1.bin").IsOn = false;

        Assert.DoesNotContain("setup_witcher_dlc-1.bin", WhatGoesOn(screen));
        Assert.Contains("setup_witcher-1.bin", WhatGoesOn(screen));
        Assert.Equal(1 + others.Length, WhatGoesOn(screen).Length);
    }

    [Fact]
    public void A_folder_of_game_files_with_no_installer_keeps_every_file_a_row()
    {
        ListedFile[] files = [new("Game.exe", 10), new("Game-1.bin", 20), new("Game-2.bin", 30)];

        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(new GameFolder(@"C:\games\loose", GameKind.FilesWithLauncher, "loose", "loose", null, files, [], null));

        Assert.Equal(3, screen.WhatGoesOnIt[0].EveryThing().Count());
        Assert.All(screen.WhatGoesOnIt[0].EveryThing(), row => Assert.Null(row.What));
    }

    [Fact]
    public void Taking_the_installer_off_takes_its_parts_and_leaves_what_else_the_game_has()
    {
        ListedFile installer = new("setup_witcher.exe", Each);
        ListedFile[] parts = [new("setup_witcher-1.bin", Each), new("setup_witcher-2.bin", Each)];
        ListedFile other = new("soundtrack.zip", 10);

        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(new GameFolder(
            @"C:\games\witcher", GameKind.GogInstaller, "witcher", "witcher", installer, [installer, .. parts, other], [], null));

        screen.TakeOff(screen.WhatGoesOnIt[0].EveryThing().Single(row => row.What == TheTreeOfWhatGoesOn.TheInstaller));

        Assert.Equal(["soundtrack.zip"], WhatGoesOn(screen));
        Assert.Equal("soundtrack.zip", Assert.Single(screen.WhatGoesOnIt[0].EveryThing()).Name);
    }

    [Fact]
    public void A_game_that_was_only_its_installer_goes_when_the_installer_does()
    {
        MakeADisc screen = WithAGameInParts(3, out _);

        screen.TakeOff(Assert.Single(screen.WhatGoesOnIt[0].EveryThing()));

        Assert.Empty(screen.Games);
        Assert.Empty(screen.WhatGoesOnIt);
    }

    [Fact]
    public void Left_off_it_stays_off_when_the_list_is_built_again_and_its_parts_with_it()
    {
        ListedFile installer = new("setup_witcher.exe", Each);
        ListedFile part = new("setup_witcher-1.bin", Each);
        ListedFile[] others = [new("soundtrack.zip", 10), new("wallpapers.zip", 20)];

        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(new GameFolder(
            @"C:\games\witcher", GameKind.GogInstaller, "witcher", "witcher", installer, [installer, part, .. others], [], null));

        screen.WhatGoesOnIt[0].EveryThing().Single(row => row.What == TheTreeOfWhatGoesOn.TheInstaller).IsOn = false;

        // Taking another row off builds the branch again from what is left.
        screen.TakeOff(screen.WhatGoesOnIt[0].EveryThing().Single(row => row.Name == "wallpapers.zip"));

        Assert.Equal(false, screen.WhatGoesOnIt[0].EveryThing().Single(row => row.What == TheTreeOfWhatGoesOn.TheInstaller).IsOn);
        Assert.Equal(["soundtrack.zip"], WhatGoesOn(screen));
    }

    [Fact]
    public void A_game_says_which_files_are_its_installers_parts()
    {
        ListedFile installer = new(@"sub\setup_witcher.exe", 1);
        ListedFile[] files =
        [
            installer, new(@"sub\setup_witcher-2.bin", 1), new(@"sub\setup_witcher-1.bin", 1),
            new("setup_witcher-3.bin", 1), new(@"sub\other.bin", 1),
        ];
        GameFolder game = new(@"C:\g", GameKind.GogInstaller, "w", "w", installer, files, [], null);

        // Beside it, by its name, in the order of their numbers.
        Assert.Equal(
            [@"sub\setup_witcher-1.bin", @"sub\setup_witcher-2.bin"],
            game.ThePartsOfItsInstaller.Select(part => part.RelativePath));

        Assert.Empty((game with { Installer = null }).ThePartsOfItsInstaller);

        // Without the installer, its parts are gone and nothing else is.
        Assert.Equal(
            ["setup_witcher-3.bin", @"sub\other.bin"],
            game.Without(@"SUB\SETUP_WITCHER.EXE").Files.Select(file => file.RelativePath));

        // Without one part, only that part: the others are still its parts.
        Assert.Equal(4, game.Without(@"sub\setup_witcher-1.bin").Files.Count);
        Assert.Equal(4, game.Without(@"sub\other.bin").Files.Count);
    }
}
