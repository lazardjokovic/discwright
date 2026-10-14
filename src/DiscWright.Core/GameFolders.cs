namespace DiscWright.Core;

/// <summary>
/// A path in, and what the folder turned out to be out. The one call the window
/// makes when somebody picks a folder.
/// </summary>
/// <remarks>
/// It exists so that reading the folder and recognising it are not the same
/// job. <see cref="GameFolderReader"/> stays pure and is tested with written
/// listings; this is the thin piece that fetches one, and the only part of
/// recognising a game that needs a disk.
/// </remarks>
public sealed class GameFolders(IFileSystem files, GameFolderReader reader)
{
    public Outcome<GameFolder> At(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Refusals.NoFolderWasChosen();
        }

        // Asked before listing, because "that folder is not there any more" and
        // "there is nothing in it" are different answers to different mistakes,
        // and a folder on a USB stick that has been pulled out gives the first.
        if (!files.FolderExists(path))
        {
            return Refusals.ThatFolderIsNotThere(path);
        }

        return reader.Read(files.List(path));
    }

    /// <summary>
    /// The usual arrangement, so the window does not have to know how to build
    /// one and a test can take the same thing apart.
    /// </summary>
    public static GameFolders InTheUsualOrder(IFileSystem files, IFileFacts facts) =>
        new(files, GameFolderReader.InTheUsualOrder(facts));
}
