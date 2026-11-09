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

        // Before anything is placed, because no disc in the set can carry
        // them and there is no arrangement of discs that fixes it.
        if (request.BytesForTheExtras > room)
        {
            return Refusals.TheExtrasAloneWillNotFitOnADisc(request.BytesForTheExtras, request.Medium, room);
        }

        // The manual, the extras and the loose files ride disc 1, so their
        // room is taken out of disc 1 before the game is laid down.
        //
        // They rode the last disc until 2026-11-09, because that is where
        // first fit leaves the slack. But the disc everybody puts in first
        // is disc 1, and on a set its menu had no Manual and no Extras: the
        // owner previewed his set, saw one window, and found neither. He
        // chose disc 1 knowing the cost, which is that without cutting a
        // set can rarely need one more disc; with cutting the bytes run on
        // and the count is the same.
        long reserved = Math.Max(0, request.BytesForTheExtras);

        List<PlannedDisc> discs = [];
        List<PlannedFile> onThisDisc = [];
        long bytesOnThisDisc = 0;

        // How much the disc being filled can take: disc 1 gives up the
        // extras' room, every other disc has all of it.
        long RoomHere() => discs.Count == 0 ? room - reserved : room;

        // Whether the disc being filled holds anything yet. Disc 1 always
        // does when it carries the extras, even with no file on it, so a
        // file too big for what is left of it moves on to disc 2 and does
        // not get squeezed in on top of them.
        bool ThisDiscHoldsSomething() => onThisDisc.Count > 0 || (discs.Count == 0 && reserved > 0);

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

                // The pieces start on a disc with no other file on it, so they
                // run in order and somebody holding disc 3 can see which piece
                // is on it without doing arithmetic. Disc 1 with only the
                // extras on it counts as fresh: the first piece takes what is
                // left of it rather than leaving disc 1 nearly empty, which
                // would cost a whole disc for the sake of a manual. A disc 1
                // the extras fill to the last byte is finished first.
                if (onThisDisc.Count > 0 || RoomHere() <= 0)
                {
                    FinishThisDisc();
                }

                // The sizes of the pieces, worked out before any is named,
                // because the count is part of every piece's name. Only the
                // first can be smaller than a disc, when it shares disc 1
                // with the extras; the join reads each piece's own offset
                // and length, so unequal pieces go back together the same.
                List<long> sizes = [];

                for (long left = file.Bytes, here = RoomHere(); left > 0; here = room)
                {
                    long take = Math.Min(here, left);
                    sizes.Add(take);
                    left -= take;
                }

                int count = sizes.Count;
                int width = Math.Max(2, count.ToString(CultureInfo.InvariantCulture).Length);
                long offset = 0;

                for (int index = 1; index <= count; index++)
                {
                    long take = sizes[index - 1];

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

            if (ThisDiscHoldsSomething() && bytesOnThisDisc + file.Bytes > RoomHere())
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

        // On disc 1, or on no disc when there are none. Disc 1 always exists
        // by now: there was at least one file, and either it went on disc 1
        // or disc 1 was finished, holding only the extras, to make way.
        return new DiscLayout(discs, room, reserved > 0 ? 1 : 0);
    }
}
