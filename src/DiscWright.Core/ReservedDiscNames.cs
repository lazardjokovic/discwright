namespace DiscWright.Core;

/// <summary>
/// The names a disc keeps for itself.
/// </summary>
/// <remarks>
/// <para>
/// Loose extras land at the root of the disc, which is where everything the
/// disc needs in order to be a disc already lives. A file called
/// <c>autorun.inf</c> among somebody's extras would replace the one that makes
/// the disc work, and nothing would say so until the disc was in a drive.
/// </para>
/// <para>
/// Refused by name rather than renamed around. Quietly putting their file
/// somewhere else is worse: they would burn a disc believing it holds a file it
/// does not hold under a name they could find it by.
/// </para>
/// </remarks>
public static class ReservedDiscNames
{
    /// <summary>
    /// Everything at a disc's root that this program writes or expects.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "autorun.inf",
        ".xdg-volume-info",
        "disc.ico",
        "disc.png",
        ChecksumList.FileName,
        SetManifest.FileName,
        "AUTORUN",
        "ARTWORK",
        "Extras",
        "Games",
        "Add-ons",
        "Start Here.hta",
        "discproject.json"
    ];

    /// <summary>Whether a name at the root of a disc belongs to the disc.</summary>
    /// <remarks>
    /// Anything beginning <c>setup_</c> counts too, not because this program
    /// writes one, but because that is how a GOG installer is recognised and a
    /// loose file by that name at the root would be read back as a game the
    /// next time the disc was opened.
    /// </remarks>
    /// <summary>
    /// Whether anything in this list of paths would be written over by the
    /// disc itself, or would write over part of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the first part of each path matters: a file called
    /// <c>autorun.inf</c> at the root collides, and one inside a folder of the
    /// game's own does not.
    /// </para>
    /// <para>
    /// <strong>This deliberately does not use <see cref="Claims"/>.</strong>
    /// That one also refuses anything beginning <c>setup_</c>, which is right
    /// for a loose extra somebody dropped on the disc and catastrophic here:
    /// every GOG installer ever made begins <c>setup_</c>, so every game would
    /// be said to collide with the disc.
    /// </para>
    /// </remarks>
    public static bool WouldBeWrittenOver(IEnumerable<string> relativePaths) =>
        relativePaths.Select(FirstPartOf).Any(
            part => All.Any(reserved => string.Equals(reserved, part, StringComparison.OrdinalIgnoreCase)));

    private static string FirstPartOf(string path)
    {
        int slash = path.IndexOfAny(['/', (char)92]);

        return slash < 0 ? path : path[..slash];
    }

    public static bool Claims(string name) =>
        name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase)
        || All.Any(reserved => string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase));
}
