namespace DiscWright.Core;

/// <summary>
/// Putting things into the folder that becomes a disc.
/// </summary>
/// <remarks>
/// Separate from <see cref="IFileSystem"/>, which only reads, because the two
/// are wanted in different places and by different code. Nothing that recognises
/// a game should be able to write a file.
/// </remarks>
public interface IStageWriter
{
    /// <summary>Makes a folder, and every folder above it, if they are not there.</summary>
    void MakeFolder(string path);

    /// <summary>Copies a whole file.</summary>
    void Copy(string source, string destination);

    /// <summary>
    /// Copies part of a file: <paramref name="bytes"/> of it, starting at
    /// <paramref name="offset"/>.
    /// </summary>
    /// <remarks>
    /// For a file too big for any one disc. The pieces are written separately
    /// rather than the file being split in place, because the source is the
    /// person's own download and this program does not touch it.
    /// </remarks>
    void CopyPart(string source, long offset, long bytes, string destination);

    /// <summary>
    /// Writes text that a modern program will read: UTF-8, and no byte order
    /// mark, because a BOM turns up as three characters in front of the first
    /// line of anything that is not expecting one.
    /// </summary>
    void WriteText(string path, string text);
}
