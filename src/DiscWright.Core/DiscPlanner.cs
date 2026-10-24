using System.Globalization;

namespace DiscWright.Core;

/// <summary>
/// Works out which file goes on which disc, and where a cut falls.
/// </summary>
/// <remarks>
/// <para>
/// First fit, in the order the files were given. Each disc is filled before the
/// next one is started, and nothing is reordered to save a disc: the order is
/// the person's, and shuffling it would be the program making a decision about
/// their disc without asking.
/// </para>
/// <para>
/// <strong>It plans one game's files, not a compilation.</strong> The shipped
/// app briefly spread several games over a set and packed them in whatever order
/// the rows happened to sit in, and that was removed on purpose: which games
/// belong together on a disc is curation, and a packer working from byte counts
/// was making that choice silently. It also produced discs labelled D1 and D2 as
/// though one continued the other when nothing on either depended on the other.
/// So this takes a list of files and is told nothing about games, and whoever
/// calls it decides what belongs on a disc.
/// </para>
/// <para>
/// Static because it holds nothing and needs nothing. The day it needs a
/// dependency it becomes an ordinary class with a constructor.
/// </para>
/// </remarks>
public static class DiscPlanner
{
    /// <summary>
    /// The suffix a cut piece gets, before its zero padded number.
    /// </summary>
    /// <remarks>
    /// Must stay exactly as the shipped app writes it, because a disc built by
    /// either version has to be readable by the other, and the join finds its
    /// pieces by this name.
    /// </remarks>
    private const string PartSuffix = ".dwpart";

    /// <summary>
    /// The whole file a piece was cut from, by name: the piece's path with the
    /// suffix and its number taken off.
    /// </summary>
    /// <remarks>
    /// Here, beside the code that puts the suffix on, so the two cannot drift.
    /// A path that is not a piece comes back as it was.
    /// </remarks>
    public static string TheWholeFileOf(string piecePath)
    {
        int at = piecePath.LastIndexOf(PartSuffix, StringComparison.Ordinal);

        if (at < 0)
        {
            return piecePath;
        }

        string number = piecePath[(at + PartSuffix.Length)..];

        return number.Length > 0 && number.All(char.IsAsciiDigit) ? piecePath[..at] : piecePath;
    }

    public static Outcome<DiscLayout> Plan(PlanRequest request)
    {
        long room = request.Medium.Capacity - request.OverheadPerDisc;

        if (room <= 0)
        {
            return Refusals.NoRoomLeftOnceTheMenuIsOn(request.Medium, request.OverheadPerDisc);
        }

        if (request.Files.Count == 0)
        {
            return Refusals.ThereIsNothingToPutOnADisc();
        }

        List<PlannedDisc> discs = [];
        List<PlannedFile> onThisDisc = [];
        long bytesOnThisDisc = 0;

        void FinishThisDisc()
        {
            discs.Add(new PlannedDisc(discs.Count + 1, [.. onThisDisc], bytesOnThisDisc));
            onThisDisc = [];
            bytesOnThisDisc = 0;
        }

        foreach (PlanFile file in request.Files)
        {
            // Every disc carries the same overhead, so a file that will not fit
            // one of them will not fit any of them.
            if (file.Bytes > room)
            {
                if (!request.MayCutFiles)
                {
                    return Refusals.ThatOneFileIsBiggerThanADisc(file, request.Medium, room);
                }

                // The pieces start on a fresh disc, so they run in order and
                // somebody holding disc 3 can see which piece is on it without
                // doing arithmetic.
                if (onThisDisc.Count > 0)
                {
                    FinishThisDisc();
                }

                int count = (int)Math.Ceiling((double)file.Bytes / room);
                int width = Math.Max(2, count.ToString(CultureInfo.InvariantCulture).Length);
                long offset = 0;

                for (int index = 1; index <= count; index++)
                {
                    long take = Math.Min(room, file.Bytes - offset);

                    // Invariant culture, unlike every size a person reads: this
                    // is a file name, and a file name that changes with the
                    // machine's language is a file the join cannot find.
                    string number = index.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

                    PlannedFile piece = new(
                        file.RelativePath + PartSuffix + number,
                        file.Source,
                        take,
                        new FilePart(index, count, offset, file.Bytes));

                    if (index < count)
                    {
                        discs.Add(new PlannedDisc(discs.Count + 1, [piece], take));
                    }
                    else
                    {
                        // The last piece keeps the room it does not use, so the
                        // files after it share its disc rather than starting
                        // another one.
                        onThisDisc = [piece];
                        bytesOnThisDisc = take;
                    }

                    offset += take;
                }

                continue;
            }

            if (onThisDisc.Count > 0 && bytesOnThisDisc + file.Bytes > room)
            {
                FinishThisDisc();
            }

            onThisDisc.Add(new PlannedFile(file.RelativePath, file.Source, file.Bytes, null));
            bytesOnThisDisc += file.Bytes;
        }

        if (onThisDisc.Count > 0)
        {
            FinishThisDisc();
        }

        // Before placing them, because no disc in the set can carry them and
        // there is no arrangement of discs that fixes it.
        if (request.BytesForTheExtras > room)
        {
            return Refusals.TheExtrasAloneWillNotFitOnADisc(request.BytesForTheExtras, request.Medium, room);
        }

        return new DiscLayout(discs, room, WhichDiscCarriesTheExtras(request, discs, room));
    }

    /// <summary>
    /// Puts the manual, the extras and the loose files on one disc of the set.
    /// </summary>
    /// <remarks>
    /// The <em>last</em> disc, because that is where the slack is. First fit
    /// fills every disc before it to the brim, so putting them at the front
    /// would push a file onto a disc that did not need to exist. If they will
    /// not fit even there, they get a disc of their own.
    /// </remarks>
    /// <returns>The disc's number, or 0 when there are no extras.</returns>
    private static int WhichDiscCarriesTheExtras(PlanRequest request, List<PlannedDisc> discs, long room)
    {
        if (request.BytesForTheExtras <= 0)
        {
            return 0;
        }

        PlannedDisc last = discs[^1];

        if (last.Bytes + request.BytesForTheExtras <= room)
        {
            return last.Number;
        }

        discs.Add(new PlannedDisc(discs.Count + 1, [], 0));

        return discs.Count;
    }
}
