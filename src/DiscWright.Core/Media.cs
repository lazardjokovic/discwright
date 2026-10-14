namespace DiscWright.Core;

/// <summary>
/// Every size of disc the app will aim at, as data rather than as a branch.
/// Adding one is a row, and nothing anywhere else changes.
/// </summary>
public static class Media
{
    // Usable capacity, which is well under the number printed on the box: a
    // DVD5 sold as "4.7 GB" holds 4.37 GiB once the filesystem has taken its
    // share, and planning against the box number produces a disc that fails at
    // 99 per cent.
    //
    // These are the same figures the shipped PowerShell app plans against, so a
    // disc laid out by either version comes out the same. Written as the GiB
    // number rather than the byte count so the lineage stays obvious and so
    // nobody has to check arithmetic to see what a row means.
    public static readonly Medium Cd = new("CD", "CD-R 700 MB", Gib(0.68));
    public static readonly Medium Dvd5 = new("DVD5", "DVD5 4.7 GB", Gib(4.37));
    public static readonly Medium Dvd9 = new("DVD9", "DVD9 8.5 GB (dual layer)", Gib(7.95));
    public static readonly Medium Bd25 = new("BD25", "BD-R 25 GB", Gib(23.3));
    public static readonly Medium Bd50 = new("BD50", "BD-R DL 50 GB (dual layer)", Gib(46.6));
    public static readonly Medium BdXl = new("BDXL", "BD-R XL 100 GB", Gib(93.0));

    /// <summary>
    /// Smallest first, which is the order the window offers them in and the
    /// order a recommendation walks: the smallest disc that fits is the answer.
    /// </summary>
    public static IReadOnlyList<Medium> All { get; } = [Cd, Dvd5, Dvd9, Bd25, Bd50, BdXl];

    /// <summary>
    /// The medium with this key, or null when there is no such thing.
    /// </summary>
    /// <remarks>
    /// Case is ignored because keys come back out of saved project files that
    /// people and older versions have written. Null rather than a default,
    /// because guessing a disc size on somebody's behalf is how a build fails
    /// at the end instead of refusing at the start.
    /// </remarks>
    public static Medium? ByKey(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(medium => string.Equals(medium.Key, key, StringComparison.OrdinalIgnoreCase));

    private static long Gib(double gibibytes) => (long)(gibibytes * 1024 * 1024 * 1024);
}
