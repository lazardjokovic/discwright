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

    public void MakeFolder(string path) => Folders.Add(path);

    public void EmptyTheFolder(string path)
    {
        Emptied.Add(path);
        Folders.Add(path);
    }

    public void Copy(string source, string destination) => Copies.Add(new Copied(source, destination, 0, null));

    public void CopyPart(string source, long offset, long bytes, string destination) =>
        Copies.Add(new Copied(source, destination, offset, bytes));

    public List<string> Binaries { get; } = [];

    public void WriteBytes(string path, byte[] bytes) => Binaries.Add(path);

    public void WriteText(string path, string text) => Texts.Add(new Written(path, text));

    public void WriteAutorunText(string path, string text) => AutorunTexts.Add(new Written(path, text));

    /// <param name="Bytes">Null for a whole file, set for one piece of one.</param>
    public sealed record Copied(string From, string To, long Offset, long? Bytes);

    public sealed record Written(string Path, string Text);
}
