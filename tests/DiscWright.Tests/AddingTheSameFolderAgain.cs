using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The same folder added a second time, which used to put the same game on
/// twice and now offers back what was taken off.
/// </summary>
/// <remarks>
/// The scene is the one the owner described: a game goes on with its patches,
/// a couple of the patches are removed, and the folder is added again. What
/// should come back is the missing patches and nothing else.
/// </remarks>
public class AddingTheSameFolderAgain
{
    private static readonly string Folder = @"C:\games\Hollow Knight";

    // ------------------------------------------------- is it on already

    [Fact]
    public void Knows_a_folder_that_is_on_the_list()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame());

        Assert.True(window.IsOnAlready(Folder));
        Assert.False(window.IsOnAlready(@"C:\games\Gothic"));
    }

    [Fact]
    public void The_same_folder_typed_differently_is_the_same_folder()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame());

        Assert.True(window.IsOnAlready(@"c:\GAMES\hollow knight\"));
    }

    [Fact]
    public void A_folder_that_is_not_on_the_list_has_nothing_missing_to_report()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();

        Assert.Null(window.WhatIsMissingFrom(AGame(), []));
    }

    // -------------------------------------------------- what is missing

    [Fact]
    public void Nothing_is_missing_when_nothing_was_taken_off()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame(), [APatch("patch_1.1.exe"), APatch("patch_1.2.exe")]);

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(
            window.WhatIsMissingFrom(AGame(), [APatch("patch_1.1.exe"), APatch("patch_1.2.exe")]));

        Assert.True(missing.NothingIsMissing);
        Assert.Equal("Hollow Knight is already on the disc", missing.AlreadyWhole.Headline);
    }

    [Fact]
    public void The_patches_that_were_removed_are_what_is_missing()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame(), [APatch("patch_1.1.exe"), APatch("patch_1.2.exe"), APatch("patch_1.3.exe")]);

        TakeOff(window, "patch_1.1.exe");
        TakeOff(window, "patch_1.3.exe");

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(window.WhatIsMissingFrom(
            AGame(), [APatch("patch_1.1.exe"), APatch("patch_1.2.exe"), APatch("patch_1.3.exe")]));

        Assert.Equal(["patch_1.1.exe", "patch_1.3.exe"], missing.WhatIsMissing);
        Assert.Empty(missing.Files);
    }

    [Fact]
    public void A_file_of_the_game_that_was_removed_is_missing_too()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame());

        TakeOff(window, "setup_hollow_knight-1.bin");

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(window.WhatIsMissingFrom(AGame(), []));

        Assert.Equal(["setup_hollow_knight-1.bin"], missing.WhatIsMissing);
        Assert.Empty(missing.Left);
    }

    [Fact]
    public void An_unticked_row_is_not_missing_because_it_is_still_on_the_window()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame(), [APatch("patch_1.1.exe")]);

        Row(window, "patch_1.1.exe").IsOn = false;

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(
            window.WhatIsMissingFrom(AGame(), [APatch("patch_1.1.exe")]));

        Assert.True(missing.NothingIsMissing);
    }

    [Fact]
    public void Something_beside_the_game_that_was_never_taken_on_counts_as_missing()
    {
        // From the folder's side it is the same thing as a patch that was
        // removed: in the folder, and not on the disc.
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame());

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(
            window.WhatIsMissingFrom(AGame(), [AThingToKeep("read me.txt")]));

        Assert.Equal(["read me.txt"], missing.WhatIsMissing);
    }

    // ------------------------------------------------------ the question

    [Fact]
    public void The_question_names_what_is_missing_and_putting_it_back_is_the_likely_answer()
    {
        TheSameFolderAgain missing = new(0, "Hollow Knight", [], [APatch("patch_1.1.exe"), APatch("patch_1.3.exe")]);

        AQuestion question = missing.Question;

        Assert.Equal("Hollow Knight is already on the disc, with 2 things from its folder left off", question.Headline);
        Assert.StartsWith("The folder holds patch_1.1.exe and patch_1.3.exe and the disc does not.", question.Because);
        Assert.Equal("Put them back", question.TheLikelyOne);
        Assert.Equal("Leave it as it is", question.TheOtherWay);
    }

    [Fact]
    public void One_thing_missing_is_asked_about_in_the_singular()
    {
        TheSameFolderAgain missing = new(0, "Hollow Knight", [new ListedFile("extras\\map.png", 10)], []);

        Assert.Equal("Hollow Knight is already on the disc, with one thing from its folder left off", missing.Question.Headline);
        Assert.StartsWith("The folder holds map.png and the disc does not.", missing.Question.Because);
        Assert.Equal("Put it back", missing.Question.TheLikelyOne);
    }

    [Fact]
    public void Many_missing_things_are_counted_rather_than_listed()
    {
        TheSameFolderAgain missing = new(
            0, "Hollow Knight", [.. Enumerable.Range(1, 8).Select(at => new ListedFile($"part{at}.bin", 10))], []);

        Assert.Contains("part1.bin, part2.bin, part3.bin, part4.bin, part5.bin and 3 more", missing.Question.Because);
    }

    // ----------------------------------------------------- putting back

    [Fact]
    public void Puts_back_exactly_what_was_missing_and_nothing_twice()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame(), [APatch("patch_1.1.exe"), APatch("patch_1.2.exe"), APatch("patch_1.3.exe")]);
        DiscProject asFirstAdded = window.AsAProject();

        TakeOff(window, "patch_1.1.exe");
        TakeOff(window, "patch_1.3.exe");
        TakeOff(window, "setup_hollow_knight-1.bin");

        TheSameFolderAgain missing = Assert.IsType<TheSameFolderAgain>(window.WhatIsMissingFrom(
            AGame(), [APatch("patch_1.1.exe"), APatch("patch_1.2.exe"), APatch("patch_1.3.exe")]));

        window.PutBack(missing);

        DiscProject now = window.AsAProject();

        Assert.Single(now.Games);
        Assert.Equal(
            asFirstAdded.Games[0].Files.Select(file => file.RelativePath).Order(),
            now.Games[0].Files.Select(file => file.RelativePath).Order());
        Assert.Equal(
            asFirstAdded.AddOnsOrNone.Select(addOn => addOn.Path).Order(),
            now.AddOnsOrNone.Select(addOn => addOn.Path).Order());
        Assert.All(now.AddOnsOrNone, addOn => Assert.Equal(0, addOn.BelongsTo));
    }

    [Fact]
    public void Puts_back_nothing_when_nothing_is_missing()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame(), [APatch("patch_1.1.exe")]);
        DiscProject before = window.AsAProject();

        window.PutBack(Assert.IsType<TheSameFolderAgain>(window.WhatIsMissingFrom(AGame(), [APatch("patch_1.1.exe")])));

        Assert.Equal(before.Games[0].Files.Count, window.AsAProject().Games[0].Files.Count);
        Assert.Equal(before.AddOnsOrNone.Count, window.AsAProject().AddOnsOrNone.Count);
    }

    [Fact]
    public void Something_to_keep_goes_back_with_the_game_and_an_add_on_goes_back_as_one()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame());

        window.PutBack(Assert.IsType<TheSameFolderAgain>(
            window.WhatIsMissingFrom(AGame(), [AThingToKeep("read me.txt"), APatch("patch_1.1.exe")])));

        DiscProject now = window.AsAProject();

        Assert.Contains(now.Games[0].Files, file => file.RelativePath == "read me.txt");
        Assert.Equal([Folder + @"\patch_1.1.exe"], now.AddOnsOrNone.Select(addOn => addOn.Path));
    }

    [Fact]
    public void Putting_back_lands_on_the_right_game_when_it_is_not_the_first()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame("Gothic", @"C:\games\Gothic"), [APatch("patch_gothic.exe")]);
        window.Add(AGame(), [APatch("patch_1.1.exe")]);

        TakeOff(window, "patch_1.1.exe");

        window.PutBack(Assert.IsType<TheSameFolderAgain>(window.WhatIsMissingFrom(AGame(), [APatch("patch_1.1.exe")])));

        Assert.Equal(
            [(0, "patch_gothic.exe"), (1, "patch_1.1.exe")],
            window.AsAProject().AddOnsOrNone.Select(addOn => (addOn.BelongsTo, System.IO.Path.GetFileName(addOn.Path))).Order());
    }

    [Fact]
    public void Putting_back_says_what_changed_about_the_list()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame(), [APatch("patch_1.1.exe")]);
        TakeOff(window, "patch_1.1.exe");

        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        window.PutBack(Assert.IsType<TheSameFolderAgain>(window.WhatIsMissingFrom(AGame(), [APatch("patch_1.1.exe")])));

        Assert.Contains(nameof(MakeADisc.WhatIsOnIt), told);
        Assert.Contains(window.WhatGoesOnIt[0].EveryThing(), thing => thing.ItsPath.EndsWith("patch_1.1.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void Putting_back_against_a_game_that_has_gone_does_nothing()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New();
        window.Add(AGame());

        window.PutBack(new TheSameFolderAgain(3, "Nobody", [new ListedFile("x.bin", 1)], []));

        Assert.Single(window.AsAProject().Games[0].Files, file => file.RelativePath == "setup_hollow_knight.exe");
        Assert.Equal(2, window.AsAProject().Games[0].Files.Count);
    }

    // ---------------------------------------------------------- helpers

    private static GameFolder AGame(string name = "Hollow Knight", string? path = null)
    {
        ListedFile installer = new($"setup_{name.ToLowerInvariant().Replace(' ', '_')}.exe", 1000);
        ListedFile part = new($"setup_{name.ToLowerInvariant().Replace(' ', '_')}-1.bin", 1000);

        return new GameFolder(path ?? Folder, GameKind.GogInstaller, name, name, installer, [installer, part], [], null);
    }

    private static LeftBehind APatch(string name) =>
        new(new ListedFile(name, 200), LeftBehindKind.LooksLikeAnAddOn);

    private static LeftBehind AThingToKeep(string name) =>
        new(new ListedFile(name, 20), LeftBehindKind.LooksLikeSomethingToKeep);

    private static AThingOnTheDisc Row(MakeADisc window, string named) =>
        window.WhatGoesOnIt.SelectMany(game => game.EveryThing()).First(thing => thing.ItsPath.EndsWith(named, StringComparison.Ordinal));

    private static void TakeOff(MakeADisc window, string named) => window.TakeOff(Row(window, named));
}
