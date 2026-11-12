using System.Runtime.InteropServices;

namespace DiscWright.Platform;

/// <summary>
/// Whether this copy runs from a package, which for DiscWright means the
/// Microsoft Store.
/// </summary>
/// <remarks>
/// <para>
/// A packaged copy is updated by the Store, so it never updates itself and its
/// version link opens its Store page rather than asking GitHub. And its writes
/// into AppData are redirected to a private place no other program sees, so
/// anything another program is meant to open, the log, has to be written into
/// the package's own folder instead.
/// </para>
/// <para>
/// Windows answers with the package family name, or with
/// APPMODEL_ERROR_NO_PACKAGE, 15700, for a copy that is not in one. Any other
/// answer is taken as not packaged too: an installed or portable copy is what
/// this program has always been, and the safe thing to fall back on.
/// </para>
/// </remarks>
public static class ThisCopy
{
    private const int NoPackage = 15700;
    private const int TooSmall = 122;

    /// <summary>The page this app has in the Microsoft Store.</summary>
    /// <remarks>
    /// Its Store ID, reserved in Partner Center on 2026-11-12. The
    /// ms-windows-store scheme opens the Store app straight on the page, with
    /// no browser and no network call made by DiscWright.
    /// </remarks>
    public const string StorePage = "ms-windows-store://pdp/?productid=9NS2FK1N3KKC";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, char[]? name);

    /// <summary>The package family name, or nothing for a copy that is not packaged.</summary>
    public static string? PackageFamilyName()
    {
        try
        {
            uint length = 0;
            int said = GetCurrentPackageFamilyName(ref length, null);

            if (said == NoPackage || said != TooSmall || length == 0)
            {
                return null;
            }

            char[] name = new char[length];

            return GetCurrentPackageFamilyName(ref length, name) == 0
                ? new string(name, 0, (int)length - 1)
                : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
