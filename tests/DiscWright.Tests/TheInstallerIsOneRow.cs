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

    // ------------------------------------------- the parts can be looked at
    //
    // The owner, 21 November 2026: untick one and the installer will not
    // work, so they must not be tickable, "but the user should still be
    // able to drop them down if he wants, always collapsed by default".

    private static AThingOnTheDisc TheInstallerRow(MakeADisc screen, int game = 0) =>
        screen.WhatGoesOnIt[game].EveryThing().Single(row => row.What == TheTreeOfWhatGoesOn.TheInstaller);

    [Fact]
    public void Its_parts_are_under_it_to_be_looked_at_and_it_is_shut_until_somebody_opens_it()
    {
        MakeADisc screen = WithAGameInParts(3, out GameFolder game);
        AThingOnTheDisc installer = TheInstallerRow(screen);

        Assert.True(installer.CanBeOpened);
        Assert.False(installer.IsOpened);

        // Under it for the list to draw, and not inside it for anything
        // that reads ticks or sizes.
        Assert.False(installer.HasThingsInside);
        Assert.Empty(installer.Inside);
        Assert.Same(installer.ShownUnderIt, installer.WhatOpensUnderIt);
        Assert.Same(installer, Assert.Single(screen.WhatGoesOnIt[0].EveryThing()));

        // By number, with the file's own name for whoever points at it.
        Assert.Equal(["Part 1 of 3", "Part 2 of 3", "Part 3 of 3"], installer.ShownUnderIt.Select(part => part.Name));
        Assert.StartsWith("Part 2 of 3" + (char)10 + "setup_witcher-2.bin" + (char)10, installer.ShownUnderIt[1].InFull, StringComparison.Ordinal);
        Assert.Equal(game.Files.Skip(1).Select(file => file.RelativePath), installer.ShownUnderIt.Select(part => part.ItsPath));

        Assert.All(installer.ShownUnderIt, part =>
        {
            Assert.True(part.IsOnlyToLookAt);
            Assert.Equal(TheTreeOfWhatGoesOn.APart, part.What);
            Assert.Equal(Each, part.Bytes);
            Assert.Equal(installer.HowDeep + 1, part.HowDeep);
            Assert.Same(screen.WhatGoesOnIt[0], part.TheGameItIsIn);
            Assert.False(part.CanBeOpened);
            Assert.EndsWith(TheTreeOfWhatGoesOn.WhatAPartIs, part.InFull, StringComparison.Ordinal);
        });

        Assert.False(installer.IsOnlyToLookAt);
        Assert.False(screen.WhatGoesOnIt[0].IsOnlyToLookAt);
    }

    [Fact]
    public void Each_part_shows_its_own_size_and_they_add_up_to_the_rows_with_the_program()
    {
        // Parts are not all one size: the last is whatever was left over.
        ListedFile installer = new("setup_witcher.exe", 7);
        ListedFile[] parts = [new("setup_witcher-1.bin", 4000), new("setup_witcher-2.bin", 3000), new("setup_witcher-3.bin", 250)];

        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(new GameFolder(
            @"C:\games\witcher", GameKind.GogInstaller, "witcher", "witcher", installer, [installer, .. parts], [], null));

        AThingOnTheDisc row = TheInstallerRow(screen);

        Assert.Equal([4000L, 3000L, 250L], row.ShownUnderIt.Select(part => part.Bytes));
        Assert.Equal(7 + row.ShownUnderIt.Sum(part => part.Bytes), row.Bytes);
        Assert.All(row.ShownUnderIt, part => Assert.Equal(1, part.Files));
    }

    [Fact]
    public void A_row_with_no_parts_has_nothing_shown_under_it_and_a_folder_shows_what_is_inside_it()
    {
        MakeADisc screen = WithAGameInParts(0, out _);
        AThingOnTheDisc installer = TheInstallerRow(screen);

        Assert.False(installer.CanBeOpened);
        Assert.Empty(installer.WhatOpensUnderIt);

        // A game is a folder: what opens under it is what is inside it.
        AThingOnTheDisc game = screen.WhatGoesOnIt[0];

        Assert.True(game.CanBeOpened);
        Assert.Same(game.Inside, game.WhatOpensUnderIt);
        Assert.Empty(game.ShownUnderIt);
    }

    [Fact]
    public void A_part_cannot_be_taken_off_by_itself_whoever_asks()
    {
        MakeADisc screen = WithAGameInParts(3, out GameFolder game);
        AThingOnTheDisc part = TheInstallerRow(screen).ShownUnderIt[1];

        screen.WhatIsSelected = part;

        // Picked, it names its game, and that is all picking it does.
        Assert.Equal("witcher", screen.ThePickedGame);
        Assert.False(screen.CanTakeSomethingOff);
        Assert.False(screen.CanMoveThePickedUp);
        Assert.False(screen.CanMoveThePickedDown);
        Assert.Null(screen.MoveThePicked(1));
        Assert.False(MakeADisc.HasAnOrder(part));
        Assert.Null(MakeADisc.WhatADragCarries(part));

        screen.TakeOff(part);

        Assert.Equal(game.Files.Select(file => file.RelativePath), WhatGoesOn(screen));
        Assert.Equal(3, TheInstallerRow(screen).ShownUnderIt.Count);

        // The installer picked can be, and takes them all.
        screen.WhatIsSelected = TheInstallerRow(screen);
        Assert.True(screen.CanTakeSomethingOff);
    }

    [Fact]
    public void A_game_let_go_over_a_part_of_another_game_takes_that_games_place()
    {
        MakeADisc screen = WithAGameInParts(2, out _);
        screen.Add(TheMainWindowWithoutAWindow.AGame("gothic", Each, parts: 2));

        AThingOnTheDisc first = screen.WhatGoesOnIt[0];
        AThingOnTheDisc aPartOfTheSecond = TheInstallerRow(screen, 1).ShownUnderIt[1];
        AThingOnTheDisc aPartOfItsOwn = TheInstallerRow(screen).ShownUnderIt[0];

        Assert.False(screen.MayBeLetGoOver(first, aPartOfItsOwn));
        Assert.True(screen.MayBeLetGoOver(first, aPartOfTheSecond));

        screen.LetGoOver(first, aPartOfTheSecond);

        Assert.Equal(["gothic", "witcher"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Opened_it_stays_open_when_the_list_is_built_again_and_shut_it_stays_shut()
    {
        ListedFile installer = new("setup_witcher.exe", Each);
        ListedFile part = new("setup_witcher-1.bin", Each);
        ListedFile[] others = [new("soundtrack.zip", 10), new("wallpapers.zip", 20), new("manual.pdf", 30)];

        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        screen.Add(new GameFolder(
            @"C:\games\witcher", GameKind.GogInstaller, "witcher", "witcher", installer, [installer, part, .. others], [], null));

        TheInstallerRow(screen).IsOpened = true;

        // Taking another row off builds the branch again from what is left.
        screen.TakeOff(screen.WhatGoesOnIt[0].EveryThing().Single(row => row.Name == "wallpapers.zip"));

        Assert.True(TheInstallerRow(screen).IsOpened);
        Assert.Equal(true, TheInstallerRow(screen).IsOn);

        TheInstallerRow(screen).IsOpened = false;
        TheInstallerRow(screen).IsOn = false;
        screen.TakeOff(screen.WhatGoesOnIt[0].EveryThing().Single(row => row.Name == "manual.pdf"));

        Assert.False(TheInstallerRow(screen).IsOpened);
        Assert.Equal(false, TheInstallerRow(screen).IsOn);
        Assert.Equal("Part 1 of 1", Assert.Single(TheInstallerRow(screen).ShownUnderIt).Name);
    }

    [Fact]
    public void The_list_draws_a_part_with_no_tick_and_no_cross()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement row = Assert.Single(System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!
            .Descendants(xaml + "HierarchicalDataTemplate"));

        // What opens under a row is what the row says, which for an
        // installer is its parts.
        Assert.Equal("{Binding WhatOpensUnderIt}", (string?)row.Attribute("ItemsSource"));

        // The tick: hidden, not collapsed, so the name starts where every
        // other name does.
        System.Xml.Linq.XElement tick = Assert.Single(row.Descendants(xaml + "CheckBox"));
        System.Xml.Linq.XElement when = Assert.Single(tick.Descendants(xaml + "DataTrigger"));

        Assert.Equal("{Binding IsOnlyToLookAt}", (string?)when.Attribute("Binding"));
        Assert.Equal("True", (string?)when.Attribute("Value"));

        System.Xml.Linq.XElement then = Assert.Single(when.Elements(xaml + "Setter"));

        Assert.Equal("Visibility", (string?)then.Attribute("Property"));
        Assert.Equal("Hidden", (string?)then.Attribute("Value"));
        Assert.Equal(
            "{StaticResource {x:Type CheckBox}}",
            (string?)Assert.Single(tick.Descendants(xaml + "Style")).Attribute("BasedOn"));

        // The cross: not there at all, by the panel it sits in, and its own
        // style still decides when it shows on every other row.
        System.Xml.Linq.XElement cross = Assert.Single(
            row.Descendants(xaml + "Button"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "TakeOffThisRow");

        Assert.Equal(
            "{Binding IsOnlyToLookAt, Converter={StaticResource WhenNot}}",
            (string?)cross.Parent!.Attribute("Visibility"));
        Assert.Null(cross.Attribute("Visibility"));
        Assert.Equal("{StaticResource OffTheRow}", (string?)cross.Attribute("Style"));
    }
}
