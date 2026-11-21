using System.Diagnostics;
using System.Text;
using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Platform;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// The menu on a set that several games share: every disc lists every game,
/// and each game's button does what is right for where that game sits.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The discs are real.</strong> Built by the builder into folders,
/// with the menu, the set's list and every file where a burned disc would
/// have them, and then the page that disc carries is run under cscript with
/// a document stood in for it. A disc is swapped by pointing the page at
/// another folder, which is all a swap is to it: the same letter with
/// another disc behind it. So the C# that writes a set and the script that
/// reads one are checked against each other on every run, and not against
/// what a test supposes the other writes.
/// </para>
/// <para>
/// The games are made up, on purpose. Whether a game is installed is asked
/// of the registry of the machine the suite runs on, and the owner's has
/// real games on it.
/// </para>
/// </remarks>
public sealed class TheMenuOnASetSeveralGamesShare : IDisposable
{
    private const long Overhead = 2L * 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwmenu_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;
    private readonly string _hdd;
    private readonly string _run;

    public TheMenuOnASetSeveralGamesShare()
    {
        _out = Path.Combine(_root, "out");
        _hdd = Path.Combine(_root, "hdd");
        _run = Path.Combine(_root, "run");
        Directory.CreateDirectory(_out);
        Directory.CreateDirectory(_hdd);
        Directory.CreateDirectory(_run);
        File.Copy(TheMenuWriter.MenuScriptInTheSourceTree(), Path.Combine(_run, "menu.js"));
    }

    public void Dispose()
    {
        try
        {
            foreach (FileInfo file in new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ============================================================ what decides

    [Fact]
    public void A_path_is_under_another_the_way_Windows_compares_names_and_nothing_is_under_nothing()
    {
        Assert.Equal(
            ["true", "true", "true", "false", "false", "false", "false"],
            Words(
                "isAtOrUnder('01 - A/setup.exe', '01 - A')",
                "isAtOrUnder('01 - A/Extras/Book/one.png', '01 - a/extras/book')",
                "isAtOrUnder('01 - A', '01 - A')",

                // The next game's folder begins with the same letters.
                "isAtOrUnder('01 - AB/setup.exe', '01 - A')",
                "isAtOrUnder('02 - B/setup.exe', '01 - A')",

                // Nothing is under the empty path, or every file on the set
                // would be the game's with no folder of its own.
                "isAtOrUnder('01 - A/setup.exe', '')",
                "isAtOrUnder('', '01 - A')"));
    }

    private const string ThreeGamesRows =
        "var rows = ["
        + "{ disc: 1, path: '01 - A/setup_a.exe', bytes: 1 }, { disc: 1, path: '01 - A/setup_a-1.bin', bytes: 4000 },"
        + "{ disc: 2, path: '01 - A/Extras/book.zip', bytes: 1210 },"
        + "{ disc: 2, path: '02 - B/setup_b.exe', bytes: 28 }, { disc: 2, path: '02 - B/big.bin.dwpart01', bytes: 3000 },"
        + "{ disc: 3, path: '02 - B/big.bin.dwpart02', bytes: 900 }, { disc: 3, path: '02 - B/Manual/b.pdf', bytes: 3 },"
        + "{ disc: 3, path: '03 - C/setup_c.exe', bytes: 1160 }, { disc: 3, path: '03 - C/Add-ons/patch.exe', bytes: 620 }];"
        + "var joins = [{ path: '02 - B/big.bin', bytes: 3900, sha256: 'x', pieces: ['02 - B/big.bin.dwpart01', '02 - B/big.bin.dwpart02'] }];"
        + "var A = { name: 'A', folder: '01 - A', apart: ['01 - A/Extras/book.zip'] };"
        + "var B = { name: 'B', folder: '02 - B', apart: ['02 - B/Manual/b.pdf'] };"
        + "var C = { name: 'C', folder: '03 - C', apart: [] };"
        + "function paths(share) { var p = []; for (var i = 0; i < share.rows.length; i++) { p.push(share.rows[i].path); } return p.join('|'); }";

    [Fact]
    public void A_games_share_is_what_is_under_its_folder_less_its_own_manual_and_extras()
    {
        Assert.Equal(
            [
                "01 - A/setup_a.exe|01 - A/setup_a-1.bin / discs 1 / joins 0",
                "02 - B/setup_b.exe|02 - B/big.bin.dwpart01|02 - B/big.bin.dwpart02 / discs 2,3 / joins 1",
                "03 - C/setup_c.exe|03 - C/Add-ons/patch.exe / discs 3 / joins 0",

                // A game's folder written in another case on the disc is the
                // same folder, as it is to Windows.
                "01 - A/setup_a.exe|01 - A/setup_a-1.bin / discs 1 / joins 0",

                // A game the list knows nothing of has nothing.
                " / discs  / joins 0",
            ],
            Words(
                ThreeGamesRows,
                "(function (s) { return paths(s) + ' / discs ' + s.discs.join(',') + ' / joins ' + s.joins.length; })(aGamesShare(rows, joins, A, 3))",
                "(function (s) { return paths(s) + ' / discs ' + s.discs.join(',') + ' / joins ' + s.joins.length; })(aGamesShare(rows, joins, B, 3))",
                "(function (s) { return paths(s) + ' / discs ' + s.discs.join(',') + ' / joins ' + s.joins.length; })(aGamesShare(rows, joins, C, 3))",
                "(function (s) { return paths(s) + ' / discs ' + s.discs.join(',') + ' / joins ' + s.joins.length; })(aGamesShare(rows, joins, { folder: '01 - a', apart: ['01 - a/EXTRAS/BOOK.ZIP'] }, 3))",
                "(function (s) { return paths(s) + ' / discs ' + s.discs.join(',') + ' / joins ' + s.joins.length; })(aGamesShare(rows, joins, { folder: '09 - Nobody' }, 3))"));
    }

    [Fact]
    public void One_game_on_a_set_has_all_of_it_whatever_folder_it_is_in()
    {
        Assert.Equal(
            ["9 / 1,2,3 / 1", "9 / 1,2,3 / 1", "9 / 1,2,3 / 1", "0 /  / 0"],
            Words(
                ThreeGamesRows,
                "(function (s) { return s.rows.length + ' / ' + s.discs.join(',') + ' / ' + s.joins.length; })(aGamesShare(rows, joins, { folder: '' }, 1))",
                "(function (s) { return s.rows.length + ' / ' + s.discs.join(',') + ' / ' + s.joins.length; })(aGamesShare(rows, joins, { folder: 'Tidied' }, 1))",
                "(function (s) { return s.rows.length + ' / ' + s.discs.join(',') + ' / ' + s.joins.length; })(aGamesShare(rows, joins, null, 1))",
                "(function (s) { return s.rows.length + ' / ' + s.discs.join(',') + ' / ' + s.joins.length; })(aGamesShare(null, null, { folder: '' }, 1))"));
    }

    [Fact]
    public void Where_a_share_is_follows_the_disc_in_the_drive()
    {
        Assert.Equal(
            [
                "here", "elsewhere", "elsewhere",
                "gathered", "gathered", "gathered",
                "elsewhere", "elsewhere", "here",

                // One game on a set is gathered, as it always was, even when
                // a list were to put all of it on the disc in the drive.
                "gathered",

                // And so is a game the list knows nothing of: there is no
                // disc to ask for, and the line says the list is missing.
                "gathered", "gathered",
            ],
            Words(
                ThreeGamesRows,
                "whereAShareIs(aGamesShare(rows, joins, A, 3), 1, 3)",
                "whereAShareIs(aGamesShare(rows, joins, A, 3), 2, 3)",
                "whereAShareIs(aGamesShare(rows, joins, A, 3), 3, 3)",
                "whereAShareIs(aGamesShare(rows, joins, B, 3), 1, 3)",
                "whereAShareIs(aGamesShare(rows, joins, B, 3), 2, 3)",
                "whereAShareIs(aGamesShare(rows, joins, B, 3), 3, 3)",
                "whereAShareIs(aGamesShare(rows, joins, C, 3), 1, 3)",
                "whereAShareIs(aGamesShare(rows, joins, C, 3), 2, 3)",
                "whereAShareIs(aGamesShare(rows, joins, C, 3), 3, 3)",
                "whereAShareIs({ rows: [rows[0]], joins: [], discs: [1] }, 1, 1)",
                "whereAShareIs(aGamesShare(rows, joins, { folder: '09 - Nobody' }, 3), 1, 3)",
                "whereAShareIs(null, 1, 3)"));
    }

    [Fact]
    public void Discs_are_said_the_way_a_person_says_them()
    {
        Assert.Equal(
            ["", "", "disc 3", "discs 1 and 2", "discs 2 to 4", "discs 10 to 13", "discs 1, 3 and 4", "discs 2 and 9"],
            Words(
                "'' + discsInWords([])",
                "'' + discsInWords(null)",
                "discsInWords([3])",
                "discsInWords([1, 2])",
                "discsInWords([2, 3, 4])",
                "discsInWords([10, 11, 12, 13])",
                "discsInWords([1, 3, 4])",
                "discsInWords([2, 9])"));
    }

    [Fact]
    public void The_button_says_where_the_game_is_and_fits_on_a_button()
    {
        string[] said = Words(
            ThreeGamesRows,
            "var a = aGamesShare(rows, joins, A, 3), b = aGamesShare(rows, joins, B, 3);",
            "aSharedSetsButtonSays('here', a, null, false, 'Install')",
            "aSharedSetsButtonSays('elsewhere', a, null, false, 'Install')",
            "aSharedSetsButtonSays('gathered', b, { complete: false }, false, 'Install')",
            "aSharedSetsButtonSays('gathered', b, { complete: true }, false, 'Install')",
            "aSharedSetsButtonSays('here', a, null, true, 'Install')",
            "aSharedSetsButtonSays('elsewhere', a, null, true, 'Install')",
            "aSharedSetsButtonSays('gathered', b, { complete: false }, true, 'Install')",
            "aSharedSetsButtonSays('here', a, null, false, 'Copy and play')",
            "aSharedSetsButtonSays('elsewhere', a, null, false, 'Copy and play')",
            "aSharedSetsButtonSays('gathered', b, { complete: false }, false, 'Copy and open')",
            "aSharedSetsButtonSays('gathered', { rows: [], joins: [], discs: [] }, { complete: false }, false, 'Install')",
            "aSharedSetsButtonSays('gathered', null, null, false, '')",

            // The longest any of them gets on a set of a dozen discs.
            "aSharedSetsButtonSays('elsewhere', { discs: [12] }, null, false, 'Copy and play')",
            "aSharedSetsButtonSays('gathered', { discs: [3, 4, 5, 6, 7, 8, 9] }, null, false, 'Copy and play')");

        Same(
            [
                "Install",
                "Install from disc 1",
                "Install from 2 discs",
                "Install",
                "Play",
                "Play",
                "Play",
                "Copy and play",
                "Copy and play from disc 1",
                "Copy and open from 2 discs",
                "Install",
                "Install",
                "Copy and play from disc 12",
                "Copy and play from 7 discs",
            ],
            said);

        // Twenty-six is what shortEnoughForAButton holds a button to.
        Assert.All(said, each => Assert.True(each.Length <= 26, each));
    }

    [Fact]
    public void The_line_and_the_hover_say_which_disc_and_nothing_for_a_game_on_this_one()
    {
        Assert.Equal(
            [
                "C is on disc 3, not on this one. The button above asks for it, and carries on by itself once it is in.",
                "",
                "",
                "",
                "On disc 1",
                "On discs 2 and 3",
                "",
                "",
            ],
            Words(
                ThreeGamesRows,
                "aSharedSetsLineSays('elsewhere', aGamesShare(rows, joins, C, 3), 'C')",
                "'' + aSharedSetsLineSays('here', aGamesShare(rows, joins, C, 3), 'C')",
                "'' + aSharedSetsLineSays('gathered', aGamesShare(rows, joins, B, 3), 'B')",
                "'' + aSharedSetsLineSays('elsewhere', { rows: [], joins: [], discs: [] }, 'C')",
                "whereAGameIsSays(aGamesShare(rows, joins, A, 3))",
                "whereAGameIsSays(aGamesShare(rows, joins, B, 3))",
                "'' + whereAGameIsSays({ rows: [], joins: [], discs: [] })",
                "'' + whereAGameIsSays(null)"));
    }

    [Fact]
    public void Which_discs_hold_a_file_or_a_folder_and_what_is_said_when_it_is_not_this_one()
    {
        Assert.Equal(
            [
                "3",
                "2",
                "1,2",
                "",
                "The manual of B is on disc 3. Put disc 3 in and press this again.",
                "The extras of A are on discs 1 and 2. Put one of them in and press this again.",
                "The extras of A are spread over discs 1 and 2. This opened what the disc in the drive holds.",
            ],
            Words(
                ThreeGamesRows,
                "whichDiscsHold(rows, '02 - B/Manual/b.pdf').join(',')",
                "whichDiscsHold(rows, '01 - A/Extras').join(',')",
                "whichDiscsHold(rows, '01 - a').join(',')",
                "'' + whichDiscsHold(rows, 'Manual/disc.pdf').join(',')",
                "itIsOnAnotherDiscSays('The manual of B', [3], false)",
                "itIsOnAnotherDiscSays('The extras of A', [1, 2], true)",
                "someOfItIsElsewhereSays('The extras of A', [1, 2])"));
    }

    [Fact]
    public void An_add_on_is_its_file_and_its_parts_and_the_set_says_where_they_are()
    {
        Assert.Equal(
            [
                "X/a.exe|X/a-1.bin|X/b.exe",

                // Whole on one disc.
                "rows 1 joins 0 discs 3 cut false known true",

                // Its installer on one disc and its part on the next.
                "rows 2 joins 0 discs 2,3 cut false known true",

                // Cut to fit: on no disc whole, and its pieces are what the
                // discs hold.
                "rows 2 joins 1 discs 2,3 cut true known true",

                // Written in another case, the same file.
                "rows 1 joins 0 discs 3 cut false known true",

                // One the list has not got, and nothing asked about.
                "rows 0 joins 0 discs  cut false known false",
                "rows 0 joins 0 discs  cut false known false",
            ],
            Words(
                ThreeGamesRows,
                "function w(f) { return 'rows ' + f.rows.length + ' joins ' + f.joins.length + ' discs ' + f.discs.join(',') + ' cut ' + f.cut + ' known ' + f.known; }",
                "whatAddOnsAreMadeOf([{ runs: 'X/a.exe', parts: ['X/a-1.bin'] }, { runs: 'X/b.exe' }]).join('|')",
                "w(whereTheyAreInTheSet(rows, joins, ['03 - C/Add-ons/patch.exe']))",
                "w(whereTheyAreInTheSet(rows, joins, ['02 - B/setup_b.exe', '03 - C/setup_c.exe']))",
                "w(whereTheyAreInTheSet(rows, joins, ['02 - B/big.bin']))",
                "w(whereTheyAreInTheSet(rows, joins, ['03 - c/ADD-ONS/PATCH.EXE']))",
                "w(whereTheyAreInTheSet(rows, joins, ['03 - C/Add-ons/never.exe']))",
                "w(whereTheyAreInTheSet(rows, joins, []))"));
    }

    [Fact]
    public void The_install_page_says_which_discs_a_game_is_on_and_as_it_did_for_a_game_with_the_set_to_itself()
    {
        string[] said = Words(
            "installPageSays('Lanternhold', 'D:/Lanternhold installer', 1048576, 5242880, { complete: false, ok: 0 }, 3, [2, 3]).says",
            "installPageSays('Big Game', 'D:/Big Game installer', 1048576, 5242880, { complete: false, ok: 0 }, 3).says",
            "installPageSays('Big Game', 'D:/Big Game installer', 1048576, 5242880, { complete: false, ok: 0 }, 3, null).says",
            "installPageSays('Big Game', 'D:/Big Game installer', 1048576, 5242880, { complete: false, ok: 0 }, 3, []).says",
            "(function (p) { return p.headline + '|' + p.says + '|' + p.go + '|' + p.note + '|' + p.short; })(anAddOnsPageSays('The North Isles', 'D:/Second installer', 1048576, 5242880, { ok: 0 }, [1, 2]))",
            "(function (p) { return p.says + '|' + p.short; })(anAddOnsPageSays('The North Isles', 'D:/Second installer', 5242880, 1048576, { ok: 1 }, [2, 3, 4]))",
            "anAddOnsPageSays('The North Isles', 'D:/Second installer', 1048576, -1, null, [1, 2]).says");

        Assert.Equal(
            "Lanternhold is on discs 2 and 3 of this set. Its installer is copied off them and put back together in "
                + "D:/Lanternhold installer. It needs 1 MB, and that drive has 5 MB free.",
            said[0]);

        string alone = "Big Game comes on 3 discs. Its installer is copied off them and put back together in "
            + "D:/Big Game installer. It needs 1 MB, and that drive has 5 MB free.";

        Assert.Equal([alone, alone, alone], said[1..4]);

        Assert.Equal(
            "Where it is put back together|The North Isles is spread over discs 1 and 2, so it cannot run from one of them. "
                + "It is copied off them and put back together in D:/Second installer. It needs 1 MB, and that drive has 5 MB free."
                + "|Copy and install|Afterwards, its own installer starts.|false",
            said[4]);

        Assert.Equal(
            "The North Isles is spread over discs 2 to 4, so it cannot run from one of them. It is copied off them and put back "
                + "together in D:/Second installer. Part of it is there already. It needs 5 MB, and that drive has 1 MB free. "
                + "That is not enough. Change the folder to a drive with more room.|true",
            said[5]);

        Assert.Equal(
            "The North Isles is spread over discs 1 and 2, so it cannot run from one of them. It is copied off them and put back "
                + "together in D:/Second installer. It needs 1 MB.",
            said[6]);
    }

    // ============================================== what the page carries about it

    [Fact]
    public void An_add_on_is_written_with_its_parts_only_when_it_has_them()
    {
        string page = MenuWriter.MenuPage(new MenuFacts(
            "Two", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
            Games:
            [
                new MenuGame("First", "01/setup.exe", "01",
                [
                    new MenuAddOn("Soundtrack", "01/Add-ons/soundtrack.exe"),
                    new MenuAddOn("The North Isles", "01/Add-ons/setup_isles.exe", ["01/Add-ons/setup_isles-1.bin", "01/Add-ons/setup_isles-2.bin"]),
                    new MenuAddOn("Nothing beside it", "01/Add-ons/alone.exe", []),
                ],
                Apart: ["01/Extras/book.zip"]),
            ]));

        Assert.Contains("{name: \"Soundtrack\", runs: \"01/Add-ons/soundtrack.exe\"}", page, StringComparison.Ordinal);
        Assert.Contains(
            "{name: \"The North Isles\", runs: \"01/Add-ons/setup_isles.exe\", parts: [\"01/Add-ons/setup_isles-1.bin\", \"01/Add-ons/setup_isles-2.bin\"]}",
            page,
            StringComparison.Ordinal);
        Assert.Contains("{name: \"Nothing beside it\", runs: \"01/Add-ons/alone.exe\"}", page, StringComparison.Ordinal);
        Assert.Contains("apart: [\"01/Extras/book.zip\"]", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_disc_names_an_add_ons_parts_where_the_build_put_them()
    {
        Spread spread = await ASetWithAnAddOnSpreadOverTwoDiscs();

        string page = File.ReadAllText(Path.Combine(spread.Discs[0], "AUTORUN", "menu.hta"));

        Assert.Contains(
            "runs: \"02 - Second/Add-ons/setup_second_isles.exe\", parts: [\"02 - Second/Add-ons/setup_second_isles-1.bin\"]}",
            page,
            StringComparison.Ordinal);
        Assert.Contains("runs: \"02 - Second/Add-ons/setup_second_soundtrack.exe\"}", page, StringComparison.Ordinal);

        // And the plan did put the installer on one disc and its part on
        // the next, which is what every test of this set leans on.
        Assert.True(File.Exists(Path.Combine(spread.Discs[0], "02 - Second", "Add-ons", "setup_second_isles.exe")));
        Assert.False(File.Exists(Path.Combine(spread.Discs[0], "02 - Second", "Add-ons", "setup_second_isles-1.bin")));
        Assert.True(File.Exists(Path.Combine(spread.Discs[1], "02 - Second", "Add-ons", "setup_second_isles-1.bin")));
        Assert.True(File.Exists(Path.Combine(spread.Discs[1], "02 - Second", "Add-ons", "setup_second_soundtrack.exe")));
    }

    // ===================================================== every disc, every game

    [Fact]
    public async Task Every_disc_lists_every_game_and_each_button_says_where_that_game_is()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "for (var n = 1; n <= 3; n++) {\n"
            + "    putIn(n); SET = loadTheSet();\n"
            + "    for (var g = 0; g < 3; g++) { showGame(g); out.push('disc ' + n + ' ' + DISC.games[g].name + ': ' + state()); }\n"
            + "}\n"
            + "showChooser();\n"
            + "out.push('list=' + el('gameRow0').innerText + ' [' + el('gameRow0').parentNode.title + '] ' + el('gameRow1').innerText + ' ['"
            + " + el('gameRow1').parentNode.title + '] ' + el('gameRow2').innerText + ' [' + el('gameRow2').parentNode.title + ']');\n");

        string theOrchard = "Quiet Orchard is on disc 3, not on this one. The button above asks for it, and carries on by itself once it is in.";
        string theTales = "Harbour Tales is on disc 1, not on this one. The button above asks for it, and carries on by itself once it is in.";

        Same(
            [
                "disc 1 Harbour Tales: Install | (no line) | back live",
                "disc 1 Lanternhold: Install from 2 discs | Nothing is copied yet. Still to come: discs 2, 3. | back live",
                "disc 1 Quiet Orchard: Install from disc 3 | " + theOrchard + " | back live",

                "disc 2 Harbour Tales: Install from disc 1 | " + theTales + " | back live",
                "disc 2 Lanternhold: Install from 2 discs | Nothing is copied yet. Install copies this disc, then asks for disc 3. | back live",
                "disc 2 Quiet Orchard: Install from disc 3 | " + theOrchard + " | back live",

                "disc 3 Harbour Tales: Install from disc 1 | " + theTales + " | back live",
                "disc 3 Lanternhold: Install from 2 discs | Nothing is copied yet. Install copies this disc, then asks for disc 2. | back live",
                "disc 3 Quiet Orchard: Install | (no line) | back live",

                "list=Harbour Tales [On disc 1] Lanternhold [On discs 2 and 3] Quiet Orchard [On disc 3]",
            ],
            said);
    }

    [Fact]
    public async Task A_game_that_is_installed_says_Play_whichever_disc_is_in_and_plays()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "installedNow['Harbour Tales'] = true; installedNow['Lanternhold'] = true; installedNow['Quiet Orchard'] = true;\n"
            + "for (var g = 0; g < 3; g++) { showGame(g); out.push(el('doIt').innerText); }\n"
            + "showGame(2); doTheThing();\n"
            + "out.push(started.join(' + ') + ' closed=' + closed + ' busy=' + restoring.busy + ' page=' + shown('whereScreen'));\n");

        Assert.Equal(
            ["Play", "Play", "Play", "close", "C:/Games/Quiet Orchard/game.exe in C:/Games/Quiet Orchard closed=true busy=false page=hidden"],
            said);
    }

    // ============================================================ whole on this disc

    [Fact]
    public async Task A_game_whole_on_the_disc_in_the_drive_installs_straight_from_it()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "showGame(0); doTheThing();\n"
            + "out.push('pressed: ' + state());\n"
            + "out.push('started: ' + started.join(' + ') + ' | page=' + shown('whereScreen') + ' ejected=' + ejected);\n"
            + "doTheThing(); goBack();\n"
            + "out.push('pressed again and back: started=' + started.length + ' screen=' + shown('gameScreen') + ' list=' + shown('chooserScreen'));\n"
            + "runningNow = true; tick(); installedNow['Harbour Tales'] = true; runningNow = false; tick();\n"
            + "out.push('after: ' + state() + ' | remove=' + shown('removeTheCopy'));\n");

        Same(
            [
                "pressed: Installing... (dead) | Installing Harbour Tales. This menu waits for the installer and says when it is done. | back dead",
                "started: <D1>/01 - Harbour Tales/setup_harbour_tales.exe in <D1>/01 - Harbour Tales | page=hidden ejected=0",
                "pressed again and back: started=1 screen=block list=hidden",
                "after: Play | Harbour Tales is installed. Play starts it. | back live | remove=hidden",
            ],
            said);

        // Nothing was copied anywhere: it is a game on a disc.
        Assert.Empty(Directory.GetFileSystemEntries(_hdd));
    }

    // ============================================================= whole on another

    [Fact]
    public async Task A_game_whole_on_another_disc_asks_for_it_and_installs_from_it_once_it_is_in()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "showGame(2); doTheThing();\n"
            + "out.push('pressed: ' + state() + ' | started=' + started.length + ' page=' + shown('whereScreen'));\n"

            // It looks before it opens the tray, and opens it once.
            + "pump(function () { return ejected > 0; });\n"
            + "out.push('disc 1 seen: ejected=' + ejected + ' looks=' + looks + ' started=' + started.length);\n"

            // The wrong disc goes in. It keeps waiting and the tray is left alone.
            + "putIn(2); var was = looks; pump(function () { return looks >= was + 3; });\n"
            + "out.push('disc 2 in: ' + state() + ' | ejected=' + ejected + ' started=' + started.length);\n"

            // The right one.
            + "putIn(3); pump(function () { return started.length > 0; });\n"
            + "out.push('disc 3 in: ' + state() + ' | title=' + document.title + ' discIn=' + SET.discIn);\n"
            + "out.push('started: ' + started.join(' + ') + ' | ejected=' + ejected + ' page=' + shown('whereScreen'));\n"
            + "runningNow = true; tick(); installedNow['Quiet Orchard'] = true; runningNow = false; tick();\n"
            + "out.push('after: ' + state());\n");

        string wait = "Put disc 3 in. This carries on by itself once it is there.";

        Same(
            [
                "pressed: Install from disc 3 (dead) | " + wait + " | back live | started=0 page=hidden",
                "disc 1 seen: ejected=1 looks=1 started=0",
                "disc 2 in: Install from disc 3 (dead) | " + wait + " | back live | ejected=1 started=0",
                "disc 3 in: Installing... (dead) | Installing Quiet Orchard. This menu waits for the installer and says when it is done. | back dead"
                    + " | title=THREE GAMES (disc 3 of 3) discIn=3",
                "started: <D3>/03 - Quiet Orchard/setup_quiet_orchard.exe in <D3>/03 - Quiet Orchard | ejected=1 page=hidden",
                "after: Play | Quiet Orchard is installed. Play starts it. | back live",
            ],
            said);

        Assert.Empty(Directory.GetFileSystemEntries(_hdd));
    }

    [Fact]
    public async Task A_disc_swapped_by_hand_before_the_press_is_found_and_the_tray_is_left_shut()
    {
        string[] discs = await TheThreeGames();

        // The page was opened from disc 1 and still believes disc 1 is in.
        string[] said = Page(discs, 1,
            "showGame(2); putIn(3);\n"
            + "out.push('believes: ' + el('doIt').innerText + ' discIn=' + SET.discIn);\n"
            + "doTheThing(); pump(function () { return started.length > 0; });\n"
            + "out.push('started: ' + started.join(' + ') + ' | ejected=' + ejected + ' discIn=' + SET.discIn);\n");

        Same(
            [
                "believes: Install from disc 3 discIn=1",
                "started: <D3>/03 - Quiet Orchard/setup_quiet_orchard.exe in <D3>/03 - Quiet Orchard | ejected=0 discIn=3",
            ],
            said);
    }

    [Fact]
    public async Task Back_is_the_way_out_of_a_wait_and_what_was_waited_for_never_starts_afterwards()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "showGame(2); doTheThing();\n"
            + "out.push('waiting: busy=' + restoring.busy + ' wanting=' + restoring.wanting + ' ' + state());\n"
            + "goBack();\n"
            + "out.push('back: busy=' + restoring.busy + ' wanting=' + restoring.wanting + ' list=' + shown('chooserScreen') + ' game=' + shown('gameScreen'));\n"

            // The disc it had asked for goes in after all. Nothing starts.
            + "putIn(3); drain();\n"
            + "out.push('disc 3 in afterwards: started=' + started.length + ' timers=' + window.timers.length);\n"

            // Another game, on the disc that is in now, as the page will see.
            + "putIn(1); showGame(0);\n"
            + "out.push('another: ' + state());\n"
            + "doTheThing(); drainSome();\n"
            + "out.push('started: ' + started.join(' + '));\n");

        Same(
            [
                "waiting: busy=true wanting=3 Install from disc 3 (dead) | Put disc 3 in. This carries on by itself once it is there. | back live",
                "back: busy=false wanting=0 list=block game=none",
                "disc 3 in afterwards: started=0 timers=0",
                "another: Install | (no line) | back live",
                "started: <D1>/01 - Harbour Tales/setup_harbour_tales.exe in <D1>/01 - Harbour Tales",
            ],
            said);
    }

    [Fact]
    public async Task Asked_for_twice_with_a_way_back_between_it_starts_once()
    {
        string[] discs = await TheThreeGames();

        // The first wait's look is still on its way when the second begins.
        // Two loops would each find the disc, and each start the installer.
        string[] said = Page(discs, 1,
            "showGame(2); doTheThing(); goBack(); showGame(2); doTheThing();\n"
            + "out.push('timers=' + window.timers.length + ' wanting=' + restoring.wanting);\n"
            + "putIn(3); pump(function () { return started.length > 0; }); drainSome();\n"
            + "out.push('started=' + started.length + ' ejected=' + ejected);\n");

        Assert.Equal(["timers=1 wanting=3", "started=1 ejected=0"], said);
    }

    // ============================================================ spread over discs

    [Fact]
    public async Task A_game_spread_over_discs_is_copied_off_them_and_nothing_of_another_game_comes_with_it()
    {
        string[] discs = await TheThreeGames();
        string into = Path.Combine(_hdd, "Lanternhold installer");

        string[] said = Page(discs, 2,
            "showGame(1); doTheThing();\n"
            + "out.push('page: ' + shown('whereScreen') + ' name=' + el('whereName').innerText + ' go=' + el('whereGo').innerText + ' | started=' + started.length);\n"
            + "out.push('says: ' + tag(el('whereSays').innerText));\n"
            + "out.push('note: ' + el('whereNote').innerText);\n"
            + "whereGo(); pump(function () { return restoring.wanting > 0; });\n"
            + "out.push('disc 2 copied: ' + state() + ' | ejected=' + ejected);\n"
            + "out.push('there: ' + under(HDD));\n"
            + "putIn(3); pump(function () { return started.length > 0; });\n"
            + "out.push('disc 3 in: ' + state() + ' | title=' + document.title);\n"
            + "out.push('started: ' + started.join(' + '));\n"
            + "out.push('there: ' + under(HDD));\n"
            + "runningNow = true; tick(); installedNow['Lanternhold'] = true; runningNow = false; tick();\n"
            + "out.push('after: ' + el('doIt').innerText + ' | ' + tag(el('stillWanted').innerText) + ' | remove=' + shown('removeTheCopy') + ' ' + el('removeTheCopy').innerText);\n"

            // Another game's screen does not keep this one's button.
            + "goBack(); showGame(2);\n"
            + "out.push('another game: remove=' + shown('removeTheCopy'));\n"
            + "showGame(1); out.push('and back: ' + el('doIt').innerText);\n");

        Assert.Equal("page: block name=Lanternhold go=Copy and install | started=0", said[0]);
        Assert.StartsWith(
            "says: Lanternhold is on discs 2 and 3 of this set. Its installer is copied off them and put back together in "
                + "<HDD>/Lanternhold installer. It needs ",
            said[1],
            StringComparison.Ordinal);
        Assert.Equal("note: Afterwards, Lanternhold's own installer asks where to install the game.", said[2]);

        Same(
            [
                "disc 2 copied: Install from 2 discs (dead) | Put disc 3 in. This carries on by itself once it is there. | back live | ejected=1",

                // Disc 2 also holds Harbour Tales's extras. They stayed on it.
                "there: Lanternhold installer/02 - Lanternhold/setup_lanternhold-1.bin, Lanternhold installer/02 - Lanternhold/setup_lanternhold.exe",

                "disc 3 in: Installing... (dead) | Installing Lanternhold. This menu waits for the installer and says when it is done. | back dead"
                    + " | title=THREE GAMES (disc 3 of 3)",
                "started: <HDD>/Lanternhold installer/02 - Lanternhold/setup_lanternhold.exe in <HDD>/Lanternhold installer",

                // Disc 3 holds Quiet Orchard and Lanternhold's own manual
                // too. Neither came: the manual is for reading, off the disc.
                "there: Lanternhold installer/02 - Lanternhold/setup_lanternhold-1.bin, Lanternhold installer/02 - Lanternhold/setup_lanternhold-2.bin, "
                    + "Lanternhold installer/02 - Lanternhold/setup_lanternhold-3.bin, Lanternhold installer/02 - Lanternhold/setup_lanternhold.exe",
            ],
            said[3..8]);

        Assert.StartsWith("after: Play | Lanternhold is installed. Play starts it. The ", said[8], StringComparison.Ordinal);
        Assert.Contains(" copied to <HDD>/Lanternhold installer is not needed any more. | remove=block Remove the copy, ", said[8], StringComparison.Ordinal);
        Assert.Equal(["another game: remove=hidden", "and back: Play"], said[9..]);

        // Byte for byte what the build was given.
        foreach (string name in new[] { "setup_lanternhold.exe", "setup_lanternhold-1.bin", "setup_lanternhold-2.bin", "setup_lanternhold-3.bin" })
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(_root, "src", "Lanternhold", name)),
                File.ReadAllBytes(Path.Combine(into, "02 - Lanternhold", name)));
        }
    }

    [Fact]
    public async Task Asked_for_from_a_disc_with_none_of_it_the_tray_opens_and_the_discs_are_taken_in_any_order()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "showGame(1); doTheThing(); whereGo();\n"
            + "out.push('pressed: ' + state() + ' | there=' + under(HDD));\n"
            + "pump(function () { return ejected > 0; });\n"
            + "out.push('disc 1 seen: ejected=' + ejected + ' there=' + under(HDD));\n"

            // Disc 3 first, though it asked for 2: either is wanted.
            + "putIn(3); pump(function () { return restoring.wanting === 2 && ejected > 1; });\n"
            + "out.push('disc 3 copied: ' + state() + ' | ejected=' + ejected);\n"
            + "out.push('there: ' + under(HDD));\n"
            + "putIn(2); pump(function () { return started.length > 0; });\n"
            + "out.push('started: ' + started.join(' + '));\n");

        Same(
            [
                "pressed: Install from 2 discs (dead) | Put disc 2 in. This carries on by itself once it is there. | back live | there=Lanternhold installer",
                "disc 1 seen: ejected=1 there=Lanternhold installer",
                "disc 3 copied: Install from 2 discs (dead) | Put disc 2 in. This carries on by itself once it is there. | back live | ejected=2",
                "there: Lanternhold installer/02 - Lanternhold/setup_lanternhold-2.bin, Lanternhold installer/02 - Lanternhold/setup_lanternhold-3.bin",
                "started: <HDD>/Lanternhold installer/02 - Lanternhold/setup_lanternhold.exe in <HDD>/Lanternhold installer",
            ],
            said);
    }

    [Fact]
    public async Task Back_is_dead_while_it_copies_and_comes_alive_for_the_wait_which_it_ends_without_losing_what_was_copied()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 2,
            "showGame(1);\n"

            // Copying, stood in: the copy of a file this small is over
            // before anything could be pressed.
            + "restoring.busy = true; restoring.copying = '1 of 2'; restoring.all = 10; showTheSet(gameAt(DISC.games, 1));\n"
            + "goBack();\n"
            + "out.push('copying: back=' + backIs() + ' game=' + shown('gameScreen') + ' list=' + shown('chooserScreen') + ' busy=' + restoring.busy);\n"
            + "restoring.copying = ''; restoring.joining = 'x.bin'; showTheSet(gameAt(DISC.games, 1)); goBack();\n"
            + "out.push('joining: back=' + backIs() + ' game=' + shown('gameScreen'));\n"
            + "restoring.joining = ''; restoring.busy = false; showTheSet(gameAt(DISC.games, 1));\n"

            // The real thing, as far as the wait for disc 3.
            + "doTheThing(); whereGo(); pump(function () { return restoring.wanting > 0; });\n"
            + "out.push('waiting: back=' + backIs() + ' there=' + under(HDD));\n"
            + "goBack();\n"
            + "out.push('back: list=' + shown('chooserScreen') + ' busy=' + restoring.busy + ' wanting=' + restoring.wanting);\n"

            // And in again: it carries on from what is there, and the
            // folder is not asked a second time on this window.
            + "showGame(1);\n"
            + "out.push('again: ' + state());\n"
            + "putIn(3); doTheThing();\n"
            + "out.push('pressed: page=' + shown('whereScreen'));\n"
            + "pump(function () { return started.length > 0; });\n"
            + "out.push('started=' + started.length + ' there=' + under(HDD).split(', ').length + ' files');\n");

        Same(
            [
                "copying: back=dead game=block list=hidden busy=true",
                "joining: back=dead game=block",
                "waiting: back=live there=Lanternhold installer/02 - Lanternhold/setup_lanternhold-1.bin, Lanternhold installer/02 - Lanternhold/setup_lanternhold.exe",
                "back: list=block busy=false wanting=0",
                "again: Install from 2 discs | 2 of 4 files copied so far. Now put disc 3 in. | back live",
                "pressed: page=hidden",
                "started=1 there=4 files",
            ],
            said);
    }

    [Fact]
    public async Task Each_game_is_asked_where_its_own_installer_goes_and_gets_a_folder_of_its_own()
    {
        string[] discs = await TwoGamesEachOverTwoDiscs();

        string[] said = Page(discs, 1,
            "showGame(0); doTheThing();\n"
            + "out.push('first: ' + el('whereName').innerText + ' | ' + tag(el('whereSays').innerText).split(' It needs ')[0]);\n"
            + "whereCancel();\n"
            + "out.push('cancelled: asked=' + theShare(0).asked + ' busy=' + restoring.busy + ' page=' + shown('whereScreen'));\n"
            + "doTheThing(); whereGo(); pump(function () { return restoring.wanting > 0; }); goBack();\n"
            + "out.push('first went: asked=' + theShare(0).asked + ' second asked=' + theShare(1).asked);\n"
            + "showGame(1); doTheThing();\n"
            + "out.push('second: ' + shown('whereScreen') + ' ' + el('whereName').innerText + ' | ' + tag(el('whereSays').innerText).split(' It needs ')[0]);\n"

            // Change folder is this game's alone.
            + "pickAFolder = function (title) { out.push('picker: ' + title); return HDD + BACKSLASH + 'elsewhere'; };\n"
            + "whereChange();\n"
            + "out.push('changed: ' + tag(theShare(1).dir) + ' first=' + tag(theShare(0).dir));\n");

        Same(
            [
                "first: First | First is on discs 1 and 2 of this set. Its installer is copied off them and put back together in <HDD>/First installer.",
                "cancelled: asked=false busy=false page=hidden",
                "first went: asked=true second asked=false",
                "second: block Second | Second is on discs 3 and 4 of this set. Its installer is copied off them and put back together in <HDD>/Second installer.",
                "picker: Where should the Second installer go?",
                "changed: <HDD>/elsewhere/Second installer first=<HDD>/First installer",
            ],
            said);
    }

    // =========================================================== manual and extras

    [Fact]
    public async Task A_games_manual_and_extras_say_which_disc_they_are_on_when_it_is_not_the_one_in()
    {
        string[] discs = await TheThreeGames(withAManualOfTheDiscsOwn: true);

        string[] said = Page(discs, 1,
            // Lanternhold's manual is on disc 3.
            "showGame(1); openTheManual();\n"
            + "out.push('manual from disc 1: ' + alerts.join(' / ') + ' | opened=' + started.concat(opened).length);\n"
            + "alerts = []; putIn(3); openTheManual();\n"
            + "out.push('manual with disc 3 in: ' + started.join(' + ') + ' | alerts=' + alerts.length);\n"

            // Harbour Tales's extras are on disc 2.
            + "started = []; putIn(1); showGame(0); openTheExtras();\n"
            + "out.push('extras from disc 1: ' + alerts.join(' / ') + ' | opened=' + opened.length);\n"
            + "alerts = []; putIn(2); openTheExtras();\n"
            + "out.push('extras with disc 2 in: ' + opened.join(' + ') + ' | alerts=' + alerts.length + ' line=' + lineIs());\n"

            // Quiet Orchard has no manual of its own, so its button opens
            // the disc's, which is on every disc and in no game's folder.
            + "opened = []; started = [];\n"
            + "for (var n = 1; n <= 3; n++) { putIn(n); showGame(2); openTheManual(); }\n"
            + "out.push('the discs own: ' + started.join(' + ') + ' | alerts=' + alerts.length);\n");

        Same(
            [
                "manual from disc 1: The manual of Lanternhold is on disc 3. Put disc 3 in and press this again. | opened=0",
                "manual with disc 3 in: <D3>/02 - Lanternhold/Manual/lanternhold_manual.zip | alerts=0",
                "extras from disc 1: The extras of Harbour Tales are on disc 2. Put disc 2 in and press this again. | opened=0",
                "extras with disc 2 in: open <D2>/01 - Harbour Tales/Extras | alerts=0 line=(no line)",
                "the discs own: <D1>/Extras/collection manual.pdf + <D2>/Extras/collection manual.pdf + <D3>/Extras/collection manual.pdf | alerts=0",
            ],
            said);
    }

    [Fact]
    public async Task Extras_that_landed_on_two_discs_open_what_this_disc_holds_and_say_where_the_rest_is()
    {
        string[] discs = await AGameWhoseExtrasLandOnTwoDiscs();

        string[] said = Page(discs, 1,
            "showGame(0); openTheExtras();\n"
            + "out.push(opened.join(' + ') + ' | alerts=' + alerts.length + ' | ' + lineIs());\n");

        Assert.Equal(
            ["open <D1>/01 - First/Extras | alerts=0 | The extras of First are spread over discs 1 and 2. This opened what the disc in the drive holds."],
            said);
    }

    [Fact]
    public async Task Manual_and_extras_are_dead_while_a_disc_is_waited_for()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 1,
            "showGame(2); doTheThing();\n"
            + "out.push(el('manual').className + ' / ' + el('extras').className);\n"
            + "showGame(1); out.push('still on the one that waits: ' + showing);\n");

        // The tray may be open under them.
        Assert.Equal("btn dead / btn dead", said[0]);
    }

    // ==================================================================== add-ons

    [Fact]
    public async Task Updates_run_from_the_disc_they_are_on_and_from_another_it_says_which()
    {
        string[] discs = await TheThreeGames();

        string[] said = Page(discs, 3,
            "installedNow['Quiet Orchard'] = true; showGame(2);\n"
            + "out.push('button: ' + el('theUpdate').innerText);\n"
            + "runTheUpdate();\n"
            + "out.push('from disc 3: ' + started.join(' + ') + ' | alerts=' + alerts.length);\n"
            + "started = []; putIn(1); runTheUpdate();\n"
            + "out.push('from disc 1: ' + alerts.join(' / ') + ' | started=' + started.length + ' busy=' + restoring.busy + ' page=' + shown('whereScreen'));\n");

        Same(
            [
                "button: Update to 3",
                "from disc 3: cmd /s /c \"\"<D3>/03 - Quiet Orchard/Add-ons/patch_quiet_orchard_1_to_2.exe\" && "
                    + "\"<D3>/03 - Quiet Orchard/Add-ons/patch_quiet_orchard_2_to_3.exe\"\" | alerts=0",
                "from disc 1: Update to 3 is on disc 3. Put disc 3 in and press this again. | started=0 busy=false page=hidden",
            ],
            said);
    }

    [Fact]
    public async Task An_add_on_runs_from_the_copy_its_game_was_installed_from_while_that_is_there()
    {
        Spread spread = await ASetWithAnAddOnSpreadOverTwoDiscs();

        // The whole of it: Second is spread over both discs, so it is
        // copied off them, The North Isles's installer off disc 1 and its
        // part off disc 2, and installed from the copy. Then the add-on.
        string[] said = Page(spread.Discs, 1,
            "showGame(1);\n"
            + "out.push('before: ' + state() + ' | add-ons ' + addOnsAre());\n"
            + "doTheThing(); whereGo(); pump(function () { return restoring.wanting > 0; });\n"
            + "out.push('disc 1 copied: ' + under(HDD));\n"
            + "putIn(2); pump(function () { return started.length > 0; });\n"
            + "out.push('started: ' + started.join(' + '));\n"
            + "runningNow = true; tick(); installedNow['Second'] = true; runningNow = false; tick();\n"
            + "out.push('installed: ' + el('doIt').innerText + ' | add-ons ' + addOnsAre());\n"
            + "started = []; runTheAddOn(addOnNamed('isles'));\n"
            + "out.push('the isles: ' + started.join(' + ') + ' | page=' + shown('whereScreen') + ' alerts=' + alerts.length);\n"
            + "runningNow = true; tick(); runningNow = false; tick();\n"
            + "started = []; runTheAddOn(addOnNamed('soundtrack'));\n"
            + "out.push('the soundtrack: ' + started.join(' + '));\n");

        Same(
            [
                "before: Install from 2 discs | Nothing is copied yet. Install copies this disc, then asks for disc 2. | back live | add-ons dead",

                // First is on disc 1 too, and none of it came.
                "disc 1 copied: Second installer/02 - Second/Add-ons/setup_second_isles.exe, Second installer/02 - Second/setup_second.exe",
                "started: <HDD>/Second installer/02 - Second/setup_second.exe in <HDD>/Second installer",

                // Drawn alive once the game is installed, which on a set
                // they were not until the menu was opened again.
                "installed: Play | add-ons live",
                "the isles: <HDD>/Second installer/02 - Second/Add-ons/setup_second_isles.exe | page=hidden alerts=0",
                "the soundtrack: <HDD>/Second installer/02 - Second/Add-ons/setup_second_soundtrack.exe",
            ],
            said);

        // Its part is beside it there, which is the only place it is.
        Assert.True(File.Exists(Path.Combine(_hdd, "Second installer", "02 - Second", "Add-ons", "setup_second_isles-1.bin")));
    }

    [Fact]
    public async Task With_no_copy_left_an_add_on_whole_on_one_disc_runs_from_that_disc_and_says_which_from_another()
    {
        Spread spread = await ASetWithAnAddOnSpreadOverTwoDiscs();

        // Installed some other day, and the copy long gone.
        string[] said = Page(spread.Discs, 2,
            "installedNow['Second'] = true; showGame(1);\n"
            + "runTheAddOn(addOnNamed('soundtrack'));\n"
            + "out.push('disc 2: ' + started.join(' + ') + ' | alerts=' + alerts.length + ' page=' + shown('whereScreen'));\n"
            + "runningNow = true; tick(); runningNow = false; tick();\n"
            + "started = []; putIn(1); runTheAddOn(addOnNamed('soundtrack'));\n"
            + "out.push('disc 1: ' + alerts.join(' / ') + ' | started=' + started.length + ' page=' + shown('whereScreen'));\n");

        Same(
            [
                "disc 2: <D2>/02 - Second/Add-ons/setup_second_soundtrack.exe | alerts=0 page=hidden",
                "disc 1: soundtrack is on disc 2. Put disc 2 in and press this again. | started=0 page=hidden",
            ],
            said);

        Assert.Empty(Directory.GetFileSystemEntries(_hdd));
    }

    [Fact]
    public async Task With_no_copy_left_an_add_on_spread_over_discs_is_fetched_by_itself_and_then_runs()
    {
        Spread spread = await ASetWithAnAddOnSpreadOverTwoDiscs();

        string[] said = Page(spread.Discs, 1,
            "installedNow['Second'] = true; showGame(1);\n"
            + "runTheAddOn(addOnNamed('isles'));\n"
            + "out.push('pressed: page=' + shown('whereScreen') + ' name=' + el('whereName').innerText + ' go=' + el('whereGo').innerText + ' started=' + started.length);\n"
            + "out.push('says: ' + tag(el('whereSays').innerText).split(' It needs ')[0]);\n"
            + "out.push('note: ' + el('whereNote').innerText + ' | ' + el('whereHeadline').innerText);\n"

            // Cancel, and nothing is kept of having asked.
            + "whereCancel();\n"
            + "out.push('cancelled: what=' + (restoring.what ? 'kept' : 'gone') + ' busy=' + restoring.busy + ' there=' + under(HDD));\n"

            + "runTheAddOn(addOnNamed('isles')); whereGo(); pump(function () { return restoring.wanting > 0; });\n"
            + "out.push('disc 1 copied: ' + el('doIt').innerText + ' | ' + lineIs());\n"
            + "out.push('there: ' + under(HDD));\n"
            + "putIn(2); pump(function () { return started.length > 0; });\n"
            + "out.push('started: ' + started.join(' + '));\n"
            + "out.push('there: ' + under(HDD));\n"
            + "out.push('afterwards: what=' + (restoring.what ? 'kept' : 'gone') + ' busy=' + restoring.busy + ' the game asked=' + theShare(1).asked + ' | ' + el('doIt').innerText);\n");

        Same(
            [
                "pressed: page=block name=Second go=Copy and install started=0",
                "says: isles is spread over discs 1 and 2, so it cannot run from one of them. It is copied off them and put back together in <HDD>/Second installer.",
                "note: Afterwards, its own installer starts. | Where it is put back together",
                "cancelled: what=gone busy=false there=",
                "disc 1 copied: Play | Put disc 2 in. This carries on by itself once it is there.",

                // Its installer alone: not the game's, which is on disc 1 too.
                "there: Second installer/02 - Second/Add-ons/setup_second_isles.exe",
                "started: <HDD>/Second installer/02 - Second/Add-ons/setup_second_isles.exe",

                // And its part alone: not the soundtrack, which is on disc 2 too.
                "there: Second installer/02 - Second/Add-ons/setup_second_isles-1.bin, Second installer/02 - Second/Add-ons/setup_second_isles.exe",
                "afterwards: what=gone busy=false the game asked=false | Play",
            ],
            said);
    }

    [Fact]
    public async Task An_add_on_that_was_cut_to_fit_is_fetched_put_back_together_and_runs_whole()
    {
        string[] discs = await ASetWithAnAddOnCutToFit();
        string whole = Path.Combine(_root, "src", "Second", "setup_second_vast.exe");

        string[] said = Page(discs, 1,
            "installedNow['Second'] = true; showGame(1);\n"
            + "runTheAddOn(0);\n"
            + "out.push('says: ' + tag(el('whereSays').innerText).split(' It needs ')[0]);\n"
            + "whereGo();\n"
            + "var swaps = 0;\n"
            + "pump(function () { if (restoring.wanting > 0 && restoring.wanting !== SET.discIn && !restoring.copying) { putIn(restoring.wanting); swaps++; } return started.length > 0; });\n"
            + "out.push('started: ' + started.join(' + ') + ' | alerts=' + alerts.length);\n"
            + "out.push('there: ' + under(HDD));\n");

        Assert.StartsWith("says: vast is spread over discs 2 and 3, so it cannot run from one of them.", said[0], StringComparison.Ordinal);
        Same(
            [
                "started: <HDD>/Second installer/02 - Second/Add-ons/setup_second_vast.exe | alerts=0",

                // Whole, and its pieces gone.
                "there: Second installer/02 - Second/Add-ons/setup_second_vast.exe",
            ],
            said[1..]);

        Assert.Equal(
            File.ReadAllBytes(whole),
            File.ReadAllBytes(Path.Combine(_hdd, "Second installer", "02 - Second", "Add-ons", "setup_second_vast.exe")));
    }

    [Fact]
    public async Task No_add_on_starts_while_the_set_is_at_work_or_before_its_game_is_installed()
    {
        Spread spread = await ASetWithAnAddOnSpreadOverTwoDiscs();

        string[] said = Page(spread.Discs, 2,
            "showGame(1); runTheAddOn(addOnNamed('soundtrack')); runTheUpdate();\n"
            + "out.push('not installed: started=' + started.length + ' alerts=' + alerts.length + ' page=' + shown('whereScreen'));\n"
            + "installedNow['Second'] = true; restoring.busy = true; runTheAddOn(addOnNamed('soundtrack')); runTheAddOn(addOnNamed('isles'));\n"
            + "out.push('at work: started=' + started.length + ' alerts=' + alerts.length + ' page=' + shown('whereScreen') + ' what=' + (restoring.what ? 'kept' : 'none'));\n"
            + "restoring.busy = false; checking = true; runTheAddOn(addOnNamed('soundtrack'));\n"
            + "out.push('checking: started=' + started.length);\n"
            + "checking = false; runTheAddOn(addOnNamed('soundtrack'));\n"
            + "out.push('free: started=' + started.length);\n");

        Same(
            [
                "not installed: started=0 alerts=0 page=hidden",
                "at work: started=0 alerts=0 page=hidden what=none",
                "checking: started=0",
                "free: started=1",
            ],
            said);
    }

    // ==================================================================== a preview

    [Fact]
    public async Task A_preview_of_a_set_says_what_the_real_one_will_and_starts_nothing()
    {
        // Built only so the same games exist on the disk for the plan.
        await TheThreeGames();
        Directory.Delete(_out, recursive: true);

        string into = Path.Combine(_root, "previewed", "disc");

        IReadOnlyList<string> menus = Assert.IsType<Outcome<IReadOnlyList<string>>.Ok>(
            new APreviewOfTheMenu(new WindowsStageWriter(), new WindowsImages())
                .Write(_three!, ADisc(7920), TheSizeOnTheDisk, into)).Value;

        Assert.Equal(3, menus.Count);

        string[] previewed = [.. menus.Select(menu => Path.GetDirectoryName(Path.GetDirectoryName(menu))!)];

        // The set's own list is beside each of them, written from the plan.
        Assert.All(previewed, disc => Assert.True(File.Exists(Path.Combine(disc, SetManifest.FileName))));

        string[] said = Page(previewed, 2,
            "if (!DISC.onADisc) { thisIsAPreview(); out.push('a preview'); }\n"
            + "SET = loadTheSet();\n"
            + "for (var g = 0; g < 3; g++) { showGame(g); out.push(DISC.games[g].name + ': ' + state()); doTheThing(); }\n"
            + "out.push('alerts=' + alerts.length + ' the same=' + (alerts[0] === alerts[1] && alerts[1] === alerts[2]) + ' | ' + alerts[0]);\n"
            + "out.push('started=' + started.length + ' timers=' + window.timers.length + ' busy=' + restoring.busy + ' page=' + shown('whereScreen'));\n");

        Same(
            [
                "a preview",
                "Harbour Tales: Install from disc 1 | Harbour Tales is on disc 1, not on this one. The button above asks for it, and carries on by itself once it is in. | back live",
                "Lanternhold: Install from 2 discs | Nothing is copied yet. Install copies this disc, then asks for disc 3. | back live",
                "Quiet Orchard: Install from disc 3 | Quiet Orchard is on disc 3, not on this one. The button above asks for it, and carries on by itself once it is in. | back live",
                "alerts=3 the same=true | This is a preview of the menu. Nothing starts from here, because the disc has not been made yet.",
                "started=0 timers=0 busy=false page=hidden",
            ],
            said);

        Assert.DoesNotContain(said, line => line.Contains("its list of files is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_preview_writes_a_sets_list_from_the_plan_and_none_for_a_disc_that_stands_alone()
    {
        // Called what a staging folder is called, which is the one kind of
        // folder outside the temporary one that the writer agrees to empty.
        string into = Path.Combine(_root, "previewed", "disc");

        GameFolder big = OnTheDisk("Big", 600, 1500);
        DiscProject cut = new("BIG", [big], new DiscOptions(CutFiles: true));

        IReadOnlyList<string> menus = Assert.IsType<Outcome<IReadOnlyList<string>>.Ok>(
            new APreviewOfTheMenu(new WindowsStageWriter(), new WindowsImages())
                .Write(cut, ADisc(1000), TheSizeOnTheDisk, into)).Value;

        Assert.Equal(3, menus.Count);

        string first = Path.GetDirectoryName(Path.GetDirectoryName(menus[0]))!;
        // Its columns are padded for whoever reads it in Notepad. One space
        // for each run of them, so what is asked here is what it says.
        string list = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Path.Combine(first, SetManifest.FileName)), " +", " ");

        // Which disc this is, every file with its disc and its size, and
        // the file that was cut with the pieces it goes back together from.
        Assert.Contains("disc 1 of 3.", list, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 " + new string('0', 64) + " 600 *setup_big.exe", list, StringComparison.Ordinal);
        Assert.Contains("2 " + new string('0', 64) + " 1000 *setup_big-1.bin.dwpart01", list, StringComparison.Ordinal);
        Assert.Contains("3 " + new string('0', 64) + " 500 *setup_big-1.bin.dwpart02", list, StringComparison.Ordinal);
        Assert.Contains("JOIN " + new string('0', 64) + " 1500 *setup_big-1.bin", list, StringComparison.Ordinal);
        Assert.Contains("PART 1 *setup_big-1.bin.dwpart01", list, StringComparison.Ordinal);
        Assert.Contains("PART 2 *setup_big-1.bin.dwpart02", list, StringComparison.Ordinal);

        // Nothing was read, and the list does not pretend otherwise.
        Assert.Contains(" " + new string('0', 64) + " ", list, StringComparison.Ordinal);

        // The script reads it as it reads a real one.
        Assert.Equal(
            ["3", "1", "3", "2", "1,2,3"],
            Words(
                "var text = readUtf8(" + ThePageOnADiscOfASet.AsJScript(Path.Combine(first, SetManifest.FileName)) + ");",
                "setRowsFrom(text).length",
                "whichDiscTheSetFileSays(text)",
                "howManyDiscsTheSetFileSays(text)",
                "setJoinsFrom(text)[0].pieces.length",
                "theDiscsOf(setRowsFrom(text)).join(',')"));

        // One disc is no set, and gets no list.
        string alone = Path.Combine(_root, "previewed alone", "disc");

        IReadOnlyList<string> one = Assert.IsType<Outcome<IReadOnlyList<string>>.Ok>(
            new APreviewOfTheMenu(new WindowsStageWriter(), new WindowsImages())
                .Write(new DiscProject("SMALL", [OnTheDisk("Small", 100)], new DiscOptions()), ADisc(1000), TheSizeOnTheDisk, alone)).Value;

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Assert.Single(one)))!, SetManifest.FileName)));
    }

    [Fact]
    public void A_preview_of_one_game_on_a_set_no_longer_says_its_list_is_missing()
    {
        // Called what a staging folder is called, which is the one kind of
        // folder outside the temporary one that the writer agrees to empty.
        string into = Path.Combine(_root, "previewed", "disc");

        IReadOnlyList<string> menus = Assert.IsType<Outcome<IReadOnlyList<string>>.Ok>(
            new APreviewOfTheMenu(new WindowsStageWriter(), new WindowsImages())
                .Write(new DiscProject("BIG", [OnTheDisk("Big", 600, 600, 600)], new DiscOptions()), ADisc(1000), TheSizeOnTheDisk, into)).Value;

        string[] previewed = [.. menus.Select(menu => Path.GetDirectoryName(Path.GetDirectoryName(menu))!)];

        string[] said = Page(previewed, 1,
            "thisIsAPreview(); SET = loadTheSet(); showGame(0);\n"
            + "out.push(state()); doTheThing(); out.push(alerts.join(' / '));\n");

        Same(
            [
                "Install disc 1 of 3 | Nothing is copied yet. Install copies this disc, then asks for the rest: discs 2, 3. | back live",
                "This is a preview of the menu. Nothing starts from here, because the disc has not been made yet.",
            ],
            said);
    }

    // ============================================ one game with the set to itself

    [Fact]
    public async Task A_file_that_would_not_go_back_together_leaves_the_button_live_and_the_next_press_tries_again()
    {
        string[] discs = await Built(new DiscProject("BIG", [OnTheDisk("Big", 600, 1500)], new DiscOptions(CutFiles: true)), 1000);

        string[] said = Page(discs, 1,
            "showGame(0);\n"
            + "var realJoin = joinThePieces;\n"
            + "joinThePieces = function (work) { work.say({ joining: '' }); work.after(['setup_big-1.bin']); };\n"
            + "doTheThing(); whereGo();\n"
            + "pump(function () { if (restoring.wanting > 0 && restoring.wanting !== SET.discIn) { putIn(restoring.wanting); } return alerts.length > 0 && !restoring.busy; }); drainSome();\n"
            + "out.push('would not join: ' + alerts[alerts.length - 1].split(String.fromCharCode(10))[0] + ' | ' + state() + ' | started=' + started.length + ' timers=' + window.timers.length);\n"
            + "joinThePieces = realJoin; alerts = [];\n"
            + "doTheThing(); pump(function () { return started.length > 0; });\n"
            + "out.push('again: ' + started.join(' + ') + ' | alerts=' + alerts.length + ' page=' + shown('whereScreen'));\n"
            + "out.push('there: ' + under(HDD));\n");

        Same(
            [
                "would not join: 1 file(s) could not be put back together: | Install disc 3 of 3 | "
                    + "Every file is here. One file was cut to fit and still has to be put back together. | back live | started=0 timers=0",
                "again: <HDD>/Big installer/setup_big.exe in <HDD>/Big installer | alerts=0 page=hidden",
                "there: Big installer/setup_big-1.bin, Big installer/setup_big.exe",
            ],
            said);
    }

    [Fact]
    public async Task A_folder_of_game_files_on_a_set_can_be_written_to_once_copied_and_starts_what_the_disc_knows_starts_it()
    {
        string folder = Path.Combine(_root, "src", "Loose");
        Directory.CreateDirectory(folder);

        ListedFile[] listed = [new("game.exe", 300), new("data one.pak", 600), new("data two.pak", 600)];

        foreach (ListedFile file in listed)
        {
            File.WriteAllBytes(Path.Combine(folder, file.RelativePath), RandomBytes((int)file.Bytes));
        }

        GameFolder loose = new(folder, GameKind.FilesWithLauncher, "Loose", "Loose", null, listed, [listed[0]], null);

        string[] discs = await Built(new DiscProject("LOOSE", [loose], new DiscOptions()), 1000);

        // Read-only, as everything on a CD is.
        foreach (string file in discs.SelectMany(disc => Directory.EnumerateFiles(disc, "*", SearchOption.AllDirectories)))
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }

        string[] said = Page(discs, 1,
            "showGame(0);\n"
            + "out.push('before: ' + el('doIt').innerText);\n"
            + "doTheThing(); whereGo();\n"
            + "pump(function () { if (restoring.wanting > 0 && restoring.wanting !== SET.discIn) { putIn(restoring.wanting); } return started.length > 0; });\n"
            + "out.push('started: ' + started.join(' + ') + ' | closed=' + closed + ' busy=' + restoring.busy + ' opened=' + opened.length);\n");

        Same(
            [
                "before: Copy and play disc 1 of 2",
                "close",
                "started: <HDD>/Loose installer/game.exe in <HDD>/Loose installer | closed=true busy=false opened=0",
            ],
            said);

        // A game copied so that it can save can save.
        Assert.All(
            Directory.EnumerateFiles(Path.Combine(_hdd, "Loose installer"), "*", SearchOption.AllDirectories),
            file => Assert.False(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly), file));
        Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(_hdd, "Loose installer"), "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task With_nothing_known_to_start_it_the_folder_is_opened_and_the_button_comes_back()
    {
        string folder = Path.Combine(_root, "src", "Loose");
        Directory.CreateDirectory(folder);

        ListedFile[] listed = [new("one.exe", 300), new("two.exe", 600), new("data.pak", 600)];

        foreach (ListedFile file in listed)
        {
            File.WriteAllBytes(Path.Combine(folder, file.RelativePath), RandomBytes((int)file.Bytes));
        }

        // Two things that might start it is a question nobody answered.
        GameFolder loose = new(folder, GameKind.FilesWithLauncher, "Loose", "Loose", null, listed, [listed[0], listed[1]], null);

        string[] discs = await Built(new DiscProject("LOOSE", [loose], new DiscOptions()), 1000);

        string[] said = Page(discs, 1,
            "showGame(0); doTheThing(); whereGo();\n"
            + "pump(function () { if (restoring.wanting > 0 && restoring.wanting !== SET.discIn) { putIn(restoring.wanting); } return opened.length > 0; });\n"
            + "out.push(opened.join(' + ') + ' | started=' + started.length + ' closed=' + closed + ' | ' + el('doIt').innerText + (el('doIt').parentNode.className.indexOf('dead') >= 0 ? ' (dead)' : '') + ' busy=' + restoring.busy);\n");

        Assert.Equal(["open <HDD>/Loose installer | started=0 closed=false | Copy and open busy=false"], said);
    }

    // =================================================================== the discs

    private DiscProject? _three;

    /// <summary>
    /// Three made-up games of the owner's three sizes, built as real disc
    /// folders: Harbour Tales whole on disc 1 with its extras on disc 2,
    /// Lanternhold over discs 2 and 3 with its manual on disc 3, and Quiet
    /// Orchard whole on disc 3 with two updates.
    /// </summary>
    private async Task<string[]> TheThreeGames(bool withAManualOfTheDiscsOwn = false)
    {
        GameFolder tales = OnTheDisk("Harbour Tales", 1, 4000, 3790);
        GameFolder lantern = OnTheDisk("Lanternhold", 28, 3910, 3910, 293);
        GameFolder orchard = OnTheDisk("Quiet Orchard", 1160);

        string extras = AFile(@"Harbour Tales own\harbour_tales_extras.zip", 1210);
        string manual = AFile(@"Lanternhold own\lanternhold_manual.zip", 3);
        string two = AFile(@"Quiet Orchard own\patch_quiet_orchard_1_to_2.exe", 620);
        string three = AFile(@"Quiet Orchard own\patch_quiet_orchard_2_to_3.exe", 32);

        _three = new DiscProject(
            "THREE GAMES",
            [
                tales with { Own = [new GameExtra(extras, GameExtraKind.Extra)] },
                lantern with { Own = [new GameExtra(manual, GameExtraKind.Manual)] },
                orchard,
            ],
            new DiscOptions())
        {
            AddOns = [new AddOn(2, two, "Update 2", 620), new AddOn(2, three, "Update 3", 32)],
            Extras = withAManualOfTheDiscsOwn ? new DiscExtras(AFile("collection manual.pdf", 40), null, []) : null
        };

        string[] discs = await Built(_three, withAManualOfTheDiscsOwn ? 7960 : 7920);

        Assert.Equal(3, discs.Length);

        return discs;
    }

    /// <summary>Two games of two files each, a file a disc: First on discs 1 and 2, Second on 3 and 4.</summary>
    private async Task<string[]> TwoGamesEachOverTwoDiscs()
    {
        string[] discs = await Built(
            new DiscProject("TWO", [OnTheDisk("First", 600, 600), OnTheDisk("Second", 600, 600)], new DiscOptions()), 1000);

        Assert.Equal(4, discs.Length);

        return discs;
    }

    private sealed record Spread(string[] Discs);

    /// <summary>
    /// First, whole on disc 1, and Second with two add-ons: The North Isles,
    /// whose installer the plan leaves on disc 1 and whose part goes to disc
    /// 2, and a soundtrack, whole on disc 2.
    /// </summary>
    private async Task<Spread> ASetWithAnAddOnSpreadOverTwoDiscs()
    {
        GameFolder first = OnTheDisk("First", 450);
        GameFolder second = OnTheDisk("Second", 300);

        string isles = AFile(@"Second own\setup_second_isles.exe", 200);
        string part = AFile(@"Second own\setup_second_isles-1.bin", 400);
        string soundtrack = AFile(@"Second own\setup_second_soundtrack.exe", 50);

        DiscProject project = new("TWO", [first, second], new DiscOptions())
        {
            AddOns =
            [
                new AddOn(1, isles, "Second - isles", 200, [new AddOnPart(part, 400)]),
                new AddOn(1, soundtrack, "Second - soundtrack", 50),
            ]
        };

        string[] discs = await Built(project, 1000);

        Assert.Equal(2, discs.Length);

        return new Spread(discs);
    }

    /// <summary>First and Second on disc 1, and an add-on of Second's bigger than a whole disc, cut over three.</summary>
    private async Task<string[]> ASetWithAnAddOnCutToFit()
    {
        GameFolder first = OnTheDisk("First", 500);
        GameFolder second = OnTheDisk("Second", 300);
        string vast = AFile(@"Second\setup_second_vast.exe", 1500);

        DiscProject project = new("TWO", [first, second], new DiscOptions(CutFiles: true))
        {
            AddOns = [new AddOn(1, vast, "Second - vast", 1500)]
        };

        string[] discs = await Built(project, 1000);

        Assert.Equal(3, discs.Length);

        return discs;
    }

    /// <summary>First with two extras of its own, one of which the plan leaves on disc 1 and one on disc 2.</summary>
    private async Task<string[]> AGameWhoseExtrasLandOnTwoDiscs()
    {
        GameFolder first = OnTheDisk("First", 500) with
        {
            Own =
            [
                new GameExtra(AFile(@"First own\artbook.zip", 400), GameExtraKind.Extra),
                new GameExtra(AFile(@"First own\soundtrack.zip", 400), GameExtraKind.Extra),
            ]
        };

        string[] discs = await Built(new DiscProject("TWO", [first, OnTheDisk("Second", 300)], new DiscOptions()), 1000);

        Assert.Equal(2, discs.Length);
        Assert.True(File.Exists(Path.Combine(discs[0], "01 - First", "Extras", "artbook.zip")));
        Assert.True(File.Exists(Path.Combine(discs[1], "01 - First", "Extras", "soundtrack.zip")));

        return discs;
    }

    private static Medium ADisc(long room) => new("TEST", "a test disc", Overhead + room);

    /// <summary>The same lines, and all of every one of them when they are not.</summary>
    private static void Same(string[] expected, string[] actual) =>
        Assert.True(
            expected.SequenceEqual(actual),
            Environment.NewLine + "expected:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", expected)
                + Environment.NewLine + "actual:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", actual));

    private static long TheSizeOnTheDisk(string path) =>
        File.Exists(path) ? new FileInfo(path).Length
        : Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
        : 0;

    private static byte[] RandomBytes(int count)
    {
        byte[] made = new byte[count];
        Random.Shared.NextBytes(made);

        return made;
    }

    private string AFile(string name, int bytes)
    {
        string path = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, RandomBytes(bytes));

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
            File.WriteAllBytes(Path.Combine(folder, file), RandomBytes(sizes[at]));
            listed.Add(new ListedFile(file, sizes[at]));
        }

        return new GameFolder(folder, GameKind.GogInstaller, name, name, listed[0], listed, [], null);
    }

    /// <summary>Builds it for real and hands back its disc folders, in order.</summary>
    private async Task<string[]> Built(DiscProject project, long room)
    {
        DiscBuilder builder = new(new WindowsFileSystem(), new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher());

        BuiltDiscs built = Assert.IsType<Outcome<BuiltDiscs>.Ok>(
            await builder.Build(project, ADisc(room), _out, null, CancellationToken.None)).Value;

        return [.. built.Discs.Select(disc => disc.Folder)];
    }

    // ==================================================================== the page

    /// <summary>
    /// The page the disc of that number carries, with its set loaded, and
    /// then the test. What the test pushed onto <c>out</c> comes back, a line
    /// each.
    /// </summary>
    /// <remarks>
    /// Stood in for: whether a game is installed, which is asked of the
    /// registry; starting anything; the installer being seen running; the
    /// tray; and where the installer is put back together, which would
    /// otherwise be the drive of this machine with the most room.
    /// </remarks>
    private string[] Page(string[] discs, int from, string test)
    {
        StringBuilder before = new();

        before.Append("var ROOTS = { HDD: ").Append(ThePageOnADiscOfASet.AsJScript(_hdd));

        for (int at = 0; at < discs.Length; at++)
        {
            before.Append(", D").Append(at + 1).Append(": ").Append(ThePageOnADiscOfASet.AsJScript(discs[at]));
        }

        before.Append(" };\nvar HDD = ROOTS.HDD;\nvar URLS = [''");

        foreach (string disc in discs)
        {
            before.Append(", ").Append(ThePageOnADiscOfASet.AsJScript(new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri));
        }

        before.Append("];\n");
        before.Append(
            "function el(id) { return document.getElementById(id); }\n"
            + "function tag(p) { var s = String(p), k; for (k in ROOTS) { s = s.split(ROOTS[k]).join('<' + k + '>'); } return s.split(BACKSLASH).join('/'); }\n"
            + "function putIn(n) { document.URL = URLS[n]; WHERE = null; }\n"
            + "function shown(id) { var d = el(id).style.display; return d === 'block' ? 'block' : (d === 'none' ? (id === 'gameScreen' ? 'none' : 'hidden') : 'hidden'); }\n"
            + "function lineIs() { return el('stillWanted').style.display === 'none' || !el('stillWanted').innerText ? '(no line)' : tag(el('stillWanted').innerText); }\n"
            + "function backIs() { return String(el('back').className || '').indexOf('dead') >= 0 ? 'dead' : 'live'; }\n"
            + "function state() { return el('doIt').innerText.split(String.fromCharCode(8230)).join('...') + (el('doIt').parentNode.className.indexOf('dead') >= 0 ? ' (dead)' : '') + ' | ' + lineIs() + ' | back ' + backIs(); }\n"
            + "function tick() { var due = window.timers; window.timers = []; for (var i = 0; i < due.length; i++) { due[i](); } }\n"

            // A few rounds of whatever is queued, for a page that should
            // have nothing more to do and may be polling: enough to show a
            // second start if there were one.
            + "function drainSome() { for (var i = 0; i < 12 && window.timers.length; i++) { WScript.Sleep(20); tick(); } }\n"
            + "function under(dir) { var f = new ActiveXObject('Scripting.FileSystemObject'), found = [];\n"
            + "    function walk(d) { var e = new Enumerator(f.GetFolder(d).Files); for (; !e.atEnd(); e.moveNext()) { found.push(String(e.item().Path)); }\n"
            + "        var s = new Enumerator(f.GetFolder(d).SubFolders); var n = 0; for (; !s.atEnd(); s.moveNext()) { n++; walk(String(s.item().Path)); }\n"
            + "        if (n === 0 && f.GetFolder(d).Files.Count === 0 && d !== dir) { found.push(String(d)); } }\n"
            + "    walk(dir); for (var i = 0; i < found.length; i++) { found[i] = found[i].substring(dir.length + 1).split(BACKSLASH).join('/'); } found.sort(); return found.join(', '); }\n"
            + "function addOnNamed(part) { var o = theOtherAddOns(gameAt(DISC.games, showing).addOns); for (var i = 0; i < o.length; i++) { if (o[i].name.indexOf(part) >= 0) { return i; } } throw new Error('no add-on named ' + part); }\n"
            + "function addOnsAre() { return el('addOns').innerHTML.indexOf('btn dead') >= 0 ? 'dead' : 'live'; }\n"
            + "var started = [], opened = [], ejected = 0, looks = 0, installedNow = {}, runningNow = false;\n"
            + "whereTheGameIs = function (name) { return installedNow[name] ? { exe: 'C:/Games/' + name + '/game.exe', dir: 'C:/Games/' + name } : null; };\n"
            + "startIt = function (what, from) { var why = whyNothingRuns(); if (why) { return why; } started.push(tag(what) + (from ? ' in ' + tag(from) : '')); return null; };\n"
            + "startTheCommand = function (line) { started.push(tag(line)); return null; };\n"
            + "openTheFolder = function (where) { var why = whyNothingRuns(); if (why) { return why; } opened.push('open ' + tag(where)); return null; };\n"
            + "installerIsRunning = function () { return runningNow; };\n"
            + "ejectTheDisc = function () { ejected++; return true; };\n"
            + "var reallyLook = lookAtTheDisc; lookAtTheDisc = function (root) { looks++; return reallyLook(root); };\n"
            + "restoreFolderFor = function (home, name) { return HDD + BACKSLASH + installerFolderName(name); };\n"
            + "SET = loadTheSet();\n");

        string went = ThePageOnADiscOfASet.RunIn(
            _run,
            ThePageOnADiscOfASet.PageScriptOf(File.ReadAllText(Path.Combine(discs[from - 1], "AUTORUN", "menu.hta"))),
            new Uri(Path.Combine(discs[from - 1], "AUTORUN", "menu.hta")).AbsoluteUri,
            before + test + "WScript.Echo(out.join('\\n'));\n");

        return went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Asks the menu's script each of those, in one go, and hands back what
    /// each came to. One that ends in a semicolon is run and not asked.
    /// </summary>
    private string[] Words(params string[] expressions)
    {
        StringBuilder asks = new();

        foreach (string expression in expressions)
        {
            if (expression.TrimEnd().EndsWith(';') || expression.TrimEnd().EndsWith('}'))
            {
                asks.Append(expression).Append('\n');
            }
            else
            {
                asks.Append("WScript.Echo('[' + (").Append(expression).Append(") + ']');\n");
            }
        }

        File.WriteAllText(Path.Combine(_run, "words.js"), asks.ToString(), new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(_run, "words.wsf"),
            "<job>\n  <script language=\"JScript\" src=\"menu.js\" />\n  <script language=\"JScript\" src=\"words.js\" />\n</job>\n",
            new UTF8Encoding(false));

        ProcessStartInfo how = new()
        {
            FileName = "cscript.exe",
            WorkingDirectory = _run,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        how.ArgumentList.Add("//nologo");
        how.ArgumentList.Add(Path.Combine(_run, "words.wsf"));

        using Process? running = Process.Start(how);
        Assert.NotNull(running);

        string output = WaitingForAScript.WhatItSaid(running, 60_000);

        Assert.True(running.ExitCode == 0, "cscript failed:\n" + output);

        return
        [
            .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.StartsWith('[') && line.EndsWith(']'))
                .Select(line => line[1..^1])
        ];
    }
}
