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

    private static MenuFacts FactsFor(BuildContext context)
    {
        GameFolder? game = context.Project.Games.Count > 0 ? context.Project.Games[0] : null;

        string name = game?.Name ?? context.Project.Label;

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
            game?.Kind == GameKind.FilesOnly ? ["Open folder", "Exit"] : ["Install", "Exit"],

            // A disc, not a preview. A preview sets this false, or previewing a
            // second disc refocuses the first one's stale window.
            true);
    }
}
