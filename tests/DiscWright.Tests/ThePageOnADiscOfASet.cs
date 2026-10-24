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
                + "pump(function () { return closed; });\n"
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

                // Disc 2 found in the drive, copied, the installer put back
                // together and started from the folder, and the menu gone.
                "start " + Path.Combine(dest, "setup_big.exe") + " in " + dest,
                "close",
                "button=Install",
                "line=Every file is here, in " + dest + ". Install runs the installer from there.",
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

        // Runs what the page queued, in order, until there is nothing left or
        // the condition holds. A job that is not finished queues itself again,
        // so this is also what waits for robocopy. A page that never settles
        // is a failure after a minute rather than a test that never ends:
        // with a fault planted in the scan, the page would ask for disc 1
        // again for ever.
        + "function pump(until) {\n"
        + "    var deadline = (new Date()).getTime() + 60 * 1000;\n"
        + "    while (window.timers.length && !until()) {\n"
        + "        if ((new Date()).getTime() > deadline) { throw new Error('the page never settled: ' + out.join(' | ')); }\n"
        + "        WScript.Sleep(10);\n"
        + "        (window.timers.shift())();\n"
        + "    }\n"
        + "}\n"
        + "function said() {\n"
        + "    out.push('button=' + document.getElementById('doIt').innerText);\n"
        + "    out.push('line=' + document.getElementById('stillWanted').innerText);\n"
        + "}\n";

    private string Run(string pageScript, string menuUrl, string test)
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

        Assert.True(running.ExitCode == 0, "cscript failed:\n" + output);

        return output;
    }

    private static string AsJScript(string text)
    {
        string slash = ((char)92).ToString();

        return "'" + text.Replace(slash, slash + slash, StringComparison.Ordinal).Replace("'", slash + "'", StringComparison.Ordinal) + "'";
    }
}
