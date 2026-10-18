using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;

namespace DiscWright.Tests;

public class PuttingTheMenuOnTheDisc
{
    [Fact]
    public async Task Writes_the_logic_and_the_shell_into_AUTORUN()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        Assert.Equal(
            [@"C:\stage\AUTORUN\menu.js", @"C:\stage\AUTORUN\menu.hta", @"C:\stage\Start Here.hta"],
            wrote.Texts.Select(text => text.Path));
    }

    [Fact]
    public async Task Lets_what_is_behind_the_buttons_through()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // An HTA is IE5 in quirks mode unless it is told otherwise, and
        // IE5 has no rgba: every see-through colour below would be
        // dropped and the menu would come out looking exactly as it
        // always did.
        //
        // IE=9 RATHER THAN IE=edge, AND THE DIFFERENCE IS THE ICON. From
        // document mode 10 upwards the HTA host stops applying the icon=
        // attribute on the hta:application tag, so the menu wears
        // Windows' blank default and the disc's own picture never
        // appears. Mode 9 applies it and still has rgba, and it is the
        // only mode with both. Found by rendering a page in each mode in
        // turn and photographing its title bar; the template says which.
        Assert.Contains(@"http-equiv=""X-UA-Compatible"" content=""IE=9""", page, StringComparison.Ordinal);

        // The attribute, not the word: the template explains the choice
        // in a comment that has to be allowed to name what it rejected.
        Assert.DoesNotContain(@"content=""IE=edge""", page, StringComparison.Ordinal);

        // Standards mode counts padding outside the width, so without this
        // the panel would grow from 240 to 284 and push itself off the edge.
        // Measured at 240 both before and after.
        Assert.Contains("padding: 28px 22px; box-sizing: border-box;", page, StringComparison.Ordinal);

        // The panel, which is what the game's own picture sits behind.
        Assert.Contains("background: #060E11; background: rgba(6, 14, 17, 0.58)", page, StringComparison.Ordinal);

        // Almost transparent, so what is behind shows through the buttons
        // instead of them sitting on it as solid blocks.
        Assert.Contains("background: rgba(18, 32, 38, 0.22)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("color: #CBD6DB; background: #0C1519", page, StringComparison.Ordinal);

        // A hex before each one. An HTA renders as an old IE unless it is
        // told otherwise, and an old IE throws rgba away: without a plain
        // colour to fall back on, those buttons would have no background at
        // all on the machine the disc is most likely to be played on.
        Assert.Contains("background: #080F12; background: rgba", page, StringComparison.Ordinal);
        Assert.Contains("background: #070D10; background: rgba", page, StringComparison.Ordinal);
        Assert.Contains("background: #0C2B2E; background: rgba", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wears_our_own_icon_when_the_disc_chose_none()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // The blank default is what a program looks like when nobody finished
        // it, and this is 33 KB of the hundreds of megabytes on a disc.
        Assert.Contains(@"icon=""menu.ico""", page, StringComparison.Ordinal);
        Assert.Contains(wrote.Binaries, path => path.EndsWith(@"AUTORUN\menu.ico", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wears_the_disc_s_own_icon_when_there_is_one()
    {
        RecordingWriter wrote = new();

        await Write(wrote, artwork: new DiscArtwork(IconPath: @"C:\pictures\cover.png"));

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // At the root of the disc, and the menu is in AUTORUN, so one up.
        // That it can reach one up was checked by rendering such a page:
        // v1 says an HTA cannot and writes a second copy beside the menu,
        // and on this engine it can.
        //
        // Named after the disc rather than disc.ico, because Explorer and
        // the taskbar cache an icon by its path, so one fixed name means
        // the second disc into a drive letter wears the first one's.
        Assert.Contains(@"icon=""../HOLLOWKNIGHT.ico""", page, StringComparison.Ordinal);

        // And the same file spelled from the root of the disc, which the
        // script turns into an absolute path at run time.
        Assert.Contains(@"icon: ""HOLLOWKNIGHT.ico""", page, StringComparison.Ordinal);

        // And ours is not carried for nothing.
        Assert.DoesNotContain(wrote.Binaries, path => path.EndsWith("menu.ico", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gives_each_disc_of_a_set_a_menu_wearing_its_own_icon()
    {
        // Two discs of one set go into the same drive letter one after the
        // other, and the taskbar caches an icon by its path exactly as
        // Explorer does. One shared name and the second disc's menu wears the
        // first disc's picture.
        string one = PageFor(disc: 1, ofDiscs: 2);
        string two = PageFor(disc: 2, ofDiscs: 2);

        Assert.Contains(@"icon=""../HOLLOWKNIGHT1.ico""", one, StringComparison.Ordinal);
        Assert.Contains(@"icon=""../HOLLOWKNIGHT2.ico""", two, StringComparison.Ordinal);
    }

    private static string PageFor(int disc, int ofDiscs)
    {
        RecordingWriter wrote = new();
        GameFolder found = new(@"C:\games\x", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", null, [], [], null);
        PlannedDisc[] discs = [.. Enumerable.Range(1, ofDiscs).Select(number => new PlannedDisc(number, [], 0))];

        BuildContext context = new(
            new DiscProject("HOLLOW KNIGHT", [found], new DiscOptions())
            {
                Artwork = new DiscArtwork(IconPath: @"C:\pictures\cover.png")
            },
            new DiscLayout(discs, 1_000_000, 1),
            discs[disc - 1],
            @"C:\stage",
            wrote);

        new WriteTheMenuStep().Run(context, CancellationToken.None).GetAwaiter().GetResult();

        return wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;
    }

    [Fact]
    public async Task Tells_the_menu_where_the_manual_and_the_extras_are()
    {
        RecordingWriter wrote = new();

        await Write(wrote, extras: new DiscExtras(ManualPath: @"C:\things\the manual.pdf",
            ExtrasFolderPath: @"C:\things\extras"));

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // Forward slashed and relative to the root, like everything else the
        // menu is handed, because a disc does not know its own drive letter.
        Assert.Contains(@"manual: ""Extras/the manual.pdf""", page, StringComparison.Ordinal);
        Assert.Contains(@"extras: ""Extras""", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promises_no_manual_on_a_disc_that_has_none()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // A Manual button on a disc with no manual is a promise the disc
        // cannot keep, found by somebody who has already burned it.
        Assert.Contains(@"manual: """"", page, StringComparison.Ordinal);
        Assert.Contains(@"extras: """"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writes_a_way_in_at_the_top_of_the_disc()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        // At the TOP, not in AUTORUN. A disc opens its menu by itself only
        // where AutoRun is switched on, and Windows has been turning that off
        // for years: without this the disc appears to do nothing at all and
        // the menu sits in a folder nobody has any reason to open.
        RecordingWriter.Written way = Assert.Single(
            wrote.Texts, text => text.Path.EndsWith("Start Here.hta", StringComparison.Ordinal));

        Assert.Equal(@"C:\stage\Start Here.hta", way.Path);

        // It starts the real menu, and says so rather than failing silently
        // when the menu is not where it expects.
        Assert.Contains("AUTORUN", way.Text, StringComparison.Ordinal);
        Assert.Contains("menu.hta", way.Text, StringComparison.Ordinal);
        Assert.Contains("mshta", way.Text, StringComparison.Ordinal);
        Assert.Contains("The menu is missing from this disc", way.Text, StringComparison.Ordinal);

        // It shows nothing of its own and gets out of the way.
        Assert.Contains("windowState=\"minimize\"", way.Text, StringComparison.Ordinal);
        Assert.Contains("window.close()", way.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Builds_its_backslashes_rather_than_writing_them()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        string way = wrote.Texts.Single(
            text => text.Path.EndsWith("Start Here.hta", StringComparison.Ordinal)).Text;

        // Every backslash in it comes from a character code. A typed one has
        // been halved on the way into a generated file more than once in this
        // project, and a launcher with a broken path is a disc that cannot be
        // opened by the one person whose AutoRun is off.
        Assert.DoesNotContain(@"\", way, StringComparison.Ordinal);
        Assert.Contains("String.fromCharCode(92)", way, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writes_the_logic_file_untouched()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        string onTheDisc = wrote.Texts.Single(text => text.Path.EndsWith("menu.js", StringComparison.Ordinal)).Text;

        // Not a generated copy. The same bytes the cscript tests run.
        Assert.Equal(MenuWriter.MenuScript(), onTheDisc);
        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, onTheDisc);
    }

    [Fact]
    public async Task Leaves_no_placeholder_on_a_disc_anybody_could_burn()
    {
        RecordingWriter wrote = new();

        await Write(wrote, game: "Rock & Roll \"Deluxe\"");

        // The last line of defence for the %%SINGLE%% lesson: whatever else
        // changes, nothing unresolved reaches a disc.
        Assert.All(wrote.Texts, text => Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, text.Text));
    }

    [Fact]
    public async Task Names_the_menu_after_the_game_rather_than_the_disc()
    {
        RecordingWriter wrote = new();

        await Write(wrote, game: "Hollow Knight");

        string page = wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        Assert.Contains("Hollow Knight", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offers_to_open_the_folder_when_there_is_nothing_to_run()
    {
        RecordingWriter wrote = new();

        await Write(wrote, kind: GameKind.FilesOnly);

        string page = wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // Not a failure and not an Install button that does nothing: the menu
        // offers the folder, and the disc is still a disc.
        Assert.Contains("Open folder", page, StringComparison.Ordinal);
        Assert.DoesNotContain(">Install<", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Has_nothing_to_do_when_no_menu_was_asked_for()
    {
        // An ordinary thing to want. The files are still there and Explorer
        // still opens the disc.
        BuildContext context = Context(new RecordingWriter(), menu: false);

        Assert.False(new WriteTheMenuStep().AppliesTo(context));
    }

    // ------------------------------------------------------------------- music

    [Fact]
    public async Task Plays_the_music_the_disc_carries()
    {
        RecordingWriter wrote = new();

        await Write(wrote, extras: new DiscExtras(MusicPath: @"C:\stuff\Hornet's Theme.MP3"));

        string page = TheMenu(wrote);

        // The picker that asks for this file is called "What plays behind the
        // menu". Nothing played it: it was copied onto every disc of every
        // set, and no menu ever named it.
        Assert.Contains("music: \"AUTORUN/music.mp3\"", page, StringComparison.Ordinal);

        // Named by the step that copies it, so the two cannot drift. Lower
        // case, because the file somebody chose was called .MP3.
        Assert.Equal(
            "music.mp3",
            CopyTheMusicStep.NameOnTheDisc(
                new DiscProject("D", [], new DiscOptions()) with
                {
                    Extras = new DiscExtras(MusicPath: @"C:\stuff\Hornet's Theme.MP3")
                }));
    }

    [Fact]
    public async Task Says_there_is_no_music_when_there_is_none()
    {
        RecordingWriter wrote = new();

        await Write(wrote);

        // Empty rather than absent, so the script can ask without checking
        // whether the field is there, and so a disc with no tune never hands
        // the player a path to nothing.
        Assert.Contains("music: \"\"", TheMenu(wrote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_quiet_when_a_preview_is_not_the_first_window()
    {
        RecordingWriter wrote = new();

        await new WriteTheMenuStep(forADisc: false, playsMusic: false).Run(
            Context(wrote, extras: new DiscExtras(MusicPath: @"C:\stuff\the tune.mp3")),
            CancellationToken.None);

        // Every disc of a set carries the tune and every disc's menu plays
        // it. Previewing a set opens a window per disc, and three of them
        // playing the same bar over each other is not a preview of anything.
        Assert.Contains("music: \"\"", TheMenu(wrote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Carries_a_player_that_works_in_what_opens_it()
    {
        RecordingWriter wrote = new();

        await Write(wrote, extras: new DiscExtras(MusicPath: @"C:\stuff\the tune.mp3"));

        string page = TheMenu(wrote);

        // Measured in mshta on this machine, one at a time, against a real
        // MP3 at document mode 9:
        //
        //     <bgsound>   nothing at all, silently
        //     <audio>     error 4, src not supported
        //     WMPlayer    playState 3, position moving, and it loops
        //
        // So the control, and the control is what v1 settled on too.
        Assert.Contains("new ActiveXObject(\"WMPlayer.OCX\")", page, StringComparison.Ordinal);
        Assert.Contains("setMode(\"loop\", true)", page, StringComparison.Ordinal);

        // And a way to turn it off, which is the first thing anybody wants
        // from a disc that starts playing music at them.
        Assert.Contains("id=\"mute\"", page, StringComparison.Ordinal);
        Assert.Contains("toggleMusic()", page, StringComparison.Ordinal);

        // Started when the page opens. The player can be in the file and
        // never asked to do anything, which is a menu that silently does
        // nothing: the state this whole thing was already in.
        Assert.Contains("startTheMusic();", page, StringComparison.Ordinal);

        // Measured from the TOP of the stage. The stage is as tall as the
        // whole window and the window has a title bar, so the stage runs off
        // the bottom of what can be seen by the height of that bar. Anchored
        // to the bottom this control sat below the edge: the page reported it
        // displayed and a photograph of the window showed nothing there.
        string style = page
            .Split("#mute {", StringSplitOptions.None)[1]
            .Split('}', StringSplitOptions.None)[0];

        Assert.Contains("top:", style, StringComparison.Ordinal);
        Assert.DoesNotContain("bottom:", style, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_music_lands_where_the_menu_looks_for_it()
    {
        RecordingWriter wrote = new();
        BuildContext context = Context(wrote, extras: new DiscExtras(MusicPath: @"C:\stuff\the tune.ogg"));

        await new CopyTheMusicStep().Run(context, CancellationToken.None);
        await new WriteTheMenuStep().Run(context, CancellationToken.None);

        // The one that matters, and the one no test watched: the file the
        // step writes and the file the menu names have to be the same file.
        string named = TheMenu(wrote)
            .Split("music: \"", StringSplitOptions.None)[1]
            .Split('"', StringSplitOptions.None)[0];

        Assert.Equal(@"AUTORUN/music.ogg", named);
        Assert.Contains(
            @"C:\stage\" + named.Replace('/', (char)92),
            wrote.Copies.Select(copy => copy.To));
    }

    private static string TheMenu(RecordingWriter wrote) =>
        wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

    // ------------------------------------------------------------------ helpers

    private static Task Write(
        IStageWriter writer,
        string game = "Hollow Knight",
        GameKind kind = GameKind.GogInstaller,
        bool menu = true,
        DiscArtwork? artwork = null,
        DiscExtras? extras = null) =>
        new WriteTheMenuStep().Run(Context(writer, game, kind, menu, artwork, extras), CancellationToken.None);

    private static BuildContext Context(
        IStageWriter writer,
        string game = "Hollow Knight",
        GameKind kind = GameKind.GogInstaller,
        bool menu = true,
        DiscArtwork? artwork = null,
        DiscExtras? extras = null)
    {
        GameFolder found = new(@"C:\games\x", kind, game, game, null, [], [], null);
        PlannedDisc disc = new(1, [], 0);

        return new BuildContext(
            new DiscProject("HOLLOW KNIGHT", [found], new DiscOptions(Menu: menu))
            {
                Artwork = artwork,
                Extras = extras
            },

            // This disc carries the extras, which is the case worth testing:
            // the other discs of a set must NOT offer a manual they do not have.
            new DiscLayout([disc], 1_000_000, 1),
            disc,
            @"C:\stage",
            writer);
    }
}
