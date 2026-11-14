using DiscWright.Core;
using DiscWright.ViewModels;

using Step = DiscWright.ViewModels.TakingFoldersOn.WhatToDo;

namespace DiscWright.Tests;

/// <summary>
/// What happens when folders are added, which until this file had no test at
/// all.
/// </summary>
/// <remarks>
/// Every piece of it was well covered. The order the pieces were asked in was
/// not, because it lived in the window's code-behind and nothing in this
/// project can reference that. What is tested here is the decision: what is
/// asked, in what order, and what the answers come to.
/// </remarks>
public class TakingFoldersOnTests
{
    // ------------------------------------------------- the ordinary folders

    [Fact]
    public void A_download_goes_on_with_nothing_asked()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Hollow Knight", ("setup_hollow_knight.exe", 900_000_000));

        WhatWasAsked asked = new();

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Hollow Knight"], asked.Questions)];

        Step.PutItOn put = Assert.IsType<Step.PutItOn>(Assert.Single(steps));

        Assert.Equal("Hollow Knight", put.Game.Name);
        Assert.Empty(put.AsAddOns);
        Assert.Empty(asked.InOrder);
    }

    [Fact]
    public void A_folder_nothing_recognises_is_explained_rather_than_added()
    {
        // Every non-empty folder becomes a disc of files, so the only folder
        // that gets this far is an empty one.
        ADisk disk = new();
        disk.Holds(@"C:\g\Empty");

        WhatWasAsked asked = new();

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Empty"], asked.Questions)];

        Step.Explain said = Assert.IsType<Step.Explain>(Assert.Single(steps));

        Assert.NotEmpty(said.Refusal.Headline);
        Assert.Empty(asked.InOrder);
    }

    [Fact]
    public void One_bad_folder_does_not_stop_the_others()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Empty");
        disk.Holds(@"C:\g\Celeste", ("setup_celeste.exe", 1000));

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Empty", @"C:\g\Celeste"], new WhatWasAsked().Questions)];

        Assert.Collection(
            steps,
            one => Assert.IsType<Step.Explain>(one),
            two => Assert.Equal("Celeste", Assert.IsType<Step.PutItOn>(two).Game.Name));
    }

    [Fact]
    public void Games_added_together_are_asked_once_when_the_same_answers_were_asked_for()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Gothic", ("setup_gothic.exe", 1000), ("Gothic manual.pdf", 40));
        disk.Holds(@"C:\g\Arcanum", ("setup_arcanum.exe", 1000), ("arcanum_manual.pdf", 50), ("map.png", 5));
        disk.Holds(@"C:\g\Celeste", ("setup_celeste.exe", 1000), ("celeste-guide.pdf", 60));

        WhatWasAsked asked = new()
        {
            AnswerAboutWhatElse = (game, left) => WhatWasLeftBehind.Answered(
                game, left.Select(each => (each, WhatBecomesOfIt.Manual))),
            TheSameForTheRest = left => TheSameAnswers.From(left.Select(each => (each, true, WhatBecomesOfIt.Manual)), null)
        };

        Step.PutItOn[] put =
        [
            .. Taking(disk).From([@"C:\g\Gothic", @"C:\g\Arcanum", @"C:\g\Celeste"], asked.Questions).OfType<Step.PutItOn>()
        ];

        // Asked about the first, told two come after it, and never again.
        Assert.Equal(["what else: Gothic, 1 left behind, 2 after"], asked.InOrder);
        Assert.Equal(3, put.Length);

        // Each later manual is its own game's manual; the map, a kind the
        // first game did not have, gets its own suggestion.
        Assert.Equal(
            [new GameExtra(@"C:\g\Arcanum\arcanum_manual.pdf", GameExtraKind.Manual, 50), new GameExtra(@"C:\g\Arcanum\map.png", GameExtraKind.Extra, 5)],
            put[1].Game.OwnOrNone);
        Assert.Equal([new GameExtra(@"C:\g\Celeste\celeste-guide.pdf", GameExtraKind.Manual, 60)], put[2].Game.OwnOrNone);
    }

    [Fact]
    public void Without_the_same_answers_every_game_is_asked_and_told_how_many_come_after()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Gothic", ("setup_gothic.exe", 1000), ("readme.txt", 4));
        disk.Holds(@"C:\g\Arcanum", ("setup_arcanum.exe", 1000), ("readme.txt", 4));

        WhatWasAsked asked = new();

        _ = Taking(disk).From([@"C:\g\Gothic", @"C:\g\Arcanum"], asked.Questions).ToList();

        Assert.Equal(
            ["what else: Gothic, 1 left behind, 1 after", "what else: Arcanum, 1 left behind, 0 after"],
            asked.InOrder);
    }

    [Fact]
    public void A_question_closed_rather_than_answered_leaves_the_folder_alone()
    {
        ADisk disk = new();
        disk.Holds(
            @"C:\g\Hollow Knight",
            ("setup_hollow_knight.exe", 900_000_000),
            ("patch_hollow_knight_1.0_to_1.1.exe", 50_000));
        disk.Holds(@"C:\g\Celeste", ("setup_celeste.exe", 1000));

        WhatWasAsked asked = new() { AnswerAboutWhatElse = (game, left) => null };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Hollow Knight", @"C:\g\Celeste"], asked.Questions)];

        // The X is not "none of them". Leaving files off is a decision, and
        // only the button that says so can make it; closing the question
        // leaves the whole folder alone, game and all, so nothing is lost
        // because nothing happened. The next folder still goes on.
        Assert.Equal("Celeste", Assert.IsType<Step.PutItOn>(Assert.Single(steps)).Game.Name);
        Assert.Equal(["what else: Hollow Knight, 1 left behind, 1 after"], asked.InOrder);
    }

    [Fact]
    public void The_dialog_tells_the_X_from_none_of_them()
    {
        // ShowDialog answers false for both, and nothing in this project can
        // press either, so the code behind is read off disk: both buttons
        // mark the question answered, and an unanswered question hands back
        // nothing rather than an empty answer.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml.cs"));

        Assert.Equal(2, code.Split("_answered = true;").Length - 1);
        Assert.Contains("if (!asking._answered)", code, StringComparison.Ordinal);

        string unanswered = code[code.IndexOf("if (!asking._answered)", StringComparison.Ordinal)..];
        unanswered = unanswered[..unanswered.IndexOf('}')];
        Assert.Contains("return null;", unanswered, StringComparison.Ordinal);
    }

    // ------------------------------------------------- the same folder again

    [Fact]
    public void A_folder_on_the_list_already_comes_back_whole_with_nothing_asked()
    {
        ADisk disk = new();
        disk.Holds(
            @"C:\g\Hollow Knight",
            ("setup_hollow_knight.exe", 900_000_000),
            ("patch_1.1.exe", 1000),
            ("read me.txt", 10));

        WhatWasAsked asked = new();

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Hollow Knight"], asked.Questions, folder => true)];

        Step.ItIsOnAlready again = Assert.IsType<Step.ItIsOnAlready>(Assert.Single(steps));

        Assert.Equal("Hollow Knight", again.Game.Name);
        Assert.Equal(["patch_1.1.exe", "read me.txt"], again.Left.Select(each => each.Name).Order());

        // Not even what else is in there. Only the screen knows what is
        // missing, so the one question is the screen's to put.
        Assert.Empty(asked.InOrder);
    }

    [Fact]
    public void A_folder_of_games_that_went_on_as_one_is_not_taken_apart_the_second_time()
    {
        ADisk disk = new();
        disk.Holds(@"C:\downloads", ("readme.txt", 200));
        disk.Holds(@"C:\downloads\Celeste", ("setup_celeste.exe", 1000));
        disk.Holds(@"C:\downloads\Hades", ("setup_hades.exe", 2000));

        WhatWasAsked asked = new() { Separately = true };

        List<Step> steps = [.. Taking(disk).From(
            [@"C:\downloads"],
            asked.Questions,
            folder => string.Equals(folder, @"C:\downloads", StringComparison.OrdinalIgnoreCase))];

        // Added separately the first time, each game is on by its own path and
        // the holding folder is not, so this is the folder that went on as one.
        Assert.IsType<Step.ItIsOnAlready>(Assert.Single(steps));
        Assert.Empty(asked.InOrder);
    }

    [Fact]
    public void Nothing_is_on_the_list_when_nobody_can_say()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Hollow Knight", ("setup_hollow_knight.exe", 900_000_000));

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Hollow Knight"], new WhatWasAsked().Questions)];

        Assert.IsType<Step.PutItOn>(Assert.Single(steps));
    }

    // ----------------------------------------- a folder that holds the games

    [Fact]
    public void A_folder_of_games_added_separately_puts_each_on_then_carries_what_was_loose()
    {
        ADisk disk = new();
        disk.Holds(@"C:\downloads", ("readme.txt", 200), ("cover.jpg", 400));
        disk.Holds(@"C:\downloads\Celeste", ("setup_celeste.exe", 1000));
        disk.Holds(@"C:\downloads\Hades", ("setup_hades.exe", 2000));

        WhatWasAsked asked = new() { Separately = true };

        List<Step> steps = [.. Taking(disk).From([@"C:\downloads"], asked.Questions)];

        // Both games, and then the loose files. The order matters: the files
        // belong to the disc rather than to either game, and they are carried
        // after the games they were sitting beside.
        Assert.Collection(
            steps,
            one => Assert.Equal("Celeste", Assert.IsType<Step.PutItOn>(one).Game.Name),
            two => Assert.Equal("Hades", Assert.IsType<Step.PutItOn>(two).Game.Name),
            three => Assert.Equal(
                [@"C:\downloads\readme.txt", @"C:\downloads\cover.jpg"],
                Assert.IsType<Step.AlsoOnTheDisc>(three).Paths));

        Assert.Equal(["separately: downloads, 2 games, 2 loose"], asked.InOrder);
    }

    [Fact]
    public void The_same_folder_added_as_one_is_one_game_and_loses_nothing()
    {
        ADisk disk = new();
        disk.Holds(@"C:\downloads", ("readme.txt", 200));
        disk.Holds(@"C:\downloads\Celeste", ("setup_celeste.exe", 1000));
        disk.Holds(@"C:\downloads\Hades", ("setup_hades.exe", 2000));

        WhatWasAsked asked = new() { Separately = false };

        List<Step> steps = [.. Taking(disk).From([@"C:\downloads"], asked.Questions)];

        // One entry named after the folder, holding everything. No separate
        // step for the loose file, because taking the folder whole already
        // takes it.
        Step.PutItOn put = Assert.IsType<Step.PutItOn>(Assert.Single(steps));

        Assert.Equal("downloads", put.Game.Name);
        Assert.DoesNotContain(steps, each => each is Step.AlsoOnTheDisc);
    }

    [Fact]
    public void A_folder_holding_one_game_is_not_asked_about()
    {
        ADisk disk = new();
        disk.Holds(@"C:\downloads");
        disk.Holds(@"C:\downloads\Celeste", ("setup_celeste.exe", 1000));

        WhatWasAsked asked = new();

        List<Step> steps = [.. Taking(disk).From([@"C:\downloads"], asked.Questions)];

        // One game inside is not a folder of games, it is a folder, and a
        // question offering a choice of one is a question nobody should see.
        Assert.Single(steps);
        Assert.Empty(asked.InOrder);
    }

    [Fact]
    public void A_folder_of_folders_of_games_goes_all_the_way_down()
    {
        ADisk disk = new();
        disk.Holds(@"C:\all");
        disk.Holds(@"C:\all\platformers");
        disk.Holds(@"C:\all\platformers\Celeste", ("setup_celeste.exe", 1000));
        disk.Holds(@"C:\all\platformers\Hollow", ("setup_hollow.exe", 1000));
        disk.Holds(@"C:\all\roguelikes");
        disk.Holds(@"C:\all\roguelikes\Hades", ("setup_hades.exe", 1000));
        disk.Holds(@"C:\all\roguelikes\Isaac", ("setup_isaac.exe", 1000));

        WhatWasAsked asked = new() { Separately = true };

        List<Step> steps = [.. Taking(disk).From([@"C:\all"], asked.Questions)];

        Assert.Equal(
            ["Celeste", "Hollow", "Hades", "Isaac"],
            steps.OfType<Step.PutItOn>().Select(each => each.Game.Name));

        // Asked ONCE, not once per level. Games are found at whatever depth
        // they sit at, so the outer folder already knows about all four and
        // there is one decision to make rather than three.
        Assert.Equal(
            ["separately: all, 4 games, 0 loose"],
            asked.InOrder.Where(each => each.StartsWith("separately", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------ the add-on alone

    [Fact]
    public void An_add_on_on_its_own_is_left_off_when_that_is_the_answer()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\extras", ("setup_hollow_knight_soundtrack.exe", 50_000));

        WhatWasAsked asked = new() { LeaveTheAddOn = true };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\extras"], asked.Questions)];

        // Nothing at all: not put on, and not explained either, because the
        // person was asked and said no rather than being refused.
        Assert.Empty(steps);
        Assert.Contains("leave the add-on: setup_hollow_knight_soundtrack.exe", asked.InOrder);
    }

    [Fact]
    public void An_add_on_on_its_own_really_goes_on_when_that_is_the_answer()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\extras", ("setup_hollow_knight_soundtrack.exe", 50_000));

        WhatWasAsked asked = new() { LeaveTheAddOn = false };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\extras"], asked.Questions)];

        // It is a question, not a refusal. Answering no has to put it on.
        Assert.IsType<Step.PutItOn>(Assert.Single(steps));
    }

    [Fact]
    public void A_game_is_never_mistaken_for_an_add_on()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Hollow Knight", ("setup_hollow_knight.exe", 900_000_000));

        WhatWasAsked asked = new() { LeaveTheAddOn = true };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Hollow Knight"], asked.Questions)];

        // The answer that would have left an add-on off must not touch a game,
        // which means the question is never put.
        Assert.IsType<Step.PutItOn>(Assert.Single(steps));
        Assert.DoesNotContain(asked.InOrder, each => each.StartsWith("leave", StringComparison.Ordinal));
    }

    // --------------------------------------------- what else was in there

    [Fact]
    public void What_was_left_behind_is_asked_about_and_the_answer_is_what_goes_on()
    {
        ADisk disk = new();
        disk.Holds(
            @"C:\g\Hollow Knight",
            ("setup_hollow_knight.exe", 900_000_000),
            ("patch_hollow_knight_1.0_to_1.1.exe", 50_000));

        LeftBehind? offered = null;

        WhatWasAsked asked = new()
        {
            AnswerAboutWhatElse = (game, left) =>
            {
                offered = left.Count > 0 ? left[0] : null;
                return (game with { Name = "renamed by the question" }, left);
            },
        };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Hollow Knight"], asked.Questions)];

        Step.PutItOn put = Assert.IsType<Step.PutItOn>(Assert.Single(steps));

        // The game that goes on is the one the question answered with, not the
        // one it was given, because the answer changes what the game is.
        Assert.Equal("renamed by the question", put.Game.Name);
        Assert.Equal("patch_hollow_knight_1.0_to_1.1.exe", Assert.Single(put.AsAddOns).Name);
        Assert.NotNull(offered);
    }

    [Fact]
    public void Nothing_is_asked_about_a_folder_that_left_nothing_behind()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Celeste", ("setup_celeste.exe", 1000));

        WhatWasAsked asked = new();

        _ = Taking(disk).From([@"C:\g\Celeste"], asked.Questions).ToList();

        // A dialog that appears to say there is nothing to tell you is worse
        // than silence, and this is most downloads.
        Assert.DoesNotContain(asked.InOrder, each => each.StartsWith("what else", StringComparison.Ordinal));
    }

    // ------------------------------------------------- which file starts it

    [Fact]
    public void Two_things_that_could_start_it_means_somebody_is_asked()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Loose", ("TheGame.exe", 1000), ("Launcher.exe", 1000), (@"data\things.pak", 5000));

        WhatWasAsked asked = new()
        {
            AnswerAboutTheLauncher = game => game with
            {
                LauncherCandidates = [game.LauncherCandidates[^1]],
            },
        };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Loose"], asked.Questions)];

        Step.PutItOn put = Assert.IsType<Step.PutItOn>(Assert.Single(steps));

        Assert.Contains("which starts it", asked.InOrder);
        Assert.Equal("Launcher.exe", Assert.Single(put.Game.LauncherCandidates).Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_and_nobody_is_asked(int howMany)
    {
        ADisk disk = new();

        List<(string, long)> files = [(@"data\things.pak", 5000)];

        for (int which = 0; which < howMany; which++)
        {
            files.Add(($"Game{which}.exe", 1000));
        }

        disk.Holds(@"C:\g\Loose", [.. files]);

        WhatWasAsked asked = new()
        {
            AnswerAboutTheLauncher = game => throw new InvalidOperationException(
                "Asked which file starts a game with nothing to choose between."),
        };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Loose"], asked.Questions)];

        Assert.IsType<Step.PutItOn>(Assert.Single(steps));
        Assert.DoesNotContain("which starts it", asked.InOrder);
    }

    [Fact]
    public void Saying_nothing_about_the_launcher_leaves_the_folder_to_be_copied()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Loose", ("TheGame.exe", 1000), ("Launcher.exe", 1000), (@"data\things.pak", 5000));

        // What closing the dialog does: answers with the game unchanged.
        WhatWasAsked asked = new() { AnswerAboutTheLauncher = game => game };

        List<Step> steps = [.. Taking(disk).From([@"C:\g\Loose"], asked.Questions)];

        // Both still there, which is what the menu reads as "copy it and open
        // the folder" rather than running the wrong one.
        Assert.Equal(2, Assert.IsType<Step.PutItOn>(Assert.Single(steps)).Game.LauncherCandidates.Count);
    }

    // ------------------------------------------------------------- the order

    [Fact]
    public void Each_folder_is_finished_before_the_next_one_is_asked_about()
    {
        // The window applies each step as it arrives, so a game appears on the
        // list before the next folder is asked about. Gathering every answer
        // first and then adding everything at once would make somebody answer
        // a pile of dialogs over an empty window, which is a different
        // program. This is the test that holds the laziness in place.
        ADisk disk = new();
        disk.Holds(@"C:\g\Celeste", ("setup_celeste.exe", 1000), ("patch_celeste_1.0_to_1.1.exe", 50));
        disk.Holds(@"C:\g\Hades", ("setup_hades.exe", 1000), ("patch_hades_1.0_to_1.1.exe", 50));

        List<string> happened = [];

        WhatWasAsked asked = new()
        {
            AnswerAboutWhatElse = (game, left) =>
            {
                happened.Add("asked about " + game.Name);
                return (game, []);
            },
        };

        foreach (Step step in Taking(disk).From([@"C:\g\Celeste", @"C:\g\Hades"], asked.Questions))
        {
            happened.Add("put on " + ((Step.PutItOn)step).Game.Name);
        }

        Assert.Equal(
            [
                "asked about Celeste",
                "put on Celeste",
                "asked about Hades",
                "put on Hades",
            ],
            happened);
    }

    [Fact]
    public void Nothing_is_asked_until_the_steps_are_read()
    {
        ADisk disk = new();
        disk.Holds(@"C:\g\Celeste", ("setup_celeste.exe", 1000), ("patch_celeste_1.0_to_1.1.exe", 50));

        WhatWasAsked asked = new();

        IEnumerable<Step> steps = Taking(disk).From([@"C:\g\Celeste"], asked.Questions);

        // Holding the answer is not asking the question.
        Assert.Empty(asked.InOrder);

        _ = steps.ToList();

        Assert.NotEmpty(asked.InOrder);
    }

    [Fact]
    public void Nobody_asked_is_a_whole_set_of_answers()
    {
        // The default answers are what closing every dialog does, and a test
        // that only cares about one question uses them for the rest. If they
        // ever stop being a complete set this is where it shows.
        ADisk disk = new();
        disk.Holds(@"C:\downloads", ("readme.txt", 200));
        disk.Holds(@"C:\downloads\Celeste", ("setup_celeste.exe", 1000), ("patch_celeste_1.0_to_1.1.exe", 50));
        disk.Holds(@"C:\downloads\Hades", ("setup_hades.exe", 1000));

        List<Step> steps =
        [
            .. Taking(disk).From([@"C:\downloads"], TakingFoldersOn.TheQuestions.NobodyIsAsked)
        ];

        // Added as one, nothing attached, nothing refused.
        Step.PutItOn put = Assert.IsType<Step.PutItOn>(Assert.Single(steps));

        Assert.Equal("downloads", put.Game.Name);
        Assert.Empty(put.AsAddOns);
    }

    // ------------------------------------------------------------- the rig

    // InTheUsualOrder exists so a test does not restate the order the
    // recognisers are asked in. This is the real reader the window uses.
    private static TakingFoldersOn Taking(ADisk disk) =>
        new(disk, GameFolderReader.InTheUsualOrder(new NoFactsToRead()));

    /// <summary>
    /// Answers to the four questions, and a record of which were put.
    /// </summary>
    private sealed class WhatWasAsked
    {
        public List<string> InOrder { get; } = [];

        public bool Separately { get; init; }

        public bool LeaveTheAddOn { get; init; }

        public Func<GameFolder, IReadOnlyList<LeftBehind>, (GameFolder, IReadOnlyList<LeftBehind>)?>?
            AnswerAboutWhatElse { get; init; }

        /// <summary>The answers to give every game after the first one asked about, or none.</summary>
        public Func<IReadOnlyList<LeftBehind>, TheSameAnswers>? TheSameForTheRest { get; init; }

        public Func<GameFolder, GameFolder>? AnswerAboutTheLauncher { get; init; }

        public TakingFoldersOn.TheQuestions Questions => new(
            (folder, inside, loose) =>
            {
                InOrder.Add($"separately: {Path.GetFileName(folder)}, {inside.Count} games, {loose} loose");
                return Separately;
            },
            game =>
            {
                InOrder.Add("leave the add-on: " + game.Installer?.Name);
                return LeaveTheAddOn;
            },
            (game, left, after) =>
            {
                InOrder.Add($"what else: {game.Name}, {left.Count} left behind, {after} after");
                (GameFolder, IReadOnlyList<LeftBehind>)? said = AnswerAboutWhatElse is null ? (game, left) : AnswerAboutWhatElse(game, left);

                return said is (GameFolder taken, IReadOnlyList<LeftBehind> addOns)
                    ? (taken, addOns, TheSameForTheRest?.Invoke(left))
                    : null;
            },
            game =>
            {
                InOrder.Add("which starts it");
                return AnswerAboutTheLauncher is null ? game : AnswerAboutTheLauncher(game);
            });
    }

    /// <summary>
    /// An installer that reports no product name, so the disc is named from
    /// the file name. What the name is derived from is TheRecogniser's
    /// business; what matters here is which folder went on.
    /// </summary>
    private sealed class NoFactsToRead : IFileFacts
    {
        public string? ProductNameOf(string path) => null;
    }

    /// <summary>A disk holding exactly the folders a test says it holds.</summary>
    private sealed class ADisk : IFileSystem
    {
        private readonly Dictionary<string, List<ListedFile>> _folders = new(StringComparer.OrdinalIgnoreCase);

        public void Holds(string path, params (string Name, long Bytes)[] files) =>
            _folders[path.TrimEnd('\\')] = [.. files.Select(each => new ListedFile(each.Name, each.Bytes))];

        public bool FolderExists(string path) => _folders.ContainsKey(path.TrimEnd('\\'));

        public FolderListing List(string path)
        {
            path = path.TrimEnd('\\');

            List<ListedFile> here = _folders.TryGetValue(path, out List<ListedFile>? found) ? found : [];

            // Everything under it, the way a real listing reads: a folder
            // holding games lists their files under their own names.
            List<ListedFile> all = [.. here];

            foreach ((string folder, List<ListedFile> files) in _folders)
            {
                if (folder.Length > path.Length
                    && folder.StartsWith(path + @"\", StringComparison.OrdinalIgnoreCase))
                {
                    string under = folder[(path.Length + 1)..];

                    all.AddRange(files.Select(each => new ListedFile(
                        Path.Combine(under, each.RelativePath), each.Bytes)));
                }
            }

            return new FolderListing(path, all);
        }
    }
}
