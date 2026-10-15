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
        bool menu = true) =>
        new WriteTheMenuStep().Run(Context(writer, game, kind, menu), CancellationToken.None);

    private static BuildContext Context(
        IStageWriter writer,
        string game = "Hollow Knight",
        GameKind kind = GameKind.GogInstaller,
        bool menu = true)
    {
        GameFolder found = new(@"C:\games\x", kind, game, game, null, [], [], null);
        PlannedDisc disc = new(1, [], 0);

        return new BuildContext(
            new DiscProject("HOLLOW KNIGHT", [found], new DiscOptions(Menu: menu)),
            new DiscLayout([disc], 1_000_000, 0),
            disc,
            @"C:\stage",
            writer);
    }
}
