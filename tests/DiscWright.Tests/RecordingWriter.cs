using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Writes nothing and remembers everything it was asked to write.
/// </summary>
/// <remarks>
/// Hand written rather than a mock, because fifty tests read it and a mock
/// built inline in each one is five lines of noise per test and unreadable at
/// the hundredth. It also means every staging test runs without a disk, which
/// is what keeps them instant on a machine where loading our own assemblies is
/// the slow part.
/// </remarks>
public sealed class RecordingWriter : IStageWriter
{
    public List<string> Folders { get; } = [];

    /// <summary>Folders it was asked to clear out before anything went in.</summary>
    public List<string> Emptied { get; } = [];

    public List<Copied> Copies { get; } = [];

    public List<Written> Texts { get; } = [];

    /// <summary>
    /// Kept apart from <see cref="Texts"/> on purpose. The two are written in
    /// different encodings, and a test that could not tell them apart would not
    /// notice autorun.inf being written the way everything else is.
    /// </summary>
    public List<Written> AutorunTexts { get; } = [];

    /// <summary>Everything it was asked to create, in the order asked.</summary>
    public IEnumerable<string> Everything =>
        Copies.Select(copy => copy.To).Concat(Texts.Select(text => text.Path));

    public void MakeFolder(string path)
    {
        Folders.Add(path);
        Made(path);
    }

    /// <summary>
    /// The stage folder as a build would leave it before any step runs, and
    /// the path back.
    /// </summary>
    /// <remarks>
    /// <see cref="DiscWright.Staging.DiscBuilder"/> empties each disc's folder
    /// before its first step, so by the time a step runs the folder is there.
    /// A test that drives one step on its own has to do the same or it is
    /// testing a situation the program never reaches.
    /// </remarks>
    public string AStageAt(string path)
    {
        MakeFolder(path);

        return path;
    }

    /// <summary>
    /// Whether a folder has been made, the way the disk would know it.
    /// </summary>
    /// <remarks>
    /// For the pictures, which go to the same places through a different port
    /// and would otherwise be the one thing in a staging test that can write
    /// into a folder nobody made.
    /// </remarks>
    public bool ThereIsAFolderAt(string path) => Tidy(path) is string at && _made.Contains(at);

    /// <summary>
    /// Every folder that exists, counting the parents of the ones asked for.
    /// </summary>
    /// <remarks>
    /// <c>Directory.CreateDirectory</c> makes the whole chain, so a fake that
    /// only remembered the path it was handed would refuse a write into a
    /// folder the real writer had just created on the way.
    /// </remarks>
    private readonly HashSet<string> _made = new(StringComparer.OrdinalIgnoreCase);

    private void Made(string path)
    {
        for (string? at = Tidy(path); at is not null; at = Tidy(System.IO.Path.GetDirectoryName(at)))
        {
            if (!_made.Add(at))
            {
                return;
            }
        }
    }

    private static string? Tidy(string? path) =>
        string.IsNullOrEmpty(path) ? null : path.TrimEnd(System.IO.Path.DirectorySeparatorChar);

    /// <summary>
    /// Refuses to write where nothing has been made, exactly as the disk does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fake used to accept a write into a folder that had never been
    /// created, where <c>File.WriteAllText</c> throws
    /// <c>DirectoryNotFoundException</c>. That is not a detail: previewing a
    /// set of two discs wrote the second disc's icon into a folder nothing had
    /// made, which threw in front of somebody using the program while a test
    /// covering that exact preview was green.
    /// </para>
    /// <para>
    /// It is the same mistake as the one above, made twice in one file: a fake
    /// kinder than the thing it stands in for proves nothing and reads as
    /// proof.
    /// </para>
    /// </remarks>
    private void MustBeSomewhere(string path)
    {
        string? folder = Tidy(System.IO.Path.GetDirectoryName(path));

        if (folder is not null && !_made.Contains(folder))
        {
            throw new DirectoryNotFoundException(
                $"Could not find a part of the path '{path}'. Nothing made {folder}, "
                + "and the real writer would throw here.");
        }
    }

    /// <summary>
    /// Refuses exactly what the real writer refuses.
    /// </summary>
    /// <remarks>
    /// This used to empty whatever it was handed, which made it kinder than
    /// the thing it stands in for. A preview folder named after the project
    /// was refused outright by the real writer and the preview wrote nothing
    /// at all, while eighteen tests went green through this.
    /// </remarks>
    public void EmptyTheFolder(string path)
    {
        if (!WhatMayBeEmptied.Allows(path, System.IO.Path.GetTempPath()))
        {
            throw new ArgumentException(
                $"Refusing to empty {path}: the real writer would refuse this too.",
                nameof(path));
        }

        Emptied.Add(path);
        Folders.Add(path);
        Made(path);
    }

    public void Copy(string source, string destination)
    {
        MustBeSomewhere(destination);
        Copies.Add(new Copied(source, destination, 0, null));
    }

    public void CopyPart(string source, long offset, long bytes, string destination)
    {
        MustBeSomewhere(destination);
        Copies.Add(new Copied(source, destination, offset, bytes));
    }

    public List<string> Binaries { get; } = [];

    public void WriteBytes(string path, byte[] bytes)
    {
        MustBeSomewhere(path);
        Binaries.Add(path);
    }

    public void WriteText(string path, string text)
    {
        MustBeSomewhere(path);
        Texts.Add(new Written(path, text));
    }

    public void WriteAutorunText(string path, string text)
    {
        MustBeSomewhere(path);
        AutorunTexts.Add(new Written(path, text));
    }

    /// <param name="Bytes">Null for a whole file, set for one piece of one.</param>
    public sealed record Copied(string From, string To, long Offset, long? Bytes);

    public sealed record Written(string Path, string Text);
}
