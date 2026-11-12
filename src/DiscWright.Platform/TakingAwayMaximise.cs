using System.Runtime.InteropServices;

namespace DiscWright.Platform;

/// <summary>
/// Takes the maximise button off a window that can still be resized.
/// </summary>
/// <remarks>
/// <para>
/// WPF offers a window that resizes with a maximise button, or one that does
/// not resize and has none, and nothing in between. The main window resizes
/// in height only: its games list lines its sizes up at a fixed width, so a
/// window stretched wide leaves them stranded in the middle of the list.
/// Maximised, it would be exactly that, and a maximise button on a window
/// held to one width would be a button that does something strange.
/// </para>
/// <para>
/// Taking the style bit away removes the button, and with it a double click
/// on the title bar maximising. Snapping to the top of the screen is
/// Windows' own and is held to the same width by MaxWidth. A refusal is
/// swallowed: the worst outcome is a button that is still there.
/// </para>
/// </remarks>
public static class TakingAwayMaximise
{
    private const int Style = -16;

    private const int MaximiseBox = 0x00010000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    public static void From(nint window)
    {
        if (window == 0)
        {
            return;
        }

        nint style = GetWindowLongPtr(window, Style);
        _ = SetWindowLongPtr(window, Style, style & ~(nint)MaximiseBox);
    }
}
