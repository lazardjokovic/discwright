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

    /// <summary>
    /// Makes a folder, and makes sure there is nothing in it.
    /// </summary>
    /// <remarks>
    /// The only destructive thing this program does to a folder, and it exists
    /// for one reason: building the same disc twice. Left over, a file from the
    /// first build is hashed into the second build's checksum list and burned
    /// onto the disc, where it is now correct and permanent. Only ever called on
    /// a staging folder this program made and named.
    /// </remarks>
    void EmptyTheFolder(string path);

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

    /// <summary>
    /// Writes text that AutoRun will read, which is a different thing: the
    /// system ANSI codepage, and no byte order mark, because AutoRun does not
    /// understand one.
    /// </summary>
    /// <remarks>
    /// Named for what it is for rather than for its encoding, so nothing in
    /// Core has to know what a codepage is. What it means on this machine is
    /// the port's business, and it is not what .NET calls the default.
    /// </remarks>
    void WriteAutorunText(string path, string text);
}
