using DiscWright.Core;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// An add-on kept somewhere other than its game's folder, attached to the
/// game that is picked in the list.
/// </summary>
/// <remarks>
/// Reported against 2.0.4 on Reddit: add-ons in a separate folder were not
/// added under the game's Install button. Adding that folder made a game of
/// it, and the question that stops that said to "add the game first and
/// attach this to it" when nothing in the window attached anything.
/// </remarks>
public class AttachingAnAddOnByHand
{
    private const string Dlc = @"D:\DLC\setup_two_dlc.exe";

    [Fact]
    public void Attaches_nothing_with_no_game_picked()
    {
        MakeADisc window = TwoGames();

        Assert.Null(window.ThePickedGame);

        window.AttachToThePickedGame([Dlc]);

        Assert.Empty(window.AsAProject().AddOnsOrNone);
    }

    [Fact]
    public void Attaches_it_to_the_picked_game_and_to_no_other()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);

        window.AttachToThePickedGame([Dlc]);

        AddOn attached = Assert.Single(window.AsAProject().AddOnsOrNone);

        Assert.Equal(1, attached.BelongsTo);
        Assert.Equal(Dlc, attached.Path);

        // Named the way every add-on is, from its file, and sized by asking
        // the machine, which answers a thousand in this rig.
        Assert.Equal("two dlc", attached.Name);
        Assert.Equal(1000, attached.Bytes);

        // And the line under the list is still on the game it went to,
        // ready for the next one.
        Assert.Equal("two", window.ThePickedGame);
    }

    [Fact]
    public void Brings_the_parts_beside_it_and_nobody_elses()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);

        window.AttachToThePickedGame([Dlc]);

        Assert.Equal(
            [
                new AddOnPart(@"D:\DLC\setup_two_dlc-1.bin", 700),
                new AddOnPart(@"D:\DLC\setup_two_dlc-2.bin", 300)
            ],
            Assert.Single(window.AsAProject().AddOnsOrNone).PartsOrNone);
    }

    [Fact]
    public void Takes_several_at_once()
    {
        MakeADisc window = TwoGames();
        Pick(window, 0);

        window.AttachToThePickedGame([Dlc, @"D:\DLC\patch_two_1.0_to_1.1.exe"]);

        Assert.Equal(
            [(0, "two dlc"), (0, "Update 1.1")],
            window.AsAProject().AddOnsOrNone.Select(addOn => (addOn.BelongsTo, addOn.Name)));
    }

    [Fact]
    public void Does_not_attach_one_twice_however_its_path_is_typed()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);

        window.AttachToThePickedGame([Dlc, Dlc]);
        window.AttachToThePickedGame([Dlc.ToUpperInvariant()]);

        Assert.Single(window.AsAProject().AddOnsOrNone);
    }

    [Fact]
    public void Does_not_attach_a_game_s_own_installer_to_it()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);

        // It is the game. Attached to itself it would be on its own disc
        // twice, once to install from and once as a button that installs it
        // again.
        window.AttachToThePickedGame([@"C:\games\two\setup_two.exe", @"C:\GAMES\TWO\SETUP_TWO-1.BIN"]);

        Assert.Empty(window.AsAProject().AddOnsOrNone);
    }

    [Fact]
    public void Does_not_attach_a_part_that_an_add_on_already_carries()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);

        // The picker's "Anything" lets a part be chosen. Its installer has
        // it already, and twice is two files wanting one name.
        window.AttachToThePickedGame([Dlc]);
        window.AttachToThePickedGame([@"D:\DLC\SETUP_TWO_DLC-1.bin"]);

        Assert.Equal(Dlc, Assert.Single(window.AsAProject().AddOnsOrNone).Path);
    }

    [Fact]
    public void Lets_two_games_have_the_same_one()
    {
        MakeADisc window = TwoGames();

        Pick(window, 0);
        window.AttachToThePickedGame([Dlc]);
        Pick(window, 1);
        window.AttachToThePickedGame([Dlc]);

        // What one game has says nothing about the other, whose Add-ons is
        // another folder.
        Assert.Equal([0, 1], window.AsAProject().AddOnsOrNone.Select(addOn => addOn.BelongsTo));
    }

    [Fact]
    public void Shows_it_as_a_ticked_row_that_can_be_left_off_or_taken_off()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);
        window.AttachToThePickedGame([Dlc]);

        AThingOnTheDisc row = Assert.Single(
            window.WhatGoesOnIt[1].EveryThing(), each => each.What == TheTreeOfWhatGoesOn.AnAddOn);

        Assert.Equal(Dlc, row.ItsPath);
        Assert.True(row.IsOn);

        // All of it at one size, the installer's thousand and its parts.
        Assert.Equal(2000, row.Bytes);

        row.IsOn = false;
        Assert.Empty(window.AsAProject().AddOnsOrNone);

        row.IsOn = true;
        Assert.Single(window.AsAProject().AddOnsOrNone);

        window.TakeOff(row);
        Assert.Empty(window.AsAProject().AddOnsOrNone);
        Assert.DoesNotContain(window.WhatGoesOnIt[1].EveryThing(), each => each.What == TheTreeOfWhatGoesOn.AnAddOn);
    }

    [Fact]
    public void Lands_in_its_game_s_Add_ons_on_the_disc_with_its_parts()
    {
        MakeADisc window = TwoGames();
        Pick(window, 1);
        window.AttachToThePickedGame([Dlc]);

        PlannedOutput output = Assert.Single(Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(window.AsAProject(), new Medium("TEST", "a test disc", 100 * 1024 * 1024), _ => 0)).Value.Outputs);

        Assert.Equal(
            [
                (@"02 - two\Add-ons\setup_two_dlc-1.bin", @"D:\DLC\setup_two_dlc-1.bin"),
                (@"02 - two\Add-ons\setup_two_dlc-2.bin", @"D:\DLC\setup_two_dlc-2.bin"),
                (@"02 - two\Add-ons\setup_two_dlc.exe", Dlc)
            ],
            output.Layout.Discs.SelectMany(disc => disc.Files)
                .Where(file => file.RelativePath.Contains("Add-ons", StringComparison.Ordinal))
                .Select(file => (file.RelativePath, file.Source))
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    private static MakeADisc TwoGames()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New(disk: new ADiskWithADlcFolder());

        window.Add(TheMainWindowWithoutAWindow.AGame("one", 1000, parts: 1));
        window.Add(TheMainWindowWithoutAWindow.AGame("two", 1000, parts: 1));

        return window;
    }

    /// <summary>Picks a row of the game, the way a click in the list does.</summary>
    private static void Pick(MakeADisc window, int game) =>
        window.WhatIsSelected = window.WhatGoesOnIt[game].Inside[0];

    private sealed class ADiskWithADlcFolder : IFileSystem
    {
        public bool FolderExists(string path) => string.Equals(path, @"D:\DLC", StringComparison.OrdinalIgnoreCase);

        public FolderListing List(string path) => new(
            path,
            FolderExists(path)
                ?
                [
                    new ListedFile("setup_two_dlc.exe", 2),
                    new ListedFile("setup_two_dlc-2.bin", 300),
                    new ListedFile("setup_two_dlc-1.bin", 700),
                    new ListedFile("setup_two_dlc_other-1.bin", 50),
                    new ListedFile("patch_two_1.0_to_1.1.exe", 30),
                    new ListedFile(@"deeper\setup_two_dlc-3.bin", 10)
                ]
                : []);
    }
}
