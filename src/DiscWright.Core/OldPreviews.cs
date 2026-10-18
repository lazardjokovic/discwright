namespace DiscWright.Core;

/// <summary>
/// When a preview nobody has looked at in a while stops being worth keeping.
/// </summary>
/// <remarks>
/// <para>
/// One folder per project instead of one per press is most of the answer, and
/// not all of it: fifty projects over a year still leave fifty folders, each
/// a few megabytes, none of them ever removed.
/// </para>
/// <para>
/// So old ones go when the program starts. <strong>Not when it closes</strong>,
/// which would be tidier and wrong: the menu is opened in mshta as a separate
/// program, and somebody can perfectly well still be reading it after the
/// window that wrote it has gone.
/// </para>
/// </remarks>
public static class OldPreviews
{
    /// <summary>
    /// How long a preview is left alone before it counts as abandoned.
    /// </summary>
    /// <remarks>
    /// A week, which is long enough to come back to a project after a weekend
    /// and short enough that the folder does not become an archive. Nothing
    /// is lost either way: a preview is rewritten from the project in under a
    /// second, which is the whole reason it may be thrown away at all.
    /// </remarks>
    public static readonly TimeSpan After = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether a preview last touched at this time has had its day.
    /// </summary>
    /// <param name="lastLookedAt">When the folder was last written to.</param>
    /// <param name="now">The time, handed in so this can be tested.</param>
    /// <remarks>
    /// A folder dated in the future is kept. A clock that has gone backwards,
    /// or a file copied from a machine whose clock was wrong, should not cost
    /// somebody a folder: the answer to not knowing how old something is, is
    /// to leave it alone.
    /// </remarks>
    public static bool HadItsDay(DateTime lastLookedAt, DateTime now) =>
        now - lastLookedAt > After;
}
