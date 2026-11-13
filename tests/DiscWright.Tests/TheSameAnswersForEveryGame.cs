using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Answering "There is more in that folder" once for every game added
/// together, by what each row looks like. xniwo on discwright#98 added nine
/// games and was asked nine times.
/// </summary>
public sealed class TheSameAnswersForEveryGame
{
    [Fact]
    public void A_later_games_rows_take_the_answer_their_look_got_whatever_they_are_called()
    {
        TheSameAnswers same = TheSameAnswers.From(
        [
            (Kept("The Witcher - manual.pdf"), true, WhatBecomesOfIt.Manual),
            (Folder("Artbook"), true, WhatBecomesOfIt.BesideTheInstaller),
            (Kept("README.txt"), false, WhatBecomesOfIt.Extra)
        ]);

        LeftBehind manual = Kept("Gothic_Guide.pdf");
        LeftBehind maps = Folder("Maps");
        LeftBehind readme = Kept("readme_gothic.txt");

        // The Artbook went beside the installer, so the Maps do too; the
        // README was left off, so the next one, an extra by its look, is too.
        Assert.Equal(
            [(manual, WhatBecomesOfIt.Manual), (maps, WhatBecomesOfIt.BesideTheInstaller)],
            same.For([manual, maps, readme], "setup_gothic.exe"));
    }

    [Fact]
    public void A_patch_is_still_judged_against_its_own_installer()
    {
        // The first game's patch was covered by its installer and left off.
        // That says nothing about the next game's, which is judged again;
        // only what an add-on becomes carries over.
        TheSameAnswers same = TheSameAnswers.From(
            [(Patch("patch_a_game_1.0_to_1.1.exe"), false, WhatBecomesOfIt.AddOn)]);

        LeftBehind needed = Patch("patch_another_1.2_to_1.3.exe");
        LeftBehind covered = Patch("patch_another_1.1_to_1.2.exe");

        Assert.Equal([(needed, WhatBecomesOfIt.AddOn)], same.For([needed, covered], "setup_another_1.2.exe"));
    }

    [Fact]
    public void A_kind_the_first_game_did_not_have_gets_its_own_suggestion()
    {
        TheSameAnswers same = TheSameAnswers.From([(Kept("manual.pdf"), true, WhatBecomesOfIt.Manual)]);

        LeftBehind soundtrack = Kept("soundtrack.zip");

        Assert.Equal([(soundtrack, WhatBecomesOfIt.Extra)], same.For([soundtrack], "setup_gothic.exe"));
    }

    [Fact]
    public void A_folder_and_a_file_of_the_same_look_are_answered_apart()
    {
        TheSameAnswers same = TheSameAnswers.From([(Folder("Artbook"), true, WhatBecomesOfIt.BesideTheInstaller)]);

        LeftBehind maps = Folder("Maps");
        LeftBehind cover = Kept("cover.png");

        Assert.Equal(
            [(maps, WhatBecomesOfIt.BesideTheInstaller), (cover, WhatBecomesOfIt.Extra)],
            same.For([maps, cover], "setup_gothic.exe"));
    }

    [Fact]
    public void The_first_row_of_a_look_decides_it()
    {
        TheSameAnswers same = TheSameAnswers.From(
        [
            (Kept("cover.png"), true, WhatBecomesOfIt.Extra),
            (Kept("wallpaper.png"), false, WhatBecomesOfIt.BesideTheInstaller)
        ]);

        LeftBehind art = Kept("box art.png");

        Assert.Equal([(art, WhatBecomesOfIt.Extra)], same.For([art], "setup_gothic.exe"));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(1, "Answer the same way for the other game being added")]
    [InlineData(8, "Answer the same way for the other 8 games being added")]
    public void The_tick_is_only_there_when_a_game_comes_after(int after, string? says)
    {
        Assert.Equal(says, WhatWasLeftBehind.TheSameForTheRest(after));
    }

    [Fact]
    public void The_dialog_offers_the_tick_and_hands_its_answers_on()
    {
        // Nothing here can open the dialog, so it is read off disk.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml.cs"));
        string markup = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml"));

        Assert.Contains("TheSameForTheRestSays = WhatWasLeftBehind.TheSameForTheRest(after);", code, StringComparison.Ordinal);
        Assert.Contains("TheSameAnswers.From(asking.Files.Select(one => (one.What, one.Wanted && said == true, (WhatBecomesOfIt)one.Treatment)))", code, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding TheSameForTheRestSays, Converter={StaticResource WhenAnything}}\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding TheSameForTheRest}\"", markup, StringComparison.Ordinal);
    }

    private static LeftBehind Kept(string name) =>
        new(new ListedFile(name, 10), LeftBehindKind.LooksLikeSomethingToKeep);

    private static LeftBehind Patch(string name) =>
        new(new ListedFile(name, 10), LeftBehindKind.LooksLikeAnAddOn);

    private static LeftBehind Folder(string name) =>
        WhatWasLeftBehind.Gathered([Kept(name + (char)92 + "a.png")])[0];
}
