namespace DiscWright.Core;

/// <summary>
/// Turning what the drive says about speed into what is written on the disc.
/// </summary>
/// <remarks>
/// <para>
/// <strong>IMAPI measures speed in sectors per second, not in KB/s.</strong>
/// A speed in KB/s means a different multiple depending on what is loaded, and
/// reporting raw sectors to somebody deciding between 8x and 24x is not an
/// answer to the question they asked.
/// </para>
/// <para>
/// Getting the base wrong halves or doubles every speed in the program. v1 had
/// it wrong once: a drive offered 750 for a High Speed CD-RW and the
/// application called it 5x, which is not a speed any CD-RW is made in. It is
/// 10x, and the real burns said so.
/// </para>
/// </remarks>
public static class WriteSpeeds
{
    /// <summary>What 1x is, in sectors per second, for each kind of disc.</summary>
    private static double AtOneTimes(MediaFamily family) => family switch
    {
        MediaFamily.Cd => 75.0,
        MediaFamily.HdDvd => 4568.0,
        MediaFamily.BluRay => 2195.0,

        // Everything else is a DVD of some kind, including the code that means
        // a hard disk, which can never be written to from here anyway.
        _ => 680.0
    };

    /// <summary>What the drive's figure is, as a multiple somebody recognises.</summary>
    public static double AsAMultiple(int sectorsPerSecond, int mediaType) =>
        Math.Round(sectorsPerSecond / AtOneTimes(OpticalMedia.FamilyOf(mediaType)), 1);

    /// <summary>The multiple, written the way it is on the disc.</summary>
    public static string InWords(int sectorsPerSecond, int mediaType)
    {
        double multiple = AsAMultiple(sectorsPerSecond, mediaType);

        return multiple == Math.Floor(multiple)
            ? $"{multiple:0}x"
            : $"{multiple:0.0}x";
    }

    /// <summary>
    /// The speed to write at, out of the ones the drive offered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slower than the drive would choose on its own. <strong>Cheap media
    /// written at full speed is the usual way to make a coaster</strong>, and
    /// the minutes saved are worth far less than the disc.
    /// </para>
    /// <para>
    /// Sixteen for a CD and eight for everything else, which are v1's figures,
    /// arrived at by burning discs rather than by reading a specification.
    /// </para>
    /// </remarks>
    public static int? Choose(IReadOnlyList<int> offered, int mediaType)
    {
        if (offered.Count == 0)
        {
            return null;
        }

        double cap = OpticalMedia.FamilyOf(mediaType) == MediaFamily.Cd ? 16 : 8;

        int[] slowEnough =
        [
            .. offered.Where(each => AsAMultiple(each, mediaType) <= cap).OrderByDescending(each => each)
        ];

        // The fastest of the ones that are slow enough. If the drive offers
        // nothing that slow, the slowest it has: refusing to write because
        // every speed is fast would be a strange way to protect somebody.
        return slowEnough.Length > 0 ? slowEnough[0] : offered.Min();
    }
}
