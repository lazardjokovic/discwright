using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// Several games that do not fit on one disc share a set: how it is planned,
/// and what a build of it puts where.
/// </summary>
/// <remarks>
/// <para>
/// The owner, 21 November 2026, with Alan Wake, Dead Space and Hollow Knight
/// on the window, 18.9 GB, and a disc of 8.5: "it says it needs 5 discs, it
/// should be 3". Each game got a disc or a set of its own, always, and no
/// disc's menu listed them all.
/// </para>
/// <para>
/// The sizes here are his, a megabyte for a byte, so the arithmetic that was
/// wrong on his window is the arithmetic that is checked.
/// </para>
/// </remarks>
public sealed class SeveralGamesOnOneSet : IDisposable
{
    private const long Overhead = 2L * 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwshared_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;

    public SeveralGamesOnOneSet()
    {
        _out = Path.Combine(_root, "out");
        Directory.CreateDirectory(_out);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------------ the plan

    [Fact]
    public void The_owners_three_games_come_to_three_discs_and_not_five()
    {
        PlannedBuild plan = Planned(TheOwnersThree(), room: 7920);

        PlannedOutput set = Assert.Single(plan.Outputs);

        Assert.Equal(3, plan.Discs);
        Assert.Equal("3 games on a set of 3 discs", plan.InWords);
        Assert.Equal("THREE GAMES", set.Label);
        Assert.False(set.InItsOwnFolder);
        Assert.Equal(3, set.Games.Count);
        Assert.True(set.Layout.EachGamesOwnIsPlaced);
    }

    [Fact]
    public void And_five_when_a_disc_for_each_game_is_asked_for()
    {
        DiscProject each = TheOwnersThree() with { Options = new DiscOptions(ADiscForEachGame: true) };

        PlannedBuild plan = Planned(each, room: 7920);

        Assert.Equal(3, plan.Outputs.Count);
        Assert.Equal(5, plan.Discs);
        Assert.Equal("3 games on 5 discs", plan.InWords);
        Assert.All(plan.Outputs, output => Assert.True(output.InItsOwnFolder));
        Assert.All(plan.Outputs, output => Assert.False(output.Layout.EachGamesOwnIsPlaced));
        Assert.Equal(string.Empty, plan.WhereEachGameGoes);
    }

    [Fact]
    public void Each_disc_is_filled_in_the_order_of_the_list_before_the_next_is_begun()
    {
        PlannedOutput set = Assert.Single(Planned(TheOwnersThree(), room: 7920).Outputs);

        Assert.Equal(
            [
                // Alan Wake's installer and both parts: 1 + 4000 + 3790.
                @"01 - Alan Wake\setup_alan_wake.exe|01 - Alan Wake\setup_alan_wake-1.bin|01 - Alan Wake\setup_alan_wake-2.bin",

                // Its own extras, which did not fit after them, then Dead
                // Space as far as it goes: 1210 + 28 + 3910.
                @"01 - Alan Wake\Extras\alan_wake_extras.zip|02 - Dead Space\setup_dead_space.exe|02 - Dead Space\setup_dead_space-1.bin",

                // The rest of it, its own, and all of Hollow Knight.
                @"02 - Dead Space\setup_dead_space-2.bin|02 - Dead Space\setup_dead_space-3.bin|02 - Dead Space\Manual\deadspace_manual.zip"
                    + @"|03 - Hollow Knight\setup_hollow_knight.exe|03 - Hollow Knight\Add-ons\patch_hollow_knight_1_to_2.exe"
                    + @"|03 - Hollow Knight\Add-ons\patch_hollow_knight_2_to_3.exe",
            ],
            set.Layout.Discs.Select(disc => string.Join("|", disc.Files.Select(file => file.RelativePath))));

        Assert.Equal([7791L, 5148L, 6018L], set.Layout.Discs.Select(disc => disc.Bytes));
        Assert.All(set.Layout.Discs, disc => Assert.True(disc.Bytes <= 7920));
    }

    [Fact]
    public void It_says_which_games_land_on_which_disc()
    {
        PlannedBuild plan = Planned(TheOwnersThree(), room: 7920);
        PlannedOutput set = plan.Outputs[0];

        Assert.Equal(
            "Disc 1: Alan Wake. Disc 2: Alan Wake, Dead Space. Disc 3: Dead Space, Hollow Knight.",
            plan.WhereEachGameGoes);

        Assert.Equal([1, 2], set.DiscsOf(set.Games[0]));
        Assert.Equal([2, 3], set.DiscsOf(set.Games[1]));
        Assert.Equal([3], set.DiscsOf(set.Games[2]));
        Assert.Equal(["Alan Wake", "Dead Space"], set.GamesOn(set.Layout.Discs[1]).Select(game => game.Name));
    }

    [Fact]
    public void Moving_a_game_in_the_list_moves_it_on_the_discs()
    {
        DiscProject three = TheOwnersThree();
        DiscProject knightFirst = three with { Games = [three.Games[2], three.Games[0], three.Games[1]] };

        // Its add-ons know their game by where it sits, so they move with it.
        knightFirst = knightFirst with { AddOns = [.. three.AddOnsOrNone.Select(addOn => addOn with { BelongsTo = 0 })] };

        PlannedBuild plan = Planned(knightFirst, room: 7920);

        Assert.StartsWith("Disc 1: Hollow Knight, Alan Wake.", plan.WhereEachGameGoes, StringComparison.Ordinal);
        Assert.Equal(
            @"01 - Hollow Knight\setup_hollow_knight.exe",
            plan.Outputs[0].Layout.Discs[0].Files[0].RelativePath);
    }

    [Fact]
    public void A_games_add_ons_come_straight_after_it_and_its_own_after_them()
    {
        GameFolder first = AGame("First", 100) with { Own = [AnOwn(@"C:\own\first book.pdf", GameExtraKind.Manual)] };
        GameFolder second = AGame("Second", 100);

        DiscProject project = new("TWO", [first, second], new DiscOptions())
        {
            AddOns =
            [
                new AddOn(1, @"C:\addons\patch_second_1_to_2.exe", "Update 2", 10),
                new AddOn(0, @"C:\addons\setup_first_dlc.exe", "First DLC", 10,
                    [new AddOnPart(@"C:\addons\setup_first_dlc-1.bin", 10)]),
            ]
        };

        PlanRequest asked = WhatWouldBeMade.RequestForASet(project, ADisc(1000), 0, _ => 7);

        Assert.Equal(
            [
                @"01 - First\setup_first.exe",
                @"01 - First\Add-ons\setup_first_dlc.exe",
                @"01 - First\Add-ons\setup_first_dlc-1.bin",
                @"01 - First\Manual\first book.pdf",
                @"02 - Second\setup_second.exe",
                @"02 - Second\Add-ons\patch_second_1_to_2.exe",
            ],
            asked.Files.Select(file => file.RelativePath));

        // Only a game's own goes on whole, and it is priced by what is asked
        // of the disk, as everything of its kind is.
        Assert.Equal([false, false, false, true, false, false], asked.Files.Select(file => file.KeepWhole));
        Assert.Equal(7, asked.Files[3].Bytes);
        Assert.Equal(@"C:\own\first book.pdf", asked.Files[3].Source);
        Assert.True(asked.EachGamesOwnIsPlaced);
    }

    [Fact]
    public void A_games_own_goes_on_once_and_the_discs_own_rides_every_disc()
    {
        GameFolder first = AGame("First", 600) with { Own = [AnOwn(@"C:\own\artbook", GameExtraKind.Extra)] };
        GameFolder second = AGame("Second", 600);

        DiscProject project = new("TWO", [first, second], new DiscOptions())
        {
            Extras = new DiscExtras(@"C:\disc\manual.pdf", null, [])
        };

        // The disc's manual is 100 and the game's artbook 300.
        long SizeOf(string path) => path.EndsWith("manual.pdf", StringComparison.Ordinal) ? 100 : 300;

        PlanRequest asked = WhatWouldBeMade.RequestForASet(project, ADisc(1000), 0, SizeOf);

        // What rides every disc is the disc's own alone. The artbook is a
        // thing of the game's, among its files.
        Assert.Equal(100, asked.BytesForTheExtras);

        PlannedOutput set = Assert.Single(Planned(project, room: 1000, SizeOf).Outputs);

        // 900 a disc beside the manual: First and its artbook, then Second.
        Assert.Equal(2, set.Layout.Discs.Count);
        Assert.True(set.Layout.ExtrasOnEveryDisc);

        PlannedFile artbook = Assert.Single(set.Layout.Discs.SelectMany(disc => disc.Files), file => file.GoesWhole);

        Assert.Equal(@"01 - First\Extras\artbook", artbook.RelativePath);
        Assert.Equal(300, artbook.Bytes);
        Assert.Null(artbook.Part);
        Assert.Contains(artbook, set.Layout.Discs[0].Files);
    }

    [Fact]
    public void One_game_on_a_set_of_its_own_is_planned_as_it_always_was()
    {
        GameFolder game = AGame("Big", 600, 600, 600) with { Own = [AnOwn(@"C:\own\artbook", GameExtraKind.Extra)] };

        PlannedOutput set = Assert.Single(Planned(new DiscProject("BIG", [game], new DiscOptions()), room: 1000, _ => 100).Outputs);

        // Its own rides every disc, so the buttons for it work whichever is
        // in, and nothing of it is among the files.
        Assert.False(set.Layout.EachGamesOwnIsPlaced);
        Assert.True(set.Layout.ExtrasOnEveryDisc);
        Assert.Equal(3, set.Layout.Discs.Count);
        Assert.DoesNotContain(set.Layout.Discs.SelectMany(disc => disc.Files), file => file.GoesWhole);
    }

    [Fact]
    public void Games_that_fit_on_one_disc_are_one_disc_and_no_set()
    {
        PlannedBuild plan = Planned(new DiscProject("TWO", [AGame("First", 400), AGame("Second", 400)], new DiscOptions()), room: 1000);

        PlannedOutput one = Assert.Single(plan.Outputs);

        Assert.Equal("one disc", plan.InWords);
        Assert.Single(one.Layout.Discs);
        Assert.False(one.Layout.EachGamesOwnIsPlaced);
        Assert.Equal(string.Empty, plan.WhereEachGameGoes);

        // Asked for a disc each, they are still one disc: there is nothing
        // to give each a disc for when one holds them.
        PlannedBuild asked = Planned(
            new DiscProject("TWO", [AGame("First", 400), AGame("Second", 400)], new DiscOptions(ADiscForEachGame: true)), room: 1000);

        Assert.Equal("one disc", asked.InWords);
    }

    [Fact]
    public void A_games_own_that_is_bigger_than_a_disc_is_refused_and_never_cut()
    {
        GameFolder first = AGame("First", 600) with { Own = [AnOwn(@"C:\own\the whole artbook", GameExtraKind.Extra)] };

        DiscProject project = new("TWO", [first, AGame("Second", 600)], new DiscOptions(CutFiles: true));

        Refusal refusal = Assert.IsType<Outcome<PlannedBuild>.No>(
            WhatWouldBeMade.For(project, ADisc(1000), _ => 5000)).Refusal;

        Assert.Equal("A game's manual or extras is bigger than a whole disc", refusal.Headline);
        Assert.Contains("the whole artbook is ", refusal.Because, StringComparison.Ordinal);
        Assert.Contains("whole or not at all", refusal.Because, StringComparison.Ordinal);
        Assert.Equal(RemedyKind.ChangeMedium, refusal.Remedy!.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_file_bigger_than_a_disc_is_cut_when_that_is_allowed_and_refused_when_not(bool mayCut)
    {
        DiscProject project = new(
            "TWO", [AGame("First", 400), AGame("Vast", 100, 2500)], new DiscOptions(CutFiles: mayCut));

        Outcome<PlannedBuild> planned = WhatWouldBeMade.For(project, ADisc(1000), _ => 0);

        if (!mayCut)
        {
            Assert.Equal(
                "One file is bigger than a whole disc",
                Assert.IsType<Outcome<PlannedBuild>.No>(planned).Refusal.Headline);

            return;
        }

        PlannedOutput set = Assert.Single(Assert.IsType<Outcome<PlannedBuild>.Ok>(planned).Value.Outputs);

        // 400 and 100 on the first, then the 2500 in three pieces of a disc
        // each, the last sharing with nothing because nothing follows it.
        Assert.Equal(4, set.Layout.Discs.Count);
        Assert.Equal(
            [@"02 - Vast\setup_vast-1.bin.dwpart01", @"02 - Vast\setup_vast-1.bin.dwpart02", @"02 - Vast\setup_vast-1.bin.dwpart03"],
            set.Layout.Discs.Skip(1).Select(disc => Assert.Single(disc.Files).RelativePath));
        Assert.Equal([2, 3, 4], set.DiscsOf(set.Games[1]).Where(disc => disc > 1));
    }

    [Fact]
    public void The_planner_keeps_a_whole_thing_whole_and_says_so_in_what_it_hands_back()
    {
        PlanRequest asked = new(
            [new PlanFile("a.bin", @"C:\a", 700), new PlanFile(@"Extras\book", @"C:\book", 400, KeepWhole: true), new PlanFile("b.bin", @"C:\b", 100)],
            ADisc(1000),
            Overhead,
            0,
            MayCutFiles: true,
            EachGamesOwnIsPlaced: true);

        DiscLayout layout = Assert.IsType<Outcome<DiscLayout>.Ok>(DiscPlanner.Plan(asked)).Value;

        Assert.True(layout.EachGamesOwnIsPlaced);
        Assert.Equal(2, layout.Discs.Count);
        Assert.Equal([false, true, false], layout.Discs.SelectMany(disc => disc.Files).Select(file => file.GoesWhole));

        // It moved to the next disc whole, and what follows shares with it.
        Assert.Equal([@"Extras\book", "b.bin"], layout.Discs[1].Files.Select(file => file.RelativePath));

        // A request that says nothing of it gets a layout that says nothing.
        Assert.False(Assert.IsType<Outcome<DiscLayout>.Ok>(DiscPlanner.Plan(asked with { EachGamesOwnIsPlaced = false })).Value.EachGamesOwnIsPlaced);
    }

    // ------------------------------------------------------- the project file

    [Fact]
    public void A_project_file_says_whether_each_game_gets_a_disc_and_one_that_says_nothing_means_no()
    {
        DiscProject each = new("TWO", [AGame("First", 400)], new DiscOptions(ADiscForEachGame: true));

        string saved = TheProjectAsJson.Of(each, "2.0.5", "DVD5", "out", DateTimeOffset.UnixEpoch);

        Assert.Contains("\"ADiscForEachGame\": true", saved, StringComparison.Ordinal);
        Assert.True(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.ADiscForEachGame);

        // A file from before there was a choice: its games share a set now.
        System.Text.Json.Nodes.JsonObject older = System.Text.Json.Nodes.JsonNode.Parse(saved)!.AsObject();
        Assert.True(older.Remove("ADiscForEachGame"));

        Assert.False(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(older.ToJsonString())).Value.Options.ADiscForEachGame);

        Assert.Contains(
            "\"ADiscForEachGame\": false",
            TheProjectAsJson.Of(each with { Options = new DiscOptions() }, "2.0.5", "DVD5", "out", DateTimeOffset.UnixEpoch),
            StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- the build

    [Fact]
    public async Task A_build_puts_the_discs_side_by_side_under_one_name_with_every_game_where_the_plan_said()
    {
        (DiscProject project, _) = ThreeOnTheDisk();

        BuiltDiscs built = await Build(project, room: 7920);

        Assert.Equal(3, built.Discs.Count);
        Assert.All(built.Discs, disc => Assert.Equal("THREE GAMES", disc.Label));
        Assert.All(built.Discs, disc => Assert.Equal(3, disc.OfDiscs));
        Assert.Equal(
            [Path.Combine(_out, "disc D1"), Path.Combine(_out, "disc D2"), Path.Combine(_out, "disc D3")],
            built.Discs.Select(disc => disc.Folder));

        // No folder for each game beside them: that is the other way.
        Assert.Equal(
            ["disc D1", "disc D2", "disc D3", TheProjectAsJson.FileName],
            Directory.GetFileSystemEntries(_out).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        Assert.Equal(
            ["setup_alan_wake-1.bin", "setup_alan_wake-2.bin", "setup_alan_wake.exe"],
            FilesUnder(built.Discs[0], "01 - Alan Wake"));
        Assert.False(Directory.Exists(Path.Combine(built.Discs[0].Folder, "02 - Dead Space")));

        Assert.Equal([@"Extras\alan_wake_extras.zip"], FilesUnder(built.Discs[1], "01 - Alan Wake"));
        Assert.Equal(["setup_dead_space-1.bin", "setup_dead_space.exe"], FilesUnder(built.Discs[1], "02 - Dead Space"));

        Assert.Equal(
            [@"Manual\deadspace_manual.zip", "setup_dead_space-2.bin", "setup_dead_space-3.bin"],
            FilesUnder(built.Discs[2], "02 - Dead Space"));
        Assert.Equal(
            [@"Add-ons\patch_hollow_knight_1_to_2.exe", @"Add-ons\patch_hollow_knight_2_to_3.exe", "setup_hollow_knight.exe"],
            FilesUnder(built.Discs[2], "03 - Hollow Knight"));
    }

    [Fact]
    public async Task Every_file_arrives_whole_and_no_game_file_is_on_two_discs()
    {
        (DiscProject project, Dictionary<string, string> sources) = ThreeOnTheDisk();

        BuiltDiscs built = await Build(project, room: 7920);

        Dictionary<string, int> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (BuiltDisc disc in built.Discs)
        {
            foreach (string file in Directory.EnumerateFiles(disc.Folder, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);

                if (!sources.TryGetValue(name, out string? source))
                {
                    continue;
                }

                seen[name] = seen.GetValueOrDefault(name) + 1;
                Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(file));
            }
        }

        // Every one of them, once.
        Assert.Equal(sources.Keys.Order(StringComparer.OrdinalIgnoreCase), seen.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(seen, each => Assert.Equal(1, each.Value));
    }

    [Fact]
    public async Task The_discs_own_manual_and_extras_are_on_every_disc_and_a_games_own_on_one()
    {
        (DiscProject project, _) = ThreeOnTheDisk();
        string manual = AFile("collection manual.pdf", 40);
        project = project with { Extras = new DiscExtras(manual, null, []) };

        BuiltDiscs built = await Build(project, room: 7960);

        Assert.Equal(3, built.Discs.Count);
        Assert.All(built.Discs, disc => Assert.True(Directory.EnumerateFiles(disc.Folder, "collection manual.pdf", SearchOption.AllDirectories).Any()));

        Assert.Equal(
            [false, true, false],
            built.Discs.Select(disc => Directory.EnumerateFiles(disc.Folder, "alan_wake_extras.zip", SearchOption.AllDirectories).Any()));
        Assert.Equal(
            [false, false, true],
            built.Discs.Select(disc => Directory.EnumerateFiles(disc.Folder, "deadspace_manual.zip", SearchOption.AllDirectories).Any()));
    }

    [Fact]
    public async Task A_folder_of_a_games_own_goes_on_whole_under_its_own_name()
    {
        string artbook = Path.Combine(_root, "src", "Artbook");
        Directory.CreateDirectory(Path.Combine(artbook, "sketches"));
        File.WriteAllBytes(Path.Combine(artbook, "cover.png"), new byte[30]);
        File.WriteAllBytes(Path.Combine(artbook, "sketches", "one.png"), new byte[20]);

        GameFolder first = OnTheDisk("First", 600) with { Own = [new GameExtra(artbook, GameExtraKind.Extra)] };
        DiscProject project = new("TWO", [first, OnTheDisk("Second", 600)], new DiscOptions());

        BuiltDiscs built = await Build(project, room: 1000);

        Assert.Equal(2, built.Discs.Count);

        string[] copies = [.. built.Discs.SelectMany(disc => Directory.EnumerateFiles(disc.Folder, "one.png", SearchOption.AllDirectories))];

        // Once, with its own shape, in the game's Extras.
        Assert.Equal(
            [Path.Combine(built.Discs[0].Folder, "01 - First", "Extras", "Artbook", "sketches", "one.png")],
            copies);
        Assert.True(File.Exists(Path.Combine(built.Discs[0].Folder, "01 - First", "Extras", "Artbook", "cover.png")));
    }

    [Fact]
    public async Task Every_disc_carries_the_sets_own_list_of_every_file_on_every_disc()
    {
        (DiscProject project, Dictionary<string, string> sources) = ThreeOnTheDisk();

        BuiltDiscs built = await Build(project, room: 7920);

        string[] lists = [.. built.Discs.Select(disc => File.ReadAllText(Path.Combine(disc.Folder, SetManifest.FileName)))];

        for (int at = 0; at < lists.Length; at++)
        {
            Assert.Contains($"disc {at + 1} of 3.", lists[at], StringComparison.OrdinalIgnoreCase);

            // Each of the games' files, with the disc it is on, on every
            // disc's copy of the list.
            Assert.Contains("*01 - Alan Wake/setup_alan_wake-2.bin", lists[at], StringComparison.Ordinal);
            Assert.Contains("*02 - Dead Space/setup_dead_space-1.bin", lists[at], StringComparison.Ordinal);
            Assert.Contains("*03 - Hollow Knight/Add-ons/patch_hollow_knight_2_to_3.exe", lists[at], StringComparison.Ordinal);
            Assert.Contains("*01 - Alan Wake/Extras/alan_wake_extras.zip", lists[at], StringComparison.Ordinal);
        }

        // Which disc, said by the first figure on its row.
        string RowOf(string path) => lists[0].Split('\n').Single(line => line.TrimEnd().EndsWith("*" + path, StringComparison.Ordinal));

        Assert.StartsWith("1 ", RowOf("01 - Alan Wake/setup_alan_wake-1.bin"), StringComparison.Ordinal);
        Assert.StartsWith("2 ", RowOf("02 - Dead Space/setup_dead_space-1.bin"), StringComparison.Ordinal);
        Assert.StartsWith("3 ", RowOf("02 - Dead Space/setup_dead_space-2.bin"), StringComparison.Ordinal);
        Assert.StartsWith("3 ", RowOf("03 - Hollow Knight/setup_hollow_knight.exe"), StringComparison.Ordinal);
        Assert.True(sources.Count > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_set_carries_its_lists_though_nobody_asked_for_a_list_of_what_every_file_should_be(int games)
    {
        // The set's own list is made from each disc's, so a set built with
        // that option off had none, and no disc of it could be installed.
        DiscProject project = games == 1
            ? new DiscProject("BIG", [OnTheDisk("Big", 600, 600, 600)], new DiscOptions(Checksums: false))
            : ThreeOnTheDisk().Project with { Options = new DiscOptions(Checksums: false) };

        BuiltDiscs built = await Build(project, room: games == 1 ? 1000 : 7920);

        Assert.True(built.Discs.Count > 1);
        Assert.All(built.Discs, disc => Assert.True(File.Exists(Path.Combine(disc.Folder, SetManifest.FileName))));
        Assert.All(built.Discs, disc => Assert.True(File.Exists(Path.Combine(disc.Folder, ChecksumList.FileName))));
    }

    [Fact]
    public async Task One_disc_built_with_that_option_off_still_carries_no_list()
    {
        BuiltDisc disc = Assert.Single((await Build(
            new DiscProject("SMALL", [OnTheDisk("Small", 100)], new DiscOptions(Checksums: false)), room: 1000)).Discs);

        Assert.False(File.Exists(Path.Combine(disc.Folder, ChecksumList.FileName)));
        Assert.False(File.Exists(Path.Combine(disc.Folder, SetManifest.FileName)));
    }

    [Fact]
    public async Task Every_discs_menu_lists_every_game_with_where_it_sits_and_what_is_apart_from_it()
    {
        (DiscProject project, _) = ThreeOnTheDisk();

        BuiltDiscs built = await Build(project, room: 7920);

        foreach (BuiltDisc disc in built.Discs)
        {
            string page = File.ReadAllText(Path.Combine(disc.Folder, "AUTORUN", "menu.hta"));

            Assert.Contains($"discNumber: {disc.Number},", page, StringComparison.Ordinal);
            Assert.Contains("discsInSet: 3,", page, StringComparison.Ordinal);
            Assert.Contains("game: \"THREE GAMES\"", page, StringComparison.Ordinal);

            Assert.Contains("name: \"Alan Wake\"", page, StringComparison.Ordinal);
            Assert.Contains("name: \"Dead Space\"", page, StringComparison.Ordinal);
            Assert.Contains("name: \"Hollow Knight\"", page, StringComparison.Ordinal);

            Assert.Contains("runs: \"01 - Alan Wake/setup_alan_wake.exe\", folder: \"01 - Alan Wake\"", page, StringComparison.Ordinal);
            Assert.Contains("apart: [\"01 - Alan Wake/Extras/alan_wake_extras.zip\"]", page, StringComparison.Ordinal);
            Assert.Contains("apart: [\"02 - Dead Space/Manual/deadspace_manual.zip\"]", page, StringComparison.Ordinal);
            Assert.Contains("folder: \"03 - Hollow Knight\"", page, StringComparison.Ordinal);
            Assert.Contains("runs: \"03 - Hollow Knight/Add-ons/patch_hollow_knight_1_to_2.exe\"", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_disc_that_stands_alone_and_a_set_of_one_game_say_nothing_is_apart()
    {
        GameFolder game = OnTheDisk("Big", 600, 600, 600) with { Own = [new GameExtra(AFile("artbook.zip", 50), GameExtraKind.Extra)] };

        BuiltDiscs set = await Build(new DiscProject("BIG", [game], new DiscOptions()), room: 1000);

        Assert.True(set.Discs.Count > 1);
        Assert.All(set.Discs, disc =>
            Assert.Contains("apart: []", File.ReadAllText(Path.Combine(disc.Folder, "AUTORUN", "menu.hta")), StringComparison.Ordinal));

        // And its own rides every disc of it, as it always did.
        Assert.All(set.Discs, disc => Assert.True(Directory.EnumerateFiles(disc.Folder, "artbook.zip", SearchOption.AllDirectories).Any()));
    }

    [Fact]
    public async Task What_a_build_says_as_it_goes_names_the_game_only_when_the_games_get_discs_of_their_own()
    {
        (DiscProject project, _) = ThreeOnTheDisk();
        List<string> said = [];

        await Build(project, room: 7920, new Hears(said));

        Assert.Contains(said, line => line.StartsWith("disc 1 of 3, step 1 of ", StringComparison.Ordinal));
        Assert.Contains(said, line => line.StartsWith("disc 3 of 3, step ", StringComparison.Ordinal));
        Assert.DoesNotContain(said, line => line.Contains("Alan Wake", StringComparison.Ordinal));

        said.Clear();
        Directory.Delete(_out, recursive: true);
        Directory.CreateDirectory(_out);

        await Build(project with { Options = new DiscOptions(ADiscForEachGame: true) }, room: 7920, new Hears(said));

        // "disc 1 of 2" twice over, for two different games, told nobody
        // which was being built.
        Assert.Contains(said, line => line.StartsWith("Alan Wake: disc 1 of 2, step 1 of ", StringComparison.Ordinal));
        Assert.Contains(said, line => line.StartsWith("Dead Space: disc 2 of 2, step ", StringComparison.Ordinal));
        Assert.Contains(said, line => line.StartsWith("Hollow Knight: step 1 of ", StringComparison.Ordinal));
        Assert.All(said, line => Assert.Matches("^(Alan Wake|Dead Space|Hollow Knight): ", line));
    }

    [Fact]
    public void The_words_a_step_says_are_as_they_were_with_no_game_named()
    {
        Assert.Equal("step 2 of 8, copying", new BuildProgress(2, 8, "copying").ToString());
        Assert.Equal("disc 2 of 3, step 2 of 8, copying", new BuildProgress(2, 8, "copying", 2, 3).ToString());
        Assert.Equal("Alan Wake: step 2 of 8, copying", new BuildProgress(2, 8, "copying", Of: "Alan Wake").ToString());
        Assert.Equal("step 2 of 8, copying", new BuildProgress(2, 8, "copying", Of: string.Empty).ToString());
    }

    [Fact]
    public void While_an_image_is_written_the_line_says_which_disc_of_the_set_it_is()
    {
        ImageProgress half = new(1, 2);

        Assert.Equal(
            "THREE GAMES, disc 2 of 3: writing the image, " + half.HowFar.ToString("P0", System.Globalization.CultureInfo.CurrentCulture),
            TheImageOf.BeingWritten(new BuiltDisc("THREE GAMES", 2, 3, @"C:\out\disc D2", 10), half));

        // One disc says whose it is, as it always did.
        Assert.Equal(
            "Alan Wake: " + half,
            TheImageOf.BeingWritten(new BuiltDisc("Alan Wake", 1, 1, @"C:\out\Alan Wake\disc", 10), half));
    }

    // ---------------------------------------------------------------- helpers

    private static Medium ADisc(long room) => new("TEST", "a test disc", Overhead + room);

    private static PlannedBuild Planned(DiscProject project, long room, Func<string, long>? sizeOf = null) =>
        Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(project, ADisc(room), sizeOf ?? TheOwnersSizes)).Value;

    private static long TheOwnersSizes(string path) =>
        path.EndsWith("alan_wake_extras.zip", StringComparison.Ordinal) ? 1210
        : path.EndsWith("deadspace_manual.zip", StringComparison.Ordinal) ? 3
        : 0;

    private static GameExtra AnOwn(string path, GameExtraKind kind) => new(path, kind);

    /// <summary>A GOG download that is only a list: an installer and its parts, of these sizes.</summary>
    private static GameFolder AGame(string name, params long[] sizes)
    {
        string tidied = name.ToLowerInvariant().Replace(' ', '_');

        ListedFile[] listed =
        [
            .. sizes.Select((bytes, at) => new ListedFile(at == 0 ? $"setup_{tidied}.exe" : $"setup_{tidied}-{at}.bin", bytes))
        ];

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, listed[0], listed, [], null);
    }

    /// <summary>
    /// The owner's three, a megabyte for a byte: Alan Wake with 1.2 GB of
    /// extras of its own, Dead Space with a manual, and Hollow Knight with
    /// two of its updates.
    /// </summary>
    private static DiscProject TheOwnersThree() =>
        new(
            "THREE GAMES",
            [
                AGame("Alan Wake", 1, 4000, 3790) with { Own = [AnOwn(@"C:\games\Alan Wake\alan_wake_extras.zip", GameExtraKind.Extra)] },
                AGame("Dead Space", 28, 3910, 3910, 293) with { Own = [AnOwn(@"C:\games\Dead Space\deadspace_manual.zip", GameExtraKind.Manual)] },
                AGame("Hollow Knight", 1160),
            ],
            new DiscOptions())
        {
            AddOns =
            [
                new AddOn(2, @"C:\games\Hollow Knight\patch_hollow_knight_1_to_2.exe", "Update 2", 620),
                new AddOn(2, @"C:\games\Hollow Knight\patch_hollow_knight_2_to_3.exe", "Update 3", 32),
            ]
        };

    // ------------------------------------------------- the same, on a real disk

    private string AFile(string name, int bytes)
    {
        string path = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] made = new byte[bytes];
        Random.Shared.NextBytes(made);
        File.WriteAllBytes(path, made);

        return path;
    }

    private GameFolder OnTheDisk(string name, params int[] sizes)
    {
        string tidied = name.ToLowerInvariant().Replace(' ', '_');
        string folder = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(folder);

        List<ListedFile> listed = [];

        for (int at = 0; at < sizes.Length; at++)
        {
            string file = at == 0 ? $"setup_{tidied}.exe" : $"setup_{tidied}-{at}.bin";
            byte[] made = new byte[sizes[at]];
            Random.Shared.NextBytes(made);
            File.WriteAllBytes(Path.Combine(folder, file), made);
            listed.Add(new ListedFile(file, sizes[at]));
        }

        return new GameFolder(folder, GameKind.GogInstaller, name, name, listed[0], listed, [], null);
    }

    /// <summary>The owner's three as real files of those sizes in bytes, and every one of them by name.</summary>
    private (DiscProject Project, Dictionary<string, string> Sources) ThreeOnTheDisk()
    {
        GameFolder wake = OnTheDisk("Alan Wake", 1, 4000, 3790);
        GameFolder space = OnTheDisk("Dead Space", 28, 3910, 3910, 293);
        GameFolder knight = OnTheDisk("Hollow Knight", 1160);

        string extras = AFile(@"Alan Wake\alan_wake_extras.zip", 1210);
        string manual = AFile(@"Dead Space\deadspace_manual.zip", 3);
        string two = AFile(@"Hollow Knight\patch_hollow_knight_1_to_2.exe", 620);
        string three = AFile(@"Hollow Knight\patch_hollow_knight_2_to_3.exe", 32);

        DiscProject project = new(
            "THREE GAMES",
            [
                wake with { Own = [new GameExtra(extras, GameExtraKind.Extra)] },
                space with { Own = [new GameExtra(manual, GameExtraKind.Manual)] },
                knight,
            ],
            new DiscOptions())
        {
            AddOns = [new AddOn(2, two, "Update 2", 620), new AddOn(2, three, "Update 3", 32)]
        };

        Dictionary<string, string> sources = new(StringComparer.OrdinalIgnoreCase);

        foreach (GameFolder game in new[] { wake, space, knight })
        {
            foreach (ListedFile file in game.Files)
            {
                sources[file.RelativePath] = Path.Combine(game.Path, file.RelativePath);
            }
        }

        foreach (string own in new[] { extras, manual, two, three })
        {
            sources[Path.GetFileName(own)] = own;
        }

        return (project, sources);
    }

    private async Task<BuiltDiscs> Build(DiscProject project, long room, IProgress<BuildProgress>? progress = null)
    {
        DiscBuilder builder = new(new WindowsFileSystem(), new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher());

        Outcome<BuiltDiscs> built = await builder.Build(project, ADisc(room), _out, progress, CancellationToken.None);

        return Assert.IsType<Outcome<BuiltDiscs>.Ok>(built).Value;
    }

    /// <summary>The files under one of a disc's folders, as paths from it, in order.</summary>
    private static string[] FilesUnder(BuiltDisc disc, string folder)
    {
        string under = Path.Combine(disc.Folder, folder);

        return
        [
            .. Directory.EnumerateFiles(under, "*", SearchOption.AllDirectories)
                .Select(file => file[(under.Length + 1)..])
                .Order(StringComparer.Ordinal)
        ];
    }

    /// <summary>Hears what a build says at once, on the thread that says it, so nothing is still on its way when the build ends.</summary>
    private sealed class Hears(List<string> said) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value)
        {
            lock (said)
            {
                said.Add(value.ToString());
            }
        }
    }
}
