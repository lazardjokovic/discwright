using System.Diagnostics;
using System.Text;

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
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
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
        Assert.Equal("Copy and play", Ask("whatToDoWith({name: 'x', runs: ''})"));
        Assert.Equal("Copy and play", Ask("whatToDoWith({name: 'x'})"));
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
        Assert.Equal("Copy and play", Ask("whatToDoWith({ name: 'A Game', runs: '' }, false)"));
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
            + " var reg = GetObject('winmgmts:' + BACKSLASH + BACKSLASH + '.' + BACKSLASH + 'root' + BACKSLASH + 'default').Get('StdRegProv');"
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
        Assert.Equal(@"D:", Ask("theDiscIsAbove('file:///D:/AUTORUN/menu.hta')"));
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
    public void Says_what_went_wrong_rather_than_failing_in_silence()
    {
        // A menu button that does nothing when pressed is the worst outcome
        // here: somebody is standing in front of a disc they burned, and no
        // amount of correct behaviour elsewhere makes up for it.
        string missing = Path.Combine(_folder, "there is no such thing.exe");

        string said = Ask("String(startIt(" + AsJScript(missing) + ", ''))");

        Assert.Contains("would not start", said, StringComparison.Ordinal);
        Assert.Contains("there is no such thing.exe", said, StringComparison.Ordinal);
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
    public void The_button_says_install_and_which_disc_and_nothing_about_copying()
    {
        // From the person's side that is what it does. The copying is how
        // the install works rather than what they came to do.
        Assert.Equal("Install, disc 2 of 3", Ask("restoreButtonSays({complete: false}, 2, 3, false)"));
        Assert.Equal("Install, disc 1 of 3", Ask("restoreButtonSays({complete: false}, 0, 3, false)"));
        Assert.Equal("Install", Ask("restoreButtonSays({complete: true}, 3, 3, false)"));
        Assert.Equal("Play", Ask("restoreButtonSays({complete: false}, 1, 3, true)"));
    }

    [Fact]
    public void Puts_the_game_back_under_the_profile_when_that_drive_has_the_room()
    {
        Assert.Equal(
            @"C:\Users\somebody\Games\Big Game",
            Ask(@"restoreFolderFor('C:\\Users\\somebody', 'Big Game', [{letter: 'C:', free: 1000}, {letter: 'D:', free: 5000}], 900)"));
    }

    [Fact]
    public void Puts_the_game_back_on_the_drive_with_the_most_room_when_the_profile_s_has_not_got_it()
    {
        // A set is by definition bigger than one disc.
        Assert.Equal(
            @"D:\Games\Half-Life  Opposing Force",
            Ask(@"restoreFolderFor('C:\\Users\\somebody', 'Half-Life: Opposing Force', [{letter: 'C:', free: 100}, {letter: 'D:', free: 5000}], 900)"));
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

        const string rows = "[{disc: 1, sha256: 'a', bytes: 5, path: 'setup.exe'}, {disc: 1, sha256: 'b', bytes: 3, path: 'big.bin.dwpart01'},"
            + " {disc: 2, sha256: 'c', bytes: 4, path: 'big.bin.dwpart02'}, {disc: 2, sha256: 'd', bytes: 2, path: 'data/more file.dat'}]";
        const string joins = "[{path: 'big.bin', sha256: 'e', bytes: 7, pieces: ['big.bin.dwpart01', 'big.bin.dwpart02']}]";

        string went = Ask(
            "(function () { var f = new ActiveXObject('Scripting.FileSystemObject'); var out = []; var quiet = function () {};"
            + " var rows = " + rows + "; var joins = " + joins + ";"
            + " copyTheSetDisc({files: f, discRoot: " + AsJScript(disc1) + ", dir: " + AsJScript(dest) + ", rows: rows, discIn: 1, say: quiet,"
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
            "disc 1 bad=0 | then ok=2 need=2 complete=false | disc 2 bad=0 | failed=0 | then ok=4 pending=0 complete=true",
            went);

        Assert.Equal("ABCDEFG", File.ReadAllText(Path.Combine(dest, "big.bin")));
        Assert.Equal("12", File.ReadAllText(Path.Combine(dest, "data", "more file.dat")));
        Assert.False(File.Exists(Path.Combine(dest, "big.bin.dwpart01")), "the pieces should be gone once the file is whole");
        Assert.False(File.Exists(Path.Combine(dest, "big.bin.dwpart02")));
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

    private string Ask(string expression)
    {
        (string output, int exitCode) = Run(expression);

        Assert.True(exitCode == 0, $"cscript failed for `{expression}`:\n{output}");

        return output.Trim();
    }

    private (string Output, int ExitCode) Run(string expression)
    {
        string job = Path.Combine(_folder, "ask.wsf");

        File.WriteAllText(
            job,
            "<job>\n"
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
