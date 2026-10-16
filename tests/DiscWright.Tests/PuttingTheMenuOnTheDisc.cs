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

        // An HTA is IE5 in quirks mode unless it is told otherwise, and IE5
        // has no rgba: every see-through colour below would be dropped and
        // the menu would come out looking exactly as it always did. Measured
        // on the real thing by rendering it and asking: documentMode 5 and
        // BackCompat without this line, 11 and CSS1Compat with it.
        Assert.Contains(@"http-equiv=""X-UA-Compatible"" content=""IE=edge""", page, StringComparison.Ordinal);

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
        Assert.Contains(@"icon=""../disc.ico""", page, StringComparison.Ordinal);

        // And ours is not carried for nothing.
        Assert.DoesNotContain(wrote.Binaries, path => path.EndsWith("menu.ico", StringComparison.Ordinal));
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
