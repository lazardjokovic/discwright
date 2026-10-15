namespace DiscWright.Core;

/// <summary>
/// Everything that has to be decided before an image is made, decided with
/// arithmetic rather than COM.
/// </summary>
/// <remarks>
/// All four of these were got wrong at least once in v1, and every one of them
/// is answerable with no disc, no drive and no machine.
/// </remarks>
public sealed record ImagePlan(
    string VolumeName,
    long Blocks,
    bool LegacyFileSystems,
    int ReportEvery,
    string? Worry)
{
    /// <summary>
    /// What the older file systems cannot take: one byte over 2 GiB.
    /// </summary>
    /// <remarks>
    /// <strong>Not 4 GiB.</strong> The format allows four; the writer stops at
    /// two. v1 carried a comment claiming the opposite and a real build died on
    /// it after copying 7.79 GB, which is the kind of mistake that only shows
    /// up at the end of a long job.
    /// </remarks>
    public const long BiggestFileTheOldWaysCanTake = 2147483648L;

    /// <summary>
    /// Blocks are 2 KiB, which is the one number in all of this that nobody
    /// chose.
    /// </summary>
    private const long BytesPerBlock = 2048;

    /// <summary>
    /// What the image would be, for this label and this much content.
    /// </summary>
    /// <param name="label">The disc's name.</param>
    /// <param name="payloadBytes">What the staged folder comes to.</param>
    /// <param name="biggestFileBytes">The largest single file in it.</param>
    /// <param name="wantsLegacyFileSystems">
    /// Whether the older file systems were asked for, so that a Windows 98 or a
    /// Mac OS 9 can read the disc.
    /// </param>
    /// <param name="discNumber">Which disc of the set this is, from one.</param>
    /// <param name="ofDiscs">How many discs the set has. One is not a set.</param>
    public static ImagePlan For(
        string label,
        long payloadBytes,
        long biggestFileBytes,
        bool wantsLegacyFileSystems,
        int discNumber = 1,
        int ofDiscs = 1)
    {
        bool legacy = wantsLegacyFileSystems;
        string? worry = null;

        // Asked for, but one file is too big for them. The image is still made,
        // without them, because a disc that reads everywhere except on a
        // twenty-five year old machine is worth far more than no disc.
        if (wantsLegacyFileSystems && biggestFileBytes > BiggestFileTheOldWaysCanTake)
        {
            legacy = false;
            worry =
                "The older file systems were left off: "
                + ByteSize.Describe(biggestFileBytes)
                + " is over the "
                + ByteSize.Describe(BiggestFileTheOldWaysCanTake)
                + " they can hold. The disc still reads on anything from Windows 2000 onwards.";
        }

        return new ImagePlan(
            NameFor(label, discNumber, ofDiscs),
            BlocksFor(payloadBytes),
            legacy,
            HowOftenToReport(payloadBytes),
            worry);
    }

    /// <summary>
    /// The volume name, which is not the disc's label.
    /// </summary>
    /// <remarks>
    /// Sixteen characters, letters, digits and underscores. Everything else
    /// becomes an underscore, so "Disc 1" and "Disc-1" arrive at the same
    /// volume name. That is fine: this is the word a drive shows in This PC,
    /// and nothing identifies a disc by it. The set manifest does that.
    ///
    /// A disc of a set ends in its number, and that number is reserved out of
    /// the sixteen before the name is cut rather than after.
    /// </remarks>
    private static string NameFor(string label, int discNumber, int ofDiscs)
    {
        // THE RULE v1 PAID FOR. A disc in a set reserves its number BEFORE the
        // name is cut, not after. Cut "A VERY LONG NAME INDEED D2" at sixteen
        // and the number comes off the end, so every disc of the set answers to
        // the same name again and This PC shows three discs it cannot tell
        // apart.
        string ending = ofDiscs <= 1 ? string.Empty : $"_D{discNumber}";
        int room = Math.Max(1, 16 - ending.Length);

        string allowed = new([
            .. label.Select(character =>
                char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_')
        ]);

        allowed = allowed.Trim('_');

        if (allowed.Length > room)
        {
            // Trimmed again AFTER cutting, not only before. The cut can land
            // where a space was folded and leave "THE_WITCHER_ENH_".
            allowed = allowed[..room].TrimEnd('_');
        }

        // Nothing but punctuation folds away to nothing, and a disc with no
        // name at all is one Windows will not mount.
        return (allowed.Length == 0 ? "DISC" : allowed) + ending;
    }

    /// <summary>
    /// How big to tell the writer the medium is.
    /// </summary>
    /// <remarks>
    /// Sized to what is actually going on, with a twentieth for the file system
    /// on top and room for its own structures. v1 used a fixed twelve and a half
    /// million blocks, which is about 25 GB, and that silently capped every
    /// build at a quarter of a BD-R XL.
    /// </remarks>
    private static long BlocksFor(long payloadBytes)
    {
        long blocks = (long)Math.Ceiling(payloadBytes * 1.05 / BytesPerBlock) + 65536;

        // The floor is v1's old fixed size, kept as a floor because nothing
        // smaller has ever been a problem and a tiny medium is one more thing
        // that could refuse a disc that would have fitted.
        return Math.Clamp(blocks, 12_500_000L, 2_147_483_000L);
    }

    /// <summary>
    /// How many blocks between one word about progress and the next.
    /// </summary>
    /// <remarks>
    /// About two hundred times over the whole write, whatever its size. A fixed
    /// interval fires a handful of times on a CD and tens of thousands of times
    /// on a BD-R XL, and each one costs more than it sounds.
    /// </remarks>
    private static int HowOftenToReport(long payloadBytes)
    {
        long blocks = (long)Math.Ceiling(payloadBytes / (double)BytesPerBlock);

        return (int)Math.Max(256, blocks / 200);
    }
}
