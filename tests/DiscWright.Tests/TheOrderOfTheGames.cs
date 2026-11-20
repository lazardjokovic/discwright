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
        Assert.False(screen.CanSortTheGames);
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
    public void There_is_nothing_to_sort_on_an_empty_list_one_game_or_a_list_in_order()
    {
        MakeADisc screen = TheScreen();
        Assert.False(screen.CanSortTheGames);
        Assert.Null(screen.SortTheGames());

        screen.Add(AGame("Beta"));
        Assert.False(screen.CanSortTheGames);

        screen.Add(AGame("Gamma"));
        Assert.False(screen.CanSortTheGames);
        Assert.Null(screen.SortTheGames());

        screen.Add(AGame("Alpha"));
        Assert.True(screen.CanSortTheGames);
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

        // Gone when there is nothing for it to do.
        Assert.Equal(
            "{Binding CanSortTheGames, Converter={StaticResource WhenSo}}",
            (string?)Named("SortTheGames").Attribute("Visibility"));
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
        Assert.Contains("=> KeepPicked(_screen.SortTheGames());", window, StringComparison.Ordinal);
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

    private static GameFolder AGame(string name, string? folder = null)
    {
        ListedFile installer = new($"setup_{name.Replace(' ', '_')}.exe", 1000);
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
