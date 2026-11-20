using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Putting the games in another order, discwright#98: the menu lists them in
/// the order of the list, and the only way to change that was to take every
/// game off and add them again.
/// </summary>
/// <remarks>
/// The danger in it is the add-ons. Each knows its game by where the game
/// sits, so an order changed without them hands a DLC to whichever game is
/// now in the old place, and nothing on the window looks wrong.
/// </remarks>
public class TheOrderOfTheGames
{
    // ------------------------------------------------- the add-ons' own order
    //
    // The owner's own Hollow Knight, 20 November 2026: four updates listed
    // 1.5.12618, 1.5.12620, 1.5.12459, 1.5.78.11833a, the order their file
    // names sort in, and nothing to drag them by.

    private static LeftBehind AnUpdate(string file) => new(new ListedFile(file, 500), LeftBehindKind.LooksLikeAnAddOn);

    private static readonly string[] HollowKnightsUpdatesAsTheyAreListed =
    [
        "patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe",
        "patch_hollow_knight_1.5.12618_(89712)_to_1.5.12620_(89718).exe",
        "patch_hollow_knight_1.5.78.11833a_(85515)_to_1.5.12459_(88294).exe",
        "patch_hollow_knight_1.5.78.11833_(77123)_to_1.5.78.11833a_(85515).exe",
    ];

    private static MakeADisc HollowKnightWithItsUpdates()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"), [.. HollowKnightsUpdatesAsTheyAreListed.Select(AnUpdate)]);

        return screen;
    }

    private static AThingOnTheDisc[] TheAddOnRows(MakeADisc screen, int game = 0) =>
        [.. screen.WhatGoesOnIt[game].EveryThing().Where(each => each.What == TheTreeOfWhatGoesOn.AnAddOn)];

    private static string[] TheAddOnsOnTheDisc(MakeADisc screen) =>
        [.. screen.AsAProject().AddOnsOrNone.Select(addOn => addOn.Name)];

    [Theory]
    [InlineData("patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe", 89712L)]
    [InlineData(@"C:\games\Hollow Knight\patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe", 89712L)]
    [InlineData("PATCH_x_1_(5)_TO_2_(60).EXE", 60L)]
    [InlineData("patch_game_1_to_2.exe", null)]
    [InlineData("patch_game_1_(5)_to_2.exe", null)]
    [InlineData("setup_game_dlc_(89712).exe", null)]
    [InlineData("soundtrack_(89712).exe", null)]
    [InlineData("patch_game_1_to_2_(abc).exe", null)]
    public void An_update_says_which_build_it_reaches_and_nothing_else_does(string file, long? build)
    {
        Assert.Equal(build, AddOn.TheBuildItReaches(file));
    }

    [Fact]
    public void A_games_updates_are_listed_lowest_build_first_and_go_on_the_disc_that_way()
    {
        MakeADisc screen = HollowKnightWithItsUpdates();

        string[] inOrder =
        [
            "Update 1.5.78.11833a (85515)", "Update 1.5.12459 (88294)", "Update 1.5.12618 (89712)", "Update 1.5.12620 (89718)",
        ];

        Assert.Equal(inOrder, TheAddOnRows(screen).Select(each => each.Name));
        Assert.Equal(inOrder, TheAddOnsOnTheDisc(screen));
    }

    [Fact]
    public void Only_the_updates_move_and_only_among_the_places_updates_had()
    {
        // A DLC has no build to go by, so it stays where it was, between
        // whatever was either side of it. And each game's own stay its own.
        AddOn[] found =
        [
            new(0, @"C:\a\patch_a_1_(1)_to_3_(30).exe", "three", 1),
            new(0, @"C:\a\setup_a_dlc.exe", "dlc", 1),
            new(1, @"C:\b\patch_b_1_(1)_to_9_(5).exe", "other game", 1),
            new(0, @"C:\a\patch_a_1_(1)_to_2_(20).exe", "two", 1),
            new(0, @"C:\a\patch_a_1_to_2.exe", "no build", 1),
            new(0, @"C:\a\patch_a_0_(0)_to_1_(10).exe", "one", 1),
        ];

        Assert.Equal(
            ["one", "dlc", "other game", "two", "no build", "three"],
            AddOn.WithUpdatesInTheirOrder(found).Select(addOn => addOn.Name));
    }

    [Fact]
    public void Moving_one_games_add_on_leaves_every_other_games_exactly_where_it_was()
    {
        // The list holds every game's add-ons together, and they need not
        // be in runs: one attached by hand later sits after another game's.
        AddOn[] all =
        [
            new(0, @"C:\a\one.exe", "a one", 1),
            new(1, @"C:\b\one.exe", "b one", 1),
            new(0, @"C:\a\two.exe", "a two", 1),
            new(1, @"C:\b\two.exe", "b two", 1),
            new(0, @"C:\a\three.exe", "a three", 1),
        ];

        IReadOnlyList<AddOn>? now = AddOn.WithOneMoved(all, 0, @"C:\a\three.exe", @"c:\A\ONE.exe", after: false);

        Assert.NotNull(now);
        Assert.Equal(["a three", "b one", "a one", "b two", "a two"], now.Select(addOn => addOn.Name));

        // Nothing to move, and nothing is what comes back.
        Assert.Null(AddOn.WithOneMoved(all, 0, @"C:\a\one.exe", @"C:\a\one.exe", after: true));
        Assert.Null(AddOn.WithOneMoved(all, 0, @"C:\a\one.exe", @"C:\a\two.exe", after: false));
        Assert.Null(AddOn.WithOneMoved(all, 0, @"C:\a\one.exe", @"C:\b\one.exe", after: true));
        Assert.Null(AddOn.WithOneMoved(all, 0, @"C:\a\nowhere.exe", @"C:\a\two.exe", after: true));
        Assert.Null(AddOn.WithOneMoved(all, 7, @"C:\a\one.exe", @"C:\a\two.exe", after: true));
    }

    [Fact]
    public void An_add_on_let_go_over_another_goes_before_it_or_after()
    {
        MakeADisc screen = HollowKnightWithItsUpdates();
        AThingOnTheDisc[] rows = TheAddOnRows(screen);

        // The last, let go over the upper half of the first: to the top.
        AThingOnTheDisc? moved = screen.MoveTheAddOnTo(rows[3], rows[0], after: false);

        string[] now =
        [
            "Update 1.5.12620 (89718)", "Update 1.5.78.11833a (85515)", "Update 1.5.12459 (88294)", "Update 1.5.12618 (89712)",
        ];

        Assert.Equal(now, TheAddOnRows(screen).Select(each => each.Name));
        Assert.Equal(now, TheAddOnsOnTheDisc(screen));

        // The row handed back is the one now in the list, and it is picked.
        Assert.NotNull(moved);
        Assert.Same(TheAddOnRows(screen)[0], moved);
        Assert.Same(moved, screen.WhatIsSelected);

        // And over the lower half of the one after it: one place down.
        rows = TheAddOnRows(screen);
        screen.MoveTheAddOnTo(rows[0], rows[1], after: true);

        Assert.Equal(
            ["Update 1.5.78.11833a (85515)", "Update 1.5.12620 (89718)", "Update 1.5.12459 (88294)", "Update 1.5.12618 (89712)"],
            TheAddOnsOnTheDisc(screen));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 2, true)]
    public void Let_go_where_it_already_is_nothing_moves(int moved, int onto, bool after)
    {
        MakeADisc screen = HollowKnightWithItsUpdates();
        AThingOnTheDisc[] rows = TheAddOnRows(screen);
        string[] before = TheAddOnsOnTheDisc(screen);

        Assert.Null(screen.MoveTheAddOnTo(rows[moved], rows[onto], after));

        Assert.Equal(before, TheAddOnsOnTheDisc(screen));
        Assert.Same(rows[0], TheAddOnRows(screen)[0]);
    }

    [Fact]
    public void A_tick_taken_off_an_add_on_stays_off_when_another_is_moved()
    {
        MakeADisc screen = HollowKnightWithItsUpdates();
        TheAddOnRows(screen)[1].IsOn = false;

        AThingOnTheDisc[] rows = TheAddOnRows(screen);
        screen.MoveTheAddOnTo(rows[3], rows[0], after: false);

        Assert.Equal(
            ["Update 1.5.12620 (89718)", "Update 1.5.78.11833a (85515)", "Update 1.5.12618 (89712)"],
            TheAddOnsOnTheDisc(screen));
        Assert.Equal(false, TheAddOnRows(screen).Single(each => each.Name == "Update 1.5.12459 (88294)").IsOn);
    }

    [Fact]
    public void An_add_on_moves_among_its_own_games_add_ons_and_nowhere_else()
    {
        MakeADisc screen = HollowKnightWithItsUpdates();
        screen.Add(AGame("Celeste"), [ADlc("setup_celeste_dlc.exe"), ADlc("setup_celeste_more.exe")]);

        AThingOnTheDisc[] its = TheAddOnRows(screen);
        AThingOnTheDisc[] others = TheAddOnRows(screen, 1);
        AThingOnTheDisc game = screen.WhatGoesOnIt[0];
        AThingOnTheDisc folder = game.EveryRow().Single(each => each.Name == TheTreeOfWhatGoesOn.AddOnsAreUnder);
        AThingOnTheDisc installer = game.EveryRow().First(each => each.Name.StartsWith("setup_", StringComparison.Ordinal));

        Assert.True(screen.MayBeLetGoOver(its[0], its[1]));

        // Not onto itself, its folder, its game, its game's installer,
        // another game, another game's add-on, or nothing at all.
        foreach (AThingOnTheDisc? over in new[] { its[0], folder, game, installer, screen.WhatGoesOnIt[1], others[0], null })
        {
            Assert.False(screen.MayBeLetGoOver(its[0], over));
            Assert.Null(screen.MoveTheAddOnTo(its[0], over, after: true));
        }

        Assert.Equal(
            ["Update 1.5.78.11833a (85515)", "Update 1.5.12459 (88294)", "Update 1.5.12618 (89712)", "Update 1.5.12620 (89718)", "celeste dlc", "celeste more"],
            TheAddOnsOnTheDisc(screen));

        // The other game's own two swap, and the first game's are untouched.
        screen.MoveTheAddOnTo(others[1], others[0], after: false);

        Assert.Equal(
            ["Update 1.5.78.11833a (85515)", "Update 1.5.12459 (88294)", "Update 1.5.12618 (89712)", "Update 1.5.12620 (89718)", "celeste more", "celeste dlc"],
            TheAddOnsOnTheDisc(screen));
    }

    [Fact]
    public void A_game_has_nowhere_to_go_over_its_own_rows_which_is_the_whole_list_when_it_is_alone()
    {
        // The owner, 20 November 2026: one game, dragged, and nothing said
        // why nothing happened. The pointer wears no-entry now.
        MakeADisc screen = HollowKnightWithItsUpdates();
        AThingOnTheDisc game = screen.WhatGoesOnIt[0];

        Assert.All(game.EveryRow(), over => Assert.False(screen.MayBeLetGoOver(game, over)));
        Assert.False(screen.MayBeLetGoOver(game, null));

        screen.Add(AGame("Celeste"));

        // With another game there is: over that game, or anything of its.
        Assert.All(screen.WhatGoesOnIt[1].EveryRow(), over => Assert.True(screen.MayBeLetGoOver(game, over)));
        Assert.All(game.EveryRow(), over => Assert.False(screen.MayBeLetGoOver(game, over)));
    }

    [Fact]
    public void A_drag_from_an_add_on_carries_the_add_on_and_from_anything_else_the_game()
    {
        MakeADisc screen = HollowKnightWithItsUpdates();
        AThingOnTheDisc game = screen.WhatGoesOnIt[0];
        AThingOnTheDisc update = TheAddOnRows(screen)[2];

        Assert.Same(update, MakeADisc.WhatADragCarries(update));
        Assert.All(
            game.EveryRow().Where(each => each.What != TheTreeOfWhatGoesOn.AnAddOn),
            row => Assert.Same(game, MakeADisc.WhatADragCarries(row)));
        Assert.Null(MakeADisc.WhatADragCarries(null));
    }

    [Fact]
    public void An_order_somebody_made_is_what_a_project_file_carries_and_what_comes_back()
    {
        // Build order is only for add-ons as they are first found. An order
        // made by hand is saved as it is and opened as it was.
        MakeADisc screen = HollowKnightWithItsUpdates();
        AThingOnTheDisc[] rows = TheAddOnRows(screen);
        screen.MoveTheAddOnTo(rows[3], rows[0], after: false);

        string saved = TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "DVD5", "out", DateTimeOffset.UtcNow);
        OpenedProject opened = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value;

        Assert.Equal(
            [
                "patch_hollow_knight_1.5.12618_(89712)_to_1.5.12620_(89718).exe",
                "patch_hollow_knight_1.5.78.11833_(77123)_to_1.5.78.11833a_(85515).exe",
                "patch_hollow_knight_1.5.78.11833a_(85515)_to_1.5.12459_(88294).exe",
                "patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe",
            ],
            opened.Entries.Where(each => each.IsAnAddOn).Select(each => Path.GetFileName(each.Installer)));
    }

    [Fact]
    public void The_sort_list_shares_the_totals_line_and_has_no_line_of_its_own()
    {
        // The owner, 20 November 2026: under the total, between it and the
        // picked game's links, it made the corner messy.
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement window = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        System.Xml.Linq.XElement ways = Assert.Single(window.Descendants(xaml + "ComboBox"), each => Id(each) == "SortTheGames");
        System.Xml.Linq.XElement total = Assert.Single(window.Descendants(xaml + "TextBlock"), each => Id(each) == "WhatIsOnIt");
        System.Xml.Linq.XElement line = ways.Parent!.Parent!;

        Assert.Equal(xaml + "DockPanel", line.Name);
        Assert.Same(line, total.Parent!.Parent);
        Assert.Equal("Right", (string?)ways.Parent!.Attribute("DockPanel.Dock"));

        // Docked before the total, or the total would take the whole line.
        Assert.Same(ways.Parent, line.Elements().First());
    }

    // --------------------------------------------------- the add-ons' own sum

    [Fact]
    public void Add_ons_follow_their_games_to_wherever_they_went()
    {
        AddOn[] addOns =
        [
            new(0, "a-dlc.exe", "A's DLC", 1),
            new(2, "c-dlc.exe", "C's DLC", 1),
            new(0, "a-patch.exe", "A's patch", 1),
        ];

        // A goes last, B first, C to the middle.
        IReadOnlyList<AddOn> after = AddOn.AfterTheGamesMoved(addOns, [2, 0, 1]);

        Assert.Equal(
            [(1, "C's DLC"), (2, "A's DLC"), (2, "A's patch")],
            after.Select(each => (each.BelongsTo, each.Name)));
    }

    [Fact]
    public void An_add_on_pointing_at_no_game_is_left_alone_rather_than_thrown_on()
    {
        AddOn stray = new(7, "x.exe", "Stray", 1);

        Assert.Equal([stray], AddOn.AfterTheGamesMoved([stray], [1, 0]));
    }

    // -------------------------------------------------------------- moving one

    [Fact]
    public void A_game_moved_up_changes_places_with_the_one_above_everywhere()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];

        AThingOnTheDisc? moved = screen.MoveTheGame(-1);

        Assert.Equal(["Alpha", "Gamma", "Beta"], screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Equal(["Alpha", "Gamma", "Beta"], screen.Games.Select(each => each.Name));
        Assert.Equal(["Alpha", "Gamma", "Beta"], screen.AsAProject().Games.Select(each => each.Name));
        Assert.Same(screen.WhatGoesOnIt[1], moved);
    }

    [Fact]
    public void A_game_moved_down_does_the_same_the_other_way()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[0];

        screen.MoveTheGame(1);

        Assert.Equal(["Beta", "Alpha", "Gamma"], screen.AsAProject().Games.Select(each => each.Name));
    }

    [Fact]
    public void Its_add_ons_go_with_it_and_nobody_elses_do()
    {
        // Alpha has a DLC and Gamma has a patch. Whatever the order, each
        // still names its own game.
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[0];

        screen.MoveTheGame(1);
        screen.MoveTheGame(1);

        DiscProject project = screen.AsAProject();

        Assert.Equal(["Beta", "Gamma", "Alpha"], project.Games.Select(each => each.Name));
        Assert.Equal(
            [("Alpha", "setup_alpha_dlc.exe"), ("Gamma", "patch_gamma_1_to_2.exe")],
            project.AddOnsOrNone
                .Select(each => (project.Games[each.BelongsTo].Name, Path.GetFileName(each.Path)))
                .Order());
    }

    [Fact]
    public void A_tick_after_a_move_still_answers_for_the_right_game()
    {
        // The ticks rebuild the disc from the lists kept as they were found.
        // If only the list shown had moved, the next tick would put the old
        // order back, or worse, mix the two.
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[0];
        screen.MoveTheGame(1);

        AThingOnTheDisc alpha = screen.WhatGoesOnIt[1];
        alpha.IsOn = false;

        DiscProject project = screen.AsAProject();

        Assert.Equal(["Beta", "Alpha", "Gamma"], screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Equal("Gamma", project.Games[^1].Name);
        Assert.All(project.AddOnsOrNone, each => Assert.Equal("Gamma", project.Games[each.BelongsTo].Name));
        Assert.Single(project.AddOnsOrNone);
    }

    [Fact]
    public void Everything_is_still_its_own_after_a_move_and_a_tick_taken_off_and_put_back()
    {
        // Two lists are kept as the games were found, and every tick
        // rebuilds the disc from them. A fault planted in either, so that
        // it stayed in the old order, passed every other test here: the
        // window looked right and the next tick gave Alpha Beta's files.
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[0];
        screen.MoveTheGame(1);

        foreach (AThingOnTheDisc game in screen.WhatGoesOnIt.ToArray())
        {
            foreach (AThingOnTheDisc row in game.EveryThing().Where(each => !each.IsAWholeGame).ToArray())
            {
                row.IsOn = false;
                row.IsOn = true;
            }
        }

        DiscProject project = screen.AsAProject();

        Assert.Equal(
            [
                ("Beta", @"C:\games\Beta", "setup_Beta.exe", 2),
                ("Alpha", @"C:\games\Alpha", "setup_Alpha.exe", 2),
                ("Gamma", @"C:\games\Gamma", "setup_Gamma.exe", 2),
            ],
            project.Games.Select(each => (each.Name, each.Path, each.Installer!.Name, each.Files.Count)));
        Assert.Equal(
            ["Alpha has setup_alpha_dlc.exe", "Gamma has patch_gamma_1_to_2.exe"],
            project.AddOnsOrNone
                .Select(each => $"{project.Games[each.BelongsTo].Name} has {Path.GetFileName(each.Path)}")
                .Order(StringComparer.Ordinal));

        // And what the list shows under each game is that game's.
        Assert.Equal(
            ["Beta", "Alpha", "Gamma"],
            screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Contains(screen.WhatGoesOnIt[1].EveryThing(), each => each.ItsPath.Contains("alpha_dlc", StringComparison.Ordinal));
        Assert.DoesNotContain(screen.WhatGoesOnIt[0].EveryThing(), each => each.ItsPath.Contains("dlc", StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_inside_a_game_moves_the_game_it_is_in()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[1].EveryThing().First(each => !each.IsAWholeGame);

        screen.MoveTheGame(-1);

        Assert.Equal(["Beta", "Alpha", "Gamma"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void The_game_stays_picked_so_it_can_be_walked_all_the_way()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];

        screen.MoveTheGame(-1);
        screen.MoveTheGame(-1);

        Assert.Equal(["Gamma", "Alpha", "Beta"], screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Same(screen.WhatGoesOnIt[0], screen.WhatIsSelected);
        Assert.Equal("Gamma", screen.ThePickedGame);
    }

    [Fact]
    public void The_top_cannot_go_up_nor_the_bottom_down_and_trying_changes_nothing()
    {
        MakeADisc screen = ThreeGames();

        screen.WhatIsSelected = screen.WhatGoesOnIt[0];
        Assert.False(screen.CanMoveTheGameUp);
        Assert.True(screen.CanMoveTheGameDown);
        Assert.Null(screen.MoveTheGame(-1));

        screen.WhatIsSelected = screen.WhatGoesOnIt[2];
        Assert.True(screen.CanMoveTheGameUp);
        Assert.False(screen.CanMoveTheGameDown);
        Assert.Null(screen.MoveTheGame(1));

        screen.WhatIsSelected = screen.WhatGoesOnIt[1];
        Assert.True(screen.CanMoveTheGameUp);
        Assert.True(screen.CanMoveTheGameDown);

        Assert.Equal(["Alpha", "Beta", "Gamma"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Nothing_picked_moves_nothing()
    {
        MakeADisc screen = ThreeGames();

        Assert.False(screen.CanMoveTheGameUp);
        Assert.False(screen.CanMoveTheGameDown);
        Assert.Null(screen.MoveTheGame(1));
        Assert.Null(screen.MoveTheGame(0));
        Assert.Equal(["Alpha", "Beta", "Gamma"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Only_the_rows_that_changed_places_are_given_to_the_tree_again()
    {
        // A list of forty redraws two, and a game that did not move keeps
        // whatever of it was open.
        MakeADisc screen = ThreeGames();
        AThingOnTheDisc alpha = screen.WhatGoesOnIt[0];
        List<System.Collections.Specialized.NotifyCollectionChangedEventArgs> changes = [];
        ((System.Collections.Specialized.INotifyCollectionChanged)screen.WhatGoesOnIt).CollectionChanged +=
            (_, change) => changes.Add(change);
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];

        screen.MoveTheGame(-1);

        Assert.Equal(2, changes.Count);
        Assert.Same(alpha, screen.WhatGoesOnIt[0]);
    }

    [Fact]
    public void The_window_hears_that_the_buttons_and_the_picked_game_changed()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[1];
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.MoveTheGame(-1);

        foreach (string named in new[]
                 {
                     nameof(MakeADisc.CanMoveTheGameUp), nameof(MakeADisc.CanMoveTheGameDown),
                     nameof(MakeADisc.CanSortTheGames), nameof(MakeADisc.ThePickedGame),
                     nameof(MakeADisc.CanTakeSomethingOff),
                 })
        {
            Assert.Contains(named, told, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Picking_a_row_tells_the_window_which_way_it_can_go()
    {
        MakeADisc screen = ThreeGames();
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        screen.WhatIsSelected = screen.WhatGoesOnIt[0];

        Assert.Contains(nameof(MakeADisc.CanMoveTheGameUp), told, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.CanMoveTheGameDown), told, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------ sorting

    [Fact]
    public void Sorting_puts_the_games_in_order_by_name_with_their_add_ons()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Witcher"), [ADlc("setup_witcher_dlc.exe")]);
        screen.Add(AGame("alan wake"));
        screen.Add(AGame("Hollow Knight"), [ADlc("patch_hollow_1_to_2.exe")]);

        Assert.True(screen.CanSortTheGames);

        screen.SortTheGames();

        DiscProject project = screen.AsAProject();

        Assert.Equal(["alan wake", "Hollow Knight", "Witcher"], project.Games.Select(each => each.Name));
        Assert.Equal(["alan wake", "Hollow Knight", "Witcher"], screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Equal(
            [("Hollow Knight", "patch_hollow_1_to_2.exe"), ("Witcher", "setup_witcher_dlc.exe")],
            project.AddOnsOrNone.Select(each => (project.Games[each.BelongsTo].Name, Path.GetFileName(each.Path))));

        // Sorted, the list of ways is still there, and the way just used
        // has nothing left to do.
        Assert.True(screen.CanSortTheGames);
        Assert.Null(screen.SortTheGames());
    }

    [Fact]
    public void A_number_in_a_name_is_read_as_a_number()
    {
        MakeADisc screen = TheScreen();

        foreach (string name in new[] { "Gothic 10", "Gothic 2", "Gothic" })
        {
            screen.Add(AGame(name));
        }

        screen.SortTheGames();

        Assert.Equal(["Gothic", "Gothic 2", "Gothic 10"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Two_games_of_one_name_stay_in_the_order_they_were_in()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Zork"));
        screen.Add(AGame("Doom", @"C:\games\first"));
        screen.Add(AGame("doom", @"C:\games\second"));

        screen.SortTheGames();

        Assert.Equal(
            [@"C:\games\first", @"C:\games\second", @"C:\games\Zork"],
            screen.Games.Select(each => each.Path));
    }

    [Fact]
    public void A_renamed_game_is_sorted_by_the_name_it_was_given()
    {
        MakeADisc screen = ThreeGames();
        screen.FinishRenaming(screen.WhatGoesOnIt[0], "Zeta");

        Assert.True(screen.CanSortTheGames);

        screen.SortTheGames();

        Assert.Equal(["Beta", "Gamma", "Zeta"], screen.AsAProject().Games.Select(each => each.Name));
    }

    [Fact]
    public void There_is_nothing_to_sort_on_an_empty_list_or_one_game_and_a_list_in_order_is_left_alone()
    {
        MakeADisc screen = TheScreen();
        Assert.False(screen.CanSortTheGames);
        Assert.Null(screen.SortTheGames());

        screen.Add(AGame("Beta"));
        Assert.False(screen.CanSortTheGames);

        // Two games: the list of ways is offered, and a way that changes
        // nothing does nothing.
        screen.Add(AGame("Gamma"));
        Assert.True(screen.CanSortTheGames);
        Assert.Null(screen.SortTheGames());
        Assert.NotNull(screen.SortTheGames(HowToSort.ByNameZToA));
    }

    // ------------------------------------------------- the ways to sort

    [Fact]
    public void The_list_offers_four_ways_by_name_first()
    {
        MakeADisc screen = TheScreen();

        Assert.Equal(
            ["By name, A to Z", "By name, Z to A", "By size, biggest first", "By size, smallest first"],
            screen.TheWaysToSort.Select(each => each.InWords));
        Assert.Equal(Enum.GetValues<HowToSort>(), screen.TheWaysToSort.Select(each => each.How));
        Assert.All(screen.TheWaysToSort, each => Assert.Equal(each.InWords, each.ToString()));
    }

    [Theory]
    [InlineData(HowToSort.ByNameAToZ, "Alpha,Beta,Gamma")]
    [InlineData(HowToSort.ByNameZToA, "Gamma,Beta,Alpha")]
    [InlineData(HowToSort.BiggestFirst, "Beta,Gamma,Alpha")]
    [InlineData(HowToSort.SmallestFirst, "Alpha,Gamma,Beta")]
    public void Each_way_puts_them_in_its_order_with_what_is_theirs(HowToSort how, string order)
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Gamma", bytes: 3000), [ADlc("patch_gamma_1_to_2.exe")]);
        screen.Add(AGame("Alpha", bytes: 1000), [ADlc("setup_alpha_dlc.exe")]);
        screen.Add(AGame("Beta", bytes: 9000));

        screen.SortTheGames(how);

        DiscProject project = screen.AsAProject();

        Assert.Equal(order.Split(','), project.Games.Select(each => each.Name));
        Assert.Equal(order.Split(','), screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Equal(
            ["Alpha has setup_alpha_dlc.exe", "Gamma has patch_gamma_1_to_2.exe"],
            project.AddOnsOrNone
                .Select(each => $"{project.Games[each.BelongsTo].Name} has {Path.GetFileName(each.Path)}")
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void By_size_is_by_what_the_list_shows_beside_the_game_add_ons_and_all()
    {
        // Small has the smaller installer and a DLC that makes it the
        // bigger of the two on the disc, which is what somebody is looking
        // at when they ask for the biggest first.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Large", bytes: 5000));
        screen.Add(AGame("Small", bytes: 1000), [new LeftBehind(new ListedFile("setup_small_dlc.exe", 90_000), LeftBehindKind.LooksLikeAnAddOn)]);

        screen.SortTheGames(HowToSort.BiggestFirst);

        Assert.Equal(["Small", "Large"], screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.True(screen.WhatGoesOnIt[0].Bytes > screen.WhatGoesOnIt[1].Bytes);
    }

    [Fact]
    public void Two_of_one_size_stay_in_the_order_they_were_in_either_way()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Second", bytes: 500));
        screen.Add(AGame("First", bytes: 500));
        screen.Add(AGame("Big", bytes: 9000));

        screen.SortTheGames(HowToSort.SmallestFirst);
        Assert.Equal(["Second", "First", "Big"], screen.WhatGoesOnIt.Select(each => each.Name));

        screen.SortTheGames(HowToSort.BiggestFirst);
        Assert.Equal(["Big", "Second", "First"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    // -------------------------------------------- an order somebody arranged

    [Fact]
    public void Sorting_an_order_nobody_arranged_asks_nothing()
    {
        MakeADisc screen = ThreeGames();

        Assert.All(Enum.GetValues<HowToSort>(), how => Assert.Null(screen.WhatSortingWouldLose(how)));
    }

    [Fact]
    public void Sorting_after_a_game_was_moved_by_hand_asks_first_and_the_likely_answer_sorts()
    {
        // The owner, on the recording: a sort overrides the order arranged
        // by dragging, so there should be a warning first.
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];
        screen.MoveTheGame(-1);

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatSortingWouldLose(HowToSort.ByNameAToZ));

        Assert.Equal("Sort the games, and lose the order you put them in?", asked.Headline);
        Assert.Contains("an order of your own", asked.Because, StringComparison.Ordinal);
        Assert.Equal("Sort them", asked.TheLikelyOne);
        Assert.Equal("Keep my order", asked.TheOtherWay);

        // Asking sorts nothing.
        Assert.Equal(["Alpha", "Gamma", "Beta"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void A_drag_counts_as_arranging_too()
    {
        MakeADisc screen = ThreeGames();

        screen.MoveTheGameTo(screen.WhatGoesOnIt[0], screen.WhatGoesOnIt[2], after: true);

        Assert.NotNull(screen.WhatSortingWouldLose(HowToSort.ByNameAToZ));
    }

    [Fact]
    public void A_sort_that_would_change_nothing_loses_nothing_and_asks_nothing()
    {
        // Moved up and back down again: by name they are as they were.
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];
        screen.MoveTheGame(-1);
        screen.MoveTheGame(1);

        Assert.Null(screen.WhatSortingWouldLose(HowToSort.ByNameAToZ));
        Assert.NotNull(screen.WhatSortingWouldLose(HowToSort.ByNameZToA));
    }

    [Fact]
    public void Once_sorted_the_order_is_nobodys_own_and_the_next_sort_asks_nothing()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];
        screen.MoveTheGame(-1);

        screen.SortTheGames(HowToSort.ByNameZToA);

        Assert.All(Enum.GetValues<HowToSort>(), how => Assert.Null(screen.WhatSortingWouldLose(how)));
    }

    [Fact]
    public void With_one_game_left_there_is_no_order_to_be_anybodys()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];
        screen.MoveTheGame(-1);

        screen.Remove(screen.Games[0]);
        screen.Remove(screen.Games[0]);
        screen.Add(AGame("Aardvark"));

        Assert.Equal(2, screen.WhatGoesOnIt.Count);
        Assert.All(Enum.GetValues<HowToSort>(), how => Assert.Null(screen.WhatSortingWouldLose(how)));
    }

    // ----------------------------------------------------- dragging a game

    [Theory]
    // Alpha, Beta, Gamma, and where each drag leaves them.
    [InlineData(0, 2, true, "Beta,Gamma,Alpha")]
    [InlineData(0, 2, false, "Beta,Alpha,Gamma")]
    [InlineData(0, 1, true, "Beta,Alpha,Gamma")]
    [InlineData(2, 0, false, "Gamma,Alpha,Beta")]
    [InlineData(2, 0, true, "Alpha,Gamma,Beta")]
    [InlineData(2, 1, false, "Alpha,Gamma,Beta")]
    [InlineData(1, 0, false, "Beta,Alpha,Gamma")]
    [InlineData(1, 2, true, "Alpha,Gamma,Beta")]
    public void A_game_let_go_over_another_goes_before_it_or_after_it(int moved, int onto, bool after, string order)
    {
        MakeADisc screen = ThreeGames();
        AThingOnTheDisc game = screen.WhatGoesOnIt[moved];

        AThingOnTheDisc? picked = screen.MoveTheGameTo(game, screen.WhatGoesOnIt[onto], after);

        Assert.Equal(order.Split(','), screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Equal(order.Split(','), screen.AsAProject().Games.Select(each => each.Name));
        Assert.Same(game, picked);
        Assert.Same(game, screen.WhatIsSelected);

        DiscProject project = screen.AsAProject();

        Assert.Equal(
            ["Alpha has setup_alpha_dlc.exe", "Gamma has patch_gamma_1_to_2.exe"],
            project.AddOnsOrNone
                .Select(each => $"{project.Games[each.BelongsTo].Name} has {Path.GetFileName(each.Path)}")
                .Order(StringComparer.Ordinal));
    }

    [Theory]
    // Let go where it already is: on itself, just before the one after it,
    // just after the one before it.
    [InlineData(1, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(1, 0, true)]
    public void A_game_let_go_where_it_already_is_moves_nothing_and_arranges_nothing(int moved, int onto, bool after)
    {
        MakeADisc screen = ThreeGames();
        List<string> told = [];
        screen.PropertyChanged += (_, changed) => told.Add(changed.PropertyName!);

        Assert.Null(screen.MoveTheGameTo(screen.WhatGoesOnIt[moved], screen.WhatGoesOnIt[onto], after));

        Assert.Equal(["Alpha", "Beta", "Gamma"], screen.WhatGoesOnIt.Select(each => each.Name));
        Assert.Empty(told);
        Assert.Null(screen.WhatSortingWouldLose(HowToSort.ByNameZToA));
    }

    [Fact]
    public void Any_row_of_a_game_carries_the_game_and_any_row_of_another_is_that_game()
    {
        MakeADisc screen = ThreeGames();
        AThingOnTheDisc insideAlpha = screen.WhatGoesOnIt[0].EveryThing().First(each => !each.IsAWholeGame);
        AThingOnTheDisc insideGamma = screen.WhatGoesOnIt[2].EveryThing().Last(each => !each.IsAWholeGame);

        screen.MoveTheGameTo(insideAlpha, insideGamma, after: true);

        Assert.Equal(["Beta", "Gamma", "Alpha"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Nothing_and_a_row_of_no_game_on_the_list_move_nothing()
    {
        MakeADisc screen = ThreeGames();
        MakeADisc another = ThreeGames();

        Assert.Null(screen.MoveTheGameTo(null, screen.WhatGoesOnIt[0], after: true));
        Assert.Null(screen.MoveTheGameTo(screen.WhatGoesOnIt[0], null, after: true));
        Assert.Null(screen.MoveTheGameTo(another.WhatGoesOnIt[0], screen.WhatGoesOnIt[2], after: true));
        Assert.Null(screen.MoveTheGameTo(screen.WhatGoesOnIt[0], another.WhatGoesOnIt[2], after: true));

        Assert.Equal(["Alpha", "Beta", "Gamma"], screen.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Everything_is_still_its_own_after_a_drag_and_a_tick_taken_off_and_put_back()
    {
        // The same danger a move has: the two lists kept as found have to
        // go with the games, or the next tick gives one game another's.
        MakeADisc screen = ThreeGames();
        screen.MoveTheGameTo(screen.WhatGoesOnIt[2], screen.WhatGoesOnIt[0], after: false);

        foreach (AThingOnTheDisc game in screen.WhatGoesOnIt.ToArray())
        {
            foreach (AThingOnTheDisc row in game.EveryThing().Where(each => !each.IsAWholeGame).ToArray())
            {
                row.IsOn = false;
                row.IsOn = true;
            }
        }

        DiscProject project = screen.AsAProject();

        Assert.Equal(
            [("Gamma", @"C:\games\Gamma"), ("Alpha", @"C:\games\Alpha"), ("Beta", @"C:\games\Beta")],
            project.Games.Select(each => (each.Name, each.Path)));
        Assert.Equal(
            ["Alpha has setup_alpha_dlc.exe", "Gamma has patch_gamma_1_to_2.exe"],
            project.AddOnsOrNone
                .Select(each => $"{project.Games[each.BelongsTo].Name} has {Path.GetFileName(each.Path)}")
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Sorting_keeps_the_picked_game_picked()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Gamma"));
        screen.Add(AGame("Alpha"));
        AThingOnTheDisc gamma = screen.WhatGoesOnIt[0];
        screen.WhatIsSelected = gamma;

        Assert.Same(gamma, screen.SortTheGames());
        Assert.Same(gamma, screen.WhatIsSelected);
        Assert.Same(gamma, screen.WhatGoesOnIt[1]);
    }

    // ------------------------------------------------- where the order reaches

    [Fact]
    public void A_saved_project_keeps_the_order_and_whose_each_add_on_is()
    {
        MakeADisc screen = ThreeGames();
        screen.WhatIsSelected = screen.WhatGoesOnIt[2];
        screen.MoveTheGame(-1);
        screen.MoveTheGame(-1);

        string written = TheProjectAsJson.Of(screen.AsAProject(), "2.0.5", "DVD5", @"C:\out", DateTimeOffset.UtcNow);
        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(written)).Value;

        ProjectEntry[] games = [.. back.Games.Where(each => !each.IsAnAddOn)];
        ProjectEntry[] addOns = [.. back.Entries.Where(each => each.IsAnAddOn)];

        Assert.Equal(["Gamma", "Alpha", "Beta"], games.Select(each => each.Name));

        // Each add-on names its game by the place the game has in the file.
        Assert.Equal(
            ["Alpha has setup_alpha_dlc.exe", "Gamma has patch_gamma_1_to_2.exe"],
            addOns
                .Select(each => $"{games[each.BelongsTo].Name} has {Path.GetFileName(each.Installer ?? each.Folder)}")
                .Order(StringComparer.Ordinal));
    }

    // ------------------------------------------------------------- the window

    [Fact]
    public void The_window_has_the_two_links_under_the_list_and_one_that_sorts()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement window = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

        System.Xml.Linq.XElement Named(string id) => Assert.Single(
            window.Descendants(xaml + "Button"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == id);

        // Grey at the ends, not gone, so the row does not jump about as a
        // game is walked up the list.
        Assert.Equal("{Binding CanMoveTheGameUp}", (string?)Named("MoveTheGameUp").Attribute("IsEnabled"));
        Assert.Equal("{Binding CanMoveTheGameDown}", (string?)Named("MoveTheGameDown").Attribute("IsEnabled"));
        Assert.Null(Named("MoveTheGameUp").Attribute("Visibility"));

        // The pair wrap together: in a panel of their own that does not
        // wrap, inside the line that does.
        Assert.Same(Named("MoveTheGameUp").Parent, Named("MoveTheGameDown").Parent);
        Assert.Equal(xaml + "StackPanel", Named("MoveTheGameUp").Parent!.Name);
        Assert.Equal("Horizontal", (string?)Named("MoveTheGameUp").Parent!.Attribute("Orientation"));
        Assert.Equal(xaml + "WrapPanel", Named("MoveTheGameUp").Parent!.Parent!.Name);

        // The ways to sort: a list, there with two games or more, saying
        // what it is for while nothing in it is chosen.
        System.Xml.Linq.XElement ways = Assert.Single(
            window.Descendants(xaml + "ComboBox"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "SortTheGames");

        Assert.Equal("{Binding TheWaysToSort}", (string?)ways.Attribute("ItemsSource"));
        Assert.Equal("SortTheGames", (string?)ways.Attribute("SelectionChanged"));
        Assert.Equal("Sort the games", (string?)ways.Attribute("AutomationProperties.Name"));
        Assert.Equal(
            "{Binding CanSortTheGames, Converter={StaticResource WhenSo}}",
            (string?)ways.Parent!.Attribute("Visibility"));
        Assert.Contains(
            ways.Parent.Elements(xaml + "TextBlock"),
            each => (string?)each.Attribute("Text") == "Sort the games"
                && (string?)each.Attribute("IsHitTestVisible") == "False");
    }

    [Fact]
    public void The_window_asks_before_a_sort_loses_an_order_and_empties_the_lists_choice_after()
    {
        string window = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string handler = window[window.IndexOf("private void SortTheGames(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("// ------------------------------------------------ dragging a game about", StringComparison.Ordinal)];

        // Sorted when there is nothing to lose, or when the answer is yes.
        Assert.Contains(
            "if (_screen.WhatSortingWouldLose(way.How) is not AQuestion losing || Ask(losing))",
            handler,
            StringComparison.Ordinal);
        Assert.Contains("KeepPicked(_screen.SortTheGames(way.How));", handler, StringComparison.Ordinal);

        // Emptied either way, after, so the same way can be chosen again.
        Assert.True(
            handler.IndexOf("WaysToSort.SelectedItem = null;", StringComparison.Ordinal)
                > handler.IndexOf("KeepPicked(_screen.SortTheGames(way.How));", StringComparison.Ordinal));
    }

    [Fact]
    public void The_window_lets_a_game_be_dragged_by_its_row_and_not_by_a_tick_a_cross_or_a_name_being_typed()
    {
        // No test can drag in a tree. This reads what the handlers do.
        string window = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("IsInside<System.Windows.Controls.Primitives.ButtonBase>(pressed)", window, StringComparison.Ordinal);
        Assert.Contains("IsInside<System.Windows.Controls.TextBox>(pressed)", window, StringComparison.Ordinal);
        Assert.Contains("_heldGame = MakeADisc.WhatADragCarries(row);", window, StringComparison.Ordinal);

        // Only once the pointer has travelled, so a click is still a click.
        Assert.Contains("SystemParameters.MinimumHorizontalDragDistance", window, StringComparison.Ordinal);
        Assert.Contains("DragDrop.DoDragDrop(Games, new DataObject(AGameBeingMoved, game), DragDropEffects.Move);", window, StringComparison.Ordinal);

        // Before the game it is let go over when over its upper half, and
        // an add-on by the half of the one row it is over.
        Assert.Contains(
            "KeepPicked(_screen.MoveTheGameTo(carried, onto, e.GetPosition(drawn).Y > drawn.ActualHeight / 2));",
            window, StringComparison.Ordinal);
        Assert.Contains(
            "KeepPicked(_screen.MoveTheAddOnTo(carried, onto, e.GetPosition(row).Y > row.ActualHeight / 2));",
            window, StringComparison.Ordinal);

        // The four-way arrow while it is carried, and never during a run.
        Assert.Contains("System.Windows.Input.Mouse.SetCursor(System.Windows.Input.Cursors.SizeAll);", window, StringComparison.Ordinal);
        Assert.Contains("e.Effects = !_screen.NothingIsRunning ? DragDropEffects.None", window, StringComparison.Ordinal);

        // No-entry wherever letting go would move nothing: the window asks
        // the screen while the drag is in the air, and again before it acts.
        Assert.Contains(
            "(_screen.MayBeLetGoOver(carried, e.OriginalSource is DependencyObject over ? TheRowAt(over) : null)",
            window, StringComparison.Ordinal);
        Assert.Contains("|| !_screen.MayBeLetGoOver(carried, onto))", window, StringComparison.Ordinal);

        // And only Move wears the arrow, so None is Windows' own no-entry.
        Assert.Contains("if (e.Effects.HasFlag(DragDropEffects.Move))", window, StringComparison.Ordinal);

        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement tree = Assert.Single(
            System.Xml.Linq.XDocument.Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml"))
                .Root!.Descendants(xaml + "TreeView"));

        Assert.Equal("AGameMayBeTakenHoldOf", (string?)tree.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("AGameIsDragged", (string?)tree.Attribute("PreviewMouseMove"));
        Assert.Equal("WearTheMovingPointer", (string?)tree.Attribute("GiveFeedback"));
    }

    [Fact]
    public void The_window_picks_the_moved_row_again_and_Alt_with_an_arrow_moves_it_too()
    {
        // The tree lets go of a row whose place is given to another, so a
        // game moved once had nothing picked and could not be moved again.
        // No test can see the tree; this reads what the handlers do.
        string window = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("=> KeepPicked(_screen.MoveTheGame(-1));", window, StringComparison.Ordinal);
        Assert.Contains("=> KeepPicked(_screen.MoveTheGame(1));", window, StringComparison.Ordinal);
        Assert.Contains("drawn.IsSelected = true;", window, StringComparison.Ordinal);
        Assert.Contains(
            "KeepPicked(_screen.MoveTheGame(e.SystemKey == System.Windows.Input.Key.Up ? -1 : 1));",
            window,
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Alpha with a DLC, Beta with nothing, Gamma with a patch.</summary>
    private static MakeADisc ThreeGames()
    {
        MakeADisc screen = TheScreen();

        screen.Add(AGame("Alpha"), [ADlc("setup_alpha_dlc.exe")]);
        screen.Add(AGame("Beta"));
        screen.Add(AGame("Gamma"), [ADlc("patch_gamma_1_to_2.exe")]);

        return screen;
    }

    private static LeftBehind ADlc(string file) => new(new ListedFile(file, 500), LeftBehindKind.LooksLikeAnAddOn);

    private static GameFolder AGame(string name, string? folder = null, long bytes = 1000)
    {
        ListedFile installer = new($"setup_{name.Replace(' ', '_')}.exe", bytes);
        ListedFile part = new($"setup_{name.Replace(' ', '_')}-1.bin", 4000);

        return new GameFolder(
            folder ?? @"C:\games\" + name, GameKind.GogInstaller, name, name, installer, [installer, part], [], null);
    }

    private static MakeADisc TheScreen() =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            new ADiskWithNothingOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesOfOneSize(512, 512),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

    private sealed class PicturesOfOneSize(int wide, int tall, bool readable = true) : IImages
    {
        public int TimesAsked { get; private set; }

        public ImageFacts Read(string path)
        {
            TimesAsked++;

            return new ImageFacts(readable, wide, tall, readable ? null : "It cannot be read.");
        }

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(
            string source, string destination, int width, int height, BackgroundFit how, KeptPart? kept = null)
        {
        }
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class ADiskWithNothingOnIt : IFileSystem
    {
        public bool FolderExists(string path) => false;

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }
}
