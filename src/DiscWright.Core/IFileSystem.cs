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
}
