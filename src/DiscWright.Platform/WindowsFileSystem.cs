using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Reads folders off the real disk.
/// </summary>
public sealed class WindowsFileSystem : IFileSystem
{
    public bool FolderExists(string path) => Directory.Exists(path);

    public FolderListing List(string path)
    {
        string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        EnumerationOptions howToWalk = new()
        {
            RecurseSubdirectories = true,

            // Two decisions in one setting, because this is the only knob there
            // is. The default skips hidden and system files; those are INCLUDED
            // here, because a game folder has them and a disc missing them is a
            // disc missing part of the game, which is why v1 walks with -Force.
            //
            // Reparse points are skipped instead: junctions and directory
            // symlinks. A folder linking back to its own parent would otherwise
            // be walked until the path ran out of room, and one linking
            // somewhere else would quietly put that somewhere else on the disc.
            AttributesToSkip = FileAttributes.ReparsePoint,

            // A folder we cannot open is skipped rather than thrown over.
            // Somebody picking a folder with one unreadable corner should get
            // the rest of their game, not a dialog about an access denial they
            // can do nothing about.
            IgnoreInaccessible = true
        };

        List<ListedFile> found = [];

        foreach (string file in Directory.EnumerateFiles(root, "*", howToWalk))
        {
            long bytes;

            try
            {
                bytes = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // It was there a moment ago and is not now. Left out rather
                // than guessed at: a file of unknown size cannot be planned.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            found.Add(new ListedFile(file[(root.Length + 1)..], bytes));
        }

        return new FolderListing(root, found);
    }
}
