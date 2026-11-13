using System.Xml.Linq;
using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// The four answers "There is more in that folder" offers since 2.0.4, and
/// what each one does to the game. Designed on 2026-11-13 after xniwo's
/// discwright#98: "Copy it onto the disc" put a manual on the disc with no
/// button, and "Attach as an add-on" made it dead until the game was
/// installed and then tried to run a PDF.
/// </summary>
public sealed class WhatEachAnswerDoes
{
    private static readonly ListedFile Setup = new("setup_the_witcher.exe", 900);

    private static readonly GameFolder TheWitcher =
        new(@"D:\GOG\The Witcher", GameKind.GogInstaller, "The Witcher", "The Witcher", Setup, [Setup], [], null);

    [Fact]
    public void A_folder_beside_the_game_is_one_row_and_a_file_on_top_stays_its_own()
    {
        IReadOnlyList<LeftBehind> rows = WhatWasLeftBehind.Gathered(
        [
            Kept("manual.pdf", 40),
            Kept(@"Artbook\page 1.png", 10),
            Kept("patch_the_witcher_1.5.exe", 300, LeftBehindKind.LooksLikeAnAddOn),
            Kept(@"Artbook\sketches\page 2.png", 20),
            Kept(@"Maps\map.png", 5)
        ]);

        // In the order the folder listed them, a folder where its first file
        // was, so the dialog reads like the folder does.
        Assert.Equal(["manual.pdf", "Artbook", "patch_the_witcher_1.5.exe", "Maps"], rows.Select(row => row.Name));

        LeftBehind artbook = rows[1];
        Assert.True(artbook.IsAFolder);
        Assert.Equal(30, artbook.Bytes);
        Assert.Equal([@"Artbook\page 1.png", @"Artbook\sketches\page 2.png"], artbook.Inside!.Select(file => file.RelativePath));
        Assert.False(rows[0].IsAFolder);
        Assert.Equal(LeftBehindKind.LooksLikeAnAddOn, rows[2].Kind);
    }

    [Theory]
    [InlineData("The Witcher - manual.pdf", WhatBecomesOfIt.Manual)]
    [InlineData("user_guide.html", WhatBecomesOfIt.Manual)]
    [InlineData("MANUAL.CHM", WhatBecomesOfIt.Manual)]
    [InlineData("manual.jpg", WhatBecomesOfIt.Extra)]
    [InlineData("soundtrack.zip", WhatBecomesOfIt.Extra)]
    [InlineData("README.txt", WhatBecomesOfIt.Extra)]
    [InlineData("cover.png", WhatBecomesOfIt.Extra)]
    public void Suggests_the_manual_for_something_called_one_and_an_extra_for_the_rest(string name, WhatBecomesOfIt expected)
    {
        Assert.Equal(expected, Kept(name, 1).Suggested);
    }

    [Fact]
    public void Suggests_an_add_on_for_an_executable_and_for_a_folder_by_what_it_is_called()
    {
        Assert.Equal(WhatBecomesOfIt.AddOn, Kept("patch_the_witcher_1.5.exe", 1, LeftBehindKind.LooksLikeAnAddOn).Suggested);

        IReadOnlyList<LeftBehind> rows = WhatWasLeftBehind.Gathered([Kept(@"Manual\en.pdf", 1), Kept(@"Artbook\a.png", 1)]);

        // A folder called manual holds the manuals; any other is an extra.
        Assert.Equal(WhatBecomesOfIt.Manual, rows[0].Suggested);
        Assert.Equal(WhatBecomesOfIt.Extra, rows[1].Suggested);
    }

    [Fact]
    public void The_games_manual_and_its_extras_become_its_own_where_they_are_in_the_folder()
    {
        IReadOnlyList<LeftBehind> rows = WhatWasLeftBehind.Gathered(
            [Kept("The Witcher - manual.pdf", 40), Kept(@"Artbook\page.png", 10)]);

        (GameFolder game, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
            TheWitcher, [(rows[0], WhatBecomesOfIt.Manual), (rows[1], WhatBecomesOfIt.Extra)]);

        Assert.Equal(
            [
                new GameExtra(@"D:\GOG\The Witcher\The Witcher - manual.pdf", GameExtraKind.Manual),
                new GameExtra(@"D:\GOG\The Witcher\Artbook", GameExtraKind.Extra)
            ],
            game.OwnOrNone);

        // Neither goes in with the game's files, and nothing is attached.
        Assert.Equal([Setup], game.Files);
        Assert.Empty(addOns);
    }

    [Fact]
    public void Beside_the_installer_goes_in_with_the_games_files_a_folder_whole_and_with_no_button()
    {
        IReadOnlyList<LeftBehind> rows = WhatWasLeftBehind.Gathered(
            [Kept("README.txt", 4), Kept(@"Artbook\page 1.png", 10), Kept(@"Artbook\page 2.png", 10)]);

        (GameFolder game, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
            TheWitcher, [(rows[0], WhatBecomesOfIt.BesideTheInstaller), (rows[1], WhatBecomesOfIt.BesideTheInstaller)]);

        Assert.Equal(
            ["setup_the_witcher.exe", "README.txt", @"Artbook\page 1.png", @"Artbook\page 2.png"],
            game.Files.Select(file => file.RelativePath));
        Assert.Null(game.Own);
        Assert.Empty(addOns);
    }

    [Fact]
    public void An_add_on_is_attached_and_a_folder_answered_as_one_goes_on_as_an_extra()
    {
        LeftBehind patch = Kept("patch_the_witcher_1.5.exe", 300, LeftBehindKind.LooksLikeAnAddOn);
        LeftBehind maps = WhatWasLeftBehind.Gathered([Kept(@"Maps\map.png", 5)])[0];

        (GameFolder game, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
            TheWitcher, [(patch, WhatBecomesOfIt.AddOn), (maps, WhatBecomesOfIt.AddOn)]);

        // There is nothing in a folder to run, so it cannot install onto
        // anything; it is kept as something to look at instead.
        Assert.Equal([patch], addOns);
        Assert.Equal([new GameExtra(@"D:\GOG\The Witcher\Maps", GameExtraKind.Extra)], game.OwnOrNone);
    }

    [Fact]
    public void Keeps_what_the_game_already_had_of_its_own()
    {
        GameExtra had = new(@"E:\goodies\map.pdf", GameExtraKind.Extra);

        (GameFolder game, _) = WhatWasLeftBehind.Answered(
            TheWitcher with { Own = [had] }, [(Kept("manual.pdf", 1), WhatBecomesOfIt.Manual)]);

        Assert.Equal([had, new GameExtra(@"D:\GOG\The Witcher\manual.pdf", GameExtraKind.Manual)], game.OwnOrNone);
    }

    [Fact]
    public void Says_what_a_folder_holds_and_which_file_looks_like_the_manual()
    {
        LeftBehind artbook = WhatWasLeftBehind.Gathered([Kept(@"Artbook\a.png", 1), Kept(@"Artbook\b.png", 1)])[0];
        LeftBehind one = WhatWasLeftBehind.Gathered([Kept(@"Maps\a.png", 1)])[0];

        Assert.Equal("A folder of 2 files", artbook.ExplainedAgainst("setup_the_witcher.exe"));
        Assert.Equal("A folder of 1 file", one.ExplainedAgainst("setup_the_witcher.exe"));
        Assert.Equal("Looks like the game's manual", Kept("manual.pdf", 1).ExplainedAgainst("setup_the_witcher.exe"));
        Assert.Equal("Looks like something to read or look at", Kept("cover.png", 1).ExplainedAgainst("setup_the_witcher.exe"));
    }

    [Fact]
    public void The_headline_counts_rows_and_calls_them_things_once_a_folder_is_among_them()
    {
        Assert.Equal("There is one more file in that folder", WhatWasLeftBehind.Headline([Kept("a.txt", 1)]));
        Assert.Equal("There are 2 more files in that folder", WhatWasLeftBehind.Headline([Kept("a.txt", 1), Kept("b.txt", 1)]));
        Assert.Equal(
            "There are 2 more things in that folder",
            WhatWasLeftBehind.Headline(WhatWasLeftBehind.Gathered([Kept("a.txt", 1), Kept(@"Artbook\a.png", 1)])));
    }

    [Fact]
    public void The_dialog_offers_the_four_answers_in_the_order_it_reads_them_back_in()
    {
        // Nothing in this project can open the dialog, so it is read off disk:
        // the dropdown's index is cast to WhatBecomesOfIt, and two lists in a
        // different order would turn a manual into an add-on.
        XDocument markup = XDocument.Load(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Equal(
            ["The game's manual", "One of the game's extras", "Attach as an add-on", "Beside the installer, no button"],
            markup.Descendants(wpf + "ComboBoxItem").Select(item => (string?)item.Attribute("Content")));
        Assert.Equal(
            [WhatBecomesOfIt.Manual, WhatBecomesOfIt.Extra, WhatBecomesOfIt.AddOn, WhatBecomesOfIt.BesideTheInstaller],
            Enum.GetValues<WhatBecomesOfIt>());

        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml.cs"));
        Assert.Contains("WhatWasLeftBehind.Gathered(left)", code, StringComparison.Ordinal);
        Assert.Contains("_treatment = (int)what.Suggested;", code, StringComparison.Ordinal);
        Assert.Contains("(WhatBecomesOfIt)one.Treatment", code, StringComparison.Ordinal);
        Assert.Contains("WhatWasLeftBehind.Answered(", code, StringComparison.Ordinal);
    }

    private static LeftBehind Kept(string path, long bytes, LeftBehindKind kind = LeftBehindKind.LooksLikeSomethingToKeep) =>
        new(new ListedFile(path, bytes), kind);
}
