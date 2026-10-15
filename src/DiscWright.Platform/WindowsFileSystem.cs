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
        // NOT trimmed of its trailing separator. A drive root trimmed to "E:"
        // is not the root of E: at all, it is "wherever the process last was on
        // drive E", and Windows hands back paths with no separator in them. The
        // old arithmetic then sliced one character too many off every name, so
        // a disc read back held "utorun.inf" and nothing matched anything. It
        // only ever showed up at a root, which is the one place a disc is read.
        string root = Path.GetFullPath(path);

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

            // Worked out rather than sliced, because the length of the root
            // is not the same as where the name starts.
            found.Add(new ListedFile(Path.GetRelativePath(root, file), bytes));
        }

        // Reported without its trailing separator, which is what the rest of
        // the program has always been given, except on a root, where there is
        // nothing to take off.
        return new FolderListing(Path.TrimEndingDirectorySeparator(root), found);
    }
}
