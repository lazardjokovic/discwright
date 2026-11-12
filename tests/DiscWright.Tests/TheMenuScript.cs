using System.Diagnostics;
using System.Text;

using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// The disc menu's own logic, run the way it will actually run: as JScript,
/// under a Windows script host, against written inputs.
/// </summary>
/// <remarks>
/// <para>
/// The menu has never had a test in either version. It is a second program, in
/// another language, on a machine that may be Windows XP, and it is invisible
/// to UI Automation, so neither xUnit nor the window suite can drive it. Taking
/// its logic out of the HTA and into a file of its own is what makes this
/// possible: <c>cscript.exe</c> runs the same file the disc carries.
/// </para>
/// <para>
/// A <c>.wsf</c> job includes it with a real <c>src</c>, so the file under test
/// is included rather than pasted or evaluated, and a syntax error in it is
/// reported with a line and a column instead of vanishing.
/// </para>
/// <para>
/// This reads <c>menu.js</c> out of the source tree rather than out of the
/// built assembly, which is deliberate: it needs no assembly of ours loaded, so
/// it runs on the development machine as well as the VM, where Smart App
/// Control refuses our own unsigned binaries.
/// </para>
/// </remarks>
public sealed class TheMenuScript : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "dwjs_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly Xunit.Abstractions.ITestOutputHelper _says;

    public TheMenuScript(Xunit.Abstractions.ITestOutputHelper says)
    {
        _says = says;

        Directory.CreateDirectory(_folder);
        File.Copy(MenuScriptInTheSourceTree(), Path.Combine(_folder, "menu.js"));
    }

    public void Dispose()
    {
        try
        {
            // The stand-in discs hold read-only files, as a CD does, and a
            // recursive delete refuses those.
            foreach (FileInfo file in new DirectoryInfo(_folder).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Calls_a_disc_on_its_own_by_the_game_s_name()
    {
        Assert.Equal("Hollow Knight", Ask("discTitle('Hollow Knight', 1, 1)"));
    }

    [Fact]
    public void Says_which_disc_of_a_set_this_one_is()
    {
        // Somebody holding three discs needs to know what is in their hand
        // without putting it in a drive.
        Assert.Equal("Hollow Knight (disc 2 of 3)", Ask("discTitle('Hollow Knight', 2, 3)"));
    }

    [Fact]
    public void Starts_at_a_guessed_rate_and_says_so_in_the_name()
    {
        // 8x CD. Only used until the first file big enough to time has finished.
        Assert.Equal("1228800", Ask("startingRate()"));
    }

    [Fact]
    public void Ignores_files_too_quick_to_time_when_working_out_the_rate()
    {
        // A thousand tiny files copied in no measurable time would divide by
        // nearly nothing and claim gigabytes a second, and the bar would jump
        // to 99 and sit there.
        string rate = Ask("rateFrom([{bytes: 5000, seconds: 0.2}, {bytes: 400, seconds: 0}], 1228800)");

        Assert.Equal("1228800", rate);
    }

    [Fact]
    public void Measures_the_rate_from_files_that_really_took_time()
    {
        // 20 MB in 10 seconds is 2 MB a second, and nothing to do with the seed.
        Assert.Equal("2097152", Ask("rateFrom([{bytes: 20971520, seconds: 10}], 1228800)"));
    }

    [Theory]
    [InlineData("percentDone(0, 0, 1228800, 1000)", "0")]
    [InlineData("percentDone(500, 0, 1228800, 1000)", "50")]
    [InlineData("percentDone(0, 0, 1228800, 0)", "0")]
    public void Counts_what_has_finished_exactly(string call, string expected)
    {
        Assert.Equal(expected, Ask(call));
    }

    [Fact]
    public void Guesses_the_file_in_flight_from_how_long_it_has_been_going()
    {
        // Nothing on a disc can see inside a copy that is happening, so the
        // file in flight is elapsed time multiplied by the measured rate.
        // 400 finished, plus 2 seconds at 100 a second, of 1000 in all.
        Assert.Equal("60", Ask("percentDone(400, 2, 100, 1000)"));
    }

    [Fact]
    public void Never_says_a_hundred_before_it_really_is()
    {
        // The last thing to happen is the check. A bar that says 100 while the
        // machine is still working reads as a program that has stopped.
        Assert.Equal("99", Ask("percentDone(999, 0, 100, 1000)"));
        Assert.Equal("99", Ask("percentDone(1000, 0, 100, 1000)"));
    }

    [Fact]
    public void Does_not_run_past_the_end_when_the_guess_overshoots()
    {
        // An optimistic rate on a slow drive would otherwise report 340 per cent.
        Assert.Equal("99", Ask("percentDone(0, 1000, 1228800, 1000)"));
    }

    [Fact]
    public void Opens_on_the_game_when_there_is_only_one()
    {
        // A chooser offering one choice is a door with one door behind it.
        Assert.Equal("game", Ask("whichScreenToOpenOn([{name: 'Hollow Knight'}])"));
        Assert.Equal("game", Ask("whichScreenToOpenOn([])"));
        Assert.Equal("game", Ask("whichScreenToOpenOn(null)"));
    }

    [Fact]
    public void Opens_on_the_chooser_when_there_is_something_to_choose()
    {
        Assert.Equal("chooser", Ask("whichScreenToOpenOn([{name: 'One'}, {name: 'Two'}])"));
    }

    [Fact]
    public void Hands_back_the_game_that_was_asked_for()
    {
        Assert.Equal("Two", Ask("gameAt([{name: 'One'}, {name: 'Two'}], 1).name"));
    }

    [Theory]
    [InlineData("gameAt([{name: 'One'}], 5)")]
    [InlineData("gameAt([{name: 'One'}], -1)")]
    [InlineData("gameAt([], 0)")]
    [InlineData("gameAt(null, 0)")]
    public void Hands_back_nothing_rather_than_falling_over(string call)
    {
        // An index past the end in JScript is not an error, it is undefined,
        // and the next line asks undefined for its name and the whole menu
        // stops with nothing on screen to say why.
        Assert.Equal("null", Ask(call));
    }

    [Fact]
    public void Says_install_for_a_game_that_has_an_installer()
    {
        Assert.Equal("Install", Ask("whatToDoWith({name: 'x', runs: 'setup_x.exe'})"));
    }

    [Fact]
    public void Says_copy_and_play_for_a_game_that_has_nothing_to_install()
    {
        // A game run off a disc cannot save: the disc is read only, and the
        // first time it tries to write a save it either fails or silently
        // writes nowhere.
        Assert.Equal("Copy and play", Ask("whatToDoWith({name: 'x', runs: '', plays: 'Game.exe'})"));
    }

    [Fact]
    public void Does_not_promise_to_play_a_game_it_is_not_going_to_start()
    {
        // NOTHING WAS PICKED TO START IT. That happens when the folder holds
        // more than one thing that could be the game and the question was
        // cancelled, and when it holds nothing runnable at all. Either way the
        // menu copies the folder and opens it, and the window that builds the
        // disc says so in as many words.
        //
        // The button said "Copy and play" anyway, so the disc promised a game
        // it was never going to run, and the two halves of the program
        // disagreed about the same disc. Watched on a real disc, 2026-11-05,
        // case 7 part 3: copied, nothing started, button had said play.
        Assert.Equal("Copy and open", Ask("whatToDoWith({name: 'x', runs: '', plays: ''})"));
        Assert.Equal("Copy and open", Ask("whatToDoWith({name: 'x', runs: ''})"));
        Assert.Equal("Copy and open", Ask("whatToDoWith({name: 'x'})"));
    }

    [Fact]
    public void Will_not_let_an_add_on_run_before_its_game()
    {
        // THE GAP THIS CLOSES. On the menu as it was, an add-on was just
        // another button: press the soundtrack installer before the game and it
        // runs, and a patch run before its base is worse, because it
        // half-applies to nothing and leaves no sign that it did.
        // Asked in words, because JScript's Echo prints a boolean as 0 or -1
        // and a test that asserts "0" says nothing to whoever reads it next.
        Assert.Equal("no", Ask("canRunAddOnYet(false) ? 'yes' : 'no'"));
        Assert.Equal("yes", Ask("canRunAddOnYet(true) ? 'yes' : 'no'"));

        // Anything that is not the game being there counts as not there.
        Assert.Equal("no", Ask("canRunAddOnYet(undefined) ? 'yes' : 'no'"));
        Assert.Equal("no", Ask("canRunAddOnYet(null) ? 'yes' : 'no'"));
    }

    [Fact]
    public void Says_why_an_add_on_cannot_be_pressed_yet()
    {
        Assert.Equal("Install the game first, then this.", Ask("whatAnAddOnSays(false)"));
    }

    [Fact]
    public void Says_nothing_under_an_add_on_that_works()
    {
        // A button that works needs no explanation, and a line of text under
        // every button is a menu nobody reads.
        Assert.Equal("", Ask("whatAnAddOnSays(true)"));
    }

    [Fact]
    public void Offers_patches_first_and_keeps_the_order_somebody_chose_for_the_rest()
    {
        // A patch applies to the version before it, so running 1.3 before 1.2
        // is how somebody ends up with a game that will not start. Everything
        // else keeps the order it was added in, which is the order somebody
        // chose.
        string names = Ask(
            "(function () { var o = addOnsInOrder(["
            + "{name: 'The soundtrack'},"
            + "{name: 'Update 1.3'},"
            + "{name: 'Update 1.2'},"
            + "{name: 'Hidden Dreams'}]);"
            + "var s = []; for (var i = 0; i < o.length; i++) { s.push(o[i].name); } return s.join(' | '); })()");

        Assert.Equal("Update 1.2 | Update 1.3 | The soundtrack | Hidden Dreams", names);
    }

    [Fact]
    public void Folds_the_patches_into_one_chain_and_leaves_the_rest_their_own_buttons()
    {
        // A disc with four patches showed four buttons, and the person
        // holding it had to know to press them oldest first. The chain is
        // the patches in running order; everything else keeps the order
        // somebody chose, and a button each.
        string chain = Ask(
            "(function () { var c = theUpdateChain(["
            + "{name: 'The soundtrack'},"
            + "{name: 'Update 1.3 (600)'},"
            + "{name: 'Update 1.2 (500)'}]);"
            + "var s = []; for (var i = 0; i < c.length; i++) { s.push(c[i].name); } return s.join(' | '); })()");

        Assert.Equal("Update 1.2 (500) | Update 1.3 (600)", chain);

        string others = Ask(
            "(function () { var o = theOtherAddOns(["
            + "{name: 'The soundtrack'},"
            + "{name: 'Update 1.2'},"
            + "{name: 'Hidden Dreams'}]);"
            + "var s = []; for (var i = 0; i < o.length; i++) { s.push(o[i].name); } return s.join(' | '); })()");

        Assert.Equal("The soundtrack | Hidden Dreams", others);
    }

    [Fact]
    public void The_update_button_names_the_version_the_chain_ends_at()
    {
        // Without the build number, which is bookkeeping nobody updates
        // "to", and nothing at all when there is no chain and no button.
        Assert.Equal(
            "Update to 1.3",
            Ask("whatTheUpdateButtonSays([{name: 'Update 1.2 (500)'}, {name: 'Update 1.3 (600)'}])"));
        Assert.Equal("Update to 1.1", Ask("whatTheUpdateButtonSays([{name: 'Update 1.1'}])"));
        Assert.Equal("null", Ask("String(whatTheUpdateButtonSays([{name: 'The soundtrack'}]))"));
        Assert.Equal("null", Ask("String(whatTheUpdateButtonSays([]))"));
    }

    [Fact]
    public void Hovering_the_update_button_says_the_whole_chain_in_order()
    {
        Assert.Equal(
            "Update 1.2, then Update 1.3",
            Ask("whatTheUpdateChainIs([{name: 'Update 1.3'}, {name: 'Update 1.2'}])"));
    }

    [Fact]
    public void The_update_command_runs_the_chain_in_order_through_cmd()
    {
        // cmd does the sequencing, so the menu fires one line and has no
        // further part: each installer is waited for by cmd, and && stops
        // the chain at the first patch whose own installer refuses.
        string line = Ask(
            "theUpdateCommand(["
            + "{name: 'Update 1.3', runs: 'GAME 1/ADD-ONS/p13.exe'},"
            + "{name: 'Update 1.2', runs: 'GAME 1/ADD-ONS/p12.exe'}], true, 'D:')");

        Assert.Equal(
            @"cmd /s /c """"D:\GAME 1\ADD-ONS\p12.exe"" && ""D:\GAME 1\ADD-ONS\p13.exe""""",
            line);
    }

    [Fact]
    public void The_update_command_obeys_the_same_gate_as_every_add_on()
    {
        // A patch run before its game half-applies to nothing and leaves
        // no sign that it did, so the chain is as dead as the buttons.
        Assert.Equal(
            "null",
            Ask("String(theUpdateCommand([{name: 'Update 1.1', runs: 'x/p.exe'}], false, 'D:'))"));
    }

    [Fact]
    public void Is_reported_with_a_line_and_a_column_when_it_will_not_parse()
    {
        // The reason for a .wsf with a real src rather than pasting the file
        // into the harness: a mistake in menu.js has to be findable.
        File.AppendAllText(Path.Combine(_folder, "menu.js"), "\nfunction broken( { }\n");

        (string output, int exitCode) = Run("discTitle('x', 1, 1)");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("error", output, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Runs one JScript expression against menu.js and returns what it printed.</summary>
    // --------------------------------------------- is the game installed

    [Theory]
    [InlineData("The Witcher Enhanced Edition Director's Cut", "The Witcher - Enhanced Edition")]
    [InlineData("Hollow Knight", "Hollow Knight")]
    [InlineData("Gothic 3", "Gothic")]
    [InlineData("STAR WARS(R): Empire At War(TM)", "Star Wars Empire at War")]
    public void Knows_two_spellings_of_the_same_game(string registry, string ours)
    {
        // GOG's registry name drops and changes punctuation, so compared as
        // written these are different games and the Play button never lights
        // up for anybody who has the game.
        Assert.Equal("yes", Ask($"isTheSameGame({Quoted(registry)}, {Quoted(ours)}) ? 'yes' : 'no'"));
    }

    [Theory]
    [InlineData("Gothic", "Gothic 3")]
    [InlineData("The Witcher", "The Witcher 3 Wild Hunt")]
    public void Takes_a_shorter_registry_name_inside_a_longer_one_of_ours(string registry, string ours)
    {
        // GOG sometimes registers fewer words than the folder had.
        Assert.Equal("yes", Ask($"isTheSameGame({Quoted(registry)}, {Quoted(ours)}) ? 'yes' : 'no'"));
    }

    [Theory]
    // These two really are inside ours, which is the whole point: "war" sits
    // in "starwars" and "ana" in "analogue". Without the guard on length they
    // would both be taken for the same game.
    [InlineData("War", "Star Wars Empire at War")]
    [InlineData("Ana", "Analogue A Hate Story")]
    [InlineData("AI", "Alan Wake")]
    [InlineData("Doom", "Hollow Knight")]
    [InlineData("", "Hollow Knight")]
    [InlineData("Hollow Knight", "")]
    public void Does_not_take_two_different_games_for_each_other(string registry, string ours)
    {
        // The short one matters: a two letter registry name sits inside half
        // the games ever made, and a Play button that runs the wrong game is
        // worse than one that does nothing.
        Assert.Equal("no", Ask($"isTheSameGame({Quoted(registry)}, {Quoted(ours)}) ? 'yes' : 'no'"));
    }

    [Fact]
    public void Asks_about_the_name_the_game_was_registered_under()
    {
        // Not the name on the disc: a folder can be renamed, and a project can
        // be given a name somebody typed.
        Assert.Equal(
            "The Witcher",
            Ask("(function () { var g = { name: 'My Favourite Game', match: 'The Witcher' };"
                + " return g.match || g.name; }())"));
    }

    [Fact]
    public void Falls_back_to_the_name_on_the_disc_when_there_is_no_other()
    {
        // Every disc built before the match name was carried has only a name.
        Assert.Equal(
            "Hollow Knight",
            Ask("(function () { var g = { name: 'Hollow Knight' }; return g.match || g.name; }())"));
    }

    [Fact]
    public void Says_Play_rather_than_Install_when_the_game_is_already_there()
    {
        // Offering to install it again over the top is how a save game dies.
        Assert.Equal("Play", Ask("whatToDoWith({ name: 'A Game', runs: 'setup.exe' }, true)"));
        Assert.Equal("Install", Ask("whatToDoWith({ name: 'A Game', runs: 'setup.exe' }, false)"));
    }

    [Fact]
    public void Still_says_copy_and_play_for_a_game_with_no_installer()
    {
        Assert.Equal(
            "Copy and play",
            Ask("whatToDoWith({ name: 'A Game', runs: '', plays: 'A Game.exe' }, false)"));
    }

    [Fact]
    public void Says_Play_before_it_looks_at_whether_anything_would_play()
    {
        // Already on the machine and the button says Play, whatever this disc
        // knows about starting it, because the copy on the machine is not this
        // disc's business. The order of these two questions is the point.
        Assert.Equal("Play", Ask("whatToDoWith({ name: 'A Game', runs: '', plays: '' }, true)"));
    }

    [Fact]
    public void Answers_about_a_game_that_is_not_installed_without_falling_over()
    {
        // The whole lookup, on whatever machine this runs on, against a name no
        // game has. It must come back with an answer rather than an error: the
        // menu runs on somebody else's machine, where anything may be missing.
        Assert.Equal("no", Ask("isInstalled({ name: 'A Game Nobody Has Ever Published' }) ? 'yes' : 'no'"));
    }

    [Fact]
    public void Answers_about_nothing_at_all()
    {
        Assert.Equal("no", Ask("isInstalled(null) ? 'yes' : 'no'"));
    }

    [Fact]
    public void Finds_whatever_is_really_installed_on_this_machine()
    {
        // The whole lookup, through the registry, on whatever machine this runs
        // on. It asserts nothing about which games are there, because that is
        // not the same on two machines and is nothing on the test VM. What it
        // proves is that the walk runs and comes back.
        string names = Ask(
            "(function () {"
            + " var out = [], bases = ['SOFTWARE' + BACKSLASH + 'WOW6432Node' + BACKSLASH + 'GOG.com' + BACKSLASH + 'Games',"
            + "                        'SOFTWARE' + BACKSLASH + 'GOG.com' + BACKSLASH + 'Games'];"
            + " var reg = wmiFor('default').Get('StdRegProv');"
            + " for (var b = 0; b < bases.length; b++) {"
            + "   var ids = keysUnder(reg, 0x80000002, bases[b]);"
            + "   for (var k = 0; k < ids.length; k++) {"
            + "     var nm = oneStringFrom(reg, 0x80000002, bases[b] + BACKSLASH + ids[k], 'gameName');"
            + "     if (nm) { out.push(nm + ' -> ' + (isInstalled({ name: nm }) ? 'found' : 'NOT FOUND')); }"
            + "   } }"
            + " return out.length ? out.join('; ') : 'nothing is installed here';"
            + "}())");

        _says.WriteLine("  " + names);

        // Every game the registry names must be found by the lookup that reads
        // the same registry. One that is not means the matching is wrong about
        // a real name on a real machine.
        Assert.DoesNotContain("NOT FOUND", names, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_calls_GetObject_because_the_engine_it_runs_in_has_none()
    {
        // THE BUG THIS EXISTS FOR. menu.hta asks for document mode 9, because
        // mode 9 is the only one with rgba that still applies the disc's icon.
        // GetObject is a JScript 5 host global and the mode 9 engine does not
        // expose it, so every call throws ReferenceError and every caller here
        // swallows it and returns a quiet no. For months that meant no game was
        // ever seen as installed: the big button always said Install and every
        // add-on stayed dead. Found on a real disc on 2026-11-04.
        //
        // cscript HAS GetObject, which is exactly why the rest of this file
        // could not catch it. So this one reads the text.
        string script = File.ReadAllText(MenuScriptInTheSourceTree());

        Assert.DoesNotContain("GetObject(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Reaches_WMI_in_a_host_that_has_no_GetObject()
    {
        // The same proof, run rather than read: GetObject is taken away before
        // menu.js loads, which is the engine the disc really gets, and the way
        // in must still work.
        string says = Ask(
            "(function () {"
            + " try {"
            + "   var reg = wmiFor('default').Get('StdRegProv');"
            + "   var ids = keysUnder(reg, 0x80000002, 'SOFTWARE');"
            + "   return ids.length > 0 ? 'reached' : 'no keys under SOFTWARE';"
            + " } catch (e) { return 'FAILED ' + e.message; }"
            + "}())",
            withoutGetObject: true);

        Assert.Equal("reached", says);
    }

    [Fact]
    public void Finds_what_is_installed_even_in_the_engine_the_disc_really_gets()
    {
        // The twin of the test above, run in a host with no GetObject, which
        // is what document mode 9 gives the menu on a real disc.
        //
        // THIS IS THE ONE THAT WOULD HAVE CAUGHT IT. The test above passes
        // under cscript, where GetObject exists, and that is why the bug lived
        // for months: every game the registry names came back NOT FOUND on a
        // real disc and green here.
        //
        // On a machine with no GOG game, this says so and proves nothing, the
        // same as its twin. It earns its keep on a machine that has one.
        string names = Ask(
            "(function () {"
            + " var out = [], bases = ['SOFTWARE' + BACKSLASH + 'WOW6432Node' + BACKSLASH + 'GOG.com' + BACKSLASH + 'Games',"
            + "                        'SOFTWARE' + BACKSLASH + 'GOG.com' + BACKSLASH + 'Games'];"
            + " var reg = wmiFor('default').Get('StdRegProv');"
            + " for (var b = 0; b < bases.length; b++) {"
            + "   var ids = keysUnder(reg, 0x80000002, bases[b]);"
            + "   for (var k = 0; k < ids.length; k++) {"
            + "     var nm = oneStringFrom(reg, 0x80000002, bases[b] + BACKSLASH + ids[k], 'gameName');"
            + "     if (nm) { out.push(nm + ' -> ' + (isInstalled({ name: nm }) ? 'found' : 'NOT FOUND')); }"
            + "   } }"
            + " return out.length ? out.join('; ') : 'nothing is installed here';"
            + "}())",
            withoutGetObject: true);

        _says.WriteLine("  without GetObject: " + names);

        Assert.DoesNotContain("NOT FOUND", names, StringComparison.Ordinal);
    }
    /// <summary>
    /// A string the script can take, with the quotes it needs.
    /// </summary>
    private static string Quoted(string text) =>
        "'" + text.Replace(((char)92).ToString(), ((char)92).ToString() + (char)92, StringComparison.Ordinal)
            .Replace("'", ((char)92) + "'", StringComparison.Ordinal) + "'";

    // --------------------------------------------- what a button actually runs

    [Fact]
    public void Finds_the_disc_above_the_menu()
    {
        // AutoRun can start a disc file with no drive letter, so the menu has
        // to work out where it is from its own address. It lives in AUTORUN,
        // so the disc is one folder further up.
        //
        // And it ends in a backslash. "D:" is whichever folder is current on
        // D, which is D:\AUTORUN when somebody double-clicks menu.hta in
        // Explorer, and a set's copy then said its files would not copy.
        Assert.Equal(@"D:\", Ask("theDiscIsAbove('file:///D:/AUTORUN/menu.hta')"));
        Assert.Equal(@"E:\", Ask("theDiscIsAbove('file:///E:/AUTORUN/menu.hta')"));
    }

    [Fact]
    public void A_set_on_its_own_disc_copies_from_the_top_of_the_disc_whatever_folder_is_current()
    {
        // The path the copy really takes: the menu's own address, through
        // theDiscIsAbove, into the robocopy line. Every earlier test handed
        // setCopyCommand "D:\" by hand, so nothing ever saw the "D:" the
        // menu actually worked out.
        Assert.Equal(
            "robocopy \"D:\\\\\" \"C:\\Games\\Big\" \"setup.exe\" /R:1 /W:1 /NJH /NJS",
            Ask(@"setCopyCommand(theDiscIsAbove('file:///D:/AUTORUN/menu.hta'), 'C:\\Games\\Big', 'setup.exe')"));

        // And a game at the top of the disc is the top of the disc, which
        // Copy and play copies from and the check reads.
        Assert.Equal(@"D:\", Ask("theGamesFolder(theDiscIsAbove('file:///D:/AUTORUN/menu.hta'), '')"));
        Assert.Equal(@"D:\setup.exe", Ask("onTheDisc(theDiscIsAbove('file:///D:/AUTORUN/menu.hta'), 'setup.exe')"));
    }

    [Fact]
    public void Finds_the_disc_when_it_is_a_share_on_another_machine()
    {
        // The leading slashes of a file URL are not part of the path, except
        // here, where two of them are.
        Assert.Equal(
            @"\\server\share",
            Ask("theDiscIsAbove('file://///server/share/AUTORUN/menu.hta')"));
    }

    [Fact]
    public void Reads_a_path_with_spaces_and_odd_characters_in_it()
    {
        Assert.Equal(
            @"D:\GAME 1",
            Ask("theDiscIsAbove('file:///D:/GAME%201/AUTORUN/menu.hta')"));
    }

    [Theory]
    [InlineData("'GAME 1/setup.exe'", @"D:\GAME 1\setup.exe")]
    [InlineData("'GAME 1'", @"D:\GAME 1")]
    [InlineData("''", "")]
    public void Turns_a_path_the_builder_wrote_into_one_Windows_can_use(string relative, string expected)
    {
        // The builder writes them with forward slashes, because they go into a
        // script rather than into a Windows API.
        Assert.Equal(expected, Ask("onTheDisc('D:', " + relative + ")"));
    }

    [Fact]
    public void Plays_what_is_installed_rather_than_installing_over_it()
    {
        string where = @"{ exe: 'C:\\Games\\HK\\hk.exe', dir: 'C:\\Games\\HK' }";

        Assert.Equal("play", Ask("whatPressingItDoes({ name: 'HK', runs: 'HK/setup.exe' }, " + where + ", 'D:').act"));
        Assert.Equal(
            @"C:\Games\HK\hk.exe",
            Ask("whatPressingItDoes({ name: 'HK', runs: 'HK/setup.exe' }, " + where + ", 'D:').run"));
    }

    [Fact]
    public void Installs_from_the_disc_when_it_is_not_installed()
    {
        const string game = "{ name: 'HK', runs: 'GAME 1/setup.exe', folder: 'GAME 1' }";

        Assert.Equal("install", Ask("whatPressingItDoes(" + game + ", null, 'D:').act"));
        Assert.Equal(@"D:\GAME 1\setup.exe", Ask("whatPressingItDoes(" + game + ", null, 'D:').run"));
        Assert.Equal(@"D:\GAME 1", Ask("whatPressingItDoes(" + game + ", null, 'D:').from"));
    }

    [Fact]
    public void Copies_a_game_with_nothing_to_install_off_the_disc()
    {
        const string game = "{ name: 'Arcanum', runs: '', folder: 'GAME 1' }";

        // A game run from the disc cannot save: the disc is read only, and the
        // first time it tries to write a save it either fails or silently
        // writes nowhere. So it comes off the disc first.
        Assert.Equal("copy", Ask("whatPressingItDoes(" + game + ", null, 'D:').act"));
        Assert.Equal(@"D:\GAME 1", Ask("whatPressingItDoes(" + game + ", null, 'D:').run"));
    }

    [Fact]
    public void Copies_a_game_that_sits_at_the_root_of_its_own_disc()
    {
        // THE ORDINARY CASE, AND IT COULD NOT BE DONE. One game on one disc
        // sits at the root, so its folder is empty. onTheDisc answers an empty
        // relative with an empty path, which is right for a file that is not
        // there and wrong for the root, so the copy was handed nothing and told
        // the person there was nothing on the disc to copy.
        //
        // Every test above gave the game a folder of its own, which is why
        // nothing caught it. Found on a real disc on 2026-11-05.
        const string game = "{ name: 'Arcanum', runs: '', folder: '' }";

        Assert.Equal("copy", Ask("whatPressingItDoes(" + game + ", null, 'D:').act"));
        Assert.Equal("D:", Ask("whatPressingItDoes(" + game + ", null, 'D:').run"));
    }

    [Fact]
    public void Installs_from_the_root_when_the_game_is_alone_on_the_disc()
    {
        // The same hole on the other branch: the folder an installer is run
        // from. An installer run from the wrong place still installs, so this
        // one cost nothing visible, which is exactly why it would have stayed.
        const string game = "{ name: 'HK', runs: 'setup.exe', folder: '' }";

        Assert.Equal("D:", Ask("whatPressingItDoes(" + game + ", null, 'D:').from"));
    }

    [Theory]
    [InlineData("Soundtrack", "Soundtrack")]
    [InlineData("Grim Fandango Remastered Soundtrack 2.0.0.9 (31045)", "Grim Fandango Remastered...")]
    [InlineData("Patch 1.0 to 1.1 for The Witcher Enhanced Edition", "Patch 1.0 to 1.1 for The...")]
    public void Cuts_a_name_down_to_something_a_button_can_hold(string name, string expected)
    {
        // An add-on is named after the file it came from, and a GOG
        // installer's name carries its version and its build number. Fifty
        // characters on a button that is one of three makes the list ragged.
        Assert.Equal(expected, Ask("shortEnoughForAButton('" + name + "')"));
    }

    [Fact]
    public void Cuts_at_a_word_rather_than_mid_word()
    {
        // A name chopped mid-word looks like a fault rather than a choice.
        Assert.Equal(
            "The Witcher, Enhanced...",
            Ask("shortEnoughForAButton('The Witcher, Enhanced Edition Soundtrack')"));
    }

    [Fact]
    public void Leaves_no_punctuation_hanging_before_the_dots()
    {
        // A comma followed by an ellipsis reads as a typo.
        Assert.Equal(
            "Hollow Knight Silksong...",
            Ask("shortEnoughForAButton('Hollow Knight Silksong, the soundtrack')"));
    }

    [Fact]
    public void Will_not_throw_most_of_a_name_away_to_find_a_space()
    {
        // One long word with a space near the front. Cutting back to that
        // space would leave two letters, so it cuts mid-word instead, which
        // is the lesser of the two.
        Assert.Equal(
            "A Supercalifragilisticexpi...",
            Ask("shortEnoughForAButton('A Supercalifragilisticexpialidocious Patch')"));
    }

    // ------------------------------------------------- copying it off the disc

    [Fact]
    public void Offers_somewhere_under_the_person_s_own_profile()
    {
        // A game copied somewhere they cannot write to half copies and stops,
        // and a game that cannot write cannot save, which is the whole reason
        // it is coming off the disc.
        Assert.Equal(
            @"C:\Users\somebody\Games\Arcanum",
            Ask(@"whereToCopyTo('C:\\Users\\somebody', 'Arcanum')"));
    }

    [Theory]
    [InlineData("Half-Life: Opposing Force", "Half-Life  Opposing Force")]
    [InlineData("What/Who?", "What Who")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("", "Game")]
    public void Makes_a_folder_name_Windows_will_accept(string name, string expected)
    {
        // Game names carry characters a folder name cannot, and a trailing dot
        // is legal in a string and not in a folder.
        Assert.Equal(
            @"C:\Users\somebody\Games" + ((char)92) + expected,
            Ask(@"whereToCopyTo('C:\\Users\\somebody', '" + name + "')"));
    }

    [Theory]
    [InlineData("Command & Conquer: Red Alert", "Command & Conquer  Red Alert")]
    [InlineData("What/Who?", "What Who")]
    [InlineData("", "Game")]
    public void Names_the_folder_the_same_way_wherever_the_game_is_put(string name, string expected)
    {
        // The suggestion tidied the name and the folder made from the one
        // somebody picked did not, so a game with a colon in its name, which
        // is most of the ones with a subtitle, could not be copied at all.
        // One function, so the two cannot disagree again.
        Assert.Equal(expected, Ask($"folderNameFor('{name}')"));
    }

    // ---------------------------------------------------- the window's size

    [Fact]
    public void Asks_for_a_window_big_enough_to_show_the_whole_stage()
    {
        // resizeTo sets the outer size, so a 480 tall window has a client area
        // 480 less the title bar, and the bottom of the stage was under the
        // frame by about 38 pixels on a real disc.
        Assert.Equal("518", Ask("sizeToShowAllOf(760, 480, 760, 442).height"));
        Assert.Equal("776", Ask("sizeToShowAllOf(760, 480, 744, 442).width"));
    }

    [Fact]
    public void Never_asks_for_a_window_smaller_than_the_stage()
    {
        // A client area already bigger than the stage is nothing to shrink.
        Assert.Equal("760", Ask("sizeToShowAllOf(760, 480, 770, 490).width"));
        Assert.Equal("480", Ask("sizeToShowAllOf(760, 480, 770, 490).height"));
    }

    [Fact]
    public void Runs_what_the_disc_named_once_the_copy_has_landed()
    {
        const string game = "{ name: 'Arcanum', plays: 'Arcanum.exe' }";

        Assert.Equal(
            @"C:\Games\Arcanum\Arcanum.exe",
            Ask(@"whatToRunAfterCopying(" + game + @", 'C:\\Games\\Arcanum')"));
    }

    [Fact]
    public void Will_not_guess_which_of_several_executables_starts_a_game()
    {
        // Guessing which of six is the game is how somebody ends up running an
        // uninstaller. The disc only names one when there was only one.
        Assert.Equal(
            "null",
            Ask(@"String(whatToRunAfterCopying({ name: 'Q', plays: '' }, 'C:\\Games\\Q'))"));
    }

    [Fact]
    public void Gathers_every_file_under_a_folder_before_copying_any()
    {
        string from = Path.Combine(_folder, "from");
        Directory.CreateDirectory(Path.Combine(from, "data", "deeper"));
        File.WriteAllText(Path.Combine(from, "game.exe"), "12345");
        File.WriteAllText(Path.Combine(from, "data", "one.dat"), "123");
        File.WriteAllText(Path.Combine(from, "data", "deeper", "two.dat"), "1234567");

        // The bar cannot say how far along it is without knowing the whole,
        // and a folder that cannot be read is better found now than half way.
        Assert.Equal("3", Ask(WithFiles("whatThereIsToCopy(f, " + AsJScript(from) + ").length")));
        Assert.Equal("15", Ask(WithFiles("howMuchInAll(whatThereIsToCopy(f, " + AsJScript(from) + "))")));
    }

    [Fact]
    public void Leaves_the_disc_s_own_files_where_they_are()
    {
        // A game that is the only thing on a disc sits at the ROOT, beside the
        // menu, the autorun and the checksums. Copying it off should not carry
        // those onto somebody's hard disk.
        string disc = Path.Combine(_folder, "disc");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        Directory.CreateDirectory(Path.Combine(disc, "modules"));

        File.WriteAllText(Path.Combine(disc, "Arcanum.exe"), "12345");
        File.WriteAllText(Path.Combine(disc, "modules", "Arcanum.dat"), "123");
        File.WriteAllText(Path.Combine(disc, "autorun.inf"), "x");
        File.WriteAllText(Path.Combine(disc, "checksums.sha256"), "x");
        File.WriteAllText(Path.Combine(disc, "Start Here.hta"), "x");
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "x");

        string ours = "['autorun.inf', 'checksums.sha256', 'Start Here.hta', 'AUTORUN']";

        // The game's two files, and neither the four of ours nor the folder.
        Assert.Equal(
            "2",
            Ask(WithFiles("whatThereIsToCopy(f, " + AsJScript(disc) + ", " + ours + ").length")));

        Assert.Equal(
            "8",
            Ask(WithFiles("howMuchInAll(whatThereIsToCopy(f, " + AsJScript(disc) + ", " + ours + "))")));
    }

    [Fact]
    public void Only_leaves_them_behind_at_the_root()
    {
        // A game with a folder of its own called AUTORUN inside it is the
        // game's business. Only the top of the disc belongs to the disc.
        string disc = Path.Combine(_folder, "deeper");
        Directory.CreateDirectory(Path.Combine(disc, "data", "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "data", "AUTORUN", "theirs.dat"), "123");

        Assert.Equal(
            "1",
            Ask(WithFiles("whatThereIsToCopy(f, " + AsJScript(disc) + ", ['AUTORUN']).length")));
    }

    [Fact]
    public void Notices_when_a_file_did_not_arrive()
    {
        string from = Path.Combine(_folder, "source");
        string to = Path.Combine(_folder, "landed");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, "whole.dat"), "12345");
        File.WriteAllText(Path.Combine(from, "cut short.dat"), "1234567890");

        File.WriteAllText(Path.Combine(to, "whole.dat"), "12345");
        File.WriteAllText(Path.Combine(to, "cut short.dat"), "12345");

        // Checked by size rather than by hash: a menu has no business reading
        // several gigabytes twice, and what this catches is the copy that
        // stopped when somebody took the disc out.
        Assert.Equal(
            "cut short.dat",
            Ask(WithFiles("String(everythingArrived(f, whatThereIsToCopy(f, " + AsJScript(from) + "), "
                + AsJScript(to) + "))")));
    }

    [Fact]
    public void Says_nothing_is_missing_when_it_all_arrived()
    {
        string from = Path.Combine(_folder, "all of it");
        string to = Path.Combine(_folder, "all of it, copied");
        Directory.CreateDirectory(Path.Combine(from, "sub"));
        Directory.CreateDirectory(Path.Combine(to, "sub"));
        File.WriteAllText(Path.Combine(from, "a.dat"), "123");
        File.WriteAllText(Path.Combine(from, "sub", "b.dat"), "4567");
        File.WriteAllText(Path.Combine(to, "a.dat"), "123");
        File.WriteAllText(Path.Combine(to, "sub", "b.dat"), "4567");

        Assert.Equal(
            "null",
            Ask(WithFiles("String(everythingArrived(f, whatThereIsToCopy(f, " + AsJScript(from) + "), "
                + AsJScript(to) + "))")));
    }

    /// <summary>
    /// One expression that has a FileSystemObject in scope.
    /// </summary>
    /// <remarks>
    /// Ask evaluates a single expression, so a var in front of it is a syntax
    /// error. A function called where it stands is still one expression.
    /// </remarks>
    private static string WithFiles(string expression) =>
        "(function () { var f = new ActiveXObject('Scripting.FileSystemObject'); return "
            + expression + "; })()";

    [Fact]
    public void Runs_an_add_on_only_once_its_game_is_there()
    {
        const string addOn = "{ name: 'Patch', runs: 'GAME 1/ADD-ONS/patch.exe' }";

        Assert.Equal(@"D:\GAME 1\ADD-ONS\patch.exe", Ask("whatAnAddOnRuns(" + addOn + ", true, 'D:')"));

        // A dead button somebody clicks anyway is still a click, and a patch
        // run before its game half-applies to nothing.
        Assert.Equal("null", Ask("String(whatAnAddOnRuns(" + addOn + ", false, 'D:'))"));
    }

    [Fact]
    public void Really_starts_what_it_is_pointed_at()
    {
        // The only proof that a menu button does anything at all. An HTA is
        // invisible to UI Automation, so no test can press one: the launching
        // lives in menu.js precisely so that this can run it for real.
        //
        // It starts a script that writes a file and stops, which is harmless,
        // observable, and leaves nothing running.
        string marker = Path.Combine(_folder, "it-ran.txt");
        string script = Path.Combine(_folder, "prove it.cmd");

        File.WriteAllText(script, "@echo off" + Environment.NewLine + "echo yes> \"" + marker + "\"");

        Assert.Equal("null", Ask("String(startIt(" + AsJScript(script) + ", ''))"));

        bool appeared = false;

        for (int waited = 0; waited < 100 && !appeared; waited++)
        {
            Thread.Sleep(100);
            appeared = File.Exists(marker);
        }

        Assert.True(appeared, $"Nothing ran. Expected {marker} to appear.");
    }

    [Fact]
    public void Leaves_the_menu_standing_where_it_was_after_starting_something_in_a_folder()
    {
        // Left in the folder it started an installer in, the menu could not
        // remove that folder afterwards: "Permission denied", the owner's
        // set on 2026-11-10, every file gone and the folder still there.
        string script = Path.Combine(_folder, "quiet.cmd");
        string copy = Path.Combine(_folder, "A Game installer");
        Directory.CreateDirectory(copy);
        File.WriteAllText(script, "@echo off");

        string said = Ask(
            "(function () { var sh = new ActiveXObject('WScript.Shell'); var before = sh.CurrentDirectory;"
            + " var r = startIt(" + AsJScript(script) + ", " + AsJScript(copy) + ");"
            + " return String(r) + '|' + (sh.CurrentDirectory === before ? 'where it was' : sh.CurrentDirectory); }())");

        Assert.Equal("null|where it was", said);
    }

    [Fact]
    public void Steps_out_of_a_copy_it_is_standing_in_and_removes_the_folder_too()
    {
        string copy = Path.Combine(_folder, "A Game installer");
        Directory.CreateDirectory(copy);
        File.WriteAllText(Path.Combine(copy, "setup.exe"), "12345");

        string said = Ask(WithFiles(
            "(function () { new ActiveXObject('WScript.Shell').CurrentDirectory = " + AsJScript(copy) + ";"
            + " return String(removeTheCopy(f, " + AsJScript(copy) + ")); }())"));

        Assert.Equal("null", said);
        Assert.False(Directory.Exists(copy), "the folder itself should be gone, not only its files");
    }

    [Fact]
    public void Says_the_installer_is_starting_before_it_does()
    {
        Assert.Equal(
            @"Starting the installer from F:\Hollow Knight installer. Windows checks a file this size before it runs it, which can take a minute.",
            Ask(@"startingTheInstallerSays('F:\\Hollow Knight installer')"));
    }

    [Fact]
    public void Says_what_went_wrong_rather_than_failing_in_silence()
    {
        // A menu button that does nothing when pressed is the worst outcome
        // here: somebody is standing in front of a disc they burned, and no
        // amount of correct behaviour elsewhere makes up for it.
        string missing = Path.Combine(_folder, "there is no such thing.exe");

        string said = Ask("String(startIt(" + AsJScript(missing) + ", ''))");

        // Looked for first, and said in words, rather than handed to Windows,
        // which answered the owner's Manual on disc 2 on 2026-11-10 with
        // "That would not start: ... (0)".
        Assert.Equal("That file is not where the menu expected it: " + missing, said);
    }

    [Fact]
    public void Says_a_folder_is_missing_rather_than_opening_somebodys_documents()
    {
        // Explorer handed a folder that does not exist opens Documents with
        // no word, which the owner met pressing Extras on disc 2.
        string missing = Path.Combine(_folder, "Extras");

        Assert.Equal(
            "That folder is not where the menu expected it: " + missing,
            Ask("String(openTheFolder(" + AsJScript(missing) + "))"));
    }

    [Fact]
    public void Tells_a_missing_file_from_a_missing_folder()
    {
        // A folder is not a file to start, and a file is not a folder to
        // open: each is looked for as what it is.
        Assert.Equal("true", Ask("String(thereIsNo(" + AsJScript(_folder) + ", false))"));
        Assert.Equal("false", Ask("String(thereIsNo(" + AsJScript(_folder) + ", true))"));
    }

    [Fact]
    public void Says_so_when_there_is_nothing_to_start_at_all()
    {
        Assert.Equal(
            "There is nothing on this disc for that button to start.",
            Ask("String(startIt('', ''))"));

        Assert.Equal(
            "There is no folder on this disc for that button to open.",
            Ask("String(openTheFolder(''))"));
    }

    /// <summary>
    /// A Windows path as a JScript string, with its backslashes doubled.
    /// </summary>
    /// <remarks>
    /// Built from a character code rather than typed. This exact character has
    /// been halved on the way into a generated file several times in this
    /// project, and a path that arrives halved points at nothing.
    /// </remarks>
    private static string AsJScript(string path)
    {
        string slash = ((char)92).ToString();

        return "'" + path.Replace(slash, slash + slash, StringComparison.Ordinal) + "'";
    }

    // ------------------------------------------- a set, put back together

    private const string ASetFile =
        "DiscWright disc set\\n===================\\n\\n  BIG GAME\\n  disc 2 of 3.  4 files in the set, 14 bytes in all.\\n\\n"
        + "disc  sha256  bytes  file\\n"
        + "1     aaaa 5             *setup.exe\\n"
        + "1     bbbb 3             *big.bin.dwpart01\\n"
        + "2     cccc 4             *big.bin.dwpart02\\n"
        + "3     dddd 2             *data/more file.dat\\n\\n"
        + "JOIN eeee 7             *big.bin\\n"
        + "PART 1   *big.bin.dwpart01\\n"
        + "PART 2   *big.bin.dwpart02\\n";

    [Fact]
    public void Reads_every_row_of_the_set_file_and_nothing_else()
    {
        // A row starts with a digit and holds a star; the name is everything
        // after the star, because it can hold spaces.
        Assert.Equal("4", Ask("setRowsFrom('" + ASetFile + "').length"));
        Assert.Equal("data/more file.dat", Ask("setRowsFrom('" + ASetFile + "')[3].path"));
        Assert.Equal("3", Ask("setRowsFrom('" + ASetFile + "')[3].disc"));
        Assert.Equal("4", Ask("setRowsFrom('" + ASetFile + "')[2].bytes"));
    }

    [Fact]
    public void Leaves_the_disc_s_own_files_out_of_the_game()
    {
        // The set file lists the menu and the autorun too, because any one
        // disc has to be able to say what the whole set is. None of that
        // goes into the folder the game is put back into. Found on the first
        // real set, where AUTORUN and Start Here.hta would have landed beside
        // the installer.
        const string rows = "[{disc: 1, path: 'setup.exe'}, {disc: 1, path: 'AUTORUN/menu.hta'}, {disc: 1, path: 'autorun.inf'},"
            + " {disc: 1, path: 'Start Here.hta'}, {disc: 2, path: 'data/more file.dat'}, {disc: 2, path: 'Disc set.txt'}]";
        const string ours = "['autorun.inf', 'checksums.sha256', 'Start Here.hta', 'Disc set.txt', 'AUTORUN']";

        Assert.Equal(
            "setup.exe,data/more file.dat",
            Ask("(function () { var kept = theGamesPartOf(" + rows + ", " + ours + "); var names = [];"
                + " for (var i = 0; i < kept.length; i++) { names.push(kept[i].path); } return names.join(','); })()"));

        // A game's own folder called AUTORUN, deeper in, is the game's.
        Assert.Equal(
            "1",
            Ask("theGamesPartOf([{disc: 1, path: 'game/AUTORUN/theirs.dat'}], ['AUTORUN']).length"));
    }

    [Fact]
    public void Reads_the_cut_files_and_their_pieces_in_order()
    {
        Assert.Equal("1", Ask("setJoinsFrom('" + ASetFile + "').length"));
        Assert.Equal("big.bin", Ask("setJoinsFrom('" + ASetFile + "')[0].path"));
        Assert.Equal("7", Ask("setJoinsFrom('" + ASetFile + "')[0].bytes"));
        Assert.Equal(
            "big.bin.dwpart01,big.bin.dwpart02",
            Ask("setJoinsFrom('" + ASetFile + "')[0].pieces.join(',')"));
    }

    [Fact]
    public void Reads_which_disc_the_set_file_belongs_to_off_the_file_itself()
    {
        // Read off the disc rather than baked into the menu, because one menu
        // drives the whole set and the disc under it gets swapped.
        Assert.Equal("2", Ask("whichDiscTheSetFileSays('" + ASetFile + "')"));
        Assert.Equal("3", Ask("howManyDiscsTheSetFileSays('" + ASetFile + "')"));
        Assert.Equal("0", Ask("whichDiscTheSetFileSays('nothing of the kind')"));
    }

    /// <summary>A scan over a written folder: what is there and how big.</summary>
    private static string ScanWith(string present, int discIn) =>
        "(function () { var have = " + present + "; "
        + "var rows = setRowsFrom('" + ASetFile + "'); var joins = setJoinsFrom('" + ASetFile + "'); "
        + "var s = setScan(rows, joins, " + discIn + ", function (p) { return have[p] === undefined ? -1 : have[p]; }); "
        + "return s.ok + '/' + s.total + ' missing=' + s.missing + ' bad=' + s.bad + ' need=' + s.need.join(',')"
        + " + ' pending=' + s.pending + ' complete=' + s.complete + ' mine=' + s.mineHere + '/' + s.mineTotal; })()";

    [Fact]
    public void Knows_which_discs_are_still_wanted_from_what_is_in_the_folder()
    {
        Assert.Equal("0/4 missing=4 bad=0 need=1,2,3 pending=0 complete=false mine=0/1", Ask(ScanWith("{}", 2)));

        Assert.Equal(
            "2/4 missing=2 bad=0 need=2,3 pending=0 complete=false mine=0/1",
            Ask(ScanWith("{'setup.exe': 5, 'big.bin.dwpart01': 3}", 2)));
    }

    [Fact]
    public void Counts_a_half_copied_file_as_bad_and_wants_its_disc_again()
    {
        // A different problem from a disc nobody has put in yet, and said so.
        Assert.Equal(
            "1/4 missing=2 bad=1 need=1,2,3 pending=0 complete=false mine=1/2",
            Ask(ScanWith("{'setup.exe': 2, 'big.bin.dwpart01': 3}", 1)));
    }

    [Fact]
    public void Is_not_complete_while_a_cut_file_is_still_in_pieces()
    {
        // Every piece here and not yet joined is work still to do on this
        // machine, not a disc still to come. It happens to anybody who copies
        // the discs in Explorer rather than from the menu.
        Assert.Equal(
            "4/4 missing=0 bad=0 need= pending=1 complete=false mine=1/1",
            Ask(ScanWith("{'setup.exe': 5, 'big.bin.dwpart01': 3, 'big.bin.dwpart02': 4, 'data/more file.dat': 2}", 3)));
    }

    [Fact]
    public void Counts_a_joined_file_s_pieces_as_arrived_once_the_pieces_are_gone()
    {
        // The join deletes the pieces when it is done, so without this every
        // piece would read as missing the moment the join succeeded.
        Assert.Equal(
            "4/4 missing=0 bad=0 need= pending=0 complete=true mine=1/1",
            Ask(ScanWith("{'setup.exe': 5, 'big.bin': 7, 'data/more file.dat': 2}", 3)));
    }

    [Theory]
    [InlineData("{}", 1, "Nothing is copied yet. Install copies this disc, then asks for the rest: discs 2, 3.")]
    [InlineData("{'setup.exe': 5, 'big.bin.dwpart01': 3}", 2, "2 of 4 files copied so far. Install copies this disc, then asks for disc 3.")]
    [InlineData("{'setup.exe': 5, 'big.bin.dwpart01': 3}", 1, "2 of 4 files copied so far. Still to come: discs 2, 3.")]
    [InlineData("{'setup.exe': 5, 'big.bin.dwpart01': 3, 'big.bin.dwpart02': 4}", 3, "3 of 4 files copied so far. Install copies this disc and finishes.")]
    [InlineData("{'setup.exe': 5, 'big.bin.dwpart01': 3, 'big.bin.dwpart02': 4}", 1, "3 of 4 files copied so far. Now put disc 3 in.")]
    [InlineData("{'setup.exe': 5, 'big.bin.dwpart01': 3, 'big.bin.dwpart02': 4, 'data/more file.dat': 2}", 3, "Every file is here. One file was cut to fit and still has to be put back together.")]
    [InlineData("{'setup.exe': 5, 'big.bin': 7, 'data/more file.dat': 2}", 3, "Every file is here, in D:\\Games\\Big. Install runs the installer from there.")]
    public void Says_where_the_set_stands_and_what_to_do_next(string present, int discIn, string expected)
    {
        // It ends on what to do next rather than on a fact: "Now put disc 3
        // in" is the same information as "still needed: disc 3", said to
        // somebody standing at a drive with a disc in their hand.
        string sentence = Ask(
            "(function () { var have = " + present + "; "
            + "var rows = setRowsFrom('" + ASetFile + "'); var joins = setJoinsFrom('" + ASetFile + "'); "
            + "var s = setScan(rows, joins, " + discIn + ", function (p) { return have[p] === undefined ? -1 : have[p]; }); "
            + "return restoreSentence(s, " + discIn + ", null, 'D:\\\\Games\\\\Big'); })()");

        Assert.Equal(expected, sentence);
    }

    [Fact]
    public void Says_what_is_happening_while_it_happens()
    {
        Assert.Equal(
            "Copying file 3 of 9 off this disc.",
            Ask("restoreSentence(null, 1, {copying: '3 of 9'}, '')"));
        Assert.Equal(
            "Putting big.bin back together. On a large file this takes a few minutes.",
            Ask("restoreSentence(null, 1, {joining: 'big.bin'}, '')"));
        Assert.Equal(
            "Put disc 2 in. This carries on by itself once it is there.",
            Ask("restoreSentence(null, 1, {wanting: 2}, '')"));
        Assert.Equal(
            "This disc says it is part of a set, but its list of files is missing.",
            Ask("restoreSentence({total: 0}, 1, null, '')"));
    }

    [Fact]
    public void Names_the_folder_while_it_copies_into_it()
    {
        // "I had no idea where it will be copied", the owner on 2026-11-10.
        Assert.Equal(
            @"Copying file 3 of 9 off this disc into D:\Games\Big.",
            Ask(@"restoreSentence(null, 1, {copying: '3 of 9'}, 'D:\\Games\\Big')"));
    }

    // ---------------------------------------------- the install page

    [Fact]
    public void Says_where_the_installer_goes_how_much_room_it_needs_and_what_comes_after()
    {
        // The owner's design on 2026-11-10: a page that asks where, like an
        // installer always did, and says the game's own installer asks about
        // the game afterwards, so the question does not seem to come twice.
        string asked = Ask(
            @"(function () { var p = installPageSays('Hollow Knight', 'D:\\Hollow Knight installer', 1288490189, 365072220160, {ok: 0, total: 9}, 2);"
            + " return [p.headline, p.says, p.go, p.note, String(p.short)].join('|'); }())");

        Assert.Equal(
            "Where the installer goes|"
                + @"Hollow Knight comes on 2 discs. Its installer is copied off them and put back together in D:\Hollow Knight installer."
                + " It needs 1.2 GB, and that drive has 340 GB free.|"
                + "Copy and install|"
                + "Afterwards, Hollow Knight's own installer asks where to install the game.|"
                + "false",
            asked);
    }

    [Fact]
    public void Says_a_complete_copy_is_already_there_rather_than_using_it_without_a_word()
    {
        // What the owner met: a whole copy from an earlier try, and Install
        // went straight to the installer, done at once, nothing said.
        string asked = Ask(
            @"(function () { var p = installPageSays('Hollow Knight', 'C:\\Games\\Hollow Knight', 1, 0, {complete: true, ok: 9, total: 9}, 2);"
            + " return p.says + '|' + p.go + '|' + p.short; }())");

        Assert.Equal(
            @"A complete copy of the installer is already in C:\Games\Hollow Knight. Install runs it from there.|Install|false",
            asked);
    }

    [Fact]
    public void Says_part_of_it_is_already_copied_and_carries_on_there()
    {
        string asked = Ask(
            @"(function () { var p = installPageSays('Hollow Knight', 'D:\\Hollow Knight installer', 1048576, -1, {ok: 4, total: 9}, 2);"
            + " return p.says + '|' + p.go; }())");

        Assert.Equal(
            @"Part of the installer is already copied to D:\Hollow Knight installer. Copying carries on there. It needs 1 MB.|Copy and install",
            asked);
    }

    [Fact]
    public void Says_when_the_drive_is_too_small_and_will_not_go_ahead()
    {
        string asked = Ask(
            @"(function () { var p = installPageSays('Big', 'E:\\Big installer', 2097152, 1048576, {ok: 0, total: 3}, 2);"
            + " return p.says + '|' + p.short; }())");

        Assert.EndsWith("That is not enough. Change the folder to a drive with more room.|true", asked, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_installer_s_folder_so_nobody_takes_it_for_the_game()
    {
        Assert.Equal("Hollow Knight installer", Ask("installerFolderName('Hollow Knight')"));
        Assert.Equal("Half-Life  Opposing Force installer", Ask("installerFolderName('Half-Life: Opposing Force')"));
    }

    [Fact]
    public void Says_each_disc_is_copied_before_the_tray_opens()
    {
        // A disc that copied in seconds went straight to an open tray and
        // read to the owner as nothing having happened.
        Assert.Equal(
            "Disc 1 is copied, 617 MB. Opening the tray for disc 2.",
            Ask("discCopiedSays(1, 646971392, 2)"));
        Assert.Equal("Disc 2 is copied, 600 MB.", Ask("discCopiedSays(2, 629145600, null)"));
        Assert.Equal("Disc 1 is copied, 617 MB. Opening the tray for disc 2.",
            Ask("restoreSentence(null, 1, {copied: 'Disc 1 is copied, 617 MB. Opening the tray for disc 2.'}, '')"));
    }

    [Fact]
    public void Says_how_far_the_copy_is_beside_the_file_it_is_on()
    {
        // "Maybe we can also add a percentage here, not just this", the
        // owner watching "Copying file 2 of 2" on 2026-11-10.
        Assert.Equal(
            @"Copying file 2 of 2 off this disc into D:\Big installer, 43%.",
            Ask(@"restoreSentence(null, 1, {copying: '2 of 2', percent: 43}, 'D:\\Big installer')"));
    }

    [Fact]
    public void Moves_the_bar_through_a_file_by_the_clock_and_the_rate()
    {
        // Nothing on a disc can see inside a copy, so the bar sat at nothing
        // for four minutes on the first real set under "Copying file 1 of
        // 1". Bytes finished, plus seconds on this file at the rate so far.
        Assert.Equal("0", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 100}, 5000)"));
        Assert.Equal("50", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 100}, 10000)"));
        Assert.Equal("75", Ask("restoreBarPercent({done: 500, all: 1000, startedAt: 5000, rate: 100}, 7500)"));
        Assert.Equal("99", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 100}, 99000)"));

        // Before anything has been measured, the 8x CD guess.
        Assert.Equal(
            Ask("percentDone(0, 2, startingRate(), 10000000)"),
            Ask("restoreBarPercent({done: 0, all: 10000000, startedAt: 1000, rate: 0}, 3000)"));
    }

    [Fact]
    public void The_button_says_install_and_which_disc_and_nothing_about_copying()
    {
        // From the person's side that is what it does. The copying is how
        // the install works rather than what they came to do.
        Assert.Equal("Install disc 2 of 3", Ask("restoreButtonSays({complete: false}, 2, 3, false)"));
        Assert.Equal("Install disc 1 of 3", Ask("restoreButtonSays({complete: false}, 0, 3, false)"));
        Assert.Equal("Install", Ask("restoreButtonSays({complete: true}, 3, 3, false)"));
        Assert.Equal("Play", Ask("restoreButtonSays({complete: false}, 1, 3, true)"));
    }

    [Fact]
    public void Says_copy_and_play_rather_than_install_for_a_folder_of_files_across_discs()
    {
        // The owner asked what a non GOG game does. The same copy and join,
        // then the game's exe runs from the folder, so the button says what
        // it would say on one disc, with the disc number after it.
        Assert.Equal("Copy and play disc 1 of 2", Ask("restoreButtonSays({complete: false}, 1, 2, false, 'Copy and play')"));
        Assert.Equal("Copy and play", Ask("restoreButtonSays({complete: true}, 2, 2, false, 'Copy and play')"));
        Assert.Equal("Install disc 1 of 2", Ask("restoreButtonSays({complete: false}, 1, 2, false, undefined)"));
    }

    [Fact]
    public void Knows_whether_the_installer_it_started_is_still_running()
    {
        // By name, through WMI, because a GOG installer asks for elevation
        // and the elevated process is not the one the menu was handed. The
        // script host running this test is a process that certainly exists.
        Assert.Equal("true", Ask("String(installerIsRunning('cscript.exe'))"));
        Assert.Equal("false", Ask("String(installerIsRunning('nothing_of_the_sort_at_all.exe'))"));
    }

    [Fact]
    public void Stops_waiting_for_the_installer_once_it_was_seen_and_is_gone_or_never_appeared()
    {
        Assert.Equal("false", Ask("String(whenTheInstallerIsDone(false, true, 1))"));
        Assert.Equal("false", Ask("String(whenTheInstallerIsDone(true, true, 30))"));
        Assert.Equal("true", Ask("String(whenTheInstallerIsDone(true, false, 2))"));
        Assert.Equal("false", Ask("String(whenTheInstallerIsDone(false, false, 4))"));
        Assert.Equal("true", Ask("String(whenTheInstallerIsDone(false, false, 5))"));
    }

    [Fact]
    public void Says_what_the_installer_is_doing_and_what_it_left()
    {
        Assert.Equal(
            "Installing Hollow Knight. This menu waits for the installer and says when it is done.",
            Ask("whatInstallingSays('Hollow Knight')"));
        Assert.Equal("Hollow Knight is installed. Play starts it.", Ask("whatTheInstallerLeft(true, 'Hollow Knight')"));
        Assert.Equal(
            "The installer closed, and Hollow Knight is not on this machine. Install runs it again.",
            Ask("whatTheInstallerLeft(false, 'Hollow Knight')"));
    }

    [Fact]
    public void Says_a_size_the_way_a_person_does()
    {
        Assert.Equal("7 bytes", Ask("bytesInWords(7)"));
        Assert.Equal("12 KB", Ask("bytesInWords(12288)"));
        Assert.Equal("491 MB", Ask("bytesInWords(514588672)"));
        Assert.Equal("1.2 GB", Ask("bytesInWords(1241037720)"));
        Assert.Equal("Remove the copy, 1.2 GB", Ask("removeTheCopyButtonSays(1241037720)"));
    }

    [Fact]
    public void Offers_to_remove_the_copy_only_once_the_game_is_installed()
    {
        Assert.Equal(
            "Hollow Knight is installed. Play starts it. The 1.2 GB copied to C:\\Users\\x\\Games\\Hollow Knight is not needed any more.",
            Ask(@"whatTheInstallerLeft(true, 'Hollow Knight', 1241037720, 'C:\\Users\\x\\Games\\Hollow Knight')"));
        Assert.Equal("Hollow Knight is installed. Play starts it.", Ask("whatTheInstallerLeft(true, 'Hollow Knight', 0, '')"));
        Assert.Equal(
            "The installer closed, and Hollow Knight is not on this machine. Install runs it again.",
            Ask(@"whatTheInstallerLeft(false, 'Hollow Knight', 1241037720, 'C:\\Users\\x\\Games\\Hollow Knight')"));
    }

    [Fact]
    public void Removes_only_a_folder_it_made_itself()
    {
        // The guard between a button and somebody's files. A drive root, the
        // profile and anything not directly under a folder called Games is
        // left alone whatever asks.
        Assert.Equal("true", Ask(@"String(mayRemoveTheCopy('C:\\Users\\x\\Games\\Hollow Knight'))"));
        Assert.Equal("true", Ask(@"String(mayRemoveTheCopy('D:\\Games\\Hollow Knight\\'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('C:\\'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('C:\\Users\\x'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('C:\\Users\\x\\Games'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('C:\\Users\\x\\Documents\\Hollow Knight'))"));
        Assert.Equal("false", Ask("String(mayRemoveTheCopy(''))"));

        // Named the way the install page names one, since 2026-11-10, and
        // never a bare drive or a folder only called installer.
        Assert.Equal("true", Ask(@"String(mayRemoveTheCopy('D:\\Hollow Knight installer'))"));
        Assert.Equal("true", Ask(@"String(mayRemoveTheCopy('F:\\My Games\\Hollow Knight installer\\'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('D:\\installer'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('D:\\ installer'))"));
        Assert.Equal("false", Ask(@"String(mayRemoveTheCopy('Hollow Knight installer'))"));
    }

    [Fact]
    public void Removes_the_copy_read_only_files_and_all_and_says_what_went_wrong()
    {
        string games = Path.Combine(_folder, "Games");
        string copy = Path.Combine(games, "Big Game");
        Directory.CreateDirectory(Path.Combine(copy, "data"));
        File.WriteAllText(Path.Combine(copy, "setup.exe"), "12345");
        File.WriteAllText(Path.Combine(copy, "data", "more.dat"), "12");
        File.SetAttributes(Path.Combine(copy, "setup.exe"), FileAttributes.ReadOnly);

        Assert.Equal("7", Ask(WithFiles("bytesIn(f, " + AsJScript(copy) + ")")));
        Assert.Equal("null", Ask(WithFiles("String(removeTheCopy(f, " + AsJScript(copy) + "))")));
        Assert.False(Directory.Exists(copy), "the copy should be gone");
        Assert.True(Directory.Exists(games), "only the copy, not the folder above it");

        // Gone already is not a failure, and a folder it did not make is
        // refused with its name.
        Assert.Equal("null", Ask(WithFiles("String(removeTheCopy(f, " + AsJScript(copy) + "))")));
        Assert.StartsWith("That is not a folder this menu made", Ask(WithFiles("removeTheCopy(f, " + AsJScript(_folder) + ")")), StringComparison.Ordinal);
        Assert.True(Directory.Exists(_folder));
    }

    [Fact]
    public void Cannot_eject_a_drive_that_is_not_there_and_says_so_quietly()
    {
        // The real eject opens a real tray, which no test may do. A letter
        // with no drive behind it, and a folder that is not a drive root,
        // both come back false rather than throwing.
        Assert.Equal("false", Ask(@"String(ejectTheDisc('Q:\\'))"));
        Assert.Equal("false", Ask("String(ejectTheDisc('not a drive'))"));
        Assert.Equal("false", Ask("String(ejectTheDisc(''))"));
    }

    [Fact]
    public void Suggests_the_drive_with_the_most_room_in_a_folder_named_for_the_installer()
    {
        // Not the profile and not Program Files: the disc goes into machines
        // nobody here has seen, and a set is bigger than one disc.
        Assert.Equal(
            @"D:\Big Game installer",
            Ask(@"restoreFolderFor('C:\\Users\\somebody', 'Big Game', [{letter: 'C:', free: 1000}, {letter: 'D:', free: 5000}], 900)"));
        Assert.Equal(
            @"C:\Half-Life  Opposing Force installer",
            Ask(@"restoreFolderFor('C:\\Users\\somebody', 'Half-Life: Opposing Force', [{letter: 'C:', free: 9000}, {letter: 'D:', free: 5000}], 900)"));
    }

    [Fact]
    public void Falls_back_to_the_profile_when_no_drive_can_be_read()
    {
        Assert.Equal(
            @"C:\Users\somebody\Big Game installer",
            Ask(@"restoreFolderFor('C:\\Users\\somebody', 'Big Game', [], 900)"));
    }

    [Fact]
    public void Quotes_a_drive_root_so_the_command_line_reads_it_as_one()
    {
        // A backslash before a closing quote escapes the quote, so a quoted
        // drive root ran on into the next argument and robocopy chose a
        // destination inside System32. Every disc is a drive root.
        Assert.Equal("\"D:\\\\\"", Ask(@"setQuote('D:\\')"));
        Assert.Equal("\"C:\\Games\\Big\"", Ask(@"setQuote('C:\\Games\\Big')"));
    }

    [Fact]
    public void Copies_one_file_at_a_time_with_robocopy_and_keeps_its_folder()
    {
        Assert.Equal(
            "robocopy \"D:\\\\\" \"C:\\Games\\Big\" \"setup.exe\" /R:1 /W:1 /NJH /NJS",
            Ask(@"setCopyCommand('D:\\', 'C:\\Games\\Big', 'setup.exe')"));

        Assert.Equal(
            "robocopy \"D:\\data\" \"C:\\Games\\Big\\data\" \"more file.dat\" /R:1 /W:1 /NJH /NJS",
            Ask(@"setCopyCommand('D:\\', 'C:\\Games\\Big', 'data/more file.dat')"));
    }

    [Fact]
    public void Joins_the_pieces_with_copy_b_in_order()
    {
        Assert.Equal(
            "copy /b \"C:\\Games\\Big\\big.bin.dwpart01\"+\"C:\\Games\\Big\\big.bin.dwpart02\" \"C:\\Games\\Big\\big.bin\"",
            Ask(@"setJoinCommand('C:\\Games\\Big', {path: 'big.bin', pieces: ['big.bin.dwpart01', 'big.bin.dwpart02']})"));
    }

    [Fact]
    public void Puts_a_set_of_two_discs_back_together_from_real_folders()
    {
        // The whole engine against real folders, under cscript: disc 1's
        // share copied, disc 2's share copied, the cut file put back together
        // from its pieces, the pieces gone, and the scan calling it complete.
        // robocopy and copy /b are the ones Windows ships, so this is the same
        // mechanism a disc runs.
        string disc1 = Path.Combine(_folder, "disc1");
        string disc2 = Path.Combine(_folder, "disc2");
        string dest = Path.Combine(_folder, "put back");
        Directory.CreateDirectory(disc1);
        Directory.CreateDirectory(Path.Combine(disc2, "data"));
        File.WriteAllText(Path.Combine(disc1, "setup.exe"), "12345");
        File.WriteAllText(Path.Combine(disc1, "big.bin.dwpart01"), "ABC");
        File.WriteAllText(Path.Combine(disc2, "big.bin.dwpart02"), "DEFG");
        File.WriteAllText(Path.Combine(disc2, "data", "more file.dat"), "12");

        // Read-only, the way every file on a CD is, and robocopy carries the
        // attribute over. On the first real set the join was perfect and
        // both pieces stayed behind, because the delete did not force it.
        File.SetAttributes(Path.Combine(disc1, "big.bin.dwpart01"), FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(disc2, "big.bin.dwpart02"), FileAttributes.ReadOnly);

        const string rows = "[{disc: 1, sha256: 'a', bytes: 5, path: 'setup.exe'}, {disc: 1, sha256: 'b', bytes: 3, path: 'big.bin.dwpart01'},"
            + " {disc: 2, sha256: 'c', bytes: 4, path: 'big.bin.dwpart02'}, {disc: 2, sha256: 'd', bytes: 2, path: 'data/more file.dat'}]";
        const string joins = "[{path: 'big.bin', sha256: 'e', bytes: 7, pieces: ['big.bin.dwpart01', 'big.bin.dwpart02']}]";

        string went = Ask(
            "(function () { var f = new ActiveXObject('Scripting.FileSystemObject'); var out = []; var quiet = function () {};"
            + " var rows = " + rows + "; var joins = " + joins + ";"

            // What the engine says while it copies carries what the bar
            // needs: when this file started and the rate so far, which is
            // the 8x CD guess until a file has been timed.
            + " var heard = function (p) { if (p.copying) { out.push('says ' + p.copying + ' rate=' + (p.rate > 0) + ' started=' + (p.startedAt > 0)); } };"
            + " copyTheSetDisc({files: f, discRoot: " + AsJScript(disc1) + ", dir: " + AsJScript(dest) + ", rows: rows, discIn: 1, say: heard,"
            + "   after: function (bad) { out.push('disc 1 bad=' + bad); }});"
            + " var one = setScan(rows, joins, 1, sizeOfUnder(f, " + AsJScript(dest) + "));"
            + " out.push('then ok=' + one.ok + ' need=' + one.need.join(',') + ' complete=' + one.complete);"
            + " copyTheSetDisc({files: f, discRoot: " + AsJScript(disc2) + ", dir: " + AsJScript(dest) + ", rows: rows, discIn: 2, say: quiet,"
            + "   after: function (bad) { out.push('disc 2 bad=' + bad); }});"
            + " joinThePieces({files: f, dir: " + AsJScript(dest) + ", joins: joins, say: quiet,"
            + "   after: function (failed) { out.push('failed=' + failed.length); }});"
            + " var two = setScan(rows, joins, 2, sizeOfUnder(f, " + AsJScript(dest) + "));"
            + " out.push('then ok=' + two.ok + ' pending=' + two.pending + ' complete=' + two.complete);"
            + " return out.join(' | '); })()");

        Assert.Equal(
            "says 1 of 2 rate=true started=true | says 2 of 2 rate=true started=true | disc 1 bad=0"
            + " | then ok=2 need=2 complete=false | disc 2 bad=0 | failed=0 | then ok=4 pending=0 complete=true",
            went);

        Assert.Equal("ABCDEFG", File.ReadAllText(Path.Combine(dest, "big.bin")));
        Assert.Equal("12", File.ReadAllText(Path.Combine(dest, "data", "more file.dat")));
        Assert.False(File.Exists(Path.Combine(dest, "big.bin.dwpart01")), "the pieces should be gone once the file is whole");
        Assert.False(File.Exists(Path.Combine(dest, "big.bin.dwpart02")));
    }

    [Fact]
    public void Copies_a_game_off_a_disc_without_holding_on_to_the_thread()
    {
        // THE BAR HAS TO BE ABLE TO MOVE. FileSystemObject.CopyFile does not
        // come back until the file is there, so on a game that is one large
        // file the page could not repaint and Windows painted the menu with
        // the not-responding ghost. Watched on a real disc, 2026-11-05, 126 MB
        // in one file, case 7.
        //
        // So the same thing the set restore does: a detached job per file, and
        // what it says while it runs carries the two numbers the bar needs,
        // when this file started and the rate so far. Without those
        // restoreBarPercent has nothing to work from and the bar sits still
        // however often it is redrawn.
        string disc = Path.Combine(_folder, "a disc");
        string dest = Path.Combine(_folder, "copied off");
        Directory.CreateDirectory(Path.Combine(disc, "data"));
        File.WriteAllText(Path.Combine(disc, "TheGame.exe"), "12345");
        File.WriteAllText(Path.Combine(disc, "data", "things.pak"), "ABCDEFG");

        string went = Ask(WithFiles(
            "(function () { var out = []; var items = whatThereIsToCopy(f, " + AsJScript(disc) + ", []);"
            + " var heard = function (p) { if (p.copying) { out.push('says ' + p.copying + ' rate=' + (p.rate > 0) + ' started=' + (p.startedAt > 0)); } };"
            + " copyAGameOff({files: f, items: items, from: " + AsJScript(disc) + ", destination: " + AsJScript(dest) + ","
            + "   say: heard, after: function (bad, stuck) { out.push('bad=' + bad + ' stuck=' + stuck); }});"
            + " return out.join(' | '); })()"));

        Assert.Equal(
            "says 1 of 2 rate=true started=true | says 2 of 2 rate=true started=true | bad=0 stuck=0",
            went);

        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, "TheGame.exe")));
        Assert.Equal("ABCDEFG", File.ReadAllText(Path.Combine(dest, "data", "things.pak")));
    }

    [Fact]
    public void Leaves_the_copy_writable_so_the_game_can_save()
    {
        // THE WHOLE REASON THAT BUTTON EXISTS. The comment above whatToDoWith
        // says it: a game run off a disc cannot save, because the disc is read
        // only and the first thing it writes either fails or goes nowhere. So
        // the menu copies it onto the machine first.
        //
        // And the copy was read-only too, because every file on a CD is and
        // every way of copying one carries the attribute across. The game
        // still could not save over anything that came off the disc. Seen on a
        // real disc, 2026-11-05: all three files of the copy, read-only.
        string disc = Path.Combine(_folder, "read only disc");
        string dest = Path.Combine(_folder, "writable copy");
        Directory.CreateDirectory(Path.Combine(disc, "data"));
        File.WriteAllText(Path.Combine(disc, "TheGame.exe"), "12345");
        File.WriteAllText(Path.Combine(disc, "data", "save.dat"), "ABC");
        File.SetAttributes(Path.Combine(disc, "TheGame.exe"), FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(disc, "data", "save.dat"), FileAttributes.ReadOnly);

        Assert.Equal("bad=0 stuck=0", Ask(WithFiles(
            "(function () { var said = ''; var items = whatThereIsToCopy(f, " + AsJScript(disc) + ", []);"
            + " copyAGameOff({files: f, items: items, from: " + AsJScript(disc) + ", destination: " + AsJScript(dest) + ","
            + "   say: function () {}, after: function (bad, stuck) { said = 'bad=' + bad + ' stuck=' + stuck; }});"
            + " return said; })()")));

        // The bytes arrived AND they can be written over, which is the half
        // that was missing.
        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, "TheGame.exe")));
        Assert.False(
            File.GetAttributes(Path.Combine(dest, "TheGame.exe")).HasFlag(FileAttributes.ReadOnly),
            "a game copied off a disc so that it can save has to be writable");
        Assert.False(
            File.GetAttributes(Path.Combine(dest, "data", "save.dat")).HasFlag(FileAttributes.ReadOnly),
            "and so does everything under it");

        // And the disc is left exactly as it was found.
        Assert.True(File.GetAttributes(Path.Combine(disc, "TheGame.exe")).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void Counts_a_file_that_would_not_copy_rather_than_claiming_it_did()
    {
        // A disc that stops being readable half way through is the ordinary
        // failure here, and the copy has to say which file it stopped on
        // rather than hand over a game with a hole in it. Simulated by naming
        // a file in the list that is not on the disc at all.
        string disc = Path.Combine(_folder, "a scratched disc");
        string dest = Path.Combine(_folder, "half a game");
        Directory.CreateDirectory(disc);
        File.WriteAllText(Path.Combine(disc, "TheGame.exe"), "12345");

        string went = Ask(WithFiles(
            "(function () { var said = '';"
            + " var items = [{from: " + AsJScript(Path.Combine(disc, "TheGame.exe")) + ", to: 'TheGame.exe', bytes: 5},"
            + "              {from: " + AsJScript(Path.Combine(disc, "gone.pak")) + ", to: 'gone.pak', bytes: 99}];"
            + " copyAGameOff({files: f, items: items, from: " + AsJScript(disc) + ", destination: " + AsJScript(dest) + ","
            + "   say: function () {}, after: function (bad, stuck) { said = 'bad=' + bad + ' stuck=' + stuck; }});"
            + " return said; })()"));

        Assert.Equal("bad=1 stuck=0", went);

        // The one that was there still arrived. A bad file is not a reason to
        // throw away the rest.
        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, "TheGame.exe")));
    }

    [Fact]
    public void Says_which_file_is_in_flight_so_the_bar_has_something_to_move_on()
    {
        // restoreBarPercent is what turns what the engine says into a number on
        // the screen, and it works off startedAt and rate. The copy off a
        // single disc used to pass a hardcoded nought where the seconds go, so
        // the estimate was written, tested, and switched off at the one call
        // site that needed it.
        Assert.Equal(
            "40",
            Ask("restoreBarPercent({done: 200, all: 1000, startedAt: 1000, rate: 100}, 3000)"));

        // Nothing in flight, so nothing is guessed on top.
        Assert.Equal(
            "20",
            Ask("restoreBarPercent({done: 200, all: 1000, startedAt: 0, rate: 100}, 3000)"));
    }
    [Fact]
    public void Opens_the_folder_when_nothing_was_picked_to_start_the_game()
    {
        // CASE 7 PART 3, AND THE REASON THIS IS A FUNCTION. The disc holds two
        // things that could be the game and the question of which one starts it
        // was cancelled when the disc was built, so the menu copies the folder
        // and opens it and runs nothing.
        //
        // It was judged by hand on a real disc on 2026-11-05 and only two
        // thirds of it could be judged: the copy was whole and nothing had
        // started, and whether Explorer ever opened could not be established
        // afterwards, because a window that has been closed leaves nothing
        // behind. All of this lived in the page then, where no test could ask
        // it anything. Now it can be asked directly.
        const string game = "{ name: 'Two Of Them', runs: '', plays: '' }";

        Assert.Equal("open", Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games\\\\Two', 0, 0, null).act"));
        Assert.Equal("C:\\Games\\Two", Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games\\\\Two', 0, 0, null).what"));
    }

    [Fact]
    public void Runs_the_one_that_was_picked_and_nothing_else()
    {
        // Case 7 part 2: two things that could be the game, one of them chosen
        // when the disc was built, so that one runs and the other is copied and
        // left alone.
        const string game = "{ name: 'Two Of Them', runs: '', plays: 'TheGame.exe' }";

        Assert.Equal("run", Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games\\\\Two', 0, 0, null).act"));
        Assert.Equal(
            "C:\\Games\\Two\\TheGame.exe",
            Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games\\\\Two', 0, 0, null).what"));
    }

    [Fact]
    public void Starts_nothing_on_top_of_a_copy_that_is_not_whole()
    {
        // A half copied game that runs is worse than one that does not: it
        // fails somewhere later and nobody connects it to the disc.
        const string game = "{ name: 'A Game', runs: '', plays: 'TheGame.exe' }";

        Assert.Equal(
            "stop",
            Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 0, 0, 'data\\\\things.pak').act"));
        Assert.Contains(
            "starting with data",
            Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 0, 0, 'data\\\\things.pak').say"),
            StringComparison.Ordinal);

        // And the same when robocopy was the one that said no.
        Assert.Equal("stop", Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 2, 0, null).act"));
        Assert.Contains(
            "2 of the files would not copy",
            Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 2, 0, null).say"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Says_so_when_the_copy_is_still_read_only_rather_than_letting_saves_fail_later()
    {
        // The attribute is cleared per file, and a file that would not give it
        // up is said out loud. A game that runs and then cannot save is a fault
        // nobody would connect to a disc they copied an hour earlier.
        const string game = "{ name: 'A Game', runs: '', plays: 'TheGame.exe' }";
        string said = Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 0, 3, null).say");

        Assert.Contains("3 of the copied files are still read-only", said, StringComparison.Ordinal);
        Assert.Contains("may not be able to save", said, StringComparison.Ordinal);

        // It is a warning and not a refusal: the game is there and it still runs.
        Assert.Equal("run", Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 0, 3, null).act"));

        // And nothing is said when every file gave it up, which is the
        // ordinary case and must not raise a dialog.
        Assert.Equal("null", Ask("whatToDoAfterACopy(" + game + ", 'C:\\\\Games', 0, 0, null).say"));
    }
    [Fact]
    public void Leaves_the_pieces_alone_when_one_of_them_is_still_missing()
    {
        string dest = Path.Combine(_folder, "half");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "big.bin.dwpart01"), "ABC");

        const string joins = "[{path: 'big.bin', sha256: 'e', bytes: 7, pieces: ['big.bin.dwpart01', 'big.bin.dwpart02']}]";

        string went = Ask(
            "(function () { var f = new ActiveXObject('Scripting.FileSystemObject'); var out = [];"
            + " joinThePieces({files: f, dir: " + AsJScript(dest) + ", joins: " + joins + ", say: function () {},"
            + "   after: function (failed) { out.push('failed=' + failed.length); }}); return out.join(' | '); })()");

        Assert.Equal("failed=0", went);
        Assert.True(File.Exists(Path.Combine(dest, "big.bin.dwpart01")));
        Assert.False(File.Exists(Path.Combine(dest, "big.bin")));
    }

    // ------------------------------------------------ a preview starts nothing

    [Fact]
    public void Starts_things_normally_on_a_real_disc()
    {
        // The default, because a disc is the ordinary case and a menu that
        // had to be told it was real would be one wrong line from inert.
        Assert.Equal("null", Ask("String(whyNothingRuns())"));
    }

    [Theory]
    [InlineData("startIt('C:\\somewhere\\setup.exe', '')")]
    [InlineData("openTheFolder('C:\\somewhere')")]
    public void Starts_nothing_at_all_once_it_is_told_it_is_a_preview(string pressing)
    {
        // A preview stages the menu, the artwork and the music and nothing
        // else, so every button points at a file that is not there. Pressing
        // one used to produce whatever Windows says about a missing path,
        // which reads as the disc being broken rather than as the preview
        // doing its job.
        string said = Ask("(thisIsAPreview(), String(" + pressing + "))");

        Assert.Contains("preview of the menu", said, StringComparison.Ordinal);
        Assert.Contains("has not been made yet", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_it_is_a_preview_before_it_says_a_path_is_missing()
    {
        // Both are true of a preview, and only one of them is useful. "There
        // is nothing on this disc for that button to start" invites somebody
        // to go looking for what went wrong with their disc.
        string said = Ask("(thisIsAPreview(), String(startIt('', '')))");

        Assert.Contains("preview of the menu", said, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing on this disc", said, StringComparison.Ordinal);
    }

    private string Ask(string expression, bool withoutGetObject = false)
    {
        (string output, int exitCode) = Run(expression, withoutGetObject);

        Assert.True(exitCode == 0, $"cscript failed for `{expression}`:\n{output}");

        return output.Trim();
    }

    private (string Output, int ExitCode) Run(string expression, bool withoutGetObject = false)
    {
        string job = Path.Combine(_folder, "ask.wsf");

        File.WriteAllText(
            job,
            "<job>\n"
            // Taking it away before menu.js loads is the whole point: this is
            // the engine the disc really gets, and cscript's own GetObject
            // would otherwise hide a call that cannot work there.
            + (withoutGetObject
                ? "  <script language=\"JScript\">GetObject = undefined;</script>\n"
                : string.Empty)
            + "  <script language=\"JScript\" src=\"menu.js\" />\n"
            + "  <script language=\"JScript\">\n"
            + "    WScript.Echo(" + expression + ");\n"
            + "  </script>\n"
            + "</job>\n",
            new UTF8Encoding(false));

        ProcessStartInfo how = new()
        {
            FileName = "cscript.exe",
            WorkingDirectory = _folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        how.ArgumentList.Add("//nologo");
        how.ArgumentList.Add(job);

        using Process? running = Process.Start(how);
        Assert.NotNull(running);

        string output = running.StandardOutput.ReadToEnd() + running.StandardError.ReadToEnd();
        running.WaitForExit(30_000);

        return (output, running.ExitCode);
    }

    // ------------------------------------------- checking the disc itself

    [Fact]
    public void Reads_the_list_the_builder_actually_writes()
    {
        // The C# writer and the JScript reader agreeing on the same bytes,
        // the same proof the set file has.
        string list = Path.Combine(_folder, "checksums.sha256");

        File.WriteAllText(list, ChecksumList.For("A GAME",
        [
            new ChecksumEntry(@"GAME 1\setup.exe", new string('a', 64), 3),
            new ChecksumEntry("readme.txt", new string('b', 64), 5)
        ]));

        string asked = Ask(WithFiles(
            "(function (s) { return s.rows.length + '|' + s.declared + '|' + s.rows[0].path + '|' + s.rows[0].hash.length; })"
            + "(sumsFrom(f.OpenTextFile(" + AsJScript(list) + ", 1).ReadAll()))"));

        Assert.Equal("2|2|GAME 1/setup.exe|64", asked);
    }

    [Fact]
    public void Trusts_no_list_that_disagrees_with_its_own_count()
    {
        // A list that has lost its tail verifies the files it still mentions
        // and reports every one of them right, which is the single way a
        // checksum list can lie to you. The count is there to be checked.
        string said = Ask(
            "sumsTrouble({rows: [1, 2], declared: 3})");

        Assert.Contains("says it covers 3 files and lists 2", said, StringComparison.Ordinal);
        Assert.Contains("damaged", said, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sumsTrouble({rows: [1, 2], declared: 2})")]
    [InlineData("sumsTrouble({rows: [1], declared: -1})")]
    public void Relies_on_a_list_that_adds_up(string call)
    {
        Assert.Equal(string.Empty, Ask(call));
    }

    [Fact]
    public void Says_there_is_nothing_to_check_against_when_the_list_is_empty()
    {
        Assert.Contains(
            "nothing to check against",
            Ask("sumsTrouble({rows: [], declared: -1})"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pulls_the_hash_out_of_what_certutil_prints()
    {
        // certutil frames the hash with a headline and a sign off, and the
        // hash itself is the one line with no spaces in it.
        string asked = Ask(
            "hashOutOf('SHA256 hash of file x:\\r\\n' + new Array(65).join('a')"
            + " + '\\r\\nCertUtil: -hashfile command completed successfully.')");

        Assert.Equal(new string('a', 64), asked);
    }

    [Fact]
    public void Takes_an_error_from_certutil_as_no_hash_at_all()
    {
        Assert.Equal(
            string.Empty,
            Ask("hashOutOf('CertUtil: -hashfile command FAILED: 0x80070002')"));
    }

    [Fact]
    public void Says_how_far_through_the_disc_the_check_is_and_which_file_it_is_on()
    {
        // Minutes of a still window is how somebody decides it has hung.
        Assert.Equal(
            "Checking the disc, 87%: file 3 of 9 against what was burned.",
            Ask("checkStep(3, 9, 87)"));
    }

    [Fact]
    public void The_check_moves_inside_one_long_file()
    {
        // The owner's 2026-11-10 night: "file 39 of 39" sat still for
        // minutes on a 600 MB piece. 100 MB done in 10 s is 10 MB/s, so 30 s
        // into a 600 MB file is another 300 MB of 1 GB: 40%, then 60% at 50 s.
        string at30 = Ask("checkPercent(104857600, 1048576000, 10485760, 10000, 40000)");
        string at50 = Ask("checkPercent(104857600, 1048576000, 10485760, 10000, 60000)");

        Assert.Equal("40", at30);
        Assert.Equal("60", at50);
    }

    [Fact]
    public void Waiting_on_a_job_says_something_on_every_look_that_finds_it_running()
    {
        // What moves the check's percentage inside one long file. A window
        // stood in, so the wait queues its next look rather than sleeping.
        string asked = Ask(
            "(function () {"
            + " var looks = []; var heard = 0;"
            + " window = { setTimeout: function (fn, ms) { looks.push(ms); } };"
            + " waitForJob({ flag: 'C:\\\\no such folder\\\\never.done' }, function () { }, function () { heard++; });"
            + " window = undefined;"
            + " return heard + '|' + looks.length;"
            + " }())");

        Assert.Equal("1|1", asked);
    }

    [Fact]
    public void The_check_guesses_before_any_file_has_finished_and_never_says_100()
    {
        // Nothing done yet: the usual 8x guess, 1.2 MB a second.
        Assert.Equal("10", Ask("checkPercent(0, 12288000, rateFrom([], startingRateFor(12288000)), 0, 1000)"));

        // Long past the end of the estimate, it still is not finished.
        Assert.Equal("99", Ask("checkPercent(100, 1000, 100, 1000, 999999)"));
    }

    [Fact]
    public void The_check_is_not_slowed_to_nothing_by_small_files_read_while_the_drive_spins_up()
    {
        // The owner's Dead Space set: a few small files first, read while
        // the drive woke, then two big ones that sat at 0% until they were
        // done. 60 KB in a second and a half is 40 KB a second, and at that
        // rate a minute into a 4 GB disc is nothing. Not counted, it is the
        // DVD guess instead: a minute is 332 MB, 8%.
        Assert.Equal(
            "8",
            Ask("checkPercent(60000, 4000000000, rateFrom([{bytes: 60000, seconds: 1.5}], startingRateFor(4000000000)), 0, 60000)"));

        // What it said before: the small file's rate, and nothing moving.
        Assert.Equal("0", Ask("checkPercent(60000, 4000000000, 60000 / 1.5, 0, 60000)"));
    }

    [Fact]
    public void Times_only_files_big_enough_to_say_something_about_the_drive()
    {
        // Small and slow is the drive spinning up, not the drive's speed.
        Assert.Equal("1228800", Ask("rateFrom([{bytes: 60000, seconds: 1.5}, {bytes: 16777215, seconds: 4}], 1228800)"));

        // 16 MB and more counts.
        Assert.Equal("4194304", Ask("rateFrom([{bytes: 60000, seconds: 1.5}, {bytes: 16777216, seconds: 4}], 1228800)"));
    }

    [Fact]
    public void Guesses_a_DVD_faster_than_a_CD()
    {
        // A full CD-R is about 703 MB; more than 900 MB is a DVD, read at a
        // guess of 4x DVD rather than 8x CD.
        Assert.Equal("1228800", Ask("startingRateFor(737280000)"));
        Assert.Equal("5540000", Ask("startingRateFor(4700000000)"));
    }

    [Fact]
    public void The_copy_bar_is_exact_once_it_is_known_how_much_has_been_read()
    {
        // Half of a 500 byte file read, with nothing done before it, of 1000.
        Assert.Equal("25", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 1, read: 250, bytes: 500}, 5000)"));

        // The clock plays no part once it is known.
        Assert.Equal("25", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 1, read: 250, bytes: 500}, 99000)"));

        // Reading more than the file holds counts as the file, and the end is
        // still not 100.
        Assert.Equal("70", Ask("restoreBarPercent({done: 200, all: 1000, read: 900, bytes: 500}, 0)"));
        Assert.Equal("99", Ask("restoreBarPercent({done: 500, all: 1000, read: 500, bytes: 500}, 0)"));

        // Not known is the estimate, as before.
        Assert.Equal("50", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 100, read: -1}, 10000)"));
        Assert.Equal("50", Ask("restoreBarPercent({done: 0, all: 1000, startedAt: 5000, rate: 100}, 10000)"));
    }

    [Fact]
    public void Asks_how_much_has_been_read_every_other_look_and_says_it_when_it_is_known()
    {
        string asked = Ask(
            "(function () {"
            + " var said = []; var asks = 0; var answer = -1;"
            + " bytesReadSoFar = function (job, program) { asks++; return program === 'robocopy.exe' ? answer : -9; };"
            + " var look = howMuchHasBeenRead({ flag: 'x' }, { copying: '1 of 2', done: 10, all: 1000, startedAt: 7, rate: 5 }, 500,"
            + "   function (p) { said.push(p.copying + '/' + p.done + '/' + p.all + '/' + p.startedAt + '/' + p.rate + '/' + p.read + '/' + p.bytes); });"
            + " look(); look();"
            + " answer = 300; look(); look();"
            + " return asks + '|' + said.join(',');"
            + "}())");

        // Asked on the second and fourth looks; the first answer was not
        // known, so only the second was said.
        Assert.Equal("2|1 of 2/10/1000/7/5/300/500", asked);
    }

    [Fact]
    public void Cannot_tell_how_much_a_job_has_read_when_there_is_no_such_job()
    {
        // The menu's own engine, without GetObject. Nothing found is -1, and
        // the copy goes on estimating.
        Assert.Equal(
            "-1",
            Ask("bytesReadSoFar({ flag: 'C:' + BACKSLASH + 'no such' + BACKSLASH + 'dwjob0_never.done' }, 'robocopy.exe')", withoutGetObject: true));
    }

    [Fact]
    public void Both_copies_watch_what_is_read_and_the_check_times_only_files_worth_timing()
    {
        // The parts that only run with a window: waitForJob looks in on a job
        // only when there is a page to keep alive, which a test under cscript
        // is not. So the wiring is read off the script itself.
        string script = File.ReadAllText(Repository.File("src", "DiscWright.Menu", "menu.js"));

        foreach (string copy in new[] { "function copyAGameOff(", "function copyTheSetDisc(" })
        {
            string body = FunctionIn(script, copy);

            Assert.Contains("howMuchHasBeenRead(job, saying,", body, StringComparison.Ordinal);
            Assert.Contains("rateFrom(state.samples, startingRateFor(state.all))", body, StringComparison.Ordinal);
        }

        string check = FunctionIn(script, "function checkTheDisc(");
        Assert.Contains("rateFrom(samples, startingRateFor(all))", check, StringComparison.Ordinal);
        Assert.Contains("samples.push(", check, StringComparison.Ordinal);
    }

    private static string FunctionIn(string script, string opening)
    {
        int start = script.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, opening + " is not in menu.js");

        // To the next function at the left margin, which is where the next
        // one starts in this file.
        int end = script.IndexOf("\nfunction ", start + opening.Length, StringComparison.Ordinal);

        return end < 0 ? script[start..] : script[start..end];
    }

    [Fact]
    public void Watches_a_real_robocopy_read_its_file()
    {
        // The whole way, on this machine: a job started the way the menu
        // starts one, the program found through its cmd, and its bytes read
        // counted by Windows, in the engine the disc gets. Slowed with an
        // inter-packet gap so there is a copy to watch.
        string from = Path.Combine(_folder, "from");
        string to = Path.Combine(_folder, "to");
        Directory.CreateDirectory(from);

        byte[] chunk = new byte[1 << 20];
        new Random(7).NextBytes(chunk);

        using (FileStream big = File.Create(Path.Combine(from, "big.bin")))
        {
            for (int i = 0; i < 32; i++)
            {
                big.Write(chunk);
            }
        }

        string Js(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

        string asked = Ask(
            "(function () {"
            + " var job = startAJob('robocopy \"" + Js(from) + "\" \"" + Js(to) + "\" big.bin /IPG:10 /R:1 /W:1 /NJH /NJS');"
            + " var most = -1; var until = (new Date()).getTime() + 15000;"
            + " while (!jobIsDone(job) && (new Date()).getTime() < until) {"
            + "   var read = bytesReadSoFar(job, 'robocopy.exe');"
            + "   if (read > most) { most = read; }"
            + "   WScript.Sleep(200);"
            + " }"
            + " var finished = false; waitForJob(job, function (code) { finished = code < 8; });"
            + " return (most > 0 ? 'seen' : 'NOT SEEN ' + most) + '|' + (most <= 33554432 ? 'within' : 'MORE ' + most) + '|' + finished;"
            + "}())",
            withoutGetObject: true);

        Assert.Equal("seen|within|true", asked);
        Assert.Equal(32L << 20, new FileInfo(Path.Combine(to, "big.bin")).Length);
    }

    [Fact]
    public void Gives_a_clean_disc_a_clean_verdict()
    {
        Assert.Equal(
            "4 files checked. Every one of them is byte for byte what was burned.",
            Ask("checkVerdict(4, [])"));
    }

    [Fact]
    public void Names_every_file_that_let_the_disc_down()
    {
        string asked = Ask(
            "checkVerdict(4, ['bad.txt', 'gone.txt']).split('\\n').join('|')");

        Assert.StartsWith("2 of 4 files on this disc are wrong or missing:", asked, StringComparison.Ordinal);
        Assert.Contains("|bad.txt|gone.txt|", asked, StringComparison.Ordinal);
        Assert.Contains("scratched or was burned badly", asked, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_real_files_with_real_hashes_and_names_what_is_wrong()
    {
        // The whole loop against a real folder: real files, the list the
        // builder writes, and the hashing done by the same certutil the disc
        // will meet. One file is good, one is in a folder, one does not match
        // its line, and one is not there at all.
        string disc = Path.Combine(_folder, "disc");
        Directory.CreateDirectory(Path.Combine(disc, "DATA"));
        File.WriteAllText(Path.Combine(disc, "good.txt"), "12345");
        File.WriteAllText(Path.Combine(disc, "DATA", "also good.txt"), "abc");
        File.WriteAllText(Path.Combine(disc, "bad.txt"), "not what the list says");

        File.WriteAllText(Path.Combine(disc, "checksums.sha256"), ChecksumList.For("A GAME",
        [
            new ChecksumEntry("good.txt", Sha256Of("12345"), 5),
            new ChecksumEntry(@"DATA\also good.txt", Sha256Of("abc"), 3),
            new ChecksumEntry("bad.txt", Sha256Of("12345"), 22),
            new ChecksumEntry("gone.txt", Sha256Of("x"), 1)
        ]));

        string asked = Ask(WithFiles(
            "(function () {"
            + " var sums = sumsFrom(f.OpenTextFile(" + AsJScript(Path.Combine(disc, "checksums.sha256")) + ", 1).ReadAll());"
            + " var steps = []; var verdict = '';"
            + " checkTheDisc(" + AsJScript(disc) + ", sums, function (w) { steps.push(w); }, function (v) { verdict = v; });"
            + " return steps.length + '|' + steps[0] + '|' + verdict.split('\\n').join('~');"
            + " }())"));

        string[] parts = asked.Split('|');

        // Three files are there to hash; the fourth is missing and never
        // reaches certutil.
        Assert.Equal("3", parts[0]);
        Assert.Matches(@"^Checking the disc, \d{1,2}%: file 1 of 3 against what was burned\.$", parts[1]);
        Assert.StartsWith("2 of 4 files on this disc are wrong or missing:", parts[2], StringComparison.Ordinal);
        Assert.Contains("bad.txt", parts[2], StringComparison.Ordinal);
        Assert.Contains("gone.txt", parts[2], StringComparison.Ordinal);
        Assert.DoesNotContain("good.txt", parts[2], StringComparison.Ordinal);
    }

    private static string Sha256Of(string text) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(text)))
            .ToLowerInvariant();

    /// <summary>
    /// Walks up to the solution and takes menu.js from the source tree, so this
    /// needs no assembly of ours loaded.
    /// </summary>
    private static string MenuScriptInTheSourceTree()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DiscWright.slnx"))
               && !File.Exists(Path.Combine(directory.FullName, "DiscWright.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Found no DiscWright solution above " + AppContext.BaseDirectory);
        }

        return Path.Combine(directory.FullName, "src", "DiscWright.Menu", "menu.js");
    }
}
