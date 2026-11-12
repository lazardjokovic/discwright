namespace DiscWright.ViewModels;

/// <summary>
/// The size a window opens at on a screen that may be smaller than the one it
/// was drawn for.
/// </summary>
/// <remarks>
/// <para>
/// The main window was drawn at 920 by 740 and could not be resized. On an
/// older laptop, 1366 by 768 with the taskbar taking its share, that is
/// taller than the room there is: on the owner's Windows 10 laptop it only
/// just fitted, with the buttons along the bottom at the very edge and no
/// way to make it smaller.
/// </para>
/// <para>
/// So the window opens at its drawn size where that fits, and otherwise at
/// the room there is, never below the smallest size its layout still works
/// at, unless the screen itself is smaller than that, when the screen wins:
/// a window cut off by the edge is worse than one that is cramped.
/// </para>
/// <para>
/// No WPF in here, so a test can ask it about a screen nobody here owns.
/// The window passes in its own numbers and the work area, in the same
/// device independent units.
/// </para>
/// </remarks>
public static class FittingTheScreen
{
    /// <summary>The room there is: a screen's work area, without the taskbar.</summary>
    public readonly record struct Room(double Left, double Top, double Width, double Height);

    /// <summary>Where the window goes, how big it is, and how small it may be made.</summary>
    public readonly record struct Fitted(double Left, double Top, double Width, double Height, double MinWidth, double MinHeight);

    /// <summary>
    /// The drawn size, made to fit the room, and centred in it.
    /// </summary>
    /// <param name="drawn">The size the window was designed at.</param>
    /// <param name="smallest">The smallest size its layout still works at.</param>
    /// <param name="room">The work area of the screen it is opening on.</param>
    public static Fitted In(
        (double Width, double Height) drawn, (double Width, double Height) smallest, Room room)
    {
        // The smallest the window may be made can never be more than the
        // screen, or Windows would hold it larger than the room again.
        double minWidth = Math.Min(smallest.Width, room.Width);
        double minHeight = Math.Min(smallest.Height, room.Height);

        double width = Math.Max(Math.Min(drawn.Width, room.Width), minWidth);
        double height = Math.Max(Math.Min(drawn.Height, room.Height), minHeight);

        return new Fitted(
            room.Left + ((room.Width - width) / 2),
            room.Top + ((room.Height - height) / 2),
            width,
            height,
            minWidth,
            minHeight);
    }
}
