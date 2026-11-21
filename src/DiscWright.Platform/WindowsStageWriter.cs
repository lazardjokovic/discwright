using System.Runtime.InteropServices;
using System.Text;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Puts things into the folder that becomes a disc, on the real disk.
/// </summary>
public sealed class WindowsStageWriter : IStageWriter, IRemoveADiscFolder, IListenForAStop
{
    // What a copy watches while it runs. Written from the thread that starts
    // a build and read from the one that copies.
    private CancellationToken _stop;
    private readonly object _guard = new();

    public void StopWhen(CancellationToken stopped)
    {
        lock (_guard)
        {
            _stop = stopped;
        }
    }

    private CancellationToken WhatToWatch()
    {
        lock (_guard)
        {
            return _stop;
        }
    }

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
        // The rule itself is in Core, so the fake the tests use obeys the
        // same one. It did not once, and a preview that wrote nothing at
        // all passed eighteen tests.
        if (!WhatMayBeEmptied.Allows(path, Path.GetTempPath()))
        {
            throw new ArgumentException(
                $"Refusing to empty {path}: only a staging folder called \"disc\" or \"disc DN\", or a preview "
                    + "folder under the temporary folder, may be emptied.",
                nameof(path));
        }

        if (Directory.Exists(path))
        {
            // Moved aside first, and only then deleted. Windows refuses to
            // move a folder that anything holds open, the working folder of a
            // menu started from it included, and a refused move has changed
            // nothing. A recursive delete took every file it could and then
            // failed on the folder, leaving the last build half gone behind a
            // "Something went wrong" (xniwo, discwright#98, 15 November).
            string aside = path.TrimEnd('\\', '/') + ".going-" + Guid.NewGuid().ToString("N")[..8];

            try
            {
                Directory.Move(path, aside);
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                throw new TheFolderIsInUse(path, held.Message, held);
            }

            Directory.Delete(aside, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Deletes a disc folder outright, for a build that keeps only its image.
    /// </summary>
    /// <remarks>
    /// The same guard as emptying one and the same way of going about it:
    /// moved aside first, so a folder something has open is refused whole
    /// and not half deleted.
    /// </remarks>
    public void Remove(string discFolder)
    {
        if (!WhatMayBeEmptied.Allows(discFolder, Path.GetTempPath()))
        {
            throw new ArgumentException(
                $"Refusing to delete {discFolder}: only a staging folder called \"disc\" or \"disc DN\" may be deleted.",
                nameof(discFolder));
        }

        if (!Directory.Exists(discFolder))
        {
            return;
        }

        string aside = discFolder.TrimEnd('\\', '/') + ".going-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            Directory.Move(discFolder, aside);
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
            throw new TheFolderIsInUse(discFolder, held.Message, held);
        }

        Directory.Delete(aside, recursive: true);
    }

    /// <summary>
    /// Copies one file, and gives it up part way when the build is stopped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The copy is Windows' own, the one <c>File.Copy</c> makes, asked for
    /// directly so that it can be told to stop: it calls back as it goes,
    /// and the answer to the call is whether to carry on. Stopped, Windows
    /// deletes what it had written of the file itself.
    /// </para>
    /// <para>
    /// Nothing else about the copy is different. The file's times and
    /// attributes come across as they always did, an existing file is
    /// written over, and a file that is not there is the same exception.
    /// </para>
    /// </remarks>
    public void Copy(string source, string destination)
    {
        CancellationToken stop = WhatToWatch();
        stop.ThrowIfCancellationRequested();

        int cancel = 0;

        // Kept in a local for the length of the call, so the collector does
        // not take the delegate while Windows still holds a pointer to it.
        CopyProgressRoutine watching = (_, _, _, _, _, _, _, _, _) => stop.IsCancellationRequested ? Cancel : Continue;

        bool copied = CopyFileEx(AsWindowsWantsIt(source), AsWindowsWantsIt(destination), watching, IntPtr.Zero, ref cancel, 0);
        int why = copied ? 0 : Marshal.GetLastWin32Error();

        GC.KeepAlive(watching);

        if (copied)
        {
            return;
        }

        if (why == RequestAborted || stop.IsCancellationRequested)
        {
            throw new OperationCanceledException(stop);
        }

        throw why switch
        {
            FileNotFound => new FileNotFoundException($"Could not find file '{source}'.", source),
            PathNotFound => new DirectoryNotFoundException($"Could not find a part of the path '{source}' or '{destination}'."),
            AccessDenied => new UnauthorizedAccessException($"Access to the path '{destination}' is denied."),
            _ => new IOException(
                new System.ComponentModel.Win32Exception(why).Message + $" : '{destination}'",
                unchecked((int)0x80070000) | why),
        };
    }

    private const uint Continue = 0;
    private const uint Cancel = 1;
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;
    private const int AccessDenied = 5;
    private const int RequestAborted = 1235;

    /// <summary>
    /// A path Windows will take whatever its length. The framework does this
    /// for every path it is handed; a call made directly has to do it itself,
    /// or a file 260 characters deep copies through <c>File.Copy</c> and not
    /// through here.
    /// </summary>
    private static string AsWindowsWantsIt(string path)
    {
        string full = Path.GetFullPath(path);

        if (full.Length < 248 || full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return full;
        }

        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
    }

    private delegate uint CopyProgressRoutine(
        long total, long done, long streamTotal, long streamDone, uint stream, uint reason, IntPtr from, IntPtr to, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CopyFileExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyFileEx(
        string existing, string made, CopyProgressRoutine? watching, IntPtr data, ref int cancel, uint flags);

    public void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder, recursive: false);
            }
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
            // Then it stays. An empty folder left behind is nothing to
            // stop anybody for.
        }
    }

    public void RemoveTheImage(string imagePath)
    {
        if (!string.Equals(Path.GetExtension(imagePath), ".iso", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Refusing to delete {imagePath}: only a disc image, a file whose name ends in .iso, may be deleted.",
                nameof(imagePath));
        }

        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }
    }

    public void CopyPart(string source, long offset, long bytes, string destination)
    {
        using FileStream from = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, Bite, FileOptions.SequentialScan);
        using FileStream to = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, Bite);

        from.Seek(offset, SeekOrigin.Begin);

        byte[] buffer = new byte[Bite];
        long left = bytes;
        CancellationToken stop = WhatToWatch();

        while (left > 0)
        {
            // Between one bite and the next, so a stop is heard within a
            // megabyte and not at the end of a piece that may be gigabytes.
            // What was written of the piece goes with its disc folder.
            stop.ThrowIfCancellationRequested();

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
