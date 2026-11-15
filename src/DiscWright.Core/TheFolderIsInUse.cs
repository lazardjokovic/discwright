namespace DiscWright.Core;

/// <summary>
/// A folder this program was about to empty is held open by something else.
/// </summary>
/// <remarks>
/// <para>
/// Thrown before anything in the folder is touched. xniwo, discwright#98,
/// 15 November: he built a disc, opened its Start Here from the disc folder,
/// and built again into the same place. The menu had that folder as its
/// working folder, the recursive delete took every file it could and then
/// failed on the folder itself, and the window said "Something went wrong"
/// about a build that had half destroyed the last one.
/// </para>
/// <para>
/// An IOException still, so anything that catches those goes on catching
/// this, and a sentence of its own where somebody is told.
/// </para>
/// </remarks>
public sealed class TheFolderIsInUse : System.IO.IOException
{
    public TheFolderIsInUse()
    {
        Folder = string.Empty;
    }

    public TheFolderIsInUse(string message)
        : base(message)
    {
        Folder = string.Empty;
    }

    public TheFolderIsInUse(string message, Exception innerException)
        : base(message, innerException)
    {
        Folder = string.Empty;
    }

    /// <summary>The folder, and what Windows said when it was asked to let go of it.</summary>
    public TheFolderIsInUse(string folder, string windowsSaid, Exception innerException)
        : base($"{folder} is in use: {windowsSaid}", innerException)
    {
        Folder = folder;
    }

    /// <summary>The folder that is held.</summary>
    public string Folder { get; }
}
