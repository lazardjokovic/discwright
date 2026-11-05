using DiscWright.Core;
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

    private static readonly string TheWayIn = Resource("start-here.hta");

    private static readonly byte[] TheOwnIcon = Bytes("menu.ico");

    /// <summary>The logic, identical on every disc.</summary>
    public static string MenuScript() => Script;

    /// <summary>
    /// DiscWright's own icon, for a disc that chose none.
    /// </summary>
    /// <remarks>
    /// Trimmed to the sizes a window actually shows, 16 up to 64. The full set
    /// is 363 KB and almost all of that is a 256 pixel frame that nothing on a
    /// disc will ever ask for.
    /// </remarks>
    public static byte[] TheIconToFallBackOn() => TheOwnIcon;

    /// <summary>
    /// The launcher that sits at the top of the disc, for when AutoRun is off.
    /// </summary>
    /// <remarks>
    /// Identical on every disc, and carries no facts about one: it finds the
    /// menu beside itself and starts it. Nothing to fill in means nothing that
    /// can be left unfilled.
    /// </remarks>
    public static string TheWayInWithoutAutorun() => TheWayIn;

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
        data.Append("    buttons: ").Append(JScript.ListOf(facts.Buttons)).Append(",\n");
        data.Append("    manual: ").Append(JScript.Literal(facts.Manual ?? string.Empty)).Append(",\n");
        data.Append("    extras: ").Append(JScript.Literal(facts.Extras ?? string.Empty)).Append(",\n");

        // What the menu plays behind itself, or nothing at all. The
        // picker that asks for it is called "What plays behind the menu",
        // and until this line existed the file was copied onto every disc
        // of every set and nothing ever played a note of it.
        data.Append("    music: ").Append(JScript.Literal(facts.Music ?? string.Empty)).Append(",\n");
        data.Append("    icon: ").Append(JScript.Literal(facts.IconOnTheDisc)).Append(",\n");

        // So the script can refuse to start things that are not there. A
        // preview stages the menu and the artwork and nothing else.
        data.Append("    onADisc: ").Append(facts.OnlyOneAtATime ? "true" : "false").Append(",\n");

        // The disc's own files, so that copying a game off the root does not
        // carry the menu and the autorun with it. Written from the one list
        // that decides what belongs to a disc rather than from a second copy
        // of it here, which would drift.
        //
        // THE ICON HAS TO BE ADDED TO IT, because that list cannot name it: an
        // icon is named after the disc it sits on, so there is no constant to
        // put into a list of constants. Without this the icon was the one thing
        // of the disc's own that a copy carried into somebody's game folder,
        // while "disc.ico" sat in the list naming a file no build has written
        // since the icon started being named after the disc. Found on a real
        // disc on 2026-11-05, in the same case that found the root copy.
        data.Append("    oursNotTheGame: ")
            .Append(JScript.ListOf(TheDiscsOwnNames(facts.IconOnTheDisc))).Append(",\n");
        data.Append("    games: ").Append(
            JScript.ArrayOf(facts.GamesOrTheOne.Select(game => JScript.Fields(
                ("name", JScript.Literal(game.Name)),

                // What to look up, which the menu needs and nobody reads.
                ("match", JScript.Literal(game.MatchName ?? game.Name)),
                ("runs", JScript.Literal(game.Runs)),
                ("folder", JScript.Literal(game.Folder)),
                ("plays", JScript.Literal(game.Plays)),
                ("addOns", JScript.ArrayOf(game.AddOns.Select(addOn => JScript.Fields(
                    ("name", JScript.Literal(addOn.Name)),
                    ("runs", JScript.Literal(addOn.Runs)))))))))).Append('\n');
        data.Append('}');

        return Template
            .Replace("%%TITLE%%", Markup(facts.GameName), StringComparison.Ordinal)
            .Replace("%%APPNAME%%", Markup(facts.ApplicationName), StringComparison.Ordinal)
            // The picture the menu is drawn on, or nothing at all.
            //
            // Written as a rule rather than always present and sometimes
            // pointing at a missing file, because a background-image that
            // does not resolve leaves the panel floating on whatever the
            // browser decides, and the disc looks broken rather than plain.
            .Replace(
                "%%BACKDROP%%",
                facts.BackdropFile is string picture
                    ? "background-image: url(\"" + picture + "\"); background-repeat: no-repeat;"
                    : string.Empty,
                StringComparison.Ordinal)
            .Replace("%%SINGLE%%", facts.OnlyOneAtATime ? "yes" : "no", StringComparison.Ordinal)
            .Replace("%%ICON%%", Markup(facts.Icon), StringComparison.Ordinal)
            .Replace("%%PRIMARY%%", Markup(facts.PrimaryButton), StringComparison.Ordinal)
            .Replace("%%DATA%%", data.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything at the root of this disc that belongs to the disc rather than
    /// to the game on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constant names, plus this disc's own icon, which is not one of them
    /// because it is named after the disc.
    /// </para>
    /// <para>
    /// <strong>An icon beside the menu rather than at the root adds nothing.</strong>
    /// That is the fallback icon, written into the menu's own folder, and the
    /// folder is named in the list already. Only a bare name is a name at the
    /// root, which is the only place this question is asked.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> TheDiscsOwnNames(string iconOnTheDisc) =>
        iconOnTheDisc.Length > 0 && !iconOnTheDisc.Contains('/', StringComparison.Ordinal)
            ? [.. ReservedDiscNames.All, iconOnTheDisc]
            : ReservedDiscNames.All;
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

    /// <summary>
    /// An embedded file that is not text.
    /// </summary>
    private static byte[] Bytes(string name)
    {
        Assembly assembly = typeof(MenuWriter).Assembly;
        string full = "DiscWright.Menu." + name;

        using Stream? stream = assembly.GetManifestResourceStream(full)
            ?? throw new InvalidOperationException(
                $"{full} is not an embedded resource. Found: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using MemoryStream all = new();
        stream.CopyTo(all);

        return all.ToArray();
    }

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
/// <param name="Icon">
/// The icon the menu window wears, relative to the menu itself. The disc's own
/// when one was chosen, and DiscWright's otherwise, because the alternative is
/// the blank default and a menu that looks like nobody finished it.
/// </param>
/// <param name="BackdropFile">
/// The picture the menu is drawn on, named relative to the page, or nothing
/// when there is none. It sits beside menu.hta, so it is a bare file name.
///
/// Until this existed the background was copied onto every disc and the page
/// never referred to it, so a picture somebody chose was written, carried and
/// never once shown.
/// </param>
/// <param name="IconOnTheDisc">
/// The same icon, written from the root of the disc with forward slashes, for
/// the script to turn into a path. An HTA's icon attribute does not resolve a
/// relative one, so the attribute alone leaves the window wearing Windows'
/// default.
/// </param>
/// <param name="Manual">Where the manual is on the disc, or nothing.</param>
/// <param name="Extras">Where the extras folder is, or nothing.</param>
public sealed record MenuFacts(
    string GameName,
    string ApplicationName,
    int DiscNumber,
    int DiscsInSet,
    string PrimaryButton,
    IReadOnlyList<string> Buttons,
    bool OnlyOneAtATime,
    IReadOnlyList<MenuGame>? Games = null,
    string? BackdropFile = null,
    string Icon = "menu.ico",
    string IconOnTheDisc = "AUTORUN/menu.ico",
    string? Manual = null,
    string? Extras = null,
    string? Music = null)
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
/// <param name="Plays">
/// What starts the game once it has been copied, relative to the game's own
/// folder, or empty when the disc does not know. Only ever set when there was
/// exactly one candidate: guessing which of six executables starts a game is
/// how somebody ends up running an uninstaller.
/// </param>
public sealed record MenuGame(
    string Name,
    string Runs,
    string Folder,
    IReadOnlyList<MenuAddOn>? Given = null,
    string? MatchName = null,
    string Plays = "")
{
    /// <summary>Its add-ons, or none.</summary>
    public IReadOnlyList<MenuAddOn> AddOns => Given ?? [];
}

/// <summary>One add-on, as the menu sees it.</summary>
/// <param name="Name">What the button says.</param>
/// <param name="Runs">What it runs, relative to the root of the disc.</param>
public sealed record MenuAddOn(string Name, string Runs);
