using System.Text.RegularExpressions;

namespace DiscWright.Core;

/// <summary>
/// What a patch would add to a disc, judged against the disc's installer.
/// </summary>
public enum WhatAPatchAdds
{
    /// <summary>It updates the game past what the installer installs.</summary>
    SomethingPastTheInstaller,

    /// <summary>The installer already installs everything it reaches.</summary>
    NothingTheInstallerLacks,

    /// <summary>The file names do not say, so nothing is claimed.</summary>
    NoWayToTell,
}

/// <summary>
/// Reads the version arithmetic GOG writes into its own file names, so the
/// window can tell a patch that matters from one the installer already
/// covers.
/// </summary>
/// <remarks>
/// <para>
/// A GOG offline installer carries its whole version, and GOG replaces the
/// download whenever the game updates, so a patch downloaded beside the
/// installer reaches at most what the installer already installs. But the
/// classic reason patches exist is the person who downloaded the big
/// installer long ago and later took only the small patches: their folder
/// holds an old installer and the road past it, and those patches are the
/// only thing making their disc current. The names tell the two apart:
/// <c>setup_hollow_knight_1.5.12620_(64bit)_(89718).exe</c> against
/// <c>patch_hollow_knight_1.5.12459_to_1.5.12618_(89712).exe</c>.
/// </para>
/// <para>
/// The build numbers in parentheses are trusted first, because GOG's builds
/// only ever grow, and the dotted versions second. Anything the names do
/// not say cleanly is <see cref="WhatAPatchAdds.NoWayToTell"/>, never a
/// guess: this decides what the window suggests and what the menu offers,
/// and a wrong "adds nothing" costs somebody the one patch they needed.
/// </para>
/// </remarks>
public static partial class APatchAgainstTheInstaller
{
    /// <summary>
    /// The verdict for one patch, from the two file names alone. A missing
    /// installer, a file that is not a patch, or names that do not compare
    /// all answer <see cref="WhatAPatchAdds.NoWayToTell"/>.
    /// </summary>
    public static WhatAPatchAdds For(string? installerFileName, string patchFileName)
    {
        string? toPart = ToPartOf(patchFileName);

        if (toPart is null || installerFileName is null)
        {
            return WhatAPatchAdds.NoWayToTell;
        }

        string installer = System.IO.Path.GetFileNameWithoutExtension(installerFileName);

        int? patchBuild = BuildIn(toPart);
        int? installerBuild = BuildIn(installer);

        if (patchBuild is int reached && installerBuild is int carried)
        {
            return reached <= carried
                ? WhatAPatchAdds.NothingTheInstallerLacks
                : WhatAPatchAdds.SomethingPastTheInstaller;
        }

        string? patchVersion = VersionIn(toPart);
        string? installerVersion = VersionIn(installer);

        if (patchVersion is null || installerVersion is null)
        {
            return WhatAPatchAdds.NoWayToTell;
        }

        return Compared(patchVersion, installerVersion) switch
        {
            null => WhatAPatchAdds.NoWayToTell,
            > 0 => WhatAPatchAdds.SomethingPastTheInstaller,
            _ => WhatAPatchAdds.NothingTheInstallerLacks,
        };
    }

    /// <summary>Whether the file name says outright that it is a patch.</summary>
    public static bool IsAPatch(string fileName) => ToPartOf(fileName) is not null;

    /// <summary>
    /// The version a patch updates the game to, read off its name, or
    /// nothing when the name does not say.
    /// </summary>
    public static string? WhatItReaches(string patchFileName) =>
        ToPartOf(patchFileName) is string toPart ? VersionIn(toPart) : null;

    /// <summary>
    /// The version an installer installs, read off its name, or nothing
    /// when the name does not say.
    /// </summary>
    public static string? WhatTheInstallerInstalls(string? installerFileName) =>
        installerFileName is null
            ? null
            : VersionIn(System.IO.Path.GetFileNameWithoutExtension(installerFileName));

    /// <summary>
    /// Everything after <c>_to_</c> in a patch's name, or nothing when
    /// there is no such thing and the file is not a patch.
    /// </summary>
    private static string? ToPartOf(string fileName)
    {
        Match patch = PatchToVersion().Match(System.IO.Path.GetFileNameWithoutExtension(fileName));

        return patch.Success ? patch.Groups[1].Value : null;
    }

    /// <summary>The last parenthesised number, which GOG uses for its build.</summary>
    private static int? BuildIn(string part)
    {
        MatchCollection all = ABuild().Matches(part);

        return all.Count > 0
            ? int.Parse(all[^1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// The first dotted version among the underscore separated pieces.
    /// </summary>
    /// <remarks>
    /// Dotted on purpose: in <c>setup_doom_2_1.1</c> the 2 is the title and
    /// the 1.1 is the version, and a rule that took any number would read
    /// the title as a version and call a useful patch covered. A number
    /// with no dot in it proves nothing here.
    /// </remarks>
    private static string? VersionIn(string part)
    {
        Match version = ADottedVersion().Match(part);

        return version.Success ? version.Groups[1].Value : null;
    }

    /// <summary>
    /// The two versions compared piece by piece, or nothing when a piece is
    /// not comparable. Positive when the patch goes further.
    /// </summary>
    /// <remarks>
    /// A piece is digits with at most one trailing letter, which is how GOG
    /// writes <c>1.5.78.11833a</c>: numbers first, then the letter. A piece
    /// stranger than that refuses to compare rather than guessing.
    /// </remarks>
    private static int? Compared(string patch, string installer)
    {
        string[] ofThePatch = patch.Split('.');
        string[] ofTheInstaller = installer.Split('.');

        for (int at = 0; at < Math.Max(ofThePatch.Length, ofTheInstaller.Length); at++)
        {
            string a = at < ofThePatch.Length ? ofThePatch[at] : "0";
            string b = at < ofTheInstaller.Length ? ofTheInstaller[at] : "0";

            Match pieceA = APiece().Match(a);
            Match pieceB = APiece().Match(b);

            if (!pieceA.Success || !pieceB.Success)
            {
                return null;
            }

            int byNumber = long.Parse(pieceA.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                .CompareTo(long.Parse(pieceB.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

            if (byNumber != 0)
            {
                return byNumber;
            }

            int byLetter = string.CompareOrdinal(pieceA.Groups[2].Value, pieceB.Groups[2].Value);

            if (byLetter != 0)
            {
                return byLetter;
            }
        }

        return 0;
    }

    [GeneratedRegex(@"^patch_.+_to_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PatchToVersion();

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex ABuild();

    [GeneratedRegex(@"(?:^|_)(\d+(?:\.\d+)+[a-z]?)(?=_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ADottedVersion();

    [GeneratedRegex(@"^(\d+)([a-z]?)$", RegexOptions.IgnoreCase)]
    private static partial Regex APiece();
}
