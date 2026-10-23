using DiscWright.Core;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Which thing a disc is compared with once it is written: the folder it was
/// built from, the list the disc carries, or nothing honestly.
/// </summary>
/// <remarks>
/// <para>
/// The comparisons themselves have their own tests. This is about the choice
/// between them, which lived in the window where the only way to reach it was
/// to burn a disc, and where both of the faults it has ever had were waiting.
/// </para>
/// <para>
/// One of them cost a disc's worth of fright: burning disc 2 of a set made in
/// the same sitting compared it against disc 1 and called every file on it
/// wrong, which looks exactly like a ruined disc. The other was quieter.
/// Opening a finished project and pressing Burn built nothing in that sitting,
/// so a disc that read back perfectly was reported as written but not checked.
/// </para>
/// </remarks>
public class WhatADiscIsReadBackAgainst
{
    // ----------------------------------------- which folder, out of what was built

    [Fact]
    public void Takes_the_folder_of_the_disc_with_that_number()
    {
        Assert.Equal(
            @"C:\out\disc 2",
            ReadingBackWhatWasWritten.WhereItWasBuiltFrom(ASetOfThree, new WhichDisc.OfASet(2, 3)));
    }

    [Fact]
    public void Takes_the_folder_of_the_disc_an_image_was_written_from()
    {
        // The one that cost something. Every disc of a set carries the same
        // label, so the label alone cannot tell them apart, and taking the
        // first disc every time is how disc 2 came to be compared with disc 1.
        Assert.Equal(
            @"C:\out\disc 2",
            ReadingBackWhatWasWritten.WhereItWasBuiltFrom(
                ASetOfThree, new WhichDisc.TheImageCalled(@"C:\out\A SET D2.iso")));
    }

    [Fact]
    public void Knows_the_image_of_a_disc_that_stands_alone_by_its_label()
    {
        // No "D1" in the name of a disc that is the whole set, because the
        // name is built the way the image's name is built.
        BuiltDiscs one = new([new BuiltDisc("ONE GAME", 1, 1, @"C:\out\only", 1000)]);

        Assert.Equal(
            @"C:\out\only",
            ReadingBackWhatWasWritten.WhereItWasBuiltFrom(
                one, new WhichDisc.TheImageCalled(@"C:\out\ONE GAME.iso")));
    }

    [Fact]
    public void Falls_back_to_the_first_disc_when_none_of_them_answers_to_it()
    {
        // Something built in this sitting is more likely to be the disc in the
        // drive than not, and the comparison says plainly what does not match
        // either way.
        Assert.Equal(
            @"C:\out\disc 1",
            ReadingBackWhatWasWritten.WhereItWasBuiltFrom(
                ASetOfThree, new WhichDisc.TheImageCalled(@"C:\out\SOMETHING ELSE.iso")));

        Assert.Equal(
            @"C:\out\disc 1",
            ReadingBackWhatWasWritten.WhereItWasBuiltFrom(ASetOfThree, new WhichDisc.OfASet(9, 3)));
    }

    [Fact]
    public void Has_no_folder_at_all_when_this_sitting_built_nothing()
    {
        Assert.Null(ReadingBackWhatWasWritten.WhereItWasBuiltFrom(null, new WhichDisc.OfASet(1, 3)));
        Assert.Null(ReadingBackWhatWasWritten.WhereItWasBuiltFrom(new BuiltDiscs([]), new WhichDisc.OfASet(1, 3)));
    }

    // --------------------------------------------------------- what it says

    [Fact]
    public void Says_which_disc_of_a_set_every_line_is_about()
    {
        // A set takes half an hour, and somebody watching it needs to know
        // where in the half hour they are.
        Assert.Equal("disc 3 of 3: hashing one.bin", new WhichDisc.OfASet(3, 3).Saying("hashing one.bin"));
    }

    [Fact]
    public void Says_nothing_about_disc_numbers_for_a_disc_on_its_own()
    {
        // There is only the one, and saying "disc 1" about it is noise.
        Assert.Equal(
            "hashing one.bin",
            new WhichDisc.TheImageCalled(@"C:\out\ONE GAME.iso").Saying("hashing one.bin"));
    }

    // ------------------------------------------------------- the whole thing

    [Fact]
    public async Task Reads_it_against_what_was_built_while_that_folder_is_there()
    {
        ADiskThatRemembers disk = new();

        Outcome<DiscCheck> how = await Reading(disk)
            .HowItCameOut(InDriveE, new WhichDisc.OfASet(2, 3), ASetOfThree, default);

        Assert.IsType<Outcome<DiscCheck>.Ok>(how);

        // The better of the two comparisons, and it knows what was meant to be
        // on the disc rather than only what the disc says about itself.
        Assert.Contains(@"C:\out\disc 2", disk.Listed);
        Assert.DoesNotContain(@"E:\" + ChecksumList.FileName, disk.Read);
    }

    [Fact]
    public async Task Reads_it_against_the_list_on_the_disc_once_the_folder_has_gone()
    {
        // A staging folder tidied away, which is what every finished project
        // looks like a week later.
        ADiskThatRemembers disk = new() { TheFolders = [@"E:\"], TheList = AListOfNothing };

        Outcome<DiscCheck> how = await Reading(disk)
            .HowItCameOut(InDriveE, new WhichDisc.OfASet(2, 3), ASetOfThree, default);

        Assert.IsType<Outcome<DiscCheck>.Ok>(how);
        Assert.Contains(@"E:\" + ChecksumList.FileName, disk.Read);
        Assert.DoesNotContain(@"C:\out\disc 2", disk.Listed);
    }

    [Fact]
    public async Task Reads_it_against_the_list_on_the_disc_when_nothing_was_built_here()
    {
        // Walking a set after opening the project it was built from is the
        // ordinary way to finish one, so this is the common case rather than
        // the odd one. It used to report a perfect disc as not checked.
        ADiskThatRemembers disk = new() { TheList = AListOfNothing };

        Outcome<DiscCheck> how = await Reading(disk)
            .HowItCameOut(InDriveE, new WhichDisc.TheImageCalled(@"C:\out\A SET D2.iso"), null, default);

        Assert.IsType<Outcome<DiscCheck>.Ok>(how);
        Assert.Contains(@"E:\" + ChecksumList.FileName, disk.Read);
    }

    [Fact]
    public async Task Says_plainly_that_nobody_can_speak_for_the_disc()
    {
        // No folder and no list. The disc is written, and that is all that can
        // honestly be said about it, which is not the same as a disc that
        // failed.
        ADiskThatRemembers disk = new() { TheFolders = [@"E:\"] };

        Outcome<DiscCheck> how = await Reading(disk)
            .HowItCameOut(InDriveE, new WhichDisc.OfASet(2, 3), ASetOfThree, default);

        Refusal why = Assert.IsType<Outcome<DiscCheck>.No>(how).Refusal;
        Assert.Equal("The disc was written but not checked", why.Headline);

        // Which drive to put it back in, because "not checked" with no drive
        // named is a sentence somebody can do nothing about.
        Assert.Contains("E:", why.Because, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("E:")]
    [InlineData(@"E:\")]
    public async Task Reads_the_disc_at_the_root_of_the_drive_it_is_in(string drive)
    {
        ADiskThatRemembers disk = new();

        await Reading(disk).HowItCameOut(
            new Recorder("id-E", drive, "A Drive", 2, false, 0, [750]),
            new WhichDisc.OfASet(1, 3),
            ASetOfThree,
            default);

        // One separator whether the drive came with one or not. A drive asked
        // about as "E:" is a path relative to whatever Windows last thought
        // the current folder on E: was, which is how a file lands somewhere
        // nobody can find, and two separators is a path Windows will still
        // open and nothing anybody wants to read in a message.
        Assert.Contains(@"E:\", disk.Listed);
        Assert.DoesNotContain("E:", disk.Listed);
        Assert.DoesNotContain(@"E:\\", disk.Listed);
    }

    [Fact]
    public async Task Says_which_disc_of_the_set_it_is_working_on()
    {
        List<string> said = [];
        ADiskThatRemembers disk = new() { TheFiles = [new ListedFile("one.bin", 3)] };

        await new ReadingBackWhatWasWritten(disk, new NoHashing(), (doing, far) => said.Add(doing))
            .HowItCameOut(InDriveE, new WhichDisc.OfASet(3, 3), ASetOfThree, default);

        Assert.NotEmpty(said);
        Assert.All(said, line => Assert.StartsWith("disc 3 of 3: ", line, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- the rig

    private static readonly BuiltDiscs ASetOfThree = new(
    [
        new BuiltDisc("A SET", 1, 3, @"C:\out\disc 1", 1000),
        new BuiltDisc("A SET", 2, 3, @"C:\out\disc 2", 1000),
        new BuiltDisc("A SET", 3, 3, @"C:\out\disc 3", 1000)
    ]);

    private static readonly Recorder InDriveE = new("id-E", "E:", "A Drive", 2, false, 0, [750]);

    /// <summary>A list that says the disc should hold nothing, which is a list.</summary>
    private static readonly string AListOfNothing = ChecksumList.For("A SET", []);

    private static ReadingBackWhatWasWritten Reading(IFileSystem disk) =>
        new(disk, new NoHashing(), (doing, far) => { });

    /// <summary>
    /// A disk that says which folders exist and remembers what it was asked.
    /// </summary>
    private sealed class ADiskThatRemembers : IFileSystem
    {
        /// <summary>Everything that exists. Null means everything does.</summary>
        internal IReadOnlyList<string>? TheFolders { get; init; }

        /// <summary>What the checksum list on the disc says, or nothing.</summary>
        internal string? TheList { get; init; }

        /// <summary>What every folder holds, which is nothing unless it is said.</summary>
        internal IReadOnlyList<ListedFile> TheFiles { get; init; } = [];

        internal List<string> Listed { get; } = [];

        internal List<string> Read { get; } = [];

        public bool FolderExists(string path) =>
            TheFolders is null || TheFolders.Contains(path, StringComparer.OrdinalIgnoreCase);

        public FolderListing List(string path)
        {
            Listed.Add(path);

            return new FolderListing(path, TheFiles);
        }

        public string? ReadText(string path)
        {
            Read.Add(path);

            return path.EndsWith(ChecksumList.FileName, StringComparison.OrdinalIgnoreCase) ? TheList : null;
        }
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }
}
