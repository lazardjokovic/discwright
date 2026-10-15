using System.Reflection;
using System.Text;

namespace DiscWright.Menu;

/// <summary>
/// Makes the two files the disc's menu is: the logic, and the shell that
/// carries this disc's own facts.
/// </summary>
/// <remarks>
/// <para>
/// It returns text and writes nothing. Whoever asked for it decides where it
/// goes, which is what keeps this testable without a disk and keeps a disc's
/// layout out of a file that is about wording and markup.
/// </para>
/// <para>
/// <c>menu.js</c> comes back untouched, exactly as it sits in the source tree.
/// That is the arrangement's whole value: the file the tests run under
/// <c>cscript</c> and the file that ends up on the disc are the same bytes.
/// </para>
/// </remarks>
public static class MenuWriter
{
    /// <summary>
    /// A placeholder left in a built menu is a disc that opens to nothing, and
    /// it cannot be fixed afterwards. This is what the test looks for.
    /// </summary>
    public const string PlaceholderPattern = "%%[A-Z_]+%%";

    private static readonly string Template = Resource("menu.hta.template");

    private static readonly string Script = Resource("menu.js");

    /// <summary>The logic, identical on every disc.</summary>
    public static string MenuScript() => Script;

    /// <summary>
    /// The shell, carrying this disc's own facts and nothing else generated.
    /// </summary>
    public static string MenuPage(MenuFacts facts)
    {
        StringBuilder data = new();
        data.Append("{\n");
        data.Append("    game: ").Append(JScript.Literal(facts.GameName)).Append(",\n");
        data.Append("    discNumber: ").Append(JScript.Number(facts.DiscNumber)).Append(",\n");
        data.Append("    discsInSet: ").Append(JScript.Number(facts.DiscsInSet)).Append(",\n");
        data.Append("    discsCopied: ").Append('[').Append(JScript.Number(facts.DiscNumber)).Append("],\n");
        data.Append("    buttons: ").Append(JScript.ListOf(facts.Buttons)).Append(",\n");
        data.Append("    games: ").Append(
            JScript.ArrayOf(facts.GamesOrTheOne.Select(game => JScript.Fields(
                ("name", JScript.Literal(game.Name)),

                // What to look up, which the menu needs and nobody reads.
                ("match", JScript.Literal(game.MatchName ?? game.Name)),
                ("runs", JScript.Literal(game.Runs)),
                ("folder", JScript.Literal(game.Folder)),
                ("addOns", JScript.ArrayOf(game.AddOns.Select(addOn => JScript.Fields(
                    ("name", JScript.Literal(addOn.Name)),
                    ("runs", JScript.Literal(addOn.Runs)))))))))).Append('\n');
        data.Append('}');

        return Template
            .Replace("%%TITLE%%", Markup(facts.GameName), StringComparison.Ordinal)
            .Replace("%%APPNAME%%", Markup(facts.ApplicationName), StringComparison.Ordinal)
            .Replace("%%SINGLE%%", facts.OnlyOneAtATime ? "yes" : "no", StringComparison.Ordinal)
            .Replace("%%PRIMARY%%", Markup(facts.PrimaryButton), StringComparison.Ordinal)
            .Replace("%%DATA%%", data.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// For text that lands in markup rather than in script: a title, a caption,
    /// the words on a button.
    /// </summary>
    private static string Markup(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Resource(string name)
    {
        Assembly assembly = typeof(MenuWriter).Assembly;
        string full = "DiscWright.Menu." + name;

        using Stream? stream = assembly.GetManifestResourceStream(full)
            ?? throw new InvalidOperationException(
                $"{full} is not an embedded resource. Found: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using StreamReader reader = new(stream, new UTF8Encoding(false));

        return reader.ReadToEnd();
    }
}

/// <summary>
/// What one disc's menu needs to know about itself.
/// </summary>
/// <param name="GameName">As it will be shown, already decided by the recogniser.</param>
/// <param name="ApplicationName">
/// What Windows uses to tell one running menu from another.
/// </param>
/// <param name="DiscNumber">From one.</param>
/// <param name="DiscsInSet">One for a disc on its own.</param>
/// <param name="PrimaryButton">The one thing to do next, and the only coloured button.</param>
/// <param name="Buttons">Every button, in the order they appear.</param>
/// <param name="Games">
/// Every game on the disc, in order. One name for the ordinary disc and several
/// for a compilation. The chooser that lets somebody pick between them is still
/// to be written; the names are carried so the disc already holds what it needs
/// when it is.
/// </param>
/// <param name="OnlyOneAtATime">
/// <para>Whether a second copy of this menu refocuses the first instead of opening.</para>
/// <para>
/// True for a disc, false for a preview. In v1 every preview of every disc
/// shared one application name with singleinstance on, so previewing a second
/// disc silently refocused the first one's stale window and the new menu was
/// never built. Measured: with it on, only the first background loaded.
/// </para>
/// </param>
public sealed record MenuFacts(
    string GameName,
    string ApplicationName,
    int DiscNumber,
    int DiscsInSet,
    string PrimaryButton,
    IReadOnlyList<string> Buttons,
    bool OnlyOneAtATime,
    IReadOnlyList<MenuGame>? Games = null)
{
    /// <summary>The games, or one standing for the disc when none were given.</summary>
    public IReadOnlyList<MenuGame> GamesOrTheOne => Games ?? [new MenuGame(GameName, string.Empty, string.Empty)];
}

/// <summary>One game, as its own disc's menu sees it.</summary>
/// <param name="Name">What the chooser shows, and what the game's screen is titled.</param>
/// <param name="Runs">
/// What the Install button runs, relative to the root of the disc, or empty for
/// a game with nothing to install. Empty is not a failure: the menu offers to
/// copy the folder and play it from there instead, because a game run off a
/// read only disc cannot save.
/// </param>
/// <param name="Folder">
/// Where the game's files sit on the disc, or empty when the game has the disc
/// to itself and its files are at the root.
/// </param>
/// <param name="Given">
/// What goes under this game, in the order it should be offered. Dead on the
/// menu until the game itself is installed.
/// </param>
/// <param name="MatchName">
/// The name the game is registered under, which is not always the name on the
/// disc. A folder can be renamed and a project can be given a name somebody
/// typed; what GOG wrote in the registry stays as it was, and that is what the
/// menu has to ask about.
/// </param>
public sealed record MenuGame(
    string Name,
    string Runs,
    string Folder,
    IReadOnlyList<MenuAddOn>? Given = null,
    string? MatchName = null)
{
    /// <summary>Its add-ons, or none.</summary>
    public IReadOnlyList<MenuAddOn> AddOns => Given ?? [];
}

/// <summary>One add-on, as the menu sees it.</summary>
/// <param name="Name">What the button says.</param>
/// <param name="Runs">What it runs, relative to the root of the disc.</param>
public sealed record MenuAddOn(string Name, string Runs);
