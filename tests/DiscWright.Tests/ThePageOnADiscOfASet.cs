using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using DiscWright.Core;
using DiscWright.Menu;

namespace DiscWright.Tests;

/// <summary>
/// The page's own part of installing a set, run under cscript with a stood-in
/// document, from pressing Install on disc 1 to the installer starting from
/// the folder after disc 2.
/// </summary>
/// <remarks>
/// <para>
/// An HTA is invisible to UI Automation, so nothing can press its buttons.
/// What this does instead is take the script block the menu writer generates,
/// the same one the disc carries, and run it under cscript with a
/// <c>document</c>, a <c>window</c> and an <c>alert</c> stood in by the test.
/// The engine it drives is the real one in menu.js, against real folders,
/// with the real robocopy and the real copy /b.
/// </para>
/// <para>
/// Everything that is asynchronous on the page goes through
/// <c>window.setTimeout</c>, so the stand-in queues the calls and the test
/// pumps them, which is how "put disc 2 in" can be answered by pointing the
/// page at another folder between two pumps.
/// </para>
/// </remarks>
public sealed class ThePageOnADiscOfASet : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "dwpage_" + Guid.NewGuid().ToString("N")[..8]);

    public ThePageOnADiscOfASet() => Directory.CreateDirectory(_folder);

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
    public void Installs_a_set_of_two_discs_from_its_menu_with_one_press()
    {
        string disc1 = Path.Combine(_folder, "disc1");
        string disc2 = Path.Combine(_folder, "disc2");
        string dest = Path.Combine(_folder, "put back");

        // Disc 1 holds the first piece of the installer and a readme. Disc 2
        // holds the second piece and a file in a folder. The set file on each
        // is the real one, written by the same code the builder uses, so this
        // is also the C# writer and the JScript reader agreeing.
        Directory.CreateDirectory(Path.Combine(disc1, "AUTORUN"));
        Directory.CreateDirectory(Path.Combine(disc2, "AUTORUN"));
        Directory.CreateDirectory(Path.Combine(disc2, "data"));
        File.WriteAllText(Path.Combine(disc1, "setup_big.exe.dwpart01"), "ABC");
        File.WriteAllText(Path.Combine(disc1, "readme.txt"), "12345");
        File.WriteAllText(Path.Combine(disc2, "setup_big.exe.dwpart02"), "DEFG");
        File.WriteAllText(Path.Combine(disc2, "data", "more file.dat"), "12");

        // Read-only, as everything on a CD is. Found on the first real set.
        File.SetAttributes(Path.Combine(disc1, "setup_big.exe.dwpart01"), FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(disc2, "setup_big.exe.dwpart02"), FileAttributes.ReadOnly);

        // And the disc's own files, which the set file lists like everything
        // else and which must not come along.
        foreach (string disc in new[] { disc1, disc2 })
        {
            File.WriteAllText(Path.Combine(disc, "autorun.inf"), "[autorun]");
            File.WriteAllText(Path.Combine(disc, "Start Here.hta"), "<hta/>");
            File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");
        }

        SetEntry[] entries =
        [
            new(1, "setup_big.exe.dwpart01", "a1", 3),
            new(1, "readme.txt", "b2", 5),
            new(1, "autorun.inf", "f6", 9),
            new(1, "Start Here.hta", "g7", 6),
            new(1, "AUTORUN/menu.hta", "h8", 6),
            new(2, "setup_big.exe.dwpart02", "c3", 4),
            new(2, "data/more file.dat", "d4", 2),
            new(2, "autorun.inf", "f6", 9),
            new(2, "Start Here.hta", "g7", 6),
            new(2, "AUTORUN/menu.hta", "h8", 6)
        ];
        SetJoin[] joins = [new("setup_big.exe", "e5", 7, ["setup_big.exe.dwpart01", "setup_big.exe.dwpart02"])];

        foreach ((string disc, int number) in new[] { (disc1, 1), (disc2, 2) })
        {
            File.WriteAllText(
                Path.Combine(disc, SetManifest.FileName),
                SetManifest.For("BIG GAME", entries, number, 2, "CD-R 700 MB", DateTimeOffset.UtcNow, joins));
        }

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Big Game", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Big Game", "setup_big.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc1, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "SET = loadTheSet();\n"
                + "SET.dir = " + AsJScript(dest) + ";\n"
                + "var game = gameAt(DISC.games, 0);\n"
                + "showTheSet(game);\n"
                + "said();\n"
                + "startIt = function (what, from) { out.push('start ' + what + ' in ' + from); return null; };\n"

                // Every distinct width the bar's fill is given, so the test
                // can see that it moved while a file copied.
                + "var drawTheSet = sayTheSet; var lastWidth = null;\n"
                + "sayTheSet = function (s) { drawTheSet(s); var w = document.getElementById('restoreBarIn').style.width;"
                + " if (w !== undefined && w !== lastWidth) { widths.push(w); lastWidth = w; } };\n"

                // The big button, pressed the way the page presses it.
                + "doTheThing();\n"
                + "pump(function () { return restoring.wanting > 0; });\n"
                + "said();\n"

                // The bar was drawn while the file copied, which on files this
                // small is 99% at once, and the button is dead
                // while the set is being worked on: a second press starts
                // nothing. Both found on the first real set.
                + "out.push('bar=' + (widths.length >= 1 ? 'drawn' : 'never drawn') + ' button=' + document.getElementById('doIt').parentNode.className);\n"
                + "var before = window.timers.length; doTheThing(); out.push('press again=' + (window.timers.length - before));\n"
                + "document.URL = " + AsJScript(new Uri(Path.Combine(disc2, "AUTORUN", "menu.hta")).AbsoluteUri) + ";\n"
                + "WHERE = null;\n"
                + "drain();\n"
                + "out.push('title=' + document.title);\n"
                + "said();\n"
                + "out.push('alerts=' + alerts.length + (alerts.length ? ' ' + alerts.join(' / ') : ''));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                // Before anything is pressed: the button, and the line under it.
                "button=Install, disc 1 of 2",
                "line=Nothing is copied yet. Install copies this disc, then asks for disc 2.",

                // Disc 1 copied, and the page asking for the next one without
                // anybody pressing anything else.
                "button=Install, disc 1 of 2",
                "line=Put disc 2 in. This carries on by itself once it is there.",
                "bar=drawn button=btn next dead",
                "press again=0",

                // Disc 2 found in the drive, the title following it, the
                // installer put back together and started from the folder.
                // The menu stays and watches the installer; the stand-in
                // starts nothing, so after five looks it says what it left.
                "start " + Path.Combine(dest, "setup_big.exe") + " in " + dest,
                "title=Big Game (disc 2 of 2)",
                "button=Install",
                "line=The installer closed, and Big Game is not on this machine. Install runs it again.",
                "alerts=0"
            ],
            lines);

        Assert.Equal("ABCDEFG", File.ReadAllText(Path.Combine(dest, "setup_big.exe")));
        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, "readme.txt")));
        Assert.Equal("12", File.ReadAllText(Path.Combine(dest, "data", "more file.dat")));
        Assert.False(File.Exists(Path.Combine(dest, "setup_big.exe.dwpart01")), "the pieces go once the file is whole");
        Assert.False(File.Exists(Path.Combine(dest, "setup_big.exe.dwpart02")));

        // And nothing of the disc's own came along with the game, although
        // the set file listed every one of them.
        Assert.False(File.Exists(Path.Combine(dest, SetManifest.FileName)));
        Assert.False(File.Exists(Path.Combine(dest, "autorun.inf")));
        Assert.False(File.Exists(Path.Combine(dest, "Start Here.hta")));
        Assert.False(Directory.Exists(Path.Combine(dest, "AUTORUN")));
    }

    [Fact]
    public void Offers_to_remove_the_copy_once_the_game_is_installed_and_removes_it_on_the_press()
    {
        // The last piece of the owner's design: after the installer has run,
        // the copy the discs were put back into is offered for removal, and
        // never removed by itself. A set whose folder is already complete,
        // with the registry stood in to say the game is installed afterwards.
        string disc = Path.Combine(_folder, "disc1");
        string games = Path.Combine(_folder, "Games");
        string dest = Path.Combine(games, "Big Game");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "setup_big.exe"), "1234567");
        File.WriteAllText(Path.Combine(dest, "readme.txt"), "12345");
        File.SetAttributes(Path.Combine(dest, "setup_big.exe"), FileAttributes.ReadOnly);

        SetEntry[] entries =
        [
            new(1, "setup_big.exe.dwpart01", "a1", 3),
            new(1, "readme.txt", "b2", 5),
            new(2, "setup_big.exe.dwpart02", "c3", 4)
        ];
        SetJoin[] joins = [new("setup_big.exe", "e5", 7, ["setup_big.exe.dwpart01", "setup_big.exe.dwpart02"])];
        File.WriteAllText(
            Path.Combine(disc, SetManifest.FileName),
            SetManifest.For("BIG GAME", entries, 1, 2, "CD-R 700 MB", DateTimeOffset.UtcNow, joins));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Big Game", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Big Game", "setup_big.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "SET = loadTheSet();\n"
                + "SET.dir = " + AsJScript(dest) + ";\n"
                + "startIt = function (what, from) { out.push('start ' + what); return null; };\n"
                + "var installedNow = false; isInstalled = function () { return installedNow; };\n"
                + "doTheThing();\n"
                + "installedNow = true;\n"
                + "drain();\n"
                + "out.push('button=' + document.getElementById('doIt').innerText);\n"
                + "out.push('line=' + document.getElementById('stillWanted').innerText);\n"
                + "out.push('offer=' + document.getElementById('removeTheCopy').innerText + ' shown=' + document.getElementById('removeTheCopy').style.display);\n"
                + "removeTheCopyNow();\n"
                + "out.push('then=' + document.getElementById('stillWanted').innerText + ' shown=' + document.getElementById('removeTheCopy').style.display);\n"
                + "out.push('alerts=' + alerts.length + (alerts.length ? ' ' + alerts.join(' / ') : ''));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Equal(
            [
                "start " + Path.Combine(dest, "setup_big.exe"),
                "button=Play",
                "line=Big Game is installed. Play starts it. The 12 bytes copied to " + dest + " is not needed any more.",
                "offer=Remove the copy, 12 bytes shown=block",
                "then=The copy is gone. Big Game is installed. Play starts it. shown=none",
                "alerts=0"
            ],
            went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        Assert.False(Directory.Exists(dest), "the copy should be gone");
        Assert.True(Directory.Exists(games), "and only the copy");
    }

    [Fact]
    public void A_one_disc_install_keeps_the_menu_open_until_the_installer_closes()
    {
        // The same watch on a disc that is not a set: the button says
        // Installing and is dead while the installer runs, and afterwards
        // says what it left rather than the menu having vanished.
        string disc = Path.Combine(_folder, "one");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "setup_small.exe"), "12345");

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Small Game", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Small Game", "setup_small.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "SET = loadTheSet();\n"
                + "startIt = function (what, from) { out.push('start ' + what + ' in ' + from); return null; };\n"
                + "doTheThing();\n"
                + "out.push('while=' + document.getElementById('doIt').innerText.replace(String.fromCharCode(8230), '...') + ' ' + document.getElementById('doIt').parentNode.className);\n"
                + "drain();\n"
                + "out.push('after=' + document.getElementById('doIt').innerText + ' ' + document.getElementById('doIt').parentNode.className);\n"
                + "out.push('line=' + document.getElementById('stillWanted').innerText);\n"
                + "out.push('closed=' + closed + ' alerts=' + alerts.length);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = [.. went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd((char)92))];

        Assert.Equal("start " + Path.Combine(disc, "setup_small.exe") + " in", lines[0]);
        Assert.Equal(
            [
                "start " + Path.Combine(disc, "setup_small.exe") + " in",
                "while=Installing... btn next dead",
                "after=Install btn next",
                "line=The installer closed, and Small Game is not on this machine. Install runs it again.",
                "closed=false alerts=0"
            ],

            // The folder the installer is started in comes with the disc's own
            // trailing separator, which is not worth asserting on.
            lines);
    }

    [Fact]
    public void A_preview_of_a_set_disc_starts_nothing()
    {
        string disc = Path.Combine(_folder, "preview");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(
            Path.Combine(disc, SetManifest.FileName),
            SetManifest.For("BIG GAME", [new SetEntry(1, "a.bin", "a1", 3), new SetEntry(2, "b.bin", "b2", 4)], 1, 2, string.Empty, DateTimeOffset.UtcNow));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Big Game", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: false,
                Games: [new MenuGame("Big Game", "a.bin", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "thisIsAPreview();\n"
                + "SET = loadTheSet();\n"
                + "SET.dir = " + AsJScript(Path.Combine(_folder, "never made")) + ";\n"
                + "doTheThing();\n"
                + "WScript.Echo(alerts.join('|') + '|timers=' + window.timers.length);\n");

        Assert.Equal(
            "This is a preview of the menu. Nothing starts from here, because the disc has not been made yet.|timers=0",
            went.Trim());
        Assert.False(Directory.Exists(Path.Combine(_folder, "never made")));
    }

    [Fact]
    public void Says_so_when_the_page_stops_before_what_was_waited_for_happens()
    {
        // About the waiting loop rather than the page, and the only test here
        // whose run is meant to fail.
        //
        // The loop used to stop as soon as the page had nothing queued,
        // whether or not what it was waiting for had happened, and the caller
        // then compared two long lists built from whatever was on the page.
        // The set test lost that race on a hosted runner once in fourteen,
        // and the failure pointed at the lists rather than at the page having
        // stopped, which cost an evening to understand. Waiting for something
        // that cannot happen now says exactly that instead.
        string went = Run(
            PageScriptFor(new MenuFacts(
                "Big Game", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Big Game", "setup_big.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(_folder, "AUTORUN", "menu.hta")).AbsoluteUri,
            test: "pump(function () { return false; });\n",
            mustSucceed: false);

        Assert.Contains("the page stopped with nothing left to run", went, StringComparison.Ordinal);

        // And not by running out of patience, which is the other way out and
        // would pass this test while meaning something else entirely.
        Assert.DoesNotContain("the page never settled", went, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The generated script block out of the built page: the data block and
    /// the page's own functions, exactly as the disc carries them.
    /// </summary>
    private static string PageScriptFor(MenuFacts facts)
    {
        Match block = Regex.Match(MenuWriter.MenuPage(facts), "<script language=\"JScript\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(block.Success, "No script block found in the generated menu.");

        return block.Groups[1].Value;
    }

    /// <summary>
    /// What the page would find around itself, stood in: enough document for
    /// the set's part of the page, a window whose timers are queued rather
    /// than fired, and an alert that is written down rather than shown.
    /// </summary>
    private static string StandIns(string menuUrl) =>
        "var out = [];\n"
        + "var alerts = [];\n"
        + "var widths = [];\n"
        + "var closed = false;\n"
        + "function alert(text) { alerts.push(String(text)); }\n"
        + "var screen = { availWidth: 1920, availHeight: 1080 };\n"
        + "var discwright = {};\n"
        + "var document = {\n"
        + "    URL: " + AsJScript(menuUrl) + ",\n"
        + "    title: '',\n"
        + "    elements: {},\n"
        + "    documentElement: { clientWidth: 760, clientHeight: 480 },\n"
        + "    body: {},\n"
        + "    getElementById: function (id) {\n"
        + "        if (!this.elements[id]) { this.elements[id] = { id: id, style: {}, innerText: '', innerHTML: '', title: '', parentNode: { className: '' } }; }\n"
        + "        return this.elements[id];\n"
        + "    }\n"
        + "};\n"
        + "var window = {\n"
        + "    timers: [],\n"
        + "    resizeTo: function () {},\n"
        + "    moveTo: function () {},\n"
        + "    close: function () { closed = true; out.push('close'); },\n"
        + "    setTimeout: function (fn, ms) { window.timers.push(fn); }\n"
        + "};\n"

        // Runs what the page queued, in order, until the condition holds. A
        // job that is not finished queues itself again, so this is also what
        // waits for robocopy.
        //
        // It gives up two ways and says which. A page that never settles is a
        // failure after a minute rather than a test that never ends: with a
        // fault planted in the scan, the page would ask for disc 1 again for
        // ever. A page that runs out of work before getting there is a
        // failure too, which it did not used to be.
        //
        // That second one was the whole trouble. The loop asked
        // "while (window.timers.length && !until())", so an empty queue
        // ended the wait whether or not what it was waiting for had happened,
        // and the caller went on to compare two long lists built from
        // whatever the page was showing. On a two core runner the set test
        // lost that race once in fourteen, and it read as a list mismatch
        // rather than as a page that stopped early, which is the most
        // expensive kind of flake there is. Nothing can change the page while
        // a pump is running except the timers it runs, so an empty queue
        // means the condition can no longer come true, and saying so is free.
        + "function pump(until) {\n"
        + "    var deadline = (new Date()).getTime() + 60 * 1000;\n"
        + "    while (!until()) {\n"
        + "        if ((new Date()).getTime() > deadline) { throw new Error('the page never settled: ' + out.join(' | ')); }\n"
        + "        if (!window.timers.length) { throw new Error('the page stopped with nothing left to run, before what this was waiting for happened: ' + out.join(' | ')); }\n"
        + "        WScript.Sleep(10);\n"
        + "        (window.timers.shift())();\n"
        + "    }\n"
        + "}\n"

        // Runs everything the page has queued and stops when it has nothing
        // left, which is a different question from waiting for something.
        //
        // Three tests used to ask for it as pump(function () { return closed; })
        // and none of them ever reached that condition: the page starts the
        // installer and then waits for a process that a stood-in startIt
        // never makes, so it queues nothing more and never closes. The wait
        // was satisfied by the empty queue instead, which is exactly the
        // confusion this pair of functions now avoids. One of those tests
        // records closed as a result, and it is false.
        + "function drain() {\n"
        + "    var deadline = (new Date()).getTime() + 60 * 1000;\n"
        + "    while (window.timers.length) {\n"
        + "        if ((new Date()).getTime() > deadline) { throw new Error('the page never ran out of work: ' + out.join(' | ')); }\n"
        + "        WScript.Sleep(10);\n"
        + "        (window.timers.shift())();\n"
        + "    }\n"
        + "}\n"
        + "function said() {\n"
        + "    out.push('button=' + document.getElementById('doIt').innerText);\n"
        + "    out.push('line=' + document.getElementById('stillWanted').innerText);\n"
        + "}\n";

    /// <summary>Runs the page under cscript and hands back all it said.</summary>
    /// <param name="mustSucceed">
    /// False for the one test that is about the waiting loop giving up, which
    /// can only be shown by a run that is meant to fail.
    /// </param>
    private string Run(string pageScript, string menuUrl, string test, bool mustSucceed = true)
    {
        File.Copy(TheMenuWriter.MenuScriptInTheSourceTree(), Path.Combine(_folder, "menu.js"), overwrite: true);
        File.WriteAllText(Path.Combine(_folder, "standins.js"), StandIns(menuUrl), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_folder, "page.js"), pageScript, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_folder, "test.js"), test, new UTF8Encoding(false));

        string job = Path.Combine(_folder, "page.wsf");
        File.WriteAllText(
            job,
            "<job>\n"
            + "  <script language=\"JScript\" src=\"standins.js\" />\n"
            + "  <script language=\"JScript\" src=\"menu.js\" />\n"
            + "  <script language=\"JScript\" src=\"page.js\" />\n"
            + "  <script language=\"JScript\" src=\"test.js\" />\n"
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
        running.WaitForExit(120_000);

        if (mustSucceed)
        {
            Assert.True(running.ExitCode == 0, "cscript failed:\n" + output);
        }
        else
        {
            Assert.True(running.ExitCode != 0, "this run was meant to fail and did not:\n" + output);
        }

        return output;
    }

    private static string AsJScript(string text)
    {
        string slash = ((char)92).ToString();

        return "'" + text.Replace(slash, slash + slash, StringComparison.Ordinal).Replace("'", slash + "'", StringComparison.Ordinal) + "'";
    }
}
