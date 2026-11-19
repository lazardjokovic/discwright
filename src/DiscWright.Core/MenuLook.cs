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
public sealed record MenuLook(
    MenuSide ListOn = MenuSide.Right,
    bool CornerAtTheBottom = false,
    bool DiscsOwnAsSmallButtons = false,
    bool OpenTheDiscButton = false)
{
    /// <summary>The menu as it comes, with nothing chosen.</summary>
    public static MenuLook Plain { get; } = new();

    /// <summary>
    /// This look, or nothing when it is the plain one, which is how a project
    /// that never touched it stays equal to one that was never asked.
    /// </summary>
    public MenuLook? OrNothingWhenPlain() => this == Plain ? null : this;
}
