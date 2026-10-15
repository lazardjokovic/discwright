namespace DiscWright.Core;

/// <summary>
/// What the discs actually in the drives have to say about the size somebody
/// is aiming at.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Medium"/> is a target and a <see cref="Recorder"/> is a tray
/// with something in it, and keeping those apart is deliberate. This is the one
/// place allowed to put them side by side, and it does two separate things with
/// the answer: it offers a size to start from, and it says so when the two have
/// drifted apart.
/// </para>
/// <para>
/// <strong>Neither of them overrules anybody.</strong> A disc is built for the
/// size that was asked for, because somebody planning a DVD with a CD still in
/// the drive from last time is doing something perfectly ordinary. All this
/// does is make sure they were told.
/// </para>
/// </remarks>
public static class TheDiscsThatAreIn
{
    /// <summary>
    /// The size to start somebody off at, read from a blank disc already in a
    /// drive, or nothing when the drives do not agree on an answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only ever from a <strong>blank</strong> disc. How much room is left on a
    /// disc with something on it is not how big that disc is, and a half full
    /// DVD would otherwise read as a CD.
    /// </para>
    /// <para>
    /// Two drives holding two different sizes gets nothing, on the same
    /// principle as everywhere else here: it refuses to guess between drives
    /// rather than picking the first one and being quietly wrong.
    /// </para>
    /// </remarks>
    public static Medium? TheSizeToStartFrom(IReadOnlyList<Recorder> drives)
    {
        Medium[] found = [.. drives.Select(TheSizeOf).OfType<Medium>().Distinct()];

        return found.Length == 1 ? found[0] : null;
    }

    /// <summary>
    /// What is worth saying about the discs in the drives, given the size that
    /// is chosen, or nothing when there is nothing worth saying.
    /// </summary>
    /// <remarks>
    /// Silent when a drive holds a disc of the size being aimed at, even if
    /// another drive holds something else entirely: the disc that matters is in
    /// the machine and the rest is not this sentence's business. Silent too
    /// when there is no disc anywhere, because somebody who has not put one in
    /// yet has done nothing wrong.
    /// </remarks>
    public static string? WhatIsWorthSaying(IReadOnlyList<Recorder> drives, Medium chosen)
    {
        Recorder[] holding = [.. drives.Where(drive => drive.WhyNot is null && drive.MediaType != 0)];

        if (holding.Length == 0 || holding.Any(drive => TheSizeOf(drive) == chosen))
        {
            return null;
        }

        // The disc that can actually be written to is the one worth naming. A
        // pressed film somebody left in the other drive is not the point.
        Recorder worth = holding.FirstOrDefault(drive => OpticalMedia.CanBeWritten(drive.MediaType))
            ?? holding[0];

        return $"The disc in {worth.Drive} is {AOrAn(worth.Holding)}, and this is being built for {chosen.Name}.";
    }

    /// <summary>
    /// Which size of disc a blank one in this drive is, or nothing when it
    /// cannot be told.
    /// </summary>
    /// <remarks>
    /// The kind of disc narrows it to a family, and how much it holds settles
    /// which one inside that family: nothing in IMAPI's numbers tells a BD-R 25
    /// from a BD-R 50, and a DVD+R is the same number whether it has one layer
    /// or two. The largest size that fits in what the disc really holds is the
    /// answer, because the figures here are usable bytes and sit under the raw
    /// capacity by design.
    /// </remarks>
    private static Medium? TheSizeOf(Recorder drive)
    {
        if (drive.WhyNot is not null || !OpticalMedia.CanBeWritten(drive.MediaType))
        {
            return null;
        }

        IReadOnlyList<Medium> family = InTheFamily(OpticalMedia.FamilyOf(drive.MediaType));

        if (family.Count == 0)
        {
            return null;
        }

        // A CD is a CD whether or not there is anything on it, because this
        // program aims at only one size of CD. Insisting on a blank one here
        // would leave somebody with a used CD-RW in the tray being planned for
        // a DVD, which is the whole fault this exists to fix.
        if (family.Count == 1)
        {
            return family[0];
        }

        // Past that it takes a blank one. Nothing in IMAPI's numbers tells a
        // BD-R 25 from a BD-R 50, or a DVD+R from a DVD+R DL, so the only way
        // to tell is how much the disc holds, and how much is LEFT on a disc
        // with something on it is not how much it holds.
        if (!drive.Blank)
        {
            return null;
        }

        long holds = drive.FreeSectors * OpticalMedia.SectorBytes;

        return family.LastOrDefault(size => size.Capacity <= holds);
    }

    /// <summary>
    /// The sizes this program aims at within one family of disc, smallest
    /// first.
    /// </summary>
    private static IReadOnlyList<Medium> InTheFamily(MediaFamily family) =>
        family switch
        {
            MediaFamily.Cd => [Media.Cd],
            MediaFamily.Dvd => [Media.Dvd5, Media.Dvd9],
            MediaFamily.BluRay => [Media.Bd25, Media.Bd50, Media.BdXl],

            // HD DVD lost, and this program has never offered to make one.
            _ => []
        };

    private static string AOrAn(string thing) =>
        thing.Length > 0 && "AEIOU".Contains(char.ToUpperInvariant(thing[0]))
            ? "an " + thing
            : "a " + thing;
}
