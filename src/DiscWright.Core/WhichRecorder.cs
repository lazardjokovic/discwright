namespace DiscWright.Core;

/// <summary>
/// Which drive to write to, out of the ones on the machine.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It refuses rather than guesses.</strong> Guessing between two drives
/// is how a disc ends up written in the wrong one, and the one in the other
/// drive is often somebody's only blank.
/// </para>
/// <para>
/// For a set, this is asked once before the first disc and the answer kept. A
/// set that wanders between two writers is a set whose discs were written by
/// two different lasers at two different speeds.
/// </para>
/// </remarks>
public static class WhichRecorder
{
    /// <summary>
    /// The one to write to, or why there is not one.
    /// </summary>
    /// <param name="all">Every recorder on the machine.</param>
    /// <param name="imageBytes">What is to be written, so it can be checked against the disc.</param>
    /// <param name="chosen">
    /// The drive somebody already picked, when they have. Empty means nobody has.
    /// </param>
    public static Outcome<Recorder> For(IReadOnlyList<Recorder> all, long imageBytes, string chosen = "")
    {
        if (all.Count == 0)
        {
            return Refusals.ThereIsNoDiscWriter();
        }

        if (chosen.Length > 0)
        {
            Recorder? said = all.FirstOrDefault(
                each => string.Equals(each.Drive.TrimEnd('\\'), chosen.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

            if (said is null)
            {
                return Refusals.ThatDriveIsNotHere(chosen);
            }

            return Whether(said, imageBytes);
        }

        Recorder[] ready = [.. all.Where(each => each.Ready)];

        if (ready.Length == 1)
        {
            return Whether(ready[0], imageBytes);
        }

        if (ready.Length > 1)
        {
            // Two blanks in two drives. Nothing here can put a question on
            // screen, and the one it picked would be the wrong one half the
            // time.
            return Refusals.MoreThanOneDriveIsReady([.. ready.Select(each => each.Drive)]);
        }

        // Nothing is ready. Say the most useful thing about why, which is
        // whether an erase would fix it, and in which drive.
        Recorder? erasable = all.FirstOrDefault(each => each.ErasingWouldHelp);

        if (erasable is not null && all.Count(each => each.ErasingWouldHelp) == 1)
        {
            return Refusals.ThereIsADiscThatCouldBeErased(erasable.Drive, erasable.Holding);
        }

        Recorder? loaded = all.FirstOrDefault(each => each.WhyNot is null && !each.Blank);

        if (loaded is not null)
        {
            return Refusals.ThatDiscCannotBeWrittenTo(loaded.Drive, loaded.Holding);
        }

        return Refusals.ThereIsNoBlankDisc([.. all.Select(each => each.Drive)]);
    }

    private static Outcome<Recorder> Whether(Recorder recorder, long imageBytes)
    {
        if (recorder.WhyNot is string why)
        {
            return Refusals.ThatDriveSaidNothing(recorder.Drive, why);
        }

        if (!recorder.Blank)
        {
            return OpticalMedia.ErasingWouldHelp(recorder.MediaType, recorder.Blank)
                ? Refusals.ThereIsADiscThatCouldBeErased(recorder.Drive, recorder.Holding)
                : Refusals.ThatDiscCannotBeWrittenTo(recorder.Drive, recorder.Holding);
        }

        if (!OpticalMedia.CanBeWritten(recorder.MediaType))
        {
            return Refusals.ThatDiscCannotBeWrittenTo(recorder.Drive, recorder.Holding);
        }

        if (!OpticalMedia.Fits(imageBytes, recorder.FreeSectors))
        {
            return Refusals.TheDiscIsTooSmallForThat(
                recorder.Drive,
                recorder.Holding,
                imageBytes,
                recorder.FreeSectors * OpticalMedia.SectorBytes);
        }

        return recorder;
    }
}
