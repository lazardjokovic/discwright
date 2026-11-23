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
                + "theShare().dir = " + AsJScript(dest) + ";\n"
                + "var game = gameAt(DISC.games, 0);\n"
                + "showTheSet(game);\n"
                + "said();\n"
                + "startIt = function (what, from) { out.push('start ' + what + ' in ' + from); return null; };\n"

                // Every distinct width the bar's fill is given, so the test
                // can see that it moved while a file copied.
                + "var drawTheSet = sayTheSet; var lastWidth = null;\n"
                + "sayTheSet = function (s) { drawTheSet(s); var w = document.getElementById('restoreBarIn').style.width;"
                + " if (w !== undefined && w !== lastWidth) { widths.push(w); lastWidth = w; }"
                + " var l = document.getElementById('stillWanted').innerText; if (l.indexOf(' is copied') >= 0) { copiedSaid = l + ' bar=' + w; } };\n"
                + "var copiedSaid = 'never';\n"

                // The big button, pressed the way the page presses it. The
                // first press opens the install page and copies nothing; the
                // page's own button goes on.
                + "doTheThing();\n"
                + "out.push('page=' + document.getElementById('whereScreen').style.display + ' game=' + document.getElementById('gameScreen').style.display + ' timers=' + window.timers.length);\n"
                + "out.push('says=' + document.getElementById('whereSays').innerText);\n"
                + "out.push('go=' + document.getElementById('whereGo').innerText + ' note=' + document.getElementById('whereNote').innerText);\n"
                + "whereGo();\n"
                + "pump(function () { return restoring.wanting > 0; });\n"
                + "out.push('copied=' + copiedSaid);\n"
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

        // The first press shows the install page, naming the folder and the
        // room, and copies nothing until its own button is pressed. It is
        // not asked again when disc 2 goes in: there is one page line.
        string says = Assert.Single(lines, each => each.StartsWith("says=", StringComparison.Ordinal));
        Assert.StartsWith("says=Big Game comes on 2 discs. Its installer is copied off them and put back together in " + dest + ". It needs ", says, StringComparison.Ordinal);
        Assert.Equal("page=block game=none timers=0", Assert.Single(lines, each => each.StartsWith("page=", StringComparison.Ordinal)));
        Assert.Equal("go=Copy and install note=Afterwards, Big Game's own installer asks where to install the game.", Assert.Single(lines, each => each.StartsWith("go=", StringComparison.Ordinal)));

        // Disc 1 said it was copied, with the bar full, before the tray
        // opened: a disc that copies in moments must still be seen to finish.
        Assert.Equal("copied=Disc 1 is copied, 8 bytes. Opening the tray for disc 2. bar=100%", Assert.Single(lines, each => each.StartsWith("copied=", StringComparison.Ordinal)));

        lines = [.. lines.Where(each => !each.StartsWith("says=", StringComparison.Ordinal) && !each.StartsWith("page=", StringComparison.Ordinal) && !each.StartsWith("go=", StringComparison.Ordinal) && !each.StartsWith("copied=", StringComparison.Ordinal))];

        Assert.Equal(
            [
                // Before anything is pressed: the button, and the line under it.
                "button=Install disc 1 of 2",
                "line=Nothing is copied yet. Install copies this disc, then asks for disc 2.",

                // Disc 1 copied, and the page asking for the next one without
                // anybody pressing anything else.
                "button=Install disc 1 of 2",
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
                + "theShare().dir = " + AsJScript(dest) + ";\n"
                + "startIt = function (what, from) { out.push('start ' + what); return null; };\n"
                + "var installedNow = false; isInstalled = function () { return installedNow; };\n"
                + "doTheThing();\n"

                // The install page first, which names the whole copy already
                // there; its Install goes on.
                + "whereGo();\n"
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

    [Theory]
    // As it comes: the panel is left where its own rule draws it, and the
    // check takes the first place, top left, there being no music.
    [InlineData(MenuSide.Right, false, false, "panel=/", "sums=16px,16px", "mute=/")]
    [InlineData(MenuSide.Right, true, false, "panel=/", "sums=16px,434px", "mute=/")]
    // The list on the left: the panel moves, and the corner is the right one.
    [InlineData(MenuSide.Left, false, false, "panel=0px/auto", "sums=714px,16px", "mute=/")]
    [InlineData(MenuSide.Left, true, false, "panel=0px/auto", "sums=714px,434px", "mute=/")]
    // With music showing as well, the two are a row in the same order.
    [InlineData(MenuSide.Right, false, true, "panel=/", "sums=52px,16px", "mute=16px,16px")]
    [InlineData(MenuSide.Left, true, true, "panel=0px/auto", "sums=714px,434px", "mute=678px,434px")]
    public void The_page_puts_the_list_and_the_corner_where_the_disc_says(
        MenuSide side, bool bottom, bool music, string panel, string sums, string mute)
    {
        // The page's own two functions, run as the page runs them, on a
        // stand-in disc that has a checksum list and so a check to show.
        string disc = Path.Combine(_folder, "laid out " + side + bottom + music);
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "checksums.sha256"), ChecksumList.For("LAID OUT", []));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Laid out", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Laid out", "good.txt", string.Empty)],
                Look: new MenuLook(side, bottom))),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function at(id) { var s = document.getElementById(id).style; return (s.left || '') + ',' + (s.top || ''); }\n"
                + "putTheListOnItsSide();\n"

                // The music button as startTheMusic leaves it when a tune plays.
                + (music ? "document.getElementById('mute').style.display = 'block';\n" : string.Empty)
                + "showTheCheck();\n"
                + "placeTheCornerButtons();\n"
                + "var p = document.getElementById('panel').style;\n"
                + "out.push('panel=' + (p.left || '') + '/' + (p.right || ''));\n"
                + "out.push('sums=' + at('sums'));\n"
                + "out.push('mute=' + (document.getElementById('mute').style.display === 'block' ? at('mute') : '/'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(panel, lines[0]);
        Assert.Equal(sums, lines[1]);
        Assert.Equal(mute, lines[2]);
    }

    [Theory]
    // Two games, small buttons: the corner has them on the list of games,
    // the list has no rows for them, and a game's screen has neither.
    [InlineData(2, true, false,
        "list: rows=none/none corner=block/block open=none at=16px,52px",
        "game: rows=none/none corner=none/none")]
    // One game: its own screen is the main menu.
    [InlineData(1, true, false,
        "",
        "game: rows=none/none corner=block/block")]
    // As it comes: rows on the list of games, nothing in the corner, and
    // since 2.0.6 nothing of the disc's on a game's screen either.
    [InlineData(2, false, false,
        "list: rows=block/block corner=none/none open=none at=,",
        "game: rows=none/none corner=none/none")]
    // With the way into the disc as well, which is last in the row and stays.
    [InlineData(2, true, true,
        "list: rows=none/none corner=block/block open=block at=16px,52px",
        "game: rows=none/none corner=none/none")]
    public void The_discs_own_are_in_the_corner_of_the_main_menu_and_nowhere_else(
        int games, bool small, bool open, string onTheList, string onAGame)
    {
        string disc = Path.Combine(_folder, "own " + games + small + open);
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Own", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [.. Enumerable.Range(1, games).Select(number => new MenuGame("Game " + number, "good.txt", string.Empty))],
                Manual: "Extras/disc.pdf",
                Extras: "Extras",
                Look: new MenuLook(DiscManualAsASmallButton: small, DiscExtrasAsASmallButton: small, OpenTheDiscButton: open))),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function d(id) { return document.getElementById(id).style.display || ''; }\n"
                + "function l(id) { return document.getElementById(id).style.left || ''; }\n"
                + "showTheWayIntoTheDisc();\n"
                + (games > 1
                    ? "showChooser();\n"
                      + "out.push('list: rows=' + d('chooserManual') + '/' + d('chooserExtras') + ' corner=' + d('discManual') + '/' + d('discExtras')"
                      + " + ' open=' + (d('openDisc') || 'none') + ' at=' + l('discManual') + ',' + l('discExtras'));\n"
                    : string.Empty)
                + "showGame(0);\n"
                + "out.push('game: rows=' + d('manual') + '/' + d('extras') + ' corner=' + d('discManual') + '/' + d('discExtras'));\n"
                + "out.push('open still=' + (d('openDisc') || 'none') + ' at=' + l('openDisc'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (games > 1)
        {
            Assert.Equal(onTheList, lines[0]);
        }

        Assert.Equal(onAGame, lines[^2]);

        // The way into the disc is on every screen, and moves up the row
        // when the two beside it go.
        Assert.Equal(
            open ? (games > 1 ? "open still=block at=16px" : "open still=block at=88px") : "open still=none at=",
            lines[^1]);
    }

    [Theory]
    // Only the manual is a small button: the book is in the corner, Extras
    // is still a row of the list, and a game's screen, since 2.0.6, offers
    // neither of the disc's.
    [InlineData(true, false,
        "list: rows=none/block corner=block/none",
        "game: rows=none/none corner=none/none")]
    // And the other way about.
    [InlineData(false, true,
        "list: rows=block/none corner=none/block",
        "game: rows=none/none corner=none/none")]
    public void With_one_tick_each_only_the_one_ticked_leaves_the_list_for_the_corner(
        bool manual, bool extras, string onTheList, string onAGame)
    {
        string disc = Path.Combine(_folder, "one each " + manual + extras);
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Own", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Game 1", "good.txt", string.Empty), new MenuGame("Game 2", "good.txt", string.Empty)],
                Manual: "Extras/disc.pdf",
                Extras: "Extras",
                Look: new MenuLook(DiscManualAsASmallButton: manual, DiscExtrasAsASmallButton: extras))),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function d(id) { return document.getElementById(id).style.display || ''; }\n"
                + "showChooser();\n"
                + "out.push('list: rows=' + d('chooserManual') + '/' + d('chooserExtras') + ' corner=' + d('discManual') + '/' + d('discExtras'));\n"
                + "showGame(0);\n"
                + "out.push('game: rows=' + d('manual') + '/' + d('extras') + ' corner=' + d('discManual') + '/' + d('discExtras'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal([onTheList, onAGame], lines);
    }

    [Fact]
    public void The_button_that_opens_the_disc_opens_the_top_of_it()
    {
        string disc = Path.Combine(_folder, "opened");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Opened", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Opened", "good.txt", string.Empty)],
                Look: new MenuLook(OpenTheDiscButton: true))),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "var opened = []; openTheFolder = function (where) { opened.push(where); return null; };\n"
                + "openTheDiscItself();\n"
                + "out.push('opened=' + opened.join('|'));\n"
                + "openTheFolder = function (where) { return 'That folder is not there.'; };\n"
                + "openTheDiscItself();\n"
                + "out.push('alerts=' + alerts.join(' / '));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("opened=" + disc + Path.DirectorySeparatorChar, lines[0]);

        // And what goes wrong is said, not swallowed.
        Assert.Equal("alerts=That folder is not there.", lines[1]);
    }

    [Theory]
    // As it comes: the disc over the list, the game over its own screen.
    [InlineData(2, null, false, "list=[Headed]block", "game=[Game 1]")]
    // Words of its own over the list, and the game still over its screen.
    [InlineData(2, "GOG Collection", false, "list=[GOG Collection]block", "game=[Game 1]")]
    // No heading over the list, and the game still over its screen.
    [InlineData(2, "GOG Collection", true, "list=[]none", "game=[Game 1]")]
    [InlineData(2, null, true, "list=[]none", "game=[Game 1]")]
    // A disc of one: its own screen is the main menu.
    [InlineData(1, null, false, "", "game=[Game 1]block")]
    [InlineData(1, "Hallownest", false, "", "game=[Hallownest]block")]
    [InlineData(1, "Hallownest", true, "", "game=[]none")]
    public void The_main_menu_is_headed_as_asked_and_a_games_screen_with_the_game(
        int games, string? heading, bool hidden, string onTheList, string onAGame)
    {
        string disc = Path.Combine(_folder, "headed " + games + heading + hidden);
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Headed", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [.. Enumerable.Range(1, games).Select(number => new MenuGame("Game " + number, "good.txt", string.Empty))],
                Look: new MenuLook(Heading: heading, HeadingHidden: hidden))),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function h(id) { var e = document.getElementById(id); return '[' + (e.innerText || '') + ']' + (e.style.display || ''); }\n"
                + (games > 1 ? "showChooser();\nout.push('list=' + h('discName'));\n" : string.Empty)
                + "showGame(0);\n"
                + "out.push('game=' + h('gameName'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (games > 1)
        {
            Assert.Equal(onTheList, lines[0]);
        }

        Assert.Equal(onAGame, lines[^1]);
    }

    [Theory]
    // Made with a list: the button is there, in the corner that was chosen.
    [InlineData(true, MenuSide.Right, false, "shown=block at=16px,16px")]
    [InlineData(true, MenuSide.Left, true, "shown=block at=714px,434px")]
    // Made without one: no button, as on the disc itself.
    [InlineData(false, MenuSide.Right, false, "shown= at=,")]
    public void A_preview_shows_the_check_where_it_will_sit_and_pressing_it_says_why_nothing_is_checked(
        bool checksums, MenuSide side, bool bottom, string shown)
    {
        // A preview folder holds the menu and nothing else: no list, no files.
        string preview = Path.Combine(_folder, "preview " + checksums + side + bottom);
        Directory.CreateDirectory(Path.Combine(preview, "AUTORUN"));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Previewed", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: false,
                Games: [new MenuGame("Previewed", "good.txt", string.Empty)],
                Look: new MenuLook(side, bottom),
                CarriesAChecksumList: checksums)),
            menuUrl: new Uri(Path.Combine(preview, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "thisIsAPreview();\n"
                + "showTheCheck();\n"
                + "placeTheCornerButtons();\n"
                + "var s = document.getElementById('sums').style;\n"
                + "out.push('shown=' + (s.display || '') + ' at=' + (s.left || '') + ',' + (s.top || ''));\n"
                + "var before = window.timers.length; checkThisDisc();\n"
                + "out.push('line=' + document.getElementById('stillWanted').innerText);\n"
                + "out.push('started=' + (window.timers.length - before) + ' busy=' + (document.getElementById('sums').className || ''));\n"
                + "out.push('alerts=' + alerts.length);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(shown, lines[0]);

        // Said under the list, where the check's own result goes, and not
        // that a list could not be read: nothing went wrong.
        Assert.Equal(
            "line=This is a preview of the menu. There is nothing to check, because the disc has not been made yet.",
            lines[1]);

        // And nothing was started: no certutil, no timer, no button gone grey.
        Assert.Equal("started=0 busy=", lines[2]);
        Assert.Equal("alerts=0", lines[3]);
    }

    // ------------------------------------------- an add-on being installed
    //
    // Found on a real disc, Metro Exodus on 2026-11-20. A DLC's installer
    // took a while to show itself, its button looked as it had before it was
    // pressed, and it was pressed again: two installers for one DLC.

    private static MenuFacts AGameWithTwoDlc() =>
        new(
            "Metro Exodus", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
            Games:
            [
                new MenuGame("Metro Exodus", "setup_metro_exodus_3.0.8.39_(64bit)_(62413).exe", string.Empty,
                [
                    new MenuAddOn(
                        "metro exodus - sams story 3.0.8.39 (64bit) (62413)",
                        "Add-ons/setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe"),
                    new MenuAddOn(
                        "metro exodus - the two colonels 3.0.8.39 (64bit) (62413)",
                        "Add-ons/setup_metro_exodus_-_the_two_colonels_3.0.8.39_(64bit)_(62413).exe"),
                ])
            ]);

    /// <summary>The game installed, nothing really started, and a switch for whether an installer is seen running.</summary>
    private const string AnInstalledGameAndAStandInInstaller =
        "function el(id) { return document.getElementById(id); }\n"
        + "var started = []; var runningNow = false; var asked = [];\n"
        + "whereTheGameIs = function () { return 'C:' + BACKSLASH + 'Games' + BACKSLASH + 'Metro'; };\n"
        + "startIt = function (what) { started.push(String(what).split(BACKSLASH).pop()); return ''; };\n"
        + "installerIsRunning = function (exe) { asked.push(exe); return runningNow; };\n"
        + "function tick() { var due = window.timers; window.timers = []; for (var i = 0; i < due.length; i++) { due[i](); } }\n"
        + "function said(i) { var t = el('addOn' + i).innerText; return t === 'Installing\\u2026' ? 'INSTALLING' : t; }\n"
        + "function row(i) { return said(i) + '/' + (el('addOns').innerHTML.split('<a class=\"')[i + 1] || '').split('\"')[0]; }\n"
        + "showGame(0);\n";

    [Fact]
    public void The_real_discs_two_buttons_say_what_they_are_and_nothing_about_versions()
    {
        string went = Run(
            PageScriptFor(AGameWithTwoDlc()),
            menuUrl: new Uri(Path.Combine(_folder, "named", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                AnInstalledGameAndAStandInInstaller
                + "out.push(row(0)); out.push(row(1));\n"
                + "out.push(el('addOn0').parentNode.title);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            ["sams story/btn", "the two colonels/btn", "metro exodus - sams story 3.0.8.39 (64bit) (62413)"],
            lines);
    }

    [Fact]
    public void An_add_on_says_it_is_installing_holds_every_add_on_and_starts_once_however_often_it_is_pressed()
    {
        string went = Run(
            PageScriptFor(AGameWithTwoDlc()),
            menuUrl: new Uri(Path.Combine(_folder, "installing", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                AnInstalledGameAndAStandInInstaller
                + "runTheAddOn(0);\n"
                + "out.push('pressed=' + row(0) + ' other=' + row(1) + ' started=' + started.length);\n"
                + "out.push('says=' + el('stillWanted').innerText);\n"

                // Pressed again before its installer has shown itself, and
                // the other one pressed too: nothing more starts.
                + "runTheAddOn(0); runTheAddOn(1);\n"
                + "out.push('pressed again=' + started.join(','));\n"

                // The installer comes up, and stays up over several looks.
                + "runningNow = true; tick(); tick(); tick();\n"
                + "out.push('running=' + row(0) + ' other=' + row(1) + ' looks queued=' + window.timers.length);\n"
                + "runTheAddOn(1);\n"
                + "out.push('other pressed while running=' + started.length);\n"

                // It closes.
                + "runningNow = false; tick();\n"
                + "out.push('closed=' + row(0) + ' other=' + row(1) + ' looks queued=' + window.timers.length);\n"
                + "out.push('then says=' + el('stillWanted').innerText);\n"
                + "out.push('watched=' + asked[0]);\n"

                // And the other can be installed now.
                + "runTheAddOn(1);\n"
                + "out.push('the other=' + row(1) + ' started=' + started.join(','));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                "pressed=INSTALLING/btn dead other=the two colonels/btn dead started=1",
                "says=Installing sams story. This menu waits for the installer and says when it is done.",
                "pressed again=setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe",
                "running=INSTALLING/btn dead other=the two colonels/btn dead looks queued=1",
                "other pressed while running=1",
                "closed=sams story/btn other=the two colonels/btn looks queued=0",
                "then says=The installer for sams story has closed.",
                "watched=setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe",
                "the other=INSTALLING/btn dead started=setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe,"
                    + "setup_metro_exodus_-_the_two_colonels_3.0.8.39_(64bit)_(62413).exe",
            ],
            lines);
    }

    [Fact]
    public void An_installer_that_is_never_seen_running_does_not_hold_the_buttons_for_ever()
    {
        // One that would not start, or started and closed between two
        // looks. After five looks without seeing it the menu lets go, as it
        // does for the game's.
        string went = Run(
            PageScriptFor(AGameWithTwoDlc()),
            menuUrl: new Uri(Path.Combine(_folder, "unseen", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                AnInstalledGameAndAStandInInstaller
                + "runTheAddOn(0);\n"
                + "var looks = 0; while (window.timers.length > 0 && looks < 50) { tick(); looks++; }\n"
                + "out.push('looks=' + looks + ' after=' + row(0) + ' other=' + row(1));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Equal("looks=5 after=sams story/btn other=the two colonels/btn", went.Trim());
    }

    [Fact]
    public void An_add_on_that_would_not_start_says_why_and_holds_nothing()
    {
        string went = Run(
            PageScriptFor(AGameWithTwoDlc()),
            menuUrl: new Uri(Path.Combine(_folder, "refused", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                AnInstalledGameAndAStandInInstaller
                + "startIt = function () { return 'Windows would not start it.'; };\n"
                + "runTheAddOn(0);\n"
                + "out.push(alerts.join('|') + ' after=' + row(0) + ' looks queued=' + window.timers.length);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Equal("Windows would not start it. after=sams story/btn looks queued=0", went.Trim());
    }

    [Fact]
    public void Going_back_to_the_list_while_an_add_on_installs_leaves_the_other_games_screen_alone()
    {
        // A disc of two games. The first game's DLC is installing, somebody
        // goes to the second game, and the installer closes: the second
        // game's screen is not redrawn as the first's.
        string went = Run(
            PageScriptFor(new MenuFacts(
                "Two", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games:
                [
                    new MenuGame("First", "01/setup_first.exe", "01",
                        [new MenuAddOn("first - expansion 1.0 (1)", "01/Add-ons/setup_first_-_expansion.exe")]),
                    new MenuGame("Second", "02/setup_second.exe", "02",
                        [new MenuAddOn("second - expansion 1.0 (2)", "02/Add-ons/setup_second_-_expansion.exe")]),
                ])),
            menuUrl: new Uri(Path.Combine(_folder, "two", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                AnInstalledGameAndAStandInInstaller
                + "runTheAddOn(0);\n"
                + "runningNow = true; tick();\n"
                + "showGame(1);\n"
                + "out.push('second while the first installs=' + row(0));\n"
                + "runningNow = false; tick();\n"
                + "out.push('second after=' + row(0) + ' says=' + (el('stillWanted').innerText.indexOf('expansion has closed') >= 0 ? 'the first' : 'its own'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            ["second while the first installs=expansion/btn dead", "second after=expansion/btn says=its own"],
            lines);
    }

    [Theory]
    [InlineData(true, "shut=block wired=true")]
    [InlineData(false, "shut= wired=false")]
    public void A_menu_with_no_frame_gets_its_own_cross_and_a_way_to_be_moved(bool noFrame, string says)
    {
        string went = Run(
            PageScriptFor(AFramelessMenu(noFrame)),
            menuUrl: new Uri(Path.Combine(_folder, "frameless", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "showTheWindowsOwnWayOut();\n"
                + "var stage = document.getElementById('stage');\n"
                + "out.push('shut=' + (document.getElementById('shut').style.display || '') + ' wired=' + (typeof stage.onmousedown === 'function'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Equal(says, went.Trim());
    }

    [Theory]
    [InlineData(true, MenuSide.Right, "40px")]
    [InlineData(true, MenuSide.Left, "28px")]
    [InlineData(false, MenuSide.Right, "")]
    public void The_list_of_a_menu_with_no_frame_starts_clear_of_the_cross(bool noFrame, MenuSide side, string padding)
    {
        // A menu with its frame is not touched at all: its panel keeps the
        // rule it was drawn with.
        string went = Run(
            PageScriptFor(new MenuFacts(
                "Frameless", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Frameless", "good.txt", string.Empty)],
                Look: new MenuLook(side, NoFrame: noFrame))),
            menuUrl: new Uri(Path.Combine(_folder, "clear", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "showTheWindowsOwnWayOut();\n"
                + "WScript.Echo('[' + (document.getElementById('panel').style.paddingTop || '') + ']');\n");

        Assert.Equal("[" + padding + "]", went.Trim());
    }

    [Fact]
    public void Its_picture_drags_the_window_and_a_button_does_not()
    {
        string went = Run(
            PageScriptFor(AFramelessMenu(true)),
            menuUrl: new Uri(Path.Combine(_folder, "dragged", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "var moved = []; var captured = 0; var released = 0;\n"
                + "window.moveTo = function (left, top) { moved.push(left + ',' + top); };\n"
                + "window.screenLeft = 300; window.screenTop = 200;\n"
                + "showTheWindowsOwnWayOut();\n"
                + "var stage = document.getElementById('stage');\n"
                + "stage.setCapture = function () { captured++; }; stage.releaseCapture = function () { released++; };\n"

                // Moving the pointer over the picture with nothing held
                // moves nothing.
                + "window.event = { button: 1, screenX: 500, screenY: 400, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousemove();\n"
                + "out.push('idle=' + moved.length);\n"

                // A press on a button is the button's.
                + "window.event = { button: 1, screenX: 500, screenY: 400, srcElement: { tagName: 'A', className: 'btn' } };\n"
                + "stage.onmousedown();\n"
                + "window.event = { button: 1, screenX: 560, screenY: 400, srcElement: { tagName: 'A', className: 'btn' } };\n"
                + "stage.onmousemove();\n"
                + "out.push('on a button=' + moved.length + ' captured=' + captured);\n"

                // The right button is not a drag.
                + "window.event = { button: 2, screenX: 500, screenY: 400, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousedown(); stage.onmousemove();\n"
                + "out.push('right button=' + moved.length);\n"

                // A press on the picture takes hold, and the window follows.
                + "window.event = { button: 1, screenX: 500, screenY: 400, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousedown();\n"
                + "window.event = { button: 1, screenX: 530, screenY: 390, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousemove();\n"
                + "window.event = { button: 1, screenX: 100, screenY: 900, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousemove();\n"
                + "out.push('dragged=' + moved.join(' ') + ' captured=' + captured);\n"

                // Let go, and it stays where it was put.
                + "stage.onmouseup();\n"
                + "window.event = { button: 0, screenX: 0, screenY: 0, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousemove();\n"
                + "out.push('let go=' + moved.length + ' released=' + released);\n"

                // And when Windows takes the pointer away, that is letting go.
                + "window.event = { button: 1, screenX: 500, screenY: 400, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousedown(); stage.onlosecapture();\n"
                + "window.event = { button: 1, screenX: 600, screenY: 400, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousemove();\n"
                + "out.push('taken away=' + moved.length);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                "idle=0",
                "on a button=0 captured=0",
                "right button=0",
                "dragged=330,190 -100,700 captured=1",
                "let go=2 released=1",
                "taken away=2",
            ],
            lines);
    }

    [Fact]
    public void A_window_that_will_not_move_is_still_a_menu()
    {
        string went = Run(
            PageScriptFor(AFramelessMenu(true)),
            menuUrl: new Uri(Path.Combine(_folder, "stuck", "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "window.moveTo = function () { throw new Error('Access is denied.'); };\n"
                + "window.screenLeft = 0; window.screenTop = 0;\n"
                + "showTheWindowsOwnWayOut();\n"
                + "var stage = document.getElementById('stage');\n"
                + "stage.setCapture = function () {}; stage.releaseCapture = function () { throw new Error('not held'); };\n"
                + "window.event = { button: 1, screenX: 5, screenY: 5, srcElement: { tagName: 'DIV', id: 'stage' } };\n"
                + "stage.onmousedown(); stage.onmousemove(); stage.onmouseup();\n"
                + "WScript.Echo('still standing');\n");

        Assert.Equal("still standing", went.Trim());
    }

    private static MenuFacts AFramelessMenu(bool noFrame) =>
        new(
            "Frameless", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
            Games: [new MenuGame("Frameless", "good.txt", string.Empty)],
            Look: new MenuLook(NoFrame: noFrame));

    [Fact]
    public void The_music_button_is_a_small_button_still_when_the_tune_is_turned_off_and_on()
    {
        string disc = Path.Combine(_folder, "muted");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Muted", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Muted", "good.txt", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                // A player that plays, standing in for the one a disc gets.
                "player = { controls: { pause: function () {}, play: function () {}, stop: function () {} } }; playing = true;\n"
                + "toggleMusic();\n"
                + "out.push('off=' + document.getElementById('mute').className);\n"
                + "toggleMusic();\n"
                + "out.push('on=' + document.getElementById('mute').className);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["off=corner off", "on=corner"], lines);
    }

    [Fact]
    public void Checks_the_disc_from_the_button_beside_the_mute()
    {
        // The one click check from discwright#98, pressed the way the page
        // presses it: real files, the real list, real certutil, and the
        // verdict landing under the panel where every other line lands. One
        // file is good, one does not match its line, one is missing.
        string disc = Path.Combine(_folder, "checked");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "good.txt"), "12345");
        File.WriteAllText(Path.Combine(disc, "bad.txt"), "not what the list says");

        File.WriteAllText(Path.Combine(disc, "checksums.sha256"), ChecksumList.For("CHECKED",
        [
            new ChecksumEntry("good.txt", HashOf("12345"), 5),
            new ChecksumEntry("bad.txt", HashOf("12345"), 22),
            new ChecksumEntry("gone.txt", HashOf("x"), 1)
        ]));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Checked", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Checked", "good.txt", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "showTheCheck();\n"
                + "placeTheCornerButtons();\n"
                + "out.push('shown=' + document.getElementById('sums').style.display + ' at=' + document.getElementById('sums').style.left);\n"
                + "checkThisDisc();\n"

                // Grey the moment it is pressed, and a second press while it
                // works starts nothing: no new certutil, no new timers.
                + "out.push('busy=' + document.getElementById('sums').className + ' saying=' + document.getElementById('stillWanted').innerText);\n"
                + "var before = window.timers.length; checkThisDisc(); out.push('press again=' + (window.timers.length - before));\n"

                // The big button is dead while the check reads the disc, and
                // a press on it starts nothing.
                + "out.push('install=' + document.getElementById('doIt').parentNode.className);\n"
                + "var starts = 0; startIt = function () { starts++; return null; }; doTheThing(); out.push('install pressed=' + starts);\n"
                + "drain();\n"
                + "out.push('after=' + document.getElementById('sums').className + ' install=' + document.getElementById('doIt').parentNode.className);\n"
                + "out.push('line=' + document.getElementById('stillWanted').innerText.split('\\n').join('~'));\n"
                + "out.push('alerts=' + alerts.length + (alerts.length ? ' ' + alerts.join(' / ') : ''));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // No music on this disc, so the check sits where the mute would.
        Assert.Equal("shown=block at=16px", lines[0]);
        // The percentage is an estimate against the clock, and files this
        // small are most of the way through in a millisecond, so only its
        // shape is pinned here. The arithmetic is TheMenuScript's.
        Assert.Matches(@"^busy=corner busy saying=Checking the disc, \d{1,2}%: file 1 of 2 against what was burned\.$", lines[1]);
        Assert.Equal("press again=0", lines[2]);
        Assert.Equal("install=btn next dead", lines[3]);
        Assert.Equal("install pressed=0", lines[4]);
        // Finished, it is a small button still: the rule it wears is what
        // gives it its box and its place, and it lost both on a real disc.
        Assert.Equal("after=corner install=btn next", lines[5]);
        Assert.StartsWith("line=2 of 3 files on this disc are wrong or missing:", lines[6], StringComparison.Ordinal);
        Assert.Contains("bad.txt", lines[6], StringComparison.Ordinal);
        Assert.Contains("gone.txt", lines[6], StringComparison.Ordinal);
        Assert.Equal("alerts=0", lines[^1]);
    }

    [Fact]
    public void Keeps_the_check_off_a_preview_because_the_disc_does_not_exist_yet()
    {
        // The preview folder can sit beside a checksums list in the output
        // folder, and a check button there would be checking a disc that
        // has not been made. The preview already says nothing starts from
        // it; this button does not even appear.
        string disc = Path.Combine(_folder, "previewed");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "checksums.sha256"), "pretend");

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Checked", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: false,
                Games: [new MenuGame("Checked", "good.txt", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "showTheCheck();\n"
                + "WScript.Echo('shown=' + document.getElementById('sums').style.display);\n");

        Assert.DoesNotContain("block", went, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_files_whose_names_go_past_ASCII_and_names_a_bad_one_as_it_is()
    {
        // discwright#98, 15 November: xniwo's extras carried a fullwidth bar,
        // colon and plus, and the check called a perfect disc "2 of 13 wrong
        // or missing", naming each with three letters of junk per character.
        // The list is UTF-8 and was read as ANSI. His names, an accent, and
        // one file that really is wrong, whose name must come out whole.
        char bar = (char)0xFF5C, colon = (char)0xFF1A, plus = (char)0xFF0B, acute = (char)0xE9;
        string disc = Path.Combine(_folder, "named");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        Directory.CreateDirectory(Path.Combine(disc, "Extras"));

        string[] good =
        [
            $"Extras/Document Files {bar} GetSampleData (15_11_2026 13{colon}24{colon}18).html",
            $"Extras/Windows NT 3.51 {plus} Shell Technology Preview.html",
            $"Caf{acute} manual.txt"
        ];
        string wrong = $"Extras/Wr{acute}ng {plus} one.txt";

        foreach (string name in good)
        {
            File.WriteAllText(Path.Combine(disc, name), "12345");
        }

        File.WriteAllText(Path.Combine(disc, wrong), "not what the list says");

        File.WriteAllText(Path.Combine(disc, ChecksumList.FileName), ChecksumList.For("NAMED",
        [
            .. good.Select(name => new ChecksumEntry(name, HashOf("12345"), 5)),
            new ChecksumEntry(wrong, HashOf("12345"), 22)
        ]));

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Named", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Named", "x.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                // The console cscript writes to cannot carry these names, so
                // every character past ASCII goes out as its code.
                "function asAscii(s) { var o = ''; for (var i = 0; i < s.length; i++) { var c = s.charCodeAt(i); o += c < 128 ? s.charAt(i) : '<' + c.toString(16) + '>'; } return o; }\n"
                + "checkThisDisc();\n"
                + "drain();\n"
                + "out.push('line=' + asAscii(document.getElementById('stillWanted').innerText.split('\\n').join('~')));\n"
                + "out.push('alerts=' + alerts.length);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string coded = string.Concat(wrong.Select(c => c < 128 ? c.ToString() : "<" + ((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture) + ">"));

        Assert.Equal(
            [
                "line=1 of 4 files on this disc are wrong or missing:~~" + coded + "~~A disc that reads badly is usually scratched or was burned badly.",
                "alerts=0"
            ],
            went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Installs_a_set_whose_files_have_names_past_ASCII()
    {
        // The same reading loaded the set's own list, so a set carrying such
        // a name looked for files that were not on the disc under the name it
        // read. A cut installer with an accent, a readme with a fullwidth bar
        // on disc 1, and a file with a fullwidth colon in a folder on disc 2.
        char bar = (char)0xFF5C, colon = (char)0xFF1A, acute = (char)0xE9;
        string installer = $"setup_caf{acute}.exe";
        string readme = $"Caf{acute} {bar} notes.txt";
        string more = $"m{acute}re {colon} data.dat";
        string disc1 = Path.Combine(_folder, "disc1");
        string disc2 = Path.Combine(_folder, "disc2");
        string dest = Path.Combine(_folder, "put back");

        Directory.CreateDirectory(Path.Combine(disc1, "AUTORUN"));
        Directory.CreateDirectory(Path.Combine(disc2, "AUTORUN"));
        Directory.CreateDirectory(Path.Combine(disc2, "data"));
        File.WriteAllText(Path.Combine(disc1, installer + ".dwpart01"), "ABC");
        File.WriteAllText(Path.Combine(disc1, readme), "12345");
        File.WriteAllText(Path.Combine(disc2, installer + ".dwpart02"), "DEFG");
        File.WriteAllText(Path.Combine(disc2, "data", more), "12");

        SetEntry[] entries =
        [
            new(1, installer + ".dwpart01", "a1", 3),
            new(1, readme, "b2", 5),
            new(2, installer + ".dwpart02", "c3", 4),
            new(2, "data/" + more, "d4", 2)
        ];
        SetJoin[] joins = [new(installer, "e5", 7, [installer + ".dwpart01", installer + ".dwpart02"])];

        foreach ((string disc, int number) in new[] { (disc1, 1), (disc2, 2) })
        {
            File.WriteAllText(
                Path.Combine(disc, SetManifest.FileName),
                SetManifest.For("CAFE", entries, number, 2, "CD-R 700 MB", DateTimeOffset.UtcNow, joins));
        }

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Cafe", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Cafe", installer, string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc1, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "SET = loadTheSet();\n"
                + "theShare().dir = " + AsJScript(dest) + ";\n"
                + "showTheSet(gameAt(DISC.games, 0));\n"
                + "var started = 0; startIt = function () { started++; return null; };\n"
                + "doTheThing();\n"
                + "whereGo();\n"
                + "pump(function () { return restoring.wanting > 0; });\n"
                + "document.URL = " + AsJScript(new Uri(Path.Combine(disc2, "AUTORUN", "menu.hta")).AbsoluteUri) + ";\n"
                + "WHERE = null;\n"
                + "drain();\n"
                + "out.push('started=' + started + ' alerts=' + alerts.length);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Contains("started=1 alerts=0", went, StringComparison.Ordinal);
        Assert.Equal("ABCDEFG", File.ReadAllText(Path.Combine(dest, installer)));
        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, readme)));
        Assert.Equal("12", File.ReadAllText(Path.Combine(dest, "data", more)));
    }

    private static string HashOf(string text) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(text)))
            .ToLowerInvariant();

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

        // "in " and then the disc, because a game alone on a disc sits at its
        // root and that is the folder the installer is run from. It used to be
        // nothing at all, which left the installer running from wherever mshta
        // happened to be: harmless for an installer, and the same hole that
        // stopped a folder of files being copied off a disc at all.
        Assert.Equal(
            "start " + Path.Combine(disc, "setup_small.exe") + " in " + disc,
            lines[0]);
        Assert.Equal(
            [
                "start " + Path.Combine(disc, "setup_small.exe") + " in " + disc,
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
                + "theShare().dir = " + AsJScript(Path.Combine(_folder, "never made")) + ";\n"
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
            test: "alert('That folder could not be made: X');\npump(function () { return false; });\n",
            mustSucceed: false);

        Assert.Contains("the page stopped with nothing left to run", went, StringComparison.Ordinal);

        // With whatever the page said in an alert, because a refusal is the
        // usual reason a page stops early and the alert is the only place it
        // says which. The release dry run of 2026-11-11 stopped on the install
        // page with nothing queued, and the message could not say why.
        Assert.Contains("alerts=That folder could not be made: X", went, StringComparison.Ordinal);

        // And not by running out of patience, which is the other way out and
        // would pass this test while meaning something else entirely.
        Assert.DoesNotContain("the page never settled", went, StringComparison.Ordinal);
    }

    // ------------------------------------------- where it goes, and when not

    [Fact]
    public void Cancel_on_the_install_page_copies_nothing_and_the_next_press_asks_again()
    {
        string disc1 = AFirstDiscOfTwo();
        string dest = Path.Combine(_folder, "suggested");

        string went = RunOnDisc1(disc1, dest,
            "doTheThing();\n"
            + "whereCancel();\n"
            + "out.push('page=' + document.getElementById('whereScreen').style.display + ' game=' + document.getElementById('gameScreen').style.display"
            + " + ' timers=' + window.timers.length + ' busy=' + restoring.busy + ' asked=' + (theShare().asked ? 'yes' : 'no'));\n"
            + "doTheThing();\n"
            + "out.push('again=' + document.getElementById('whereScreen').style.display);\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["page=none game=block timers=0 busy=false asked=no", "again=block"], lines);
        Assert.False(Directory.Exists(dest), "nothing is made when nobody said where");
    }

    [Fact]
    public void Manual_and_extras_are_dead_while_the_set_copies_and_live_while_it_installs()
    {
        // Pressing Extras under the copy froze the menu for a minute on
        // 2026-11-10: the page waited on the drive the copy was reading.
        string disc1 = AFirstDiscOfTwo();

        string went = RunOnDisc1(disc1, Path.Combine(_folder, "suggested"),
            "var opened = 0; startIt = function () { opened++; return null; }; openTheFolder = function () { opened++; return null; };\n"

            // Something to open: a press with nothing behind it opens
            // nothing at all, since 2.0.6.
            + "DISC.manual = 'Extras/manual.pdf'; DISC.extras = 'Extras';\n"
            + "restoring.busy = true;\n"
            + "showTheSet(gameAt(DISC.games, 0));\n"
            + "out.push('copying=' + document.getElementById('manual').className + ' / ' + document.getElementById('extras').className);\n"
            + "openTheManual(); openTheExtras();\n"
            + "out.push('opened=' + opened);\n"
            + "restoring.installing = true;\n"
            + "showTheSet(gameAt(DISC.games, 0));\n"
            + "out.push('installing=' + document.getElementById('manual').className + ' / ' + document.getElementById('extras').className);\n"
            + "openTheManual(); openTheExtras();\n"
            + "out.push('opened=' + opened);\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["copying=btn dead / btn dead", "opened=0", "installing=btn / btn", "opened=2"], lines);
    }

    [Fact]
    public void Each_games_screen_opens_its_own_manual_and_extras_and_the_list_of_games_the_discs()
    {
        // The real page, pressed: The Witcher has its own manual, Hollow
        // Knight only its own extras, and the disc a manual of its own,
        // which the list of games offers and, since 2.0.6, Hollow Knight's
        // screen does not: xniwo, discwright#98, 23 November 2026, and the
        // owner, "show only the game's own". A press on its Manual that got
        // there anyway opens nothing.
        string disc = Path.Combine(_folder, "several");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Two Games", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games:
                [
                    new MenuGame("The Witcher", "01 - The Witcher/setup_the_witcher.exe", "01 - The Witcher",
                        Manual: "01 - The Witcher/Manual/witcher manual.pdf"),
                    new MenuGame("Hollow Knight", "02 - Hollow Knight/setup_hollow_knight.exe", "02 - Hollow Knight",
                        Extras: "02 - Hollow Knight/Extras")
                ],
                Manual: "Extras/disc manual.pdf")),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "var opened = [];\n"
                + "startIt = function (w) { opened.push('start ' + w.substring(theDisc().length)); return null; };\n"
                + "openTheFolder = function (w) { opened.push('open ' + w.substring(theDisc().length)); return null; };\n"
                + "function shown(id) { return document.getElementById(id).style.display; }\n"
                + "showChooser();\n"
                + "out.push('list manual=' + shown('chooserManual') + ' extras=' + shown('chooserExtras'));\n"
                + "openTheDiscsManual();\n"
                + "showGame(0);\n"
                + "out.push('witcher manual=' + shown('manual') + ' extras=' + shown('extras'));\n"
                + "openTheManual(); openTheExtras();\n"
                + "showGame(1);\n"
                + "out.push('knight manual=' + shown('manual') + ' extras=' + shown('extras'));\n"
                + "openTheManual(); openTheExtras();\n"
                + "out.push(opened.join(' | ').split(String.fromCharCode(92)).join('/'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                "list manual=block extras=none",
                "witcher manual=block extras=none",
                "knight manual=none extras=block",
                "start Extras/disc manual.pdf | start 01 - The Witcher/Manual/witcher manual.pdf"
                    + " | open 02 - Hollow Knight/Extras"
            ],
            lines.Select(line => line.Replace("start /", "start ", StringComparison.Ordinal)
                .Replace("open /", "open ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_game_with_more_add_ons_than_the_panel_holds_scrolls_them_above_its_Exit()
    {
        // A reader on Reddit, 2026-11-14: a base game with seven DLC
        // installers beside it ran Exit off the bottom of the panel, and the
        // page never scrolls. The add-ons get the room above Manual, Extras
        // and Exit and scroll past it, and a game with few gets its height
        // back, even straight after one with many.
        string disc = Path.Combine(_folder, "many-add-ons");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");

        MenuAddOn[] seven =
        [
            .. Enumerable.Range(1, 7).Select(n => new MenuAddOn($"DLC {n}", $"01 - Big Game/Add-ons/setup_big_game_dlc_{n}.exe")),
        ];

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Two Games", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games:
                [
                    new MenuGame("Big Game", "01 - Big Game/setup_big_game.exe", "01 - Big Game", seven,
                        Manual: "01 - Big Game/Manual/big game manual.pdf"),
                    new MenuGame("Small Game", "02 - Small Game/setup_small_game.exe", "02 - Small Game",
                        [new MenuAddOn("Soundtrack", "02 - Small Game/Add-ons/soundtrack.exe")])
                ])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function el(id) { return document.getElementById(id); }\n"

                // What the browser would measure: a 480 panel, the add-ons
                // starting 100 down under the name and the big button, and
                // Manual, Back and Exit under them taking three buttons.
                + "el('panel').clientHeight = 480;\n"
                + "el('addOns').offsetTop = 100;\n"
                + "el('gameBelow').offsetHeight = 3 * 47;\n"
                + "el('addOns').scrollHeight = 7 * 47;\n"
                + "showGame(0);\n"
                + "out.push('many=' + el('addOns').style.height + ' gap=' + el('addOns').style.marginBottom + ' buttons=' + el('addOns').innerHTML.split('runTheAddOn(').length);\n"
                + "out.push('note=' + el('addOnNote').innerText.replace(/\\u00a0/g, '+'));\n"
                + "el('addOns').scrollHeight = 47;\n"
                + "showGame(1);\n"
                + "out.push('few=' + el('addOns').style.height + ' gap=' + el('addOns').style.marginBottom);\n"
                + "out.push('note=' + el('addOnNote').innerText.replace(/\\u00a0/g, '+'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // 480, less 28 of padding, the 100 above and the 141 and 9 below. Cut
        // short, the list ends mid-button, so the 9 it was given goes under it
        // and the line beneath does not touch the cut; whole, the last
        // button's own 9 is there and nothing is added.
        Assert.Equal(
            [
                "many=202px gap=9px buttons=8",
                "note=These add to the game, so install the game+first.",
                "few=auto gap=",
                "note=This adds to the game, so install the game+first.",
            ],
            lines);
    }

    [Fact]
    public void The_check_s_result_takes_its_room_from_the_list_and_scrolls_in_what_is_left()
    {
        // xniwo, discwright#98, 15 November: under a full list of add-ons the
        // disc check's result ran off the bottom of the panel and could not be
        // read. The line under the screen was never counted when the list was
        // fitted. Now it is, the list gives up room down to one button, and
        // whatever is left is the line's, scrolling inside it. The same on
        // the list of games, where the check can be pressed too.
        string disc = Path.Combine(_folder, "full-panel");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");

        MenuAddOn[] seven =
        [
            .. Enumerable.Range(1, 7).Select(n => new MenuAddOn($"DLC {n}", $"01 - Big Game/Add-ons/setup_big_game_dlc_{n}.exe")),
        ];

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Two Games", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games:
                [
                    new MenuGame("Big Game", "01 - Big Game/setup_big_game.exe", "01 - Big Game", seven),
                    new MenuGame("Small Game", "02 - Small Game/setup_small_game.exe", "02 - Small Game")
                ])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function el(id) { return document.getElementById(id); }\n"
                + "function sizes(list) { return list + '=' + el(list).style.height + ' under=' + el('underAll').style.height; }\n"

                // The seven DLC screen as the browser would measure it, with
                // nothing under it yet: the list gets what it always got.
                + "el('panel').clientHeight = 480;\n"
                + "el('addOns').offsetTop = 100;\n"
                + "el('gameBelow').offsetHeight = 3 * 47;\n"
                + "el('addOns').scrollHeight = 7 * 47;\n"
                + "el('underAll').offsetHeight = 0; el('underAll').scrollHeight = 0;\n"
                + "showGame(0);\n"
                + "out.push(sizes('addOns'));\n"

                // A long verdict, eleven lines and its gap: the list is cut to
                // one button, and the line gets the 155 left under Exit.
                + "el('underAll').offsetHeight = 194; el('underAll').scrollHeight = 194; el('underAll').offsetTop = 297;\n"
                + "sayTheCheck('2 of 13 files on this disc are wrong or missing');\n"
                + "out.push(sizes('addOns'));\n"

                // A short one: the list takes back all but the line's room,
                // and the line is whole.
                + "el('underAll').offsetHeight = 50; el('underAll').scrollHeight = 50; el('underAll').offsetTop = 402;\n"
                + "sayTheCheck('13 files checked.');\n"
                + "out.push(sizes('addOns'));\n"

                // The list of games, with the same long verdict under it.
                + "showChooser();\n"
                + "el('gameList').offsetTop = 80; el('chooserBelow').offsetHeight = 47; el('gameList').scrollHeight = 20 * 47;\n"
                + "el('underAll').offsetHeight = 194; el('underAll').scrollHeight = 194; el('underAll').offsetTop = 300;\n"
                + "sayTheCheck('2 of 13 files on this disc are wrong or missing');\n"
                + "out.push(sizes('gameList'));\n"

                // Next to no room at all, and the line still keeps two lines.
                + "el('underAll').offsetTop = 440;\n"
                + "sayTheCheck('2 of 13 files on this disc are wrong or missing');\n"
                + "out.push(sizes('gameList'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Equal(
            [
                "addOns=202px under=auto",
                "addOns=47px under=155px",
                "addOns=152px under=auto",
                "gameList=122px under=152px",
                "gameList=122px under=32px"
            ],
            went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void The_line_under_the_screen_shows_what_it_is_told_and_goes_when_told_nothing()
    {
        // Every sentence under the screen comes through sayUnderneath now, so
        // its two halves are held here: words shown, and null taking the line
        // away, as the end of a copy does. Shown again afterwards, because a
        // line hidden once must not stay hidden under the next sentence.
        string disc = Path.Combine(_folder, "said");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Said", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Said", "setup_said.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function line() { var l = document.getElementById('stillWanted'); return l.style.display + ' ' + l.innerText; }\n"
                + "showGame(0);\n"
                + "sayUnderneath('40% copied'); out.push(line());\n"
                + "sayUnderneath(null); out.push(line());\n"
                + "sayUnderneath('Starting the installer'); out.push(line());\n"
                + "WScript.Echo(out.join('\\n'));\n");

        Assert.Equal(
            ["block 40% copied", "none 40% copied", "block Starting the installer"],
            went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void An_add_on_s_button_leaves_off_its_game_s_name_and_its_hover_keeps_it()
    {
        // Recording the scrolling add-ons, 2026-11-14: seven GOG DLC, each
        // named after the game, read "HARBOUR TALES - TH..." seven times.
        string disc = Path.Combine(_folder, "named-after-the-game");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Harbour Tales", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games:
                [
                    new MenuGame("Harbour Tales", "setup_harbour_tales.exe", string.Empty,
                    [
                        new MenuAddOn("harbour tales - the north isles 1.0 (80001)", "Add-ons/setup_harbour_tales_-_the_north_isles.exe"),
                        new MenuAddOn("Original Soundtrack", "Add-ons/soundtrack.exe"),
                    ])
                ])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function el(id) { return document.getElementById(id); }\n"
                + "showGame(0);\n"
                + "out.push(el('addOn0').innerText + ' | ' + el('addOn0').parentNode.title);\n"
                + "out.push(el('addOn1').innerText + ' | ' + el('addOn1').parentNode.title);\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                // Without the game's name in front and without the version
                // and the build after, which tell nobody which DLC it is.
                // The hover keeps all of it.
                "the north isles | harbour tales - the north isles 1.0 (80001)",
                "Original Soundtrack | Original Soundtrack",
            ],
            lines);
    }

    [Fact]
    public void The_line_under_the_add_ons_counts_a_chain_of_patches_as_the_one_button_it_is()
    {
        // Two patches fold into one Update button, so the line says "This";
        // a patch and a DLC are two buttons, so it says "These".
        string disc = Path.Combine(_folder, "patches-and-a-dlc");
        Directory.CreateDirectory(Path.Combine(disc, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc, "AUTORUN", "menu.hta"), "<hta/>");

        string went = Run(
            PageScriptFor(new MenuFacts(
                "Two Games", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games:
                [
                    new MenuGame("Patched Game", "01 - Patched Game/setup.exe", "01 - Patched Game",
                    [
                        new MenuAddOn("Update 1.1", "01 - Patched Game/Add-ons/patch_1.0_to_1.1.exe"),
                        new MenuAddOn("Update 1.2", "01 - Patched Game/Add-ons/patch_1.1_to_1.2.exe"),
                    ]),
                    new MenuGame("Other Game", "02 - Other Game/setup.exe", "02 - Other Game",
                    [
                        new MenuAddOn("Update 2.1", "02 - Other Game/Add-ons/patch_2.0_to_2.1.exe"),
                        new MenuAddOn("Soundtrack", "02 - Other Game/Add-ons/soundtrack.exe"),
                    ])
                ])),
            menuUrl: new Uri(Path.Combine(disc, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "function el(id) { return document.getElementById(id); }\n"
                + "showGame(0);\n"
                + "out.push(el('addOnNote').innerText.replace(/\\u00a0/g, '+'));\n"
                + "showGame(1);\n"
                + "out.push(el('addOnNote').innerText.replace(/\\u00a0/g, '+'));\n"
                + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                "This adds to the game, so install the game+first.",
                "These add to the game, so install the game+first.",
            ],
            lines);
    }

    [Fact]
    public void A_drive_without_the_room_leaves_the_page_s_button_dead()
    {
        // The words are installPageSays's; what is pinned here is that the
        // page acts on its verdict: drawn dead, and a press copies nothing.
        string disc1 = AFirstDiscOfTwo();
        string dest = Path.Combine(_folder, "suggested");

        string went = RunOnDisc1(disc1, dest,
            "installPageSays = function () { return { headline: 'h', says: 's', go: 'Copy and install', note: 'n', short: true }; };\n"
            + "doTheThing();\n"
            + "out.push('go=' + document.getElementById('whereGo').className);\n"
            + "whereGo();\n"
            + "out.push('timers=' + window.timers.length + ' asked=' + (theShare().asked ? 'yes' : 'no') + ' page=' + document.getElementById('whereScreen').style.display);\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["go=btn next dead", "timers=0 asked=no page=block"], lines);
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void Change_folder_with_the_picker_closed_changes_nothing()
    {
        string disc1 = AFirstDiscOfTwo();
        string dest = Path.Combine(_folder, "suggested");

        string went = RunOnDisc1(disc1, dest,
            "pickAFolder = function (title) { out.push('picker=' + title); return null; };\n"
            + "doTheThing();\n"
            + "whereChange();\n"
            + "out.push('dir=' + theShare().dir + ' page=' + document.getElementById('whereScreen').style.display + ' timers=' + window.timers.length);\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["picker=Where should the Big Game installer go?", "dir=" + dest + " page=block timers=0"], lines);
    }

    [Fact]
    public void Change_folder_puts_the_installer_in_a_folder_of_its_own_there_and_the_page_says_so()
    {
        string disc1 = AFirstDiscOfTwo();
        string elsewhere = Path.Combine(_folder, "my games");
        Directory.CreateDirectory(elsewhere);

        string went = RunOnDisc1(disc1, Path.Combine(_folder, "suggested"),
            "pickAFolder = function () { return " + AsJScript(elsewhere) + "; };\n"
            + "doTheThing();\n"
            + "whereChange();\n"
            + "out.push('says=' + document.getElementById('whereSays').innerText);\n"
            + "whereGo();\n"
            + "pump(function () { return restoring.wanting > 0; });\n"
            + "out.push('dir=' + theShare().dir);\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string mine = Path.Combine(elsewhere, "Big Game installer");

        Assert.Contains("put back together in " + mine + ".", lines[0], StringComparison.Ordinal);
        Assert.Equal("dir=" + mine, lines[1]);
        Assert.Equal("12345", File.ReadAllText(Path.Combine(mine, "readme.txt")));
        Assert.False(File.Exists(Path.Combine(elsewhere, "readme.txt")), "never spilled into the folder picked");
    }

    [Fact]
    public void A_complete_copy_already_there_is_named_and_installs_from_it()
    {
        // What the owner met on 2026-11-10: a whole copy from an earlier try,
        // and Install went straight to the installer without a word.
        string disc1 = AFirstDiscOfTwo();
        string dest = Path.Combine(_folder, "already");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "setup_big.exe"), "ABCDEFG");
        File.WriteAllText(Path.Combine(dest, "readme.txt"), "12345");

        string went = RunOnDisc1(disc1, dest,
            "startIt = function (what, from) { out.push('start ' + what); return null; };\n"
            + "doTheThing();\n"
            + "out.push('says=' + document.getElementById('whereSays').innerText);\n"
            + "out.push('go=' + document.getElementById('whereGo').innerText);\n"
            + "whereGo();\n"

            // Said and drawn before the start, which waits for the page to
            // redraw; then the start itself.
            + "out.push('starting=' + document.getElementById('doIt').innerText + ' ' + document.getElementById('doIt').parentNode.className"
            + " + ' | ' + document.getElementById('stillWanted').innerText);\n"
            + "drain();\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("says=A complete copy of the installer is already in " + dest + ". Install runs it from there.", lines[0]);
        Assert.Equal("go=Install", lines[1]);
        // The ellipsis after "Starting" does not survive cscript's console,
        // so the words either side of it are what is held.
        Assert.StartsWith("starting=Starting", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(
            " btn next dead | Starting the installer from " + dest
                + ". Windows checks a file this size before it runs it, which can take a minute.",
            lines[2],
            StringComparison.Ordinal);
        Assert.Equal("start " + Path.Combine(dest, "setup_big.exe"), lines[3]);
    }
    [Fact]
    public void The_check_is_dead_while_the_set_copies_and_a_press_on_it_starts_nothing()
    {
        // The owner's catch on 2026-11-10: it stayed live under the copy.
        // A list to check against, or a live press would do nothing anyway
        // and the guard could go unnoticed.
        string disc1 = AFirstDiscOfTwo();
        File.WriteAllText(Path.Combine(disc1, "checksums.sha256"), ChecksumList.For("BIG GAME",
            [new ChecksumEntry("readme.txt", HashOf("12345"), 5)]));

        string went = RunOnDisc1(disc1, Path.Combine(_folder, "suggested"),
            "restoring.busy = true;\n"
            + "showTheSet(gameAt(DISC.games, 0));\n"
            + "out.push('check=' + document.getElementById('sums').className);\n"
            + "var before = window.timers.length; checkThisDisc();\n"
            + "out.push('pressed=' + (window.timers.length - before) + ' check=' + document.getElementById('sums').className);\n"
            + "WScript.Echo(out.join('\\n'));\n");

        string[] lines = went.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["check=corner dead", "pressed=0 check=corner dead"], lines);
    }

    /// <summary>
    /// Disc 1 of a set of two, holding a readme and the first piece of an
    /// installer, with the real set file. Returns where disc 1 is.
    /// </summary>
    private string AFirstDiscOfTwo()
    {
        string disc1 = Path.Combine(_folder, "first");
        Directory.CreateDirectory(Path.Combine(disc1, "AUTORUN"));
        File.WriteAllText(Path.Combine(disc1, "setup_big.exe.dwpart01"), "ABC");
        File.WriteAllText(Path.Combine(disc1, "readme.txt"), "12345");
        File.WriteAllText(Path.Combine(disc1, "AUTORUN", "menu.hta"), "<hta/>");

        SetEntry[] entries =
        [
            new(1, "setup_big.exe.dwpart01", "a1", 3),
            new(1, "readme.txt", "b2", 5),
            new(2, "setup_big.exe.dwpart02", "c3", 4)
        ];

        File.WriteAllText(
            Path.Combine(disc1, SetManifest.FileName),
            SetManifest.For("BIG GAME", entries, 1, 2, "CD-R 700 MB", DateTimeOffset.UtcNow,
                [new("setup_big.exe", "e5", 7, ["setup_big.exe.dwpart01", "setup_big.exe.dwpart02"])]));

        return disc1;
    }

    /// <summary>The page on disc 1, its set loaded and its folder suggested, then the test.</summary>
    private string RunOnDisc1(string disc1, string suggested, string test) =>
        Run(
            PageScriptFor(new MenuFacts(
                "Big Game", "DiscWright.Test", 1, 2, "Install", ["Install", "Exit"], OnlyOneAtATime: true,
                Games: [new MenuGame("Big Game", "setup_big.exe", string.Empty)])),
            menuUrl: new Uri(Path.Combine(disc1, "AUTORUN", "menu.hta")).AbsoluteUri,
            test:
                "SET = loadTheSet();\n"
                + "theShare().dir = " + AsJScript(suggested) + ";\n"
                + "showTheSet(gameAt(DISC.games, 0));\n"
                + test);

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The generated script block out of the built page: the data block and
    /// the page's own functions, exactly as the disc carries them.
    /// </summary>
    internal static string PageScriptFor(MenuFacts facts) => PageScriptOf(MenuWriter.MenuPage(facts));

    /// <summary>The same, out of a page a build wrote onto a disc.</summary>
    internal static string PageScriptOf(string page)
    {
        Match block = Regex.Match(page, "<script language=\"JScript\">(.*?)</script>", RegexOptions.Singleline);
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

        // OK / Cancel, written down and answered from a queue the test fills,
        // OK when it has said nothing.
        + "var confirms = [];\n"
        + "var answers = [];\n"
        + "function confirm(text) { confirms.push(String(text)); return answers.length ? answers.shift() : true; }\n"
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
        + "        if ((new Date()).getTime() > deadline) { throw new Error('the page never settled: ' + out.join(' | ') + ' | alerts=' + alerts.join(' / ')); }\n"
        + "        if (!window.timers.length) { throw new Error('the page stopped with nothing left to run, before what this was waiting for happened: ' + out.join(' | ') + ' | alerts=' + alerts.join(' / ')); }\n"
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
        + "        if ((new Date()).getTime() > deadline) { throw new Error('the page never ran out of work: ' + out.join(' | ') + ' | alerts=' + alerts.join(' / ')); }\n"
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
    private string Run(string pageScript, string menuUrl, string test, bool mustSucceed = true) =>
        RunIn(_folder, pageScript, menuUrl, test, mustSucceed);

    /// <summary>The same, for another class of tests with a folder of its own.</summary>
    internal static string RunIn(string _folder, string pageScript, string menuUrl, string test, bool mustSucceed = true)
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

        string output = WaitingForAScript.WhatItSaid(running, 120_000);

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

    internal static string AsJScript(string text)
    {
        string slash = ((char)92).ToString();

        return "'" + text.Replace(slash, slash + slash, StringComparison.Ordinal).Replace("'", slash + "'", StringComparison.Ordinal) + "'";
    }
}
