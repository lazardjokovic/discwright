using System.Text.RegularExpressions;

using DiscWright.Menu;

namespace DiscWright.Tests;

public class TheMenuWriter
{
    [Fact]
    public void Leaves_no_placeholder_behind_in_a_built_menu()
    {
        string page = MenuWriter.MenuPage(Facts());

        // The lesson of %%SINGLE%%. A placeholder left in a built menu is a disc
        // that opens to nothing, and it cannot be fixed once the disc is burned.
        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, page);
    }

    [Fact]
    public void Leaves_no_placeholder_behind_for_an_awkward_name_either()
    {
        string page = MenuWriter.MenuPage(Facts(name: "100% complete & <b>bold</b>"));

        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, page);
    }

    [Fact]
    public void Includes_the_logic_rather_than_repeating_it()
    {
        string page = MenuWriter.MenuPage(Facts());

        Assert.Contains("src=\"menu.js\"", page, StringComparison.Ordinal);

        // If a function from menu.js were pasted in here as well, the disc
        // would carry two copies and only one of them would be tested.
        Assert.DoesNotContain("function discTitle", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Hands_back_the_logic_file_untouched()
    {
        string script = MenuWriter.MenuScript();

        // Nothing is substituted into it, so the file the cscript tests run and
        // the file that ends up on the disc are the same bytes.
        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, script);
        Assert.Contains("function discTitle", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Carries_the_same_logic_the_tests_run()
    {
        // The embedded copy against the file on disk. If these ever drift, the
        // cscript tests would be testing something the disc does not carry.
        Assert.Equal(
            File.ReadAllText(MenuScriptInTheSourceTree()).ReplaceLineEndings("\n"),
            MenuWriter.MenuScript().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Sizes_the_window_before_the_body_exists()
    {
        string page = MenuWriter.MenuPage(Facts());

        int sizes = page.IndexOf("window.resizeTo", StringComparison.Ordinal);
        int body = page.IndexOf("<body", StringComparison.Ordinal);

        // From body onload instead, mshta shows its own default window first:
        // measured on a real CD-RW at 1440x753 for 2.6 seconds before the menu
        // appeared at all.
        Assert.True(sizes > 0 && sizes < body, "The window must be sized in the head, before the body.");
    }

    [Fact]
    public void Sizes_the_window_again_once_there_is_a_body_to_measure()
    {
        string page = MenuWriter.MenuPage(Facts());

        // The head's size is the outer size, title bar included, so the
        // bottom of the stage was under the frame. Once the body exists the
        // page measures what it got and asks for the difference, with the
        // arithmetic in menu.js where cscript checks it.
        int body = page.IndexOf("<body", StringComparison.Ordinal);
        int start = page.IndexOf("function start()", StringComparison.Ordinal);

        Assert.True(start > 0 && start < body);
        Assert.Contains("fitTheWindow();", page[start..body], StringComparison.Ordinal);
        Assert.Contains("sizeToShowAllOf(760, 480, viewport.clientWidth, viewport.clientHeight)", page, StringComparison.Ordinal);

        // The document's size and not the body's. Everything on the page is
        // positioned absolutely, so the body has no height in standards mode
        // and answered 0, which asked for a window twice the stage.
        Assert.Contains("document.documentElement.clientHeight", page[..body], StringComparison.Ordinal);
    }

    [Fact]
    public void Makes_the_folder_a_game_is_copied_into_from_a_tidied_name()
    {
        string page = MenuWriter.MenuPage(Facts());

        // The suggested folder tidied the name and the one actually made did
        // not, so a game with a colon in its name could not be copied at all.
        Assert.Contains("folderNameFor(game ? game.name : \"Game\")", page, StringComparison.Ordinal);
        Assert.Contains("could not be made: \"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disc_of_a_set_installs_from_the_folder_the_set_is_put_back_into()
    {
        string page = MenuWriter.MenuPage(Facts());

        // The page's part of the set: read the set file once off the disc,
        // draw the one big button and the line under it from it, and send
        // Install through the engine in menu.js rather than at the disc. The
        // engine itself is tested under cscript against real folders.
        Assert.Contains("SET = loadTheSet();", page, StringComparison.Ordinal);
        Assert.Contains("if (SET && !where)", page, StringComparison.Ordinal);
        Assert.Contains("restoreGo();", page, StringComparison.Ordinal);
        Assert.Contains("restoreButtonSays(scan, SET.discIn, SET.of, isInstalled(game), whatToDoWith(game, false))", page, StringComparison.Ordinal);
        Assert.Contains("copyTheSetDisc({", page, StringComparison.Ordinal);
        Assert.Contains("joinThePieces({", page, StringComparison.Ordinal);
        Assert.Contains("window.setTimeout(checkForTheNextDisc, 1500);", page, StringComparison.Ordinal);
        Assert.Contains("id=\"restoreBar\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"removeTheCopy\"", page, StringComparison.Ordinal);
        Assert.Contains("offerToRemoveTheCopy(installed);", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Copies_a_game_off_a_single_disc_through_the_engine_too()
    {
        string page = MenuWriter.MenuPage(Facts());

        // THE PAGE IS GLUE AND NOTHING ELSE. Copying a game off one disc used
        // to be written out here in full, which is why it was never once run
        // by a test: the suite loads menu.js under cscript and cannot load
        // this. Both bugs case 7 found by hand lived on this path.
        //
        // So the same shape as the set: ask, draw, hand over to menu.js, and
        // react to what comes back. These assertions are here so the page and
        // the engine cannot quietly drift apart, which is the other thing that
        // went wrong on this disc.
        Assert.Contains("copyAGameOff({", page, StringComparison.Ordinal);
        Assert.Contains("keepTheGameBarMoving();", page, StringComparison.Ordinal);
        Assert.Contains("window.setTimeout(keepTheGameBarMoving, 500);", page, StringComparison.Ordinal);
        Assert.Contains("restoreBarPercent(copying, (new Date()).getTime())", page, StringComparison.Ordinal);
        Assert.Contains("whatToDoAfterACopy(game, where, bad, stuck,", page, StringComparison.Ordinal);

        // And the page no longer carries a copy loop of its own.
        Assert.DoesNotContain("files.CopyFile(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("copyTheNextOne", page, StringComparison.Ordinal);
    }
    [Fact]
    public void Keeps_one_disc_menu_at_a_time_and_lets_previews_pile_up()
    {
        Assert.Contains("singleinstance=\"yes\"", MenuWriter.MenuPage(Facts(onlyOne: true)), StringComparison.Ordinal);

        // Every preview of every disc shared one application name with this on,
        // so previewing a second disc silently refocused the first one's stale
        // window and the new menu was never shown at all.
        Assert.Contains("singleinstance=\"no\"", MenuWriter.MenuPage(Facts(onlyOne: false)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rock & Roll")]
    [InlineData("STAR WARS™: Empire at War")]
    [InlineData("A name with \"quotes\" in it")]
    [InlineData("A name with a \\ backslash")]
    [InlineData("100% complete (v2)")]
    [InlineData("Данные игры")]
    public void Survives_a_name_that_has_broken_something_before(string name)
    {
        string page = MenuWriter.MenuPage(Facts(name: name));

        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, page);

        // Nothing may close the script block early. Counted as "</script"
        // WITHOUT the closing bracket, on purpose: counting the whole tag once
        // let an unescaped "<" through, because the still-escaped ">" meant the
        // complete tag never appeared and the count stayed at two. A parser is
        // less fussy than that, and so is this now.
        Assert.Equal(2, Regex.Count(page, "</script", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void Describes_each_game_well_enough_for_the_chooser_to_act_on_it()
    {
        string page = MenuWriter.MenuPage(Facts() with
        {
            Games =
            [
                new MenuGame("Grim Fandango", "01 - Grim Fandango/setup_grim.exe", "01 - Grim Fandango"),
                new MenuGame("Loom", string.Empty, "02 - Loom")
            ]
        });

        // A name alone is not enough: the chooser has to know what to run and
        // where the game's files are, or its only button does nothing.
        Assert.Contains("name: \"Grim Fandango\"", page, StringComparison.Ordinal);
        Assert.Contains("runs: \"01 - Grim Fandango/setup_grim.exe\"", page, StringComparison.Ordinal);
        Assert.Contains("folder: \"02 - Loom\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_a_single_game_disc_one_game_rather_than_none()
    {
        string page = MenuWriter.MenuPage(Facts(name: "Hollow Knight"));

        // The chooser never appears for it, but the game screen reads the same
        // list, so a disc with one game still has one entry in it.
        Assert.Contains("name: \"Hollow Knight\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Carries_both_screens_so_a_disc_can_be_either_kind()
    {
        string page = MenuWriter.MenuPage(Facts());

        // Every disc gets the same menu, and which screen it opens on is
        // decided when it runs rather than when it is built. One menu, tested
        // once, and no second template to keep in step with the first.
        Assert.Contains("id=\"chooserScreen\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"gameScreen\"", page, StringComparison.Ordinal);
        Assert.Contains("whichScreenToOpenOn", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_screen_that_picks_a_game_scrolls_its_list_and_has_an_Exit_under_it()
    {
        // xniwo on 2.0.1, discwright#98: a disc of many games lost the ones
        // past the seventh off the bottom of the panel, and the screen had no
        // Exit at all.
        string page = MenuWriter.MenuPage(Facts());

        string chooser = page[page.IndexOf("<div id=\"chooserScreen\"", StringComparison.Ordinal)..];
        chooser = chooser[..chooser.IndexOf("<div id=\"gameScreen\"", StringComparison.Ordinal)];

        // Exit after the list and outside it, so scrolling never takes it away.
        int list = chooser.IndexOf("<div id=\"gameList\"></div>", StringComparison.Ordinal);
        int exit = chooser.IndexOf("id=\"chooserExit\"", StringComparison.Ordinal);
        Assert.True(list >= 0 && exit > list, "Exit is not under the list on the screen that picks a game.");
        Assert.Contains("onclick=\"doExit(); return false;\">Exit</a>", chooser[exit..], StringComparison.Ordinal);

        // The list scrolls rather than running off the panel, and is fitted
        // every time the screen is drawn, the way back from a game included.
        Assert.Contains("#gameList { overflow-x: hidden; overflow-y: auto;", page, StringComparison.Ordinal);

        string drawn = page[page.IndexOf("function showChooser()", StringComparison.Ordinal)..];
        drawn = drawn[..drawn.IndexOf("\nfunction ", 10, StringComparison.Ordinal)];
        Assert.Contains("fitTheGameList();", drawn, StringComparison.Ordinal);
        Assert.Contains("chooserListRoom(", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Puts_a_game_s_name_on_the_chooser_as_text_and_never_as_markup()
    {
        string page = MenuWriter.MenuPage(Facts());

        // Built into the row's markup, a game called "Command & Conquer" would
        // arrive as "Command  Conquer" and one with a bracket in it as
        // something worse. The row is built empty and filled with innerText.
        Assert.Contains("innerText = DISC.games[j].name", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_let_a_name_close_the_script_block()
    {
        string page = MenuWriter.MenuPage(Facts(name: "Nasty </script><h1>take over"));

        // Two script blocks in the template, and a name cannot make a third end.
        Assert.Equal(2, Regex.Count(page, "</script", RegexOptions.IgnoreCase));

        // And the generated block carries no raw angle bracket at all, which is
        // the assertion that does not depend on which of the two brackets
        // happens to be escaped. Asserted on the block rather than the whole
        // page, because the page is markup and is full of honest brackets.
        Match data = Regex.Match(page, @"var DISC = (\{.*?\n\});", RegexOptions.Singleline);
        Assert.True(data.Success, "No data block found in the generated menu.");
        Assert.DoesNotContain('<', data.Groups[1].Value);
        Assert.DoesNotContain('>', data.Groups[1].Value);
    }

    [Fact]
    public void Does_not_let_a_control_character_break_the_script()
    {
        // The window cannot produce one. A project file read off disk can, and
        // that is the path that reaches a build. A newline inside a literal is
        // an unterminated string, and the whole HTA fails to parse.
        string page = MenuWriter.MenuPage(Facts(name: "Two\r\nLines"));

        Assert.Contains("TwoLines", page, StringComparison.Ordinal);
    }

    private static MenuFacts Facts(string name = "Hollow Knight", bool onlyOne = true) =>
        new(name, "DiscWright." + name, 1, 1, "Install", ["Install", "Exit"], onlyOne);

    internal static string MenuScriptInTheSourceTree()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DiscWright.slnx"))
               && !File.Exists(Path.Combine(directory.FullName, "DiscWright.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("no solution above " + AppContext.BaseDirectory),
            "src", "DiscWright.Menu", "menu.js");
    }
}
