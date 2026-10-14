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
            [@"C:\stage\AUTORUN\menu.js", @"C:\stage\AUTORUN\menu.hta"],
            wrote.Texts.Select(text => text.Path));
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

        string page = wrote.Texts.Single(text => text.Path.EndsWith(".hta", StringComparison.Ordinal)).Text;

        Assert.Contains("Hollow Knight", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offers_to_open_the_folder_when_there_is_nothing_to_run()
    {
        RecordingWriter wrote = new();

        await Write(wrote, kind: GameKind.FilesOnly);

        string page = wrote.Texts.Single(text => text.Path.EndsWith(".hta", StringComparison.Ordinal)).Text;

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
