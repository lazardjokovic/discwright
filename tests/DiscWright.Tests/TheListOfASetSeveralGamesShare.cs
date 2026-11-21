using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// What the file every disc of a set carries says about the set, when
/// several games share it.
/// </summary>
/// <remarks>
/// A set of one game says no disc of it can install the game. Said of a set
/// several games share, that sends somebody off to copy three discs to a hard
/// drive to install a game that was whole on the one in their hand.
/// </remarks>
public class TheListOfASetSeveralGamesShare
{
    [Fact]
    public void Says_which_discs_each_game_is_on_and_that_one_on_a_single_disc_installs_from_it()
    {
        string file = Written();

        Assert.Contains(
            "This set holds 3 games, each in a folder of its own. The menu on any disc\nlists them all and asks for the disc a game is on.",
            file,
            StringComparison.Ordinal);

        Assert.Contains(
            "  game            is on\n"
                + "  Harbour Tales   disc 1\n"
                + "  Lanternhold     discs 2 and 3\n"
                + "  Quiet Orchard   disc 3\n",
            file,
            StringComparison.Ordinal);

        Assert.Contains(
            "A game that is all on one disc installs from that disc, like a game on a\ndisc of its own.",
            file,
            StringComparison.Ordinal);

        // What was true of one game with a set to itself is not said here.
        Assert.DoesNotContain("This disc cannot install the game on its own", file, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy the contents of every disc in the set", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_how_a_game_spread_over_discs_is_put_back_by_hand_and_that_the_menu_does_it()
    {
        string file = Written();

        Assert.Contains(
            "A game spread over several discs cannot install from any one of them. To\n"
                + "put one back by hand:\n"
                + "\n"
                + "  1. Make one empty folder on a hard drive.\n"
                + "  2. Copy that game's folder off every disc it is on into that one folder,\n"
                + "     letting Windows merge them. The order does not matter.\n"
                + "  3. Run the installer from that folder, never from a disc.\n"
                + "\n"
                + "The menu does all of that for you: press that game's button on any disc,\n"
                + "put the others in when it asks, and it installs from the folder. This\n"
                + "file is here so it can be done by hand as well.\n",
            file,
            StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_file_that_was_cut_putting_it_back_together_is_a_step_of_its_own()
    {
        string file = Written(joins: [new SetJoin("02 - Lanternhold/big.bin", "ffff", 9, ["02 - Lanternhold/big.bin.dwpart01", "02 - Lanternhold/big.bin.dwpart02"])]);

        Assert.Contains(
            "  3. Put the files that were cut to fit back together. How is below.\n"
                + "  4. Run the installer from that folder, never from a disc.\n",
            file,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Says_where_each_games_own_manual_and_extras_are_and_does_not_count_them_as_the_game()
    {
        string file = Written();

        // Harbour Tales installs from disc 1 although its extras are on
        // disc 2: they are for reading, and are listed apart.
        Assert.Contains(
            "A game's own manual and extras are in its folder, on one disc with it:\n"
                + "\n"
                + "  Harbour Tales   disc 2\n"
                + "  Lanternhold     disc 3\n",
            file,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Says_nothing_of_spreading_when_every_game_is_on_one_disc_and_nothing_of_manuals_when_there_are_none()
    {
        string file = SetManifest.For(
            "TWO",
            [new SetEntry(1, "01 - First/setup.exe", "aa", 5), new SetEntry(2, "02 - Second/setup.exe", "bb", 6)],
            1, 2, "DVD", DateTimeOffset.UnixEpoch, null,
            [new SetGame("First", "01 - First"), new SetGame("Second", "02 - Second", [])]);

        Assert.Contains("  First    disc 1\n  Second   disc 2\n", file, StringComparison.Ordinal);
        Assert.Contains("A game that is all on one disc installs from that disc", file, StringComparison.Ordinal);
        Assert.DoesNotContain("spread over several discs", file, StringComparison.Ordinal);
        Assert.DoesNotContain("by hand", file, StringComparison.Ordinal);
        Assert.DoesNotContain("manual and extras", file, StringComparison.Ordinal);
    }

    [Fact]
    public void One_game_and_no_games_named_say_what_a_set_always_said()
    {
        SetEntry[] entries = [new(1, "setup.exe", "aa", 5), new(2, "setup-1.bin", "bb", 6)];

        string asItWas = SetManifest.For("BIG", entries, 1, 2, "DVD", DateTimeOffset.UnixEpoch);

        Assert.Contains("This disc cannot install the game on its own, and neither can any other", asItWas, StringComparison.Ordinal);
        Assert.Contains("  2. Copy the contents of every disc in the set into that one folder.", asItWas, StringComparison.Ordinal);
        Assert.Contains("press Install on", asItWas, StringComparison.Ordinal);

        Assert.Equal(asItWas, SetManifest.For("BIG", entries, 1, 2, "DVD", DateTimeOffset.UnixEpoch, null, null));
        Assert.Equal(asItWas, SetManifest.For("BIG", entries, 1, 2, "DVD", DateTimeOffset.UnixEpoch, null, []));
        Assert.Equal(asItWas, SetManifest.For("BIG", entries, 1, 2, "DVD", DateTimeOffset.UnixEpoch, null, [new SetGame("Big", string.Empty)]));
    }

    [Fact]
    public void Folders_are_compared_the_way_Windows_compares_them_and_a_longer_name_is_another_game()
    {
        string file = SetManifest.For(
            "TWO",
            [
                new SetEntry(1, @"01 - a\SETUP.EXE", "aa", 5),
                new SetEntry(2, "01 - AB/setup.exe", "bb", 6),
                new SetEntry(3, "01 - AB/setup-1.bin", "cc", 6),
            ],
            1, 3, "DVD", DateTimeOffset.UnixEpoch, null,
            [new SetGame("A", "01 - A"), new SetGame("AB", @"01 - AB\"), new SetGame("Nowhere", "09 - Nowhere")]);

        Assert.Contains(
            "  A         disc 1\n  AB        discs 2 and 3\n  Nowhere   no disc of this set\n",
            file,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { 4 }, "disc 4")]
    [InlineData(new[] { 1, 2 }, "discs 1 and 2")]
    [InlineData(new[] { 2, 3, 4 }, "discs 2 to 4")]
    [InlineData(new[] { 9, 10, 11, 12 }, "discs 9 to 12")]
    [InlineData(new[] { 1, 3, 4 }, "discs 1, 3 and 4")]
    public void Discs_are_said_as_the_menu_says_them(int[] discs, string said)
    {
        string file = SetManifest.For(
            "TWO",
            [.. discs.Select(disc => new SetEntry(disc, "01 - A/part" + disc + ".bin", "aa", 5)), new SetEntry(1, "02 - B/setup.exe", "bb", 1)],
            1, discs.Max(), "DVD", DateTimeOffset.UnixEpoch, null,
            [new SetGame("A", "01 - A"), new SetGame("B", "02 - B")]);

        Assert.Contains("  A      " + said + "\n", file, StringComparison.Ordinal);
    }

    [Fact]
    public void None_of_it_is_taken_for_a_row_even_when_a_game_is_named_with_a_number_and_a_star()
    {
        string file = SetManifest.For(
            "TWO",
            [new SetEntry(1, "01 - 7 Days/setup.exe", "aa", 5), new SetEntry(2, "02 - 2 *s/setup.exe", "bb", 6), new SetEntry(1, "02 - 2 *s/Manual/m.pdf", "cc", 1)],
            1, 2, "DVD", DateTimeOffset.UnixEpoch, null,
            [new SetGame("7 Days", "01 - 7 Days"), new SetGame("2 *s", "02 - 2 *s", ["02 - 2 *s/Manual/m.pdf"])]);

        // Three readers take a row to be a line that starts with a digit
        // and holds a star. Exactly the three files are that, and nothing
        // said about the games is.
        string[] rows = [.. file.Split('\n').Where(line => line.Length > 0 && char.IsDigit(line[0]))];

        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Contains(" *", row, StringComparison.Ordinal));
        Assert.Contains("  7 Days   disc 1\n  2 *s     disc 2\n", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_disc_says_the_same_about_the_games()
    {
        static string About(string file) =>
            file[file.IndexOf("This set holds", StringComparison.Ordinal)..file.IndexOf("On this disc:", StringComparison.Ordinal)];

        Assert.Equal(About(Written(1)), About(Written(2)));
        Assert.Equal(About(Written(1)), About(Written(3)));
        Assert.Contains("On this disc: 1 of the 8 files.", Written(1), StringComparison.Ordinal);
        Assert.Contains("On this disc: 4 of the 8 files.", Written(3), StringComparison.Ordinal);
    }

    private static string Written(int thisDisc = 1, IReadOnlyList<SetJoin>? joins = null) =>
        SetManifest.For(
            "THREE GAMES",
            [
                new SetEntry(1, "01 - Harbour Tales/setup_harbour_tales.exe", "a1", 100),
                new SetEntry(2, "01 - Harbour Tales/Extras/book.zip", "a2", 10),
                new SetEntry(2, "02 - Lanternhold/setup_lanternhold.exe", "b1", 100),
                new SetEntry(2, "02 - Lanternhold/setup_lanternhold-1.bin", "b2", 100),
                new SetEntry(3, "02 - Lanternhold/setup_lanternhold-2.bin", "b3", 100),
                new SetEntry(3, "02 - Lanternhold/Manual/manual.pdf", "b4", 1),
                new SetEntry(3, "03 - Quiet Orchard/setup_quiet_orchard.exe", "c1", 100),
                new SetEntry(3, "03 - Quiet Orchard/Add-ons/patch.exe", "c2", 10),
            ],
            thisDisc, 3, "DVD9 8.5 GB", DateTimeOffset.UnixEpoch, joins,
            [
                new SetGame("Harbour Tales", "01 - Harbour Tales", [@"01 - Harbour Tales\Extras\book.zip"]),
                new SetGame("Lanternhold", "02 - Lanternhold", ["02 - Lanternhold/Manual/manual.pdf"]),
                new SetGame("Quiet Orchard", "03 - Quiet Orchard"),
            ]);
}
