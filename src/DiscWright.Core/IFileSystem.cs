namespace DiscWright.Core;

/// <summary>
/// Reading folders, which is the one thing recognising a game cannot do for
/// itself.
/// </summary>
/// <remarks>
/// Deliberately two methods and nothing else. Every question this program asks
/// about a folder is answered from one listing, so the port does not grow a
/// method per question, and the test for anything downstream of it is a written
/// list rather than a fake filesystem.
/// </remarks>
public interface IFileSystem
{
    /// <summary>Whether there is a folder at this path at all.</summary>
    bool FolderExists(string path);

    /// <summary>
    /// Everything in the folder, recursively, with each file's path relative to
    /// it. Hidden and system files included, because a game folder has them and
    /// a disc missing them is a disc missing part of the game.
    /// </summary>
    FolderListing List(string path);

    /// <summary>
    /// The files directly in the folder, and nothing from the folders under it.
    /// </summary>
    /// <remarks>
    /// For a question about the folder itself, which is all the window asks
    /// when it looks for images: they are written straight into the folder
    /// chosen. Asking <see cref="List"/> instead walked everything below it,
    /// on the window's thread, every time the box changed, so typing
    /// <c>C:\</c> into it walked the whole drive and thirteen characters took
    /// nearly two minutes.
    ///
    /// Defaulted from the full listing so the fakes in the tests need nothing
    /// new; the real disk answers it without going down a level.
    /// </remarks>
    FolderListing ListHere(string path)
    {
        FolderListing all = List(path);

        return all with
        {
            Files =
            [
                .. all.Files.Where(file =>
                    file.RelativePath.IndexOfAny(['/', (char)92]) < 0)
            ]
        };
    }

    /// <summary>
    /// A text file's contents, or nothing when there is no such file.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than required, because almost nothing here reads a
    /// file: the pipeline is built out of listing folders and handing paths
    /// to things that copy them. The one caller that does need it is checking
    /// a disc against the checksum list the disc carries.
    ///
    /// A default also means the fakes that stand in for this in tests do not
    /// all have to grow a method none of them use.
    /// </remarks>
    string? ReadText(string path) => null;

    /// <summary>Whether there is a file at that path.</summary>
    /// <remarks>
    /// Asked of every file somebody chose for a disc before the disc is built
    /// or its menu previewed, because files get moved after they are chosen.
    /// True by default, which is every fake that does not care.
    /// </remarks>
    bool FileExists(string path) => true;
}
