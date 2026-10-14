namespace DiscWright.Core;

/// <summary>
/// Every no the app can say, in one file.
/// </summary>
/// <remarks>
/// Together rather than scattered at the point each one is raised, so the whole
/// voice of the program can be read in one sitting and kept consistent. It is
/// also the only way to notice that two refusals contradict each other, which
/// is impossible to see when each is a string built where it is thrown.
/// </remarks>
public static class Refusals
{
    /// <summary>
    /// The disc is so small that the menu and artwork leave nothing behind.
    /// </summary>
    public static Refusal NoRoomLeftOnceTheMenuIsOn(Medium medium, long overheadPerDisc) =>
        new("There is no room on a disc that size",
            $"The menu, the icon and the artwork come to {ByteSize.Describe(overheadPerDisc)} on every disc, "
                + $"and a {medium.Name} holds {ByteSize.Describe(medium.Capacity)}.",
            [
                new Figure("a disc holds", ByteSize.Describe(medium.Capacity)),
                new Figure("the menu and artwork need", ByteSize.Describe(overheadPerDisc))
            ],
            new Remedy("Use a larger disc", RemedyKind.ChangeMedium));

    /// <summary>
    /// One file is bigger than a whole disc, and cutting files was not allowed.
    /// </summary>
    /// <remarks>
    /// Named, because the answer is a bigger blank and the person needs to know
    /// which file decided that. The numbers are figures as well as prose: the
    /// window has 655 pixels for this line and the sentence wants 930, so it
    /// shows the figures, and reading them back out of our own prose would be
    /// parsing our own writing.
    /// </remarks>
    public static Refusal ThatOneFileIsBiggerThanADisc(PlanFile file, Medium medium, long roomPerDisc) =>
        new("One file is bigger than a whole disc",
            $"{file.RelativePath} is {ByteSize.Describe(file.Bytes)}, and a {medium.Name} leaves "
                + $"{ByteSize.Describe(roomPerDisc)} for files once the menu is on it. Letting it cut the file "
                + "across discs is the other way out, and then every disc of the set is needed to put it back together.",
            [
                new Figure("the file", ByteSize.Describe(file.Bytes)),
                new Figure("a disc leaves", ByteSize.Describe(roomPerDisc))
            ],
            new Remedy("Use a larger disc", RemedyKind.ChangeMedium));

    /// <summary>
    /// The manual and extras alone are bigger than a disc, so no disc can carry
    /// them and there is nowhere to put them.
    /// </summary>
    public static Refusal TheExtrasAloneWillNotFitOnADisc(long bytesForTheExtras, Medium medium, long roomPerDisc) =>
        new("The manual and extras will not fit on a disc",
            $"They come to {ByteSize.Describe(bytesForTheExtras)}, and a {medium.Name} leaves "
                + $"{ByteSize.Describe(roomPerDisc)} for files. Leave some of them off, or use a larger disc.",
            [
                new Figure("the manual and extras", ByteSize.Describe(bytesForTheExtras)),
                new Figure("a disc leaves", ByteSize.Describe(roomPerDisc))
            ],
            new Remedy("Use a larger disc", RemedyKind.ChangeMedium));

    /// <summary>
    /// Asked to plan nothing. Worth saying rather than handing back a set of no
    /// discs, which every caller would then have to notice for itself.
    /// </summary>
    public static Refusal ThereIsNothingToPutOnADisc() =>
        new("There is nothing to put on a disc", "No files were chosen.");
}
