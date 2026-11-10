namespace DiscWright.Core;

/// <summary>
/// How a pile of files comes out as discs. The whole answer, and the only thing
/// the staging and burning steps need in order to do their work.
/// </summary>
/// <param name="Discs">In the order they will be burned, numbered from one.</param>
/// <param name="RoomPerDisc">
/// What was left for files after the menu and artwork took their share. Kept
/// because every figure in a refusal and every "disc 2 of 3, 4.1 GB used" line
/// is measured against it, and recomputing it elsewhere is how two parts of a
/// program come to disagree about how big a disc is.
/// </param>
/// <param name="ExtrasOnEveryDisc">
/// Whether there are a manual, extras or loose files, which ride every disc of
/// the set when there are.
/// </param>
/// <remarks>
/// <strong>Every disc, since 2026-11-10.</strong> They rode the last disc, then
/// disc 1 (#161), and each time a disc of the set offered Manual and Extras
/// buttons that opened nothing, or, once the game was installed, opened the
/// person's Documents. The owner, installing his Hollow Knight set: buttons
/// that work on one disc and not the other confuse, and they are small, "it's
/// just something nostalgic", so every disc carries them and pays the room.
/// </remarks>
public sealed record DiscLayout(
    IReadOnlyList<PlannedDisc> Discs,
    long RoomPerDisc,
    bool ExtrasOnEveryDisc)
{
    /// <summary>Whether this disc carries the manual, the extras and the loose files.</summary>
    public bool CarriesTheExtras(int discNumber) =>
        ExtrasOnEveryDisc && discNumber >= 1 && discNumber <= Discs.Count;
}

/// <summary>One disc, and everything that goes on it.</summary>
/// <param name="Number">From one. The number printed on the cover and shown by the menu.</param>
/// <param name="Files">May be empty: a disc that carries only the extras has no files of its own.</param>
/// <param name="Bytes">The files on this disc, summed. Not including the menu and artwork.</param>
public sealed record PlannedDisc(int Number, IReadOnlyList<PlannedFile> Files, long Bytes);

/// <summary>
/// A file as it will appear on a disc, which is not always a whole file.
/// </summary>
/// <param name="Part">
/// Null for the ordinary case. Set when this is one piece of a file too big for
/// any single disc, in which case <see cref="RelativePath"/> is the piece's name
/// rather than the file's.
/// </param>
public sealed record PlannedFile(string RelativePath, string Source, long Bytes, FilePart? Part);

/// <summary>
/// Which piece of a cut file this is, and everything needed to put it back.
/// </summary>
/// <remarks>
/// A set splits a game between discs freely, and splits a <em>file</em> only
/// when that one file is bigger than a whole disc. When it has to, the pieces
/// carry their own arithmetic so the join needs nothing else: where this piece
/// started, how many there are, and how big the finished file should be.
/// </remarks>
/// <param name="Index">From one.</param>
/// <param name="Count">How many pieces in all.</param>
/// <param name="Offset">Where this piece starts inside the whole file.</param>
/// <param name="WholeBytes">
/// How big the file is once joined. Checked after a join, because a join that
/// produces the wrong length has gone wrong in a way no checksum needs to prove.
/// </param>
public sealed record FilePart(int Index, int Count, long Offset, long WholeBytes);
