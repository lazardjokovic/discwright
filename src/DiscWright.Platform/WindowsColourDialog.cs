using System.Runtime.InteropServices;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Windows' own colour dialog, the one Paint has: a spectrum to click in, a
/// slider for how light, and boxes for red, green and blue.
/// </summary>
/// <remarks>
/// <para>
/// xniwo, discwright#98, 23 November 2026: typing six hex figures is not how
/// anybody chooses a colour, and he asked for "a custom colour option, which
/// you click on for options (eg a colour wheel), which changes the value of
/// the hash". Windows has had that dialog since 3.1 and every PC this app
/// runs on carries it, so nothing is drawn here and nothing is installed.
/// </para>
/// <para>
/// It opens with the spectrum showing, starting at the colour already in the
/// box, and what it gives back is a colour SolidColour wrote, so the box and
/// the disc only ever get six hex figures. Cancelled, it gives back nothing
/// and the box keeps what it had.
/// </para>
/// </remarks>
public static class WindowsColourDialog
{
    private const int StartAtTheColourGiven = 0x1;

    private const int OpenWithTheSpectrum = 0x2;

    private const int AnyColourAtAll = 0x100;

    /// <summary>
    /// The sixteen colours somebody can keep in the dialog's own row, kept
    /// for as long as the program runs so a colour saved there for the
    /// backdrop is there again for the text. White to begin with, as
    /// Windows begins them. Never freed: sixty-four bytes, once.
    /// </summary>
    private static readonly nint TheColoursKept = SixteenWhites();

    [StructLayout(LayoutKind.Sequential)]
    private struct Asking
    {
        public int Size;
        public nint Owner;
        public nint Instance;
        public int Colour;
        public nint ColoursKept;
        public int Flags;
        public nint CustomData;
        public nint Hook;
        public nint Template;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref Asking asking);

    /// <summary>
    /// Shows the dialog over the window given, starting at the colour given
    /// when it is one, and says the colour chosen, or nothing when it was
    /// cancelled.
    /// </summary>
    public static string? Ask(nint owner, string? startingAt)
    {
        Asking asking = new()
        {
            Size = Marshal.SizeOf<Asking>(),
            Owner = owner,
            Colour = SolidColour.ToWindows(startingAt),
            ColoursKept = TheColoursKept,
            Flags = StartAtTheColourGiven | OpenWithTheSpectrum | AnyColourAtAll,
        };

        return ChooseColor(ref asking) ? SolidColour.FromWindows(asking.Colour) : null;
    }

    private static nint SixteenWhites()
    {
        nint kept = Marshal.AllocHGlobal(16 * sizeof(int));

        for (int each = 0; each < 16; each++)
        {
            Marshal.WriteInt32(kept, each * sizeof(int), 0xFFFFFF);
        }

        return kept;
    }
}
