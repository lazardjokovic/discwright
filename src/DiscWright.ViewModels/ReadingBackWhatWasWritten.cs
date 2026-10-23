using DiscWright.Core;
using DiscWright.Staging;

namespace DiscWright.ViewModels;

/// <summary>
/// Reading a disc back after it was written, against the right thing: the
/// folder it was built from, the list the disc carries, or nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written is not the same as readable.</strong> The drive says it
/// wrote the disc, and whether the disc is the disc is a different question,
/// which is why it is asked every time.
/// </para>
/// <para>
/// <strong>This choice was written twice in <see cref="MakeADisc"/>,</strong>
/// once for a disc on its own and once for a disc of a set, the two halves
/// differing only in how they found the folder. Both were reachable only by
/// burning something, and a mutation pass planted seventeen faults in them
/// that nothing objected to. The two comparisons themselves are Core classes
/// with tests of their own; it was only the choosing that was stuck in the
/// window.
/// </para>
/// <para>
/// Both faults that cost somebody something were in the choosing.
/// <see cref="BuiltDiscs"/> was asked for its first disc whatever had been
/// burned, so burning disc 2 of a set made in the same sitting compared it
/// against disc 1 and called every file on it wrong, which looks exactly like
/// a ruined disc. And opening a finished project and pressing Burn left
/// nothing built in this sitting, so a disc that read back perfectly was
/// reported as written but not checked.
/// </para>
/// </remarks>
/// <param name="disk">For reading the disc, and for saying whether a folder is still there.</param>
/// <param name="hasher">For proving what came back is what went on.</param>
/// <param name="say">One line about what is happening, and how far through it is.</param>
public sealed class ReadingBackWhatWasWritten(
    IFileSystem disk, IHasher hasher, Action<string, double> say)
{
    /// <summary>
    /// How the disc in that drive came out, or why nothing can honestly be
    /// said about it.
    /// </summary>
    /// <remarks>
    /// A refusal here is not a disc that failed. It is a disc nobody can speak
    /// for: there is no folder to compare it with and no list on the disc, so
    /// it is written and that is all anybody knows.
    /// </remarks>
    /// <param name="onto">The drive it is in.</param>
    /// <param name="which">Which disc this is, which decides what it is compared with.</param>
    /// <param name="made">What this sitting built, or nothing when it built nothing.</param>
    public async Task<Outcome<DiscCheck>> HowItCameOut(
        Recorder onto, WhichDisc which, BuiltDiscs? made, CancellationToken ct)
    {
        // The root of the disc, from the letter. A drive is "D:" and a path
        // into it needs the separator, which is built from its character code
        // because a typed one is halved on the way into generated code.
        string root = onto.Drive.TrimEnd((char)92) + (char)92;

        Progress<CheckProgress> saying = new(step => say(which.Saying(step.ToString()), step.HowFar));

        // The folder it was built from, while that folder is still there. It
        // is the better of the two comparisons: it knows what was meant to be
        // on the disc, where the list on the disc only knows what the disc
        // says about itself.
        if (WhereItWasBuiltFrom(made, which) is string staged && disk.FolderExists(staged))
        {
            return await TheDiscAgainstWhatWasBuilt.Check(root, staged, disk, hasher, saying, ct);
        }

        // Walking a set after opening the project it was built from is the
        // ordinary way to finish one, so this is the common case and not the
        // odd one.
        if (TheDiscAgainstItsOwnList.IsOn(root, disk))
        {
            return await TheDiscAgainstItsOwnList.Check(root, disk, hasher, saying, ct);
        }

        return Refusals.TheDiscWasNotChecked(onto.Drive);
    }

    /// <summary>
    /// The folder the disc that was written was built from, or nothing when
    /// this sitting built nothing.
    /// </summary>
    /// <remarks>
    /// Falls back to the first disc rather than to nothing when what was built
    /// holds no disc answering to this one. Something built in this sitting is
    /// more likely to be the disc in the drive than not, and the comparison
    /// says plainly what does not match either way.
    /// </remarks>
    public static string? WhereItWasBuiltFrom(BuiltDiscs? made, WhichDisc which) =>
        made is { Discs.Count: > 0 } built
            ? (which.OutOf(built.Discs) ?? built.Discs[0]).Folder
            : null;
}

/// <summary>Which disc was written, which is asked in two ways.</summary>
/// <remarks>
/// A set is walked by number, and a disc burned on its own is known only by
/// the image it came from. The two used to be two methods; this is the whole
/// of the difference between them.
/// </remarks>
public abstract record WhichDisc
{
    private WhichDisc()
    {
    }

    /// <summary>
    /// Disc three of five, while the set is being walked, and every line the
    /// window says about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The words live here because they are about this disc and nothing else,
    /// and because they were four interpolated strings inside the walk, built
    /// from <c>at + 1</c> and a count, that nothing objected to being emptied.
    /// </para>
    /// <para>
    /// <strong>How many there are is part of knowing which disc this is.</strong>
    /// The walk said "disc 1 of 2" while it wrote and the check said "disc 1"
    /// while it read back, which is the same disc described two ways in
    /// consecutive lines over somebody watching a bar for half an hour.
    /// </para>
    /// </remarks>
    /// <param name="Number">Which disc, from one.</param>
    /// <param name="OfThem">How many the set has.</param>
    public sealed record OfASet(int Number, int OfThem) : WhichDisc
    {
        /// <summary>"disc 2 of 3", which heads every line about this disc.</summary>
        public string InWords => $"disc {Number} of {OfThem}";

        /// <summary>
        /// What the window says while it waits for somebody to swap the disc.
        /// </summary>
        /// <remarks>
        /// The disc it is waiting for, not the one just written. A dialog is
        /// asking for the next blank at the same moment, and the bar behind it
        /// has to agree with the dialog.
        /// </remarks>
        public string WaitingForABlank => $"waiting for a blank disc for {InWords}";
    }

    /// <summary>The disc an image was written from, burned on its own.</summary>
    public sealed record TheImageCalled(string Path) : WhichDisc;

    /// <summary>
    /// Which of the built discs this is, or nothing when none of them answers
    /// to it.
    /// </summary>
    /// <remarks>
    /// Every disc in a set carries the same label, so the label alone cannot
    /// tell them apart. The name is rebuilt the way the image's name is built,
    /// so the two cannot drift: "{label} D{number}" in a set, "{label}" for a
    /// disc on its own.
    /// </remarks>
    public BuiltDisc? OutOf(IReadOnlyList<BuiltDisc> discs) => this switch
    {
        OfASet(int number, _) => discs.FirstOrDefault(disc => disc.Number == number),

        TheImageCalled(string path) when System.IO.Path.GetFileNameWithoutExtension(path) is string name =>
            discs.FirstOrDefault(disc => string.Equals(
                disc.OfDiscs > 1 ? $"{disc.Label} D{disc.Number}" : disc.Label,
                name,
                StringComparison.OrdinalIgnoreCase)),

        _ => null
    };

    /// <summary>
    /// What a line about this disc reads like.
    /// </summary>
    /// <remarks>
    /// Every line of a set says which disc it is about, because a set takes
    /// half an hour and somebody watching it needs to know where in the hour
    /// they are. A disc on its own needs no such thing: there is only the one,
    /// and saying "disc 1" about it is noise.
    /// </remarks>
    public string Saying(string step) => this switch
    {
        OfASet set => $"{set.InWords}: {step}",
        _ => step
    };
}
