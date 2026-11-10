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
        // At or over, because every disc carries them now: extras that fill a
        // disc to the last byte leave no room for the game on any of them,
        // and planning on would make empty discs for ever.
        if (request.BytesForTheExtras > 0 && request.BytesForTheExtras >= room)
        {
            return Refusals.TheExtrasAloneWillNotFitOnADisc(request.BytesForTheExtras, request.Medium, room);
        }

        // The manual, the extras and the loose files ride EVERY disc, so
        // their room comes off each one before the game is laid down.
        //
        // They rode the last disc until 2026-11-09, then disc 1 (#161), and
        // both times another disc of the set offered Manual and Extras
        // buttons with nothing behind them. On 2026-11-10 the owner pressed
        // Manual on disc 2 of his Hollow Knight set and got "That would not
        // start", and once the game was installed the same buttons opened his
        // Documents. He chose every disc, knowing it costs their room on each.
        long reserved = Math.Max(0, request.BytesForTheExtras);

        List<PlannedDisc> discs = [];
        List<PlannedFile> onThisDisc = [];
        long bytesOnThisDisc = 0;

        // How much any disc can take once the extras are on it.
        long RoomHere() => room - reserved;

        // Whether the disc being filled holds anything yet. Every disc does
        // when there are extras, even with no file on it, so a file too big
        // for what is left moves on rather than being squeezed in on top.
        bool ThisDiscHoldsSomething() => onThisDisc.Count > 0 || reserved > 0;

        void FinishThisDisc()
        {
            discs.Add(new PlannedDisc(discs.Count + 1, [.. onThisDisc], bytesOnThisDisc));
            onThisDisc = [];
            bytesOnThisDisc = 0;
        }

        foreach (PlanFile file in request.Files)
        {
            // Every disc carries the same overhead and the same extras, so a
            // file that will not fit one of them will not fit any of them.
            if (file.Bytes > RoomHere())
            {
                if (!request.MayCutFiles)
                {
                    return Refusals.ThatOneFileIsBiggerThanADisc(file, request.Medium, RoomHere());
                }

                // The pieces start on a disc with no other file on it, so they
                // run in order and somebody holding disc 3 can see which piece
                // is on it without doing arithmetic. A disc with only the
                // extras on it counts as fresh.
                if (onThisDisc.Count > 0)
                {
                    FinishThisDisc();
                }

                // The sizes of the pieces, worked out before any is named,
                // because the count is part of every piece's name. Each takes
                // what a disc has left beside the extras; the join reads each
                // piece's own offset and length, so the last, smaller piece
                // goes back together the same.
                List<long> sizes = [];

                for (long left = file.Bytes, here = RoomHere(); left > 0; here = RoomHere())
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

        return new DiscLayout(discs, room, reserved > 0);
    }
}
