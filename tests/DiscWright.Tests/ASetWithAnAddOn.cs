using DiscWright.Core;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// A game too big for one disc, with an add-on beside it.
/// </summary>
/// <remarks>
/// Reported against v1 on 2026-11-09 (discwright#181): a GOG game bigger than
/// 100 GB with an exe add-on. Adding the add-on switched v1's disc set option
/// off and the game could no longer be made into several discs. Here there is
/// no such option to switch: a game that does not fit one disc is a set, and
/// an add-on is planned beside its game like any other file. Nothing asked it
/// until the report.
/// </remarks>
public class ASetWithAnAddOn
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void Is_still_a_set_once_an_add_on_is_added()
    {
        PlannedOutput output = Assert.Single(Planned(withTheAddOn: true).Outputs);

        Assert.True(output.Layout.Discs.Count > 1, "the game no longer became a set");
    }

    [Fact]
    public void Carries_the_add_on_on_one_of_the_discs_of_the_set()
    {
        PlannedOutput output = Assert.Single(Planned(withTheAddOn: true).Outputs);

        Assert.Single(
            output.Layout.Discs.SelectMany(disc => disc.Files),
            file => file.RelativePath.EndsWith("dlc_big_game_soundtrack.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void Makes_the_same_number_of_discs_or_one_more_than_without_it()
    {
        int without = Assert.Single(Planned(withTheAddOn: false).Outputs).Layout.Discs.Count;
        int with = Assert.Single(Planned(withTheAddOn: true).Outputs).Layout.Discs.Count;

        Assert.InRange(with, without, without + 1);
    }

    private static PlannedBuild Planned(bool withTheAddOn)
    {
        // An installer and four data files of 30 MB each, on a 100 MB disc.
        ListedFile installer = new("setup_big_game_1.0_(100).exe", 30 * MB);
        ListedFile[] files =
        [
            installer,
            .. Enumerable.Range(1, 4).Select(at => new ListedFile($"setup_big_game_1.0_(100)-{at}.bin", 30 * MB))
        ];

        GameFolder game = new(@"C:\games\Big Game", GameKind.GogInstaller, "Big Game", "Big Game", installer, files, [], null);

        DiscProject project = new(
            "BIG GAME",
            [game],
            new DiscOptions(),
            AddOns: withTheAddOn ? [new AddOn(0, @"C:\games\Big Game\dlc_big_game_soundtrack.exe", "Soundtrack", 20 * MB)] : []);

        return Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(project, new Medium("TEST", "a test disc", 100 * MB), _ => 0)).Value;
    }
}
