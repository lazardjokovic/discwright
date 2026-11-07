using System.Runtime.InteropServices;

namespace DiscWright.Platform;

/// <summary>
/// Asks Windows to draw a window's title bar dark or light.
/// </summary>
/// <remarks>
/// <para>
/// The title bar is Windows' own and no brush of ours reaches it, so without
/// this a dark window sits under a bright white bar, which is what dark mode
/// looks like when it is half done.
/// </para>
/// <para>
/// Attribute 20 is the documented <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>;
/// before Windows 10 build 18985 the same switch answered to 19, tried
/// second. A refusal is swallowed on purpose: the worst outcome is a light
/// title bar, which is where every window started, and a window that cannot
/// open because its title bar could not be recoloured would be absurd.
/// </para>
/// </remarks>
public static class PaintingTheTitleBar
{
    private const int UseImmersiveDarkMode = 20;

    private const int UseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int bytes);

    public static void OfWindow(nint window, bool dark)
    {
        if (window == 0)
        {
            return;
        }

        int wanted = dark ? 1 : 0;

        if (DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref wanted, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(window, UseImmersiveDarkModeBefore20H1, ref wanted, sizeof(int));
        }
    }
}
