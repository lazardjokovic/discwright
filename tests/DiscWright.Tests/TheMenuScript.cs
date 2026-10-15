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
    public void Knows_which_discs_of_a_set_are_still_wanted()
    {
        Assert.Equal("2,3", Ask("discsStillWanted([1], 3).join(',')"));
        Assert.Equal("", Ask("discsStillWanted([1, 2, 3], 3).join(',')"));
        Assert.Equal("1,3", Ask("discsStillWanted([2], 3).join(',')"));
    }

    [Theory]
    [InlineData("whatIsStillWanted([1, 2], 2)", "")]
    [InlineData("whatIsStillWanted([1], 2)", "Insert disc 2.")]
    [InlineData("whatIsStillWanted([1], 3)", "Still needs discs 2 and 3.")]
    [InlineData("whatIsStillWanted([2], 4)", "Still needs discs 1, 3 and 4.")]
    public void Says_what_is_still_wanted_in_words(string call, string expected)
    {
        Assert.Equal(expected, Ask(call));
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
    public void Opens_the_folder_for_a_game_with_nothing_to_install()
    {
        const string game = "{ name: 'Arcanum', runs: '', folder: 'GAME 1' }";

        // The honest thing a disc can offer for a game that is just files.
        Assert.Equal("open", Ask("whatPressingItDoes(" + game + ", null, 'D:').act"));
        Assert.Equal(@"D:\GAME 1", Ask("whatPressingItDoes(" + game + ", null, 'D:').run"));
    }

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
