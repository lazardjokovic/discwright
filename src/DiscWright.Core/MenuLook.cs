namespace DiscWright.Core;

/// <summary>Which edge of the menu its list of buttons sits against.</summary>
public enum MenuSide
{
    /// <summary>The right, where it has always been.</summary>
    Right,

    /// <summary>The left, for a picture whose subject is on the right.</summary>
    Left,
}

/// <summary>
/// How the disc's menu is laid out, where somebody wanted it otherwise than
/// it comes.
/// </summary>
/// <remarks>
/// <para>
/// Asked for on discwright#98, 16 November 2026: "could DW2 give the option
/// of positioning the main menu on the left or right? That way you can avoid
/// overlapping logos embedded on one side of a wallpaper". The list covers a
/// third of the picture, and which third matters is the picture's business.
/// </para>
/// <para>
/// <strong>The small buttons go to the other side.</strong> The music and the
/// disc check are about the window rather than about a game, and sit in a
/// corner away from the list. Which side that is follows the list, so the
/// only thing left to choose is top or bottom: a corner on the list's own
/// side would put them on top of its heading or its last button.
/// </para>
/// </remarks>
/// <param name="ListOn">Which edge the list of buttons sits against.</param>
/// <param name="CornerAtTheBottom">
/// Whether the small buttons sit in the bottom corner rather than the top one.
/// </param>
/// <param name="DiscsOwnAsSmallButtons">
/// <para>
/// Whether the disc's own manual and extras, the ones chosen in Artwork,
/// music and extras, are small buttons in the corner of the main menu rather
/// than rows in its list.
/// </para>
/// <para>
/// discwright#98, 12 and 16 November: "they would only show on the main menu
/// as icons, and be completely separate from any game menus, since game
/// menus show their own manuals and extras", and "that way you can shorten
/// the main list". So with this on a game's screen offers only what is the
/// game's own, and no longer falls back to the disc's.
/// </para>
/// </param>
/// <param name="OpenTheDiscButton">
/// Whether the corner has a button that opens the disc in Explorer,
/// discwright#98: "an icon in the corner of any disc menu to show the
/// related explorer directory (optional)". On every screen, because it is
/// about the disc.
/// </param>
/// <param name="Heading">
/// <para>
/// What the main menu is headed with, or nothing for the disc's own name.
/// Always as <see cref="TidiedHeading"/> leaves it.
/// </para>
/// <para>
/// discwright#98, 12 November: "I'd like to see a separate option for Disc
/// Label ('what is it called') and 'Menu Label' (final disc main menu)". The
/// disc's name is what This PC shows and is cut to what a disc can carry;
/// what is written across the top of the menu need not be the same words.
/// </para>
/// </param>
/// <param name="HeadingHidden">
/// Whether the main menu has no heading at all, discwright#98, 3 October and
/// again on 12 November: "can the text directly above the menu buttons be
/// hidden?", for a picture that already has the title painted into it. A
/// game's own screen on a disc of several keeps its name, which is how
/// anybody knows which game they are about to install.
/// </param>
public sealed record MenuLook(
    MenuSide ListOn = MenuSide.Right,
    bool CornerAtTheBottom = false,
    bool DiscsOwnAsSmallButtons = false,
    bool OpenTheDiscButton = false,
    string? Heading = null,
    bool HeadingHidden = false,
    MenuFont Font = MenuFont.Tahoma,
    bool NoFrame = false)
{
    /// <summary>
    /// The longest heading the menu is given. Three lines of the panel in
    /// the menu's own capitals, past which it pushes the buttons down for
    /// nothing anybody reads.
    /// </summary>
    public const int LongestHeading = 60;

    /// <summary>
    /// A heading as the menu will have it: without the spaces around it, no
    /// longer than <see cref="LongestHeading"/>, on one line, and nothing at
    /// all when there is nothing left.
    /// </summary>
    public static string? TidiedHeading(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        // A line break in a project file somebody edited would be a heading
        // of two lines the box it was typed in can never show.
        string oneLine = string.Join(' ', typed.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

        if (oneLine.Length == 0)
        {
            return null;
        }

        return oneLine.Length <= LongestHeading ? oneLine : oneLine[..LongestHeading].TrimEnd();
    }

    /// <summary>The menu as it comes, with nothing chosen.</summary>
    public static MenuLook Plain { get; } = new();

    /// <summary>
    /// This look, or nothing when it is the plain one, which is how a project
    /// that never touched it stays equal to one that was never asked.
    /// </summary>
    public MenuLook? OrNothingWhenPlain() => this == Plain ? null : this;
}
