using System.Windows;

using DiscWright.ViewModels;

namespace DiscWright.App;

/// <summary>
/// Puts a window on the screen at a size the screen has room for.
/// </summary>
/// <remarks>
/// The arithmetic is <see cref="FittingTheScreen"/>, where a test can reach
/// it; what is here is only reading the work area and writing the answer onto
/// the window. The work area is the main screen's, without the taskbar, in the
/// same device independent units the window is measured in, so a laptop at
/// 125% or 150% has less room than its pixels suggest and gets it right.
/// </remarks>
internal static class OnThisScreen
{
    /// <summary>
    /// Opens a window at its drawn size where that fits, smaller where it does
    /// not, and centred either way. The drawn size is its Width and Height and
    /// the smallest its MinWidth and MinHeight, all as its markup gives them.
    /// </summary>
    public static void Fit(Window window)
    {
        FittingTheScreen.Fitted fitted = FittingTheScreen.In(
            (window.Width, window.Height), (window.MinWidth, window.MinHeight), TheRoom());

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.MinWidth = fitted.MinWidth;
        window.MinHeight = fitted.MinHeight;
        window.Width = fitted.Width;
        window.Height = fitted.Height;
        window.Left = fitted.Left;
        window.Top = fitted.Top;
    }

    /// <summary>
    /// Makes a dialog no taller than the screen. Its own placement over its
    /// owner is left alone, and its content scrolls, so being shorter loses
    /// nothing.
    /// </summary>
    public static void NoTallerThanTheScreen(Window dialog)
    {
        FittingTheScreen.Room room = TheRoom();
        dialog.Height = Math.Min(dialog.Height, room.Height);
        dialog.Width = Math.Min(dialog.Width, room.Width);
    }

    private static FittingTheScreen.Room TheRoom()
    {
        Rect area = SystemParameters.WorkArea;
        return new FittingTheScreen.Room(area.Left, area.Top, area.Width, area.Height);
    }
}
