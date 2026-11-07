namespace DiscWright.ViewModels;

/// <summary>
/// What a drop onto the list of games means, as folders.
/// </summary>
/// <remarks>
/// <para>
/// Asked for in discwright#98 as dragging GOG exes into the list. A game
/// here is a folder, so a dropped file means the folder it is in, which is
/// also the right reading of the likeliest drag of all: the setup exe
/// itself, pulled out of a Downloads window. A dropped folder means itself.
/// </para>
/// <para>
/// Pure, and in this project rather than behind the window, so the one
/// piece of judgement in a drop can be tested without one. Everything after
/// this list is the same road the Add a game button takes.
/// </para>
/// </remarks>
public static class WhatWasDropped
{
    /// <summary>
    /// The folders a drop names, in the order dropped, each once. Two files
    /// out of the same folder are one folder, because they are one game and
    /// adding it twice is the mistake the list already refuses politely.
    /// </summary>
    /// <param name="dropped">Whatever Windows handed over, files and folders mixed.</param>
    /// <param name="isFolder">Whether a path is a folder, answered by the real disk.</param>
    public static IReadOnlyList<string> AsFolders(IEnumerable<string> dropped, Func<string, bool> isFolder)
    {
        List<string> folders = [];
        HashSet<string> once = new(StringComparer.OrdinalIgnoreCase);

        foreach (string each in dropped)
        {
            string folder = isFolder(each)
                ? each
                : Path.GetDirectoryName(each) ?? string.Empty;

            // A file with no folder part is a name like "setup.exe" alone,
            // which points nowhere a game could be.
            if (folder.Length > 0 && once.Add(folder))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }
}
