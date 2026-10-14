namespace DiscWright.Core;

/// <summary>
/// Everything the planner needs to be asked.
/// </summary>
/// <remarks>
/// A record rather than five parameters, because the rule in ARCHITECTURE.md is
/// that a fourth constructor parameter means the parameters are a record that
/// does not exist yet. It also means a test can say what it is changing and
/// leave the rest alone.
/// </remarks>
/// <param name="Files">
/// In the order they should be laid down. The planner fills each disc in turn
/// and never reorders them, because the order is the person's and reordering it
/// to save a disc would be the program making a decision about their disc.
/// </param>
/// <param name="Medium">The size of disc being aimed at.</param>
/// <param name="OverheadPerDisc">
/// What the menu, the icon, the background and the music take on <em>every</em>
/// disc. Subtracted once, up front, because a file that will not fit one disc
/// will not fit any of them.
/// </param>
/// <param name="BytesForTheExtras">
/// The manual, the extras folder and any loose files, measured together. They
/// land on one disc rather than all of them, so they are weighed separately
/// from the files above. Zero when there are none.
/// </param>
/// <param name="MayCutFiles">
/// Whether a single file bigger than a whole disc may be cut across several.
/// Off, such a file is refused by name. On, it is cut and the pieces carry the
/// arithmetic to put it back together.
/// </param>
public sealed record PlanRequest(
    IReadOnlyList<PlanFile> Files,
    Medium Medium,
    long OverheadPerDisc,
    long BytesForTheExtras,
    bool MayCutFiles);
