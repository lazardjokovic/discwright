using DiscWright.Core;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// A big DLC arrives the way a big game does, an installer and its numbered
/// parts, and all of it goes wherever the installer goes.
/// </summary>
/// <remarks>
/// Reported against 2.0.4 on Reddit: add-ons were "being weird". An add-on
/// was one file, so a DLC's installer went into Add-ons without its parts,
/// where it cannot install, and its parts were asked about separately and
/// suggested as extras. Nothing here had ever described an add-on with
/// parts.
/// </remarks>
public class AnAddOnCarriesItsParts
{
    private const string Folder = @"C:\games\a game";

    // ------------------------------------------------- what was left behind

    [Fact]
    public void An_installer_left_behind_keeps_its_parts_and_they_are_not_asked_about()
    {
        IReadOnlyList<LeftBehind> left = LeftIn(
            ("setup_game.exe", 40),
            ("setup_game-1.bin", 900),
            ("setup_game_dlc.exe", 2),
            ("setup_game_dlc-1.bin", 700),
            ("setup_game_dlc-2.bin", 300),
            ("readme.txt", 1));

        Assert.Equal(["setup_game_dlc.exe", "readme.txt"], left.Select(each => each.Name));

        LeftBehind dlc = left[0];

        Assert.Equal(["setup_game_dlc-1.bin", "setup_game_dlc-2.bin"], dlc.Parts!.Select(part => part.Name));
        Assert.Equal(1002, dlc.Bytes);
        Assert.Equal(WhatBecomesOfIt.AddOn, dlc.Suggested);

        // Something with no parts says so by having none, not an empty list.
        Assert.Null(left[1].Parts);
        Assert.Equal(1, left[1].Bytes);
    }

    [Fact]
    public void Each_installer_keeps_its_own_parts_and_nobody_elses()
    {
        IReadOnlyList<LeftBehind> left = LeftIn(
            ("setup_game.exe", 40),
            ("setup_game-1.bin", 900),
            ("setup_game_dlc_one.exe", 2),
            ("setup_game_dlc_one-1.bin", 100),
            ("setup_game_dlc_two.exe", 2),
            ("setup_game_dlc_two-1.bin", 200),
            ("setup_game_dlc_two-2.bin", 300));

        Assert.Equal(["setup_game_dlc_one.exe", "setup_game_dlc_two.exe"], left.Select(each => each.Name));
        Assert.Equal(["setup_game_dlc_one-1.bin"], left[0].Parts!.Select(part => part.Name));
        Assert.Equal(
            ["setup_game_dlc_two-1.bin", "setup_game_dlc_two-2.bin"],
            left[1].Parts!.Select(part => part.Name));
    }

    [Fact]
    public void A_part_whose_installer_is_not_there_is_still_asked_about()
    {
        IReadOnlyList<LeftBehind> left = LeftIn(
            ("setup_game.exe", 40),
            ("setup_game-1.bin", 900),
            ("setup_something_else-1.bin", 500));

        // Losing a file quietly is the worst way to lose it. With nothing
        // to carry it, it is a row like any other.
        LeftBehind orphan = Assert.Single(left);
        Assert.Equal("setup_something_else-1.bin", orphan.Name);
        Assert.Null(orphan.Parts);
    }

    [Fact]
    public void Parts_belong_to_the_installer_in_the_same_folder_and_to_no_other()
    {
        IReadOnlyList<LeftBehind> left = LeftIn(
            ("setup_game.exe", 40),
            ("setup_game_dlc.exe", 2),
            (@"Backup\setup_game_dlc-1.bin", 700),
            (@"DLC\setup_other.exe", 3),
            (@"DLC\setup_other-1.bin", 600),
            ("setup_other-2.bin", 50));

        // The installer looks beside itself and nowhere else, so a part in
        // another folder is not one it could use.
        Assert.Equal(
            ["setup_game_dlc.exe", @"Backup\setup_game_dlc-1.bin", @"DLC\setup_other.exe", "setup_other-2.bin"],
            left.Select(each => each.File.RelativePath));

        Assert.Null(left[0].Parts);
        Assert.Equal([@"DLC\setup_other-1.bin"], left[2].Parts!.Select(part => part.RelativePath));
    }

    [Fact]
    public void Gathered_into_its_folder_the_parts_are_still_in_it()
    {
        IReadOnlyList<LeftBehind> rows = WhatWasLeftBehind.Gathered(LeftIn(
            ("setup_game.exe", 40),
            (@"DLC\setup_other.exe", 3),
            (@"DLC\setup_other-1.bin", 600)));

        LeftBehind folder = Assert.Single(rows);

        Assert.Equal(
            [@"DLC\setup_other.exe", @"DLC\setup_other-1.bin"],
            folder.Inside!.Select(file => file.RelativePath));
        Assert.Equal(603, folder.Bytes);
    }

    // --------------------------------------------------- what each answer does

    [Fact]
    public void Attached_to_a_game_it_points_at_its_parts_where_they_are()
    {
        LeftBehind dlc = TheDlc();

        AddOn attached = dlc.AttachedTo(1, Folder);

        Assert.Equal(1, attached.BelongsTo);
        Assert.Equal(@"C:\games\a game\setup_game_dlc.exe", attached.Path);
        Assert.Equal("game dlc", attached.Name);

        // The installer's own size and the size of all of it are two
        // numbers: the first is what one file is planned at, the second is
        // what somebody is told it costs.
        Assert.Equal(2, attached.Bytes);
        Assert.Equal(1002, attached.BytesWithItsParts);
        Assert.Equal(
            [
                new AddOnPart(@"C:\games\a game\setup_game_dlc-1.bin", 700),
                new AddOnPart(@"C:\games\a game\setup_game_dlc-2.bin", 300)
            ],
            attached.PartsOrNone);
    }

    [Fact]
    public void An_add_on_of_one_file_has_no_parts()
    {
        AddOn patch = new LeftBehind(new ListedFile("patch_game_1_to_2.exe", 30), LeftBehindKind.LooksLikeAnAddOn)
            .AttachedTo(0, Folder);

        Assert.Null(patch.Parts);
        Assert.Empty(patch.PartsOrNone);
        Assert.Equal(30, patch.BytesWithItsParts);
    }

    [Fact]
    public void Put_beside_the_installer_it_goes_with_its_parts()
    {
        (GameFolder game, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
            TheGame(), [(TheDlc(), WhatBecomesOfIt.BesideTheInstaller)]);

        Assert.Equal(
            ["setup_game.exe", "setup_game-1.bin", "setup_game_dlc.exe", "setup_game_dlc-1.bin", "setup_game_dlc-2.bin"],
            game.Files.Select(file => file.RelativePath));
        Assert.Empty(addOns);
    }

    [Fact]
    public void Kept_as_an_extra_its_parts_are_kept_with_it()
    {
        (GameFolder game, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
            TheGame(), [(TheDlc(), WhatBecomesOfIt.Extra)]);

        Assert.Equal(
            [
                new GameExtra(@"C:\games\a game\setup_game_dlc.exe", GameExtraKind.Extra, 2),
                new GameExtra(@"C:\games\a game\setup_game_dlc-1.bin", GameExtraKind.Extra, 700),
                new GameExtra(@"C:\games\a game\setup_game_dlc-2.bin", GameExtraKind.Extra, 300)
            ],
            game.OwnOrNone);
        Assert.Empty(addOns);
    }

    [Fact]
    public void Answered_as_an_add_on_it_is_attached_whole()
    {
        LeftBehind dlc = TheDlc();

        (GameFolder game, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
            TheGame(), [(dlc, WhatBecomesOfIt.AddOn)]);

        Assert.Same(dlc, Assert.Single(addOns));
        Assert.Equal(2, game.Files.Count);
        Assert.Null(game.Own);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, " Its 1 part goes with it.")]
    [InlineData(2, " Its 2 parts go with it.")]
    public void The_dialog_says_its_parts_go_with_it(int parts, string? says)
    {
        LeftBehind dlc = new(
            new ListedFile("setup_game_dlc.exe", 2),
            LeftBehindKind.LooksLikeAnAddOn,
            Parts: parts == 0
                ? null
                : [.. Enumerable.Range(1, parts).Select(at => new ListedFile($"setup_game_dlc-{at}.bin", 100))]);

        string explained = dlc.ExplainedAgainst("setup_game.exe");

        Assert.StartsWith("Looks like something that installs onto the game, so: game dlc", explained, StringComparison.Ordinal);

        if (says is null)
        {
            Assert.DoesNotContain("part", explained, StringComparison.Ordinal);
        }
        else
        {
            Assert.EndsWith(says, explained, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------- what goes on the disc

    [Fact]
    public void The_plan_lays_the_parts_beside_the_installer_under_their_own_names()
    {
        DiscProject project = new(
            "A GAME", [TheGame()], new DiscOptions(), AddOns: [TheDlc().AttachedTo(0, Folder)]);

        PlannedOutput output = Assert.Single(Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(project, new Medium("TEST", "a test disc", 100 * 1024 * 1024), _ => 0)).Value.Outputs);

        PlannedFile[] inAddOns =
        [
            .. output.Layout.Discs.SelectMany(disc => disc.Files)
                .Where(file => file.RelativePath.StartsWith(@"Add-ons\", StringComparison.Ordinal))
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
        ];

        // By name in the folder it runs from is the only place the
        // installer looks, so a part under any other name or in any other
        // folder is a part it does not have.
        Assert.Equal(
            [
                (@"Add-ons\setup_game_dlc-1.bin", @"C:\games\a game\setup_game_dlc-1.bin", 700L),
                (@"Add-ons\setup_game_dlc-2.bin", @"C:\games\a game\setup_game_dlc-2.bin", 300L),
                (@"Add-ons\setup_game_dlc.exe", @"C:\games\a game\setup_game_dlc.exe", 2L)
            ],
            inAddOns.Select(file => (file.RelativePath, file.Source, file.Bytes)));
    }

    [Fact]
    public void The_plan_keeps_each_games_parts_under_that_game()
    {
        GameFolder other = TheGame() with { Path = @"C:\games\other", Name = "Other" };

        DiscProject project = new(
            "TWO", [other, TheGame()], new DiscOptions(), AddOns: [TheDlc().AttachedTo(1, Folder)]);

        PlannedOutput output = Assert.Single(Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(project, new Medium("TEST", "a test disc", 100 * 1024 * 1024), _ => 0)).Value.Outputs);

        Assert.Equal(
            [
                @"02 - a game\Add-ons\setup_game_dlc-1.bin",
                @"02 - a game\Add-ons\setup_game_dlc-2.bin",
                @"02 - a game\Add-ons\setup_game_dlc.exe"
            ],
            output.Layout.Discs.SelectMany(disc => disc.Files)
                .Select(file => file.RelativePath)
                .Where(path => path.Contains("Add-ons", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- the window

    [Theory]
    [InlineData(1, "With its 1 part.")]
    [InlineData(2, "With its 2 parts.")]
    public void On_the_window_it_is_one_row_at_the_size_of_all_of_it(int parts, string says)
    {
        AddOn addOn = new(
            0,
            @"C:\games\a game\setup_game_dlc.exe",
            "game dlc",
            2,
            [.. Enumerable.Range(1, parts).Select(at => new AddOnPart($@"C:\games\a game\setup_game_dlc-{at}.bin", 500))]);

        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(TheGame(), [addOn]);
        AThingOnTheDisc row = Assert.Single(
            tree.Inside.Single(each => each.Name == TheTreeOfWhatGoesOn.AddOnsAreUnder).Inside);

        // One row and one tick: a part with a tick of its own is a way to
        // make a disc whose add-on cannot install.
        Assert.Equal(2 + (500 * parts), row.Bytes);
        Assert.EndsWith(says, row.InFull, StringComparison.Ordinal);
    }

    [Fact]
    public void On_the_window_an_add_on_of_one_file_says_nothing_about_parts()
    {
        AddOn addOn = new(0, @"C:\games\a game\setup_game_soundtrack.exe", "game soundtrack", 70);

        AThingOnTheDisc tree = TheTreeOfWhatGoesOn.For(TheGame(), [addOn]);
        AThingOnTheDisc row = Assert.Single(
            tree.Inside.Single(each => each.Name == TheTreeOfWhatGoesOn.AddOnsAreUnder).Inside);

        Assert.Equal(70, row.Bytes);
        Assert.DoesNotContain("part", row.InFull, StringComparison.Ordinal);
    }

    [Fact]
    public void Added_with_its_game_it_goes_onto_the_disc_with_its_parts()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();

        window.Add(TheGame(), [TheDlc()]);

        AddOn onTheDisc = Assert.Single(window.AsAProject().AddOnsOrNone);

        Assert.Equal(
            [@"C:\games\a game\setup_game_dlc-1.bin", @"C:\games\a game\setup_game_dlc-2.bin"],
            onTheDisc.PartsOrNone.Select(part => part.Path));
    }

    [Fact]
    public void Taken_off_and_put_back_it_comes_back_with_its_parts()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(TheGame(), [TheDlc()]);

        window.TakeOff(window.WhatGoesOnIt
            .SelectMany(game => game.EveryThing())
            .First(thing => thing.ItsPath.EndsWith("setup_game_dlc.exe", StringComparison.Ordinal)));

        Assert.Empty(window.AsAProject().AddOnsOrNone);

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(
            window.WhatIsMissingFrom(TheGame(), [TheDlc()]));

        // One thing is missing, not three: its parts are not things of
        // their own to be asked about.
        Assert.Equal(["setup_game_dlc.exe"], missing.WhatIsMissing);

        window.PutBack(missing);

        Assert.Equal(2, Assert.Single(window.AsAProject().AddOnsOrNone).PartsOrNone.Count);
    }

    // ------------------------------------------------------------------ helpers

    private static GameFolder TheGame()
    {
        ListedFile installer = new("setup_game.exe", 40);

        return new GameFolder(
            Folder, GameKind.GogInstaller, "a game", "a game", installer,
            [installer, new ListedFile("setup_game-1.bin", 900)], [], null);
    }

    private static LeftBehind TheDlc() => new(
        new ListedFile("setup_game_dlc.exe", 2),
        LeftBehindKind.LooksLikeAnAddOn,
        Parts: [new ListedFile("setup_game_dlc-1.bin", 700), new ListedFile("setup_game_dlc-2.bin", 300)]);

    /// <summary>
    /// What is left of a folder once the game has taken its installer and
    /// that installer's parts, which is the first two rules of a download.
    /// </summary>
    private static IReadOnlyList<LeftBehind> LeftIn(params (string Path, long Bytes)[] files)
    {
        FolderListing folder = new(Folder, [.. files.Select(file => new ListedFile(file.Path, file.Bytes))]);

        ListedFile[] taken =
        [
            .. folder.Files.Where(file =>
                file.RelativePath is "setup_game.exe" || file.RelativePath.StartsWith("setup_game-", StringComparison.Ordinal))
        ];

        return WhatWasLeftBehind.In(
            folder,
            new GameFolder(Folder, GameKind.GogInstaller, "a game", "a game", taken[0], taken, [], null));
    }
}
