using DiscWright.Core;
using System.Globalization;
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

        // Whether the disc will carry a list to be checked against. A real
        // disc looks for the file itself; this is for a preview, which has
        // no disc to look at and still has to show the button where it
        // will sit.
        data.Append("    checksums: ").Append(facts.CarriesAChecksumList ? "true" : "false").Append(",\n");

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
            .Append(JScript.ListOf(TheDiscsOwnNames(facts))).Append(",\n");

        // Where the list sits and which corner the small buttons take. Two
        // words each, written by this program alone, so nothing a project
        // file says can reach the page as anything else.
        MenuLook look = facts.Look ?? MenuLook.Plain;
        data.Append("    look: ").Append(JScript.Fields(
            ("listOn", JScript.Literal(look.ListOn == MenuSide.Left ? "left" : "right")),
            ("corner", JScript.Literal(look.CornerAtTheBottom ? "bottom" : "top")),
            ("discsOwn", JScript.Literal(look.TheDiscsOwnInAWord switch
            {
                "SmallButtons" => "small",
                "Manual" => "manual",
                "Extras" => "extras",
                _ => "list",
            })),
            ("openTheDisc", look.OpenTheDiscButton ? "true" : "false"),

            // Tidied here as well as where it is typed and read, because
            // this is the last place before the disc and costs nothing.
            ("heading", JScript.Literal(MenuLook.TidiedHeading(look.Heading) ?? string.Empty)),
            ("headingHidden", look.HeadingHidden ? "true" : "false"),

            // What the page does about having no frame: its own way out and
            // a way to be moved. The frame itself is taken off in the tag
            // at the top, which is read before any script runs.
            ("frame", JScript.Literal(look.NoFrame ? "none" : "window")))).Append(",\n");
        data.Append("    games: ").Append(
            JScript.ArrayOf(facts.GamesOrTheOne.Select(game => JScript.Fields(
                ("name", JScript.Literal(game.Name)),

                // What to look up, which the menu needs and nobody reads.
                ("match", JScript.Literal(game.MatchName ?? game.Name)),
                ("runs", JScript.Literal(game.Runs)),
                ("folder", JScript.Literal(game.Folder)),
                ("plays", JScript.Literal(game.Plays)),
                ("manual", JScript.Literal(game.Manual)),
                ("extras", JScript.Literal(game.Extras)),

                // What under the game's folder is not what installs it: its
                // own manual and extras, on a set several games share. The
                // menu works out which discs a game needs from everything
                // under its folder but these.
                ("apart", JScript.ListOf(game.Apart ?? [])),
                ("addOns", JScript.ArrayOf(game.AddOns.Select(addOn => JScript.Fields(
                    ("name", JScript.Literal(addOn.Name)),
                    ("runs", JScript.Literal(addOn.Runs)))))))))).Append('\n');
        data.Append('}');

        return Template
            .Replace("%%TITLE%%", Markup(facts.GameName), StringComparison.Ordinal)
            .Replace("%%APPNAME%%", Markup(facts.ApplicationName), StringComparison.Ordinal)
            // The picture the menu is drawn on, the colour instead, or
            // nothing at all.
            //
            // Written as a rule rather than always present and sometimes
            // pointing at a missing file, because a background-image that
            // does not resolve leaves the panel floating on whatever the
            // browser decides, and the disc looks broken rather than plain.
            //
            // The colour goes through SolidColour on its way into a style
            // rule on the page, so nothing that is not exactly a colour can
            // ride a project file into the disc's own markup. The picture
            // wins when a file somebody edited carries both.
            .Replace(
                "%%BACKDROP%%",
                facts.BackdropFile is string picture
                    ? "background-image: url(\"" + picture + "\"); background-repeat: no-repeat;"
                    : SolidColour.Tidied(facts.BackdropColour) is string colour
                        ? "background: " + colour + ";"
                        : string.Empty,
                StringComparison.Ordinal)
            // One of six lines this program wrote, chosen by name. Never
            // text from a project file: it lands inside the page's style.
            // The disc's own font first when it carries one, under a name
            // no machine has, and after it the face from the list for a
            // machine that cannot load it. Both are words this program
            // wrote: the file's own name never reaches the page.
            .Replace(
                "%%FONT%%",
                (facts.HasAFontOfItsOwn ? "\"" + AFontForTheMenu.FamilyOnThePage + "\", " : string.Empty)
                    + MenuFonts.For((facts.Look ?? MenuLook.Plain).Font).AsThePageAsksForIt,
                StringComparison.Ordinal)
            .Replace(
                "%%OWNFONT%%",
                facts.HasAFontOfItsOwn
                    ? "@font-face { font-family: \"" + AFontForTheMenu.FamilyOnThePage + "\"; src: url(\""
                        + AFontForTheMenu.FileName + "\") format(\"woff\"); }"
                    : string.Empty,
                StringComparison.Ordinal)
            // The window's title bar and its edge, which only the tag at
            // the top of the page can take off: mshta reads it once, before
            // the page has a script to ask.
            .Replace("%%CAPTION%%", (facts.Look ?? MenuLook.Plain).NoFrame ? "no" : "yes", StringComparison.Ordinal)
            .Replace("%%BORDER%%", (facts.Look ?? MenuLook.Plain).NoFrame ? "none" : "thin", StringComparison.Ordinal)
            // What the heading and the buttons are written in. Tidied here
            // as well, the last place before the disc, so only ever six hex
            // figures reach the page's style.
            .Replace(
                "%%INK%%",
                SolidColour.Tidied((facts.Look ?? MenuLook.Plain).Ink) ?? MenuLook.TheHeadingsOwnInk,
                StringComparison.Ordinal)
            .Replace(
                "%%BUTTONINK%%",
                SolidColour.Tidied((facts.Look ?? MenuLook.Plain).Ink) ?? MenuLook.TheButtonsOwnInk,
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
    /// <para>
    /// <strong>And every other disc's icon of the set.</strong> A set's file
    /// lists every disc's files and is read once, from the first disc that
    /// goes in, so disc 2's icon was the one row of a disc's own that this
    /// disc could not recognise, and it was copied in beside the game. Found
    /// by the owner on 2026-11-10, HOLLOWKNIGHT2.ico in the folder his set was
    /// put back together in.
    /// </para>
    private static IEnumerable<string> TheDiscsOwnNames(MenuFacts facts) =>
    [
        .. ReservedDiscNames.All,
        .. new[] { facts.IconOnTheDisc }
            .Concat(facts.IconsOfTheSet ?? [])
            .Where(icon => icon.Length > 0 && !icon.Contains('/', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];
    /// <summary>
    /// For text that lands in markup rather than in script: a title, a caption,
    /// the words on a button.
    /// </summary>
    /// <remarks>
    /// Anything past plain ASCII is written as a numbered character reference,
    /// for the reason <see cref="JScript.Literal"/> gives: the menu has no
    /// charset, so mshta reads its bytes in the machine's ANSI codepage, and a
    /// name with an accent or a trademark sign in it came out as the bytes of
    /// its UTF-8 read one by one. By character rather than by UTF-16 unit, so
    /// a character outside the first plane is one reference and not two
    /// halves of one, which markup does not accept.
    /// </remarks>
    private static string Markup(string value)
    {
        StringBuilder safe = new(value.Length);

        foreach (Rune character in value.EnumerateRunes())
        {
            switch (character.Value)
            {
                case '&':
                    safe.Append("&amp;");
                    break;

                case '<':
                    safe.Append("&lt;");
                    break;

                case '>':
                    safe.Append("&gt;");
                    break;

                case '"':
                    safe.Append("&quot;");
                    break;

                case > 0x7E:
                    safe.Append(CultureInfo.InvariantCulture, $"&#{character.Value};");
                    break;

                default:
                    safe.Append(character.ToString());
                    break;
            }
        }

        return safe.ToString();
    }

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
/// <param name="IconsOfTheSet">
/// Every disc of the set's own icon name, when the set has one, so a copy off
/// any disc leaves all of them behind.
/// </param>
/// <param name="Manual">Where the manual is on the disc, or nothing.</param>
/// <param name="Extras">Where the extras folder is, or nothing.</param>
/// <param name="Look">How the menu is laid out, or nothing for the menu as it comes.</param>
/// <param name="CarriesAChecksumList">
/// Whether the disc is being made with a checksum list, which is what the
/// check button checks against.
/// </param>
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
    string? Music = null,
    string? BackdropColour = null,
    IReadOnlyList<string>? IconsOfTheSet = null,
    MenuLook? Look = null,
    bool CarriesAChecksumList = false,
    bool HasAFontOfItsOwn = false)
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
/// <param name="Manual">
/// What the game's own Manual button opens, from the root of the disc: its
/// one manual, or the folder holding several. Empty when it has none, and then
/// its screen offers the disc's.
/// </param>
/// <param name="Extras">
/// The folder of the game's own extras, from the root of the disc, or empty.
/// </param>
public sealed record MenuGame(
    string Name,
    string Runs,
    string Folder,
    IReadOnlyList<MenuAddOn>? Given = null,
    string? MatchName = null,
    string Plays = "",
    string Manual = "",
    string Extras = "",
    IReadOnlyList<string>? Apart = null)
{
    /// <summary>Its add-ons, or none.</summary>
    public IReadOnlyList<MenuAddOn> AddOns => Given ?? [];
}

/// <summary>One add-on, as the menu sees it.</summary>
/// <param name="Name">What the button says.</param>
/// <param name="Runs">What it runs, relative to the root of the disc.</param>
public sealed record MenuAddOn(string Name, string Runs);
