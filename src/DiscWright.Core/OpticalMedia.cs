namespace DiscWright.Core;

/// <summary>
/// What a drive means when it says the disc in it is a number.
/// </summary>
/// <remarks>
/// These are IMAPI's physical media types. The numbers come back from the drive
/// and mean nothing on their own, and a report saying "media type 9" is a
/// report nobody can act on.
/// </remarks>
public static class OpticalMedia
{
    /// <summary>Optical media is addressed in sectors of 2 KiB and nothing else.</summary>
    public const int SectorBytes = 2048;

    private static readonly Dictionary<int, (string Name, bool Writable, bool Rewritable, MediaFamily Family)> Known =
        new()
        {
            [0] = ("no disc", false, false, MediaFamily.Nothing),
            [1] = ("CD-ROM, pressed", false, false, MediaFamily.Cd),
            [2] = ("CD-R", true, false, MediaFamily.Cd),
            [3] = ("CD-RW", true, true, MediaFamily.Cd),
            [4] = ("DVD-ROM, pressed", false, false, MediaFamily.Dvd),
            [5] = ("DVD-RAM", true, true, MediaFamily.Dvd),
            [6] = ("DVD+R", true, false, MediaFamily.Dvd),
            [7] = ("DVD+RW", true, true, MediaFamily.Dvd),
            [8] = ("DVD+R DL", true, false, MediaFamily.Dvd),
            [9] = ("DVD-R", true, false, MediaFamily.Dvd),
            [10] = ("DVD-RW", true, true, MediaFamily.Dvd),
            [11] = ("DVD-R DL", true, false, MediaFamily.Dvd),
            [12] = ("hard disk", false, false, MediaFamily.Dvd),
            [13] = ("DVD+RW DL", true, true, MediaFamily.Dvd),
            [14] = ("HD DVD-ROM", false, false, MediaFamily.HdDvd),
            [15] = ("HD DVD-R", true, false, MediaFamily.HdDvd),
            [16] = ("HD DVD-RAM", true, true, MediaFamily.HdDvd),
            [17] = ("BD-ROM, pressed", false, false, MediaFamily.BluRay),
            [18] = ("BD-R", true, false, MediaFamily.BluRay),
            [19] = ("BD-RE", true, true, MediaFamily.BluRay)
        };

    /// <summary>What to call it, in the words on the box it came in.</summary>
    public static string NameOf(int mediaType) =>
        Known.TryGetValue(mediaType, out var known) ? known.Name : $"an unfamiliar disc, type {mediaType}";

    /// <summary>Whether anything can be written to it at all.</summary>
    public static bool CanBeWritten(int mediaType) =>
        Known.TryGetValue(mediaType, out var known) && known.Writable;

    /// <summary>
    /// Whether it can be erased and used again.
    /// </summary>
    /// <remarks>
    /// Not the same question as whether it can be written. A CD-R is written
    /// once and never again; a CD-RW can be written, erased and written again.
    /// v1 told everybody their disc was not rewritable, including people
    /// holding a CD-RW, which is the sort of thing that makes somebody doubt
    /// the rest of the dialog.
    /// </remarks>
    public static bool CanBeErased(int mediaType) =>
        Known.TryGetValue(mediaType, out var known) && known.Rewritable;

    /// <summary>Which family it belongs to, which is what sets 1x.</summary>
    public static MediaFamily FamilyOf(int mediaType) =>
        Known.TryGetValue(mediaType, out var known) ? known.Family : MediaFamily.Dvd;

    /// <summary>
    /// Whether erasing this disc would make it usable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things have to be true and they are three different questions. The
    /// disc has to be writable at all; it has to be the rewritable kind, which
    /// a CD-R is not however many times somebody wishes it were; and there has
    /// to be something on it, because erasing a blank is a minute spent to
    /// achieve nothing.
    /// </para>
    /// <para>
    /// Answering is separate from doing. <strong>Nothing erases on its own
    /// initiative</strong>: this says an erase would help, the person holding
    /// the disc decides, and only then does anything happen to it.
    /// </para>
    /// </remarks>
    public static bool ErasingWouldHelp(int mediaType, bool blank) =>
        CanBeWritten(mediaType) && CanBeErased(mediaType) && !blank;

    /// <summary>
    /// Whether an image of this size fits on what is left of the disc.
    /// </summary>
    public static bool Fits(long imageBytes, long freeSectors) =>
        SectorsFor(imageBytes) <= freeSectors;

    /// <summary>How many sectors a file of this size takes up.</summary>
    public static long SectorsFor(long bytes) => (long)Math.Ceiling(bytes / (double)SectorBytes);
}

/// <summary>Which kind of disc, which is what decides what 1x means.</summary>
public enum MediaFamily
{
    Nothing,
    Cd,
    Dvd,
    HdDvd,
    BluRay
}
