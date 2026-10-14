namespace DiscWright.Core;

/// <summary>
/// The folders inside this one that are GOG downloads in their own right.
/// </summary>
/// <remarks>
/// <para>
/// Pointing at the folder that <em>holds</em> your downloads, rather than at one
/// game inside it, is the likeliest way to arrive here by mistake. A folder like
/// <c>C:\GOG Games</c> has no installer of its own, so it is not a download, and
/// taking it whole puts every game somebody owns onto one disc entry named after
/// the folder.
/// </para>
/// <para>
/// <strong>Nothing refuses it.</strong> A real game folder can have an installer
/// buried somewhere too, and refusing that would be worse than taking it whole.
/// This only says what is in there, and the window asks.
/// </para>
/// </remarks>
public static class GamesInside
{
    /// <summary>
    /// How deep to look. Three is enough for a folder of folders of games,
    /// which is how downloads actually get filed, and shallow enough that
    /// pointing at a drive does not walk it.
    /// </summary>
    private const int HowDeep = 3;

    /// <summary>
    /// The folders beneath this one that hold an installer of their own, as
    /// paths relative to it, nearest first.
    /// </summary>
    /// <remarks>
    /// The folder itself is never one of them. If it holds an installer it is a
    /// download, not a folder of them, and the recognisers have it already.
    /// </remarks>
    public static IReadOnlyList<string> Of(FolderListing folder)
    {
        HashSet<string> holders = new(StringComparer.OrdinalIgnoreCase);

        foreach (ListedFile file in folder.Files)
        {
            if (!IsAnInstaller(file))
            {
                continue;
            }

            string within = FolderPartOf(file.RelativePath);

            // The folder itself, so this is a download rather than a folder of
            // them, and there is nothing here to ask about.
            if (within.Length == 0)
            {
                return [];
            }

            if (Depth(within) <= HowDeep)
            {
                holders.Add(within);
            }
        }

        // A folder inside one that already counts is part of that game, not a
        // game beside it: a download with its own subfolder holding a second
        // installer is still one download.
        return
        [
            .. holders
                .Where(folder => !holders.Any(outer => IsInside(folder, outer)))
                .OrderBy(Depth)
                .ThenBy(each => each, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static bool IsAnInstaller(ListedFile file) =>
        file.Name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase)
        && string.Equals(file.Extension, ".exe", StringComparison.OrdinalIgnoreCase);

    private static string FolderPartOf(string relativePath)
    {
        int slash = relativePath.LastIndexOfAny(['/', (char)92]);

        return slash < 0 ? string.Empty : relativePath[..slash];
    }

    /// <remarks>
    /// Counted rather than split, because the overload a three argument Split
    /// picks is not the one it looks like, and this quietly said four folders
    /// down was two.
    /// </remarks>
    private static int Depth(string relativeFolder) =>
        1 + relativeFolder.Count(character => character == '/' || character == (char)92);

    private static bool IsInside(string inner, string outer) =>
        inner.Length > outer.Length
        && inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase)
        && (inner[outer.Length] == '/' || inner[outer.Length] == (char)92);
}
