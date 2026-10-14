namespace DiscWright.Core;

/// <summary>
/// Whatever turns a staged folder into one file that is the disc.
/// </summary>
/// <remarks>
/// <para>
/// An interface in Core so that the window can ask for an image without knowing
/// that the answer is IMAPI, and so that everything above it can be tested with
/// no COM at all.
/// </para>
/// <para>
/// Making an image and burning one are kept apart on purpose, although both are
/// IMAPI. They fail differently, they are wanted separately, and this one has to
/// work on a machine with no drive in it at all.
/// </para>
/// </remarks>
public interface IMakeImages
{
    Task<Outcome<MadeImage>> Make(
        string stagedFolder,
        string imagePath,
        string label,
        bool wantsLegacyFileSystems,
        IProgress<ImageProgress>? progress,
        CancellationToken ct);
}

/// <summary>One image, made.</summary>
/// <param name="Path">Where it is.</param>
/// <param name="Bytes">How big it came out.</param>
/// <param name="Worry">
/// Something true and worth saying that did not stop it, or null.
/// </param>
public sealed record MadeImage(string Path, long Bytes, string? Worry);

/// <summary>How far through writing the image.</summary>
/// <param name="Blocks">Blocks written.</param>
/// <param name="OfBlocks">Blocks in all.</param>
public sealed record ImageProgress(long Blocks, long OfBlocks)
{
    /// <summary>From nothing to one.</summary>
    public double HowFar => OfBlocks == 0 ? 0 : (double)Blocks / OfBlocks;

    /// <remarks>
    /// In percent rather than in blocks, because nobody has any feeling for how
    /// many blocks a disc is.
    /// </remarks>
    public override string ToString() => $"writing the image, {HowFar:P0}";
}
