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
/// <param name="forADisc">
/// Whether this is a disc being made, as against a preview of one. The only
/// thing it changes is whether the menu insists on being the only one open: a
/// disc should refocus its own window rather than opening a second, and a
/// preview must not refocus the stale window left from previewing something
/// else a minute ago.
/// </param>
/// <param name="playsMusic">
/// Whether this menu plays the tune. Always true on a disc. A preview of a
/// set opens a window for every disc, and every disc of a set carries the
/// tune, so without this they all start playing it over one another.
/// </param>
public sealed class WriteTheMenuStep(bool forADisc = true, bool playsMusic = true) : IBuildStep
{
    /// <summary>Where the menu lives on a disc. Uppercase, as v1 writes it.</summary>
    private const string Folder = "AUTORUN";

    /// <summary>
    /// What the background is called, beside the page that draws it.
    /// </summary>
    private const string TheBackdrop = "background.png";

    /// <summary>
    /// The one file at the top of the disc that starts the menu by hand.
    /// </summary>
    /// <remarks>
    /// The name is reserved in <see cref="ReservedDiscNames"/>, so a game
    /// bringing its own file of this name is moved out of the way rather than
    /// quietly overwriting this one.
    /// </remarks>
    private const string TheWayIn = "Start Here.hta";

    /// <summary>What the menu wears when the disc chose no artwork.</summary>
    private const string OurOwnIcon = "menu.ico";

    /// <summary>Where the extras land, which is where the manual lands too.</summary>
    private const string ExtrasFolder = "Extras";

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

        // At the TOP of the disc, where somebody looking at it in Explorer
        // will see it. A disc opens its menu by itself only where AutoRun is
        // switched on, and Windows has been turning that off for years:
        // without this the disc appears to do nothing at all, and the menu
        // sits in AUTORUN where nobody has any reason to look.
        context.Writer.WriteText(context.On(TheWayIn), MenuWriter.TheWayInWithoutAutorun());

        // Only when the disc has no icon of its own. A menu wearing the blank
        // default looks like nobody finished it, and this is 33 KB.
        if (context.Project.Artwork?.IconPath is null)
        {
            context.Writer.WriteBytes(
                context.On(Folder + @"\" + OurOwnIcon), MenuWriter.TheIconToFallBackOn());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// What the menu wears, named relative to the page: the disc's own icon
    /// at the root when it has one, and ours beside the page when it does
    /// not.
    /// </summary>
    private static string TheIconFor(BuildContext context) =>
        context.Project.Artwork?.IconPath is not null
            ? "../" + context.IconFileName
            : OurOwnIcon;

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
    /// Where the manual is on this disc, or nothing.
    /// </summary>
    /// <remarks>
    /// Only on the one disc of a set that carries the extras. A Manual button
    /// on a disc with no manual is a promise the disc cannot keep, found by
    /// somebody who has already burned it.
    /// </remarks>
    private static string? TheManualOn(BuildContext context)
    {
        DiscExtras? extras = context.Project.Extras;

        return extras?.ManualPath is not null && context.Disc.Number == context.Layout.DiscCarryingTheExtras
            ? ExtrasFolder + "/" + System.IO.Path.GetFileName(extras.ManualPath)
            : null;
    }

    /// <summary>
    /// What this disc plays behind its menu, or nothing.
    /// </summary>
    /// <remarks>
    /// Every disc of a set, unlike the manual and the extras. Music belongs to
    /// the menu rather than to the content, every disc has a menu of its own,
    /// and that is why the planner counts it as overhead on all of them.
    /// </remarks>
    private string? TheMusicOn(BuildContext context) =>
        playsMusic && CopyTheMusicStep.NameOnTheDisc(context.Project) is string tune
            ? Folder + "/" + tune
            : null;

    /// <summary>Where the extras are on this disc, or nothing.</summary>
    private static string? TheExtrasOn(BuildContext context)
    {
        DiscExtras? extras = context.Project.Extras;

        bool anything = extras?.ExtrasFolderPath is not null || extras?.Loose.Count > 0;

        return anything && context.Disc.Number == context.Layout.DiscCarryingTheExtras
            ? ExtrasFolder
            : null;
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
            string folder = OnTheDisc.FolderFor(at + 1, game.Name, games.Count, game.Files.Select(file => file.RelativePath));

            string runs = game.Installer is null
                ? string.Empty
                : Join(folder, game.Installer.RelativePath);

            List<MenuAddOn> addOns =
            [
                .. context.Project.AddOnsFor(at).Select(addOn => new MenuAddOn(
                    addOn.Name,
                    Join(Join(folder, AddOn.Folder), System.IO.Path.GetFileName(addOn.Path))))
            ];

            // Only when there is exactly one. Several is a question nobody
            // has answered yet, and the menu does not get to guess.
            string plays = game.LauncherCandidates.Count == 1
                ? WithForwardSlashes(game.LauncherCandidates[0].RelativePath)
                : string.Empty;

            onTheMenu.Add(new MenuGame(game.Name, runs, folder, addOns, game.MatchName, plays));
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

    private MenuFacts FactsFor(BuildContext context)
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
            forADisc,

            GamesOn(context),

            // The same condition the artwork step copies it under, so the
            // page never names a file that was not written.
            context.Project.Artwork?.BackgroundPath is not null ? TheBackdrop : null,

            // The disc's own icon sits at the root and the menu sits in
            // AUTORUN, so it is one folder up from here. Checked by
            // rendering a page that asks for one that way: it wears it.
            TheIconFor(context),

            // The same file again, spelled from the root of the disc with
            // forward slashes, for the script to turn into an absolute path.
            context.Project.Artwork?.IconPath is not null
                ? context.IconFileName
                : Folder + "/" + OurOwnIcon,

            TheManualOn(context),
            TheExtrasOn(context),

            // Named from the step that copies it rather than spelled out
            // again here. The one thing worse than a menu that plays nothing
            // is a menu that asks the player for a file nobody wrote.
            TheMusicOn(context));
    }
}
