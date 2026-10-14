using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Menu;

/// <summary>
/// Puts the menu on the disc: the logic, and the shell that carries this
/// disc's own facts.
/// </summary>
/// <remarks>
/// The step lives here, beside the menu, rather than in <c>Staging</c>. A step
/// is just something that implements an interface from <c>Core</c>, so it can
/// live wherever the thing it does lives, and <c>Staging</c> is not allowed to
/// reference <c>Menu</c> anyway. The window decides which steps make up a
/// build; nothing else has to know the list.
/// </remarks>
public sealed class WriteTheMenuStep : IBuildStep
{
    /// <summary>Where the menu lives on a disc. Uppercase, as v1 writes it.</summary>
    private const string Folder = "AUTORUN";

    public string Says => "writing the menu";

    /// <summary>
    /// A disc with no menu is an ordinary thing to want: the files are still
    /// there and Explorer still opens it.
    /// </summary>
    public bool AppliesTo(BuildContext context) => context.Project.Options.Menu;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        context.Writer.MakeFolder(context.On(Folder));

        // Untouched, so the file the tests run under cscript and the file on
        // the disc are the same bytes.
        context.Writer.WriteText(context.On(Folder + @"\menu.js"), MenuWriter.MenuScript());

        context.Writer.WriteText(context.On(Folder + @"\menu.hta"), MenuWriter.MenuPage(FactsFor(context)));

        return Task.CompletedTask;
    }

    /// <summary>
    /// The buttons this disc should have, which is only ever the ones it can
    /// honour.
    /// </summary>
    /// <remarks>
    /// A Manual button on a disc with no manual is worse than no button: it is
    /// a promise the disc cannot keep, discovered by somebody who has already
    /// burned it. So the buttons are worked out from what is actually going on
    /// the disc, and the one disc of a set that carries the extras is the only
    /// one that offers them.
    /// </remarks>
    private static List<string> ButtonsFor(BuildContext context, GameFolder? game)
    {
        List<string> buttons = [game?.Kind == GameKind.FilesOnly ? "Open folder" : "Install"];

        DiscExtras? extras = context.Project.Extras;
        bool thisDiscCarriesThem = context.Disc.Number == context.Layout.DiscCarryingTheExtras;

        if (extras?.ManualPath is not null && thisDiscCarriesThem)
        {
            buttons.Add("Manual");
        }

        if ((extras?.ExtrasFolderPath is not null || extras?.Loose.Count > 0) && thisDiscCarriesThem)
        {
            buttons.Add("Extras");
        }

        buttons.Add("Exit");

        return buttons;
    }

    /// <summary>
    /// Each game, with where it landed on this disc and what starts it.
    /// </summary>
    /// <remarks>
    /// The folder comes from <see cref="OnTheDisc"/>, which is the same place
    /// the builder asked when it copied the files there. Worked out twice they
    /// would agree until somebody changed one of them, and then the menu's only
    /// button would point at a folder that is not there.
    /// </remarks>
    private static List<MenuGame> GamesOn(BuildContext context)
    {
        IReadOnlyList<GameFolder> games = context.Project.Games;
        List<MenuGame> onTheMenu = [];

        for (int at = 0; at < games.Count; at++)
        {
            GameFolder game = games[at];
            string folder = OnTheDisc.FolderFor(at + 1, game.Name, games.Count);

            string runs = game.Installer is null
                ? string.Empty
                : Join(folder, game.Installer.RelativePath);

            onTheMenu.Add(new MenuGame(game.Name, runs, folder));
        }

        return onTheMenu;
    }

    /// <summary>
    /// A path on the disc, with forward slashes, because it goes into the
    /// menu's script rather than into a Windows API and the menu builds its own
    /// paths from it.
    /// </summary>
    private static string Join(string folder, string file) =>
        folder.Length == 0
            ? WithForwardSlashes(file)
            : WithForwardSlashes(folder) + "/" + WithForwardSlashes(file);

    private static string WithForwardSlashes(string path) => path.Replace('\\', '/');

    private static MenuFacts FactsFor(BuildContext context)
    {
        GameFolder? game = context.Project.Games.Count > 0 ? context.Project.Games[0] : null;

        // A disc holding several games is named after the DISC, not after
        // whichever game happened to be added first. Somebody looking at a
        // compilation called "Hollow Knight" would reasonably conclude the
        // other three were not on it.
        string name = context.Project.Games.Count > 1
            ? context.Project.Label
            : game?.Name ?? context.Project.Label;

        return new MenuFacts(
            name,

            // What Windows tells one running menu from another by. The disc's
            // label rather than the game's name, because two discs of one set
            // are the same application and a second one should refocus the
            // first rather than opening beside it.
            "DiscWright." + context.Project.Label,
            context.Disc.Number,
            context.Layout.Discs.Count,

            // One accent, on one button: whichever is the thing to do next.
            game?.Kind == GameKind.FilesOnly ? "Open folder" : "Install",
            ButtonsFor(context, game),

            // A disc, not a preview. A preview sets this false, or previewing a
            // second disc refocuses the first one's stale window.
            true,

            GamesOn(context));
    }
}
