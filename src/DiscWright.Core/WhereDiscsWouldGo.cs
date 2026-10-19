namespace DiscWright.Core;

/// <summary>
/// Where to build a disc when nobody has said where.
/// </summary>
/// <remarks>
/// <para>
/// v1 filled this in the moment a game was added, from the game's own name,
/// and so a refusal about having nowhere to put it never happened to anybody.
/// The rewrite left the box empty instead, so every session began with a red
/// line about a folder nobody had been asked for yet, and it was reported
/// three times as a stale message about the disc.
/// </para>
/// <para>
/// <strong>Not the Desktop, if the Desktop is in a cloud folder.</strong> v1
/// used it and said so, and v1 was right on a machine where the Desktop is a
/// folder on the disc. Redirected into OneDrive, which is where this was
/// found, it would hand a sync client several gigabytes of ISO nobody asked
/// it to upload. The home folder is never redirected, so that is the answer
/// when the Desktop has gone to the cloud.
/// </para>
/// <para>
/// Handed the paths rather than asking for them, because this has to be the
/// same answer in a test as on the machine it was written on.
/// </para>
/// </remarks>
/// <param name="Desktop">The Desktop, as the shell gives it, or nothing.</param>
/// <param name="Home">The home folder, which is never redirected.</param>
/// <param name="Synced">Folders a sync client watches, usually just OneDrive.</param>
public sealed record WhereDiscsWouldGo(string Desktop, string Home, IReadOnlyList<string> Synced)
{
    /// <summary>
    /// The folder a disc for this game would be built in, or nothing when
    /// there is nowhere sensible to suggest.
    /// </summary>
    public string? ForAGameCalled(string name)
    {
        if (Named(name) is not string called)
        {
            return null;
        }

        string? where = Desktop.Length > 0 && !IsSynced(Desktop) ? Desktop
            : Home.Length > 0 ? Home
            : null;

        return where is null ? null : System.IO.Path.Combine(where, called + " Disc");
    }

    private bool IsSynced(string path) =>
        Synced.Any(root => root.Length > 0
            && path.StartsWith(root, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The game's name, with everything Windows will not take in a folder
    /// name turned into a space and the spaces tidied up.
    /// </summary>
    /// <remarks>
    /// Dropped rather than replaced with an underscore, because "Baldur_s
    /// Gate" is somebody's folder forever and "Baldurs Gate" is a name.
    /// </remarks>
    private static string? Named(string name)
    {
        char[] cleaned =
        [
            .. name.Select(each =>
                System.IO.Path.GetInvalidFileNameChars().Contains(each) ? ' ' : each)
        ];

        string tidied = string.Join(" ", new string(cleaned).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // A game called ":" leaves nothing at all, and "  Disc" is not a
        // folder anybody would recognise as theirs.
        return tidied.Length == 0 ? null : tidied;
    }
}
