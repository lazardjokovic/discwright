using System.Text;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Puts things into the folder that becomes a disc, on the real disk.
/// </summary>
public sealed class WindowsStageWriter : IStageWriter
{
    /// <summary>
    /// The same bite the hasher reads in, for the same reason: larger stopped
    /// helping and starts costing memory on a machine doing something else.
    /// </summary>
    private const int Bite = 1024 * 1024;

    /// <summary>
    /// UTF-8 with no byte order mark. A mark turns up as three characters in
    /// front of the first line of anything not expecting one, and the checksum
    /// list is read by tools that are not.
    /// </summary>
    private static readonly UTF8Encoding Utf8WithoutAMark = new(false);

    public void MakeFolder(string path) => Directory.CreateDirectory(path);

    /// <summary>
    /// The only destructive thing this program does, so it is deliberately
    /// narrow: it refuses anything that is not a staging folder this program
    /// named.
    /// </summary>
    /// <remarks>
    /// A guard rather than trust. The path comes from inside this program
    /// today, and the day somebody wires an output folder straight through to
    /// it by mistake, the mistake is a thrown exception rather than somebody's
    /// documents.
    /// </remarks>
    public void EmptyTheFolder(string path)
    {
        string name = new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;

        if (!name.StartsWith("disc", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Refusing to empty {path}: only a staging folder called \"disc\" or \"disc DN\" may be emptied.",
                nameof(path));
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    public void Copy(string source, string destination) =>
        File.Copy(source, destination, overwrite: true);

    public void CopyPart(string source, long offset, long bytes, string destination)
    {
        using FileStream from = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, Bite, FileOptions.SequentialScan);
        using FileStream to = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, Bite);

        from.Seek(offset, SeekOrigin.Begin);

        byte[] buffer = new byte[Bite];
        long left = bytes;

        while (left > 0)
        {
            // Trimmed to what is left, not to the buffer. Reading a whole
            // megabyte when 300 KB was asked for is how a part that copied
            // perfectly comes back as damaged.
            int want = (int)Math.Min(buffer.Length, left);
            int read = from.Read(buffer, 0, want);

            if (read <= 0)
            {
                throw new IOException($"{source} ended {left} bytes before the part did.");
            }

            to.Write(buffer, 0, read);
            left -= read;
        }
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void WriteText(string path, string text) =>
        File.WriteAllText(path, text, Utf8WithoutAMark);

    /// <summary>
    /// The one file written in the system ANSI codepage, because AutoRun reads
    /// it that way and has no Unicode mode at all.
    /// </summary>
    public void WriteAutorunText(string path, string text) =>
        File.WriteAllText(path, text, WindowsAutorunEncoding.ForAutorunFiles);
}
