using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The lines the window shows while a set is being written.
/// </summary>
/// <remarks>
/// <para>
/// Burning a set is half an hour of somebody watching a bar, and these
/// sentences are the only thing telling them which disc of how many they are
/// on and whether the program is waiting for them to do something.
/// </para>
/// <para>
/// They were four interpolated strings inside the walk, built from
/// <c>at + 1</c> and a count, and a mutation pass found nothing objecting to
/// any of them: not to the words going empty, not to the lines never being
/// said, and not to the disc being counted as <c>at - 1</c>.
/// </para>
/// </remarks>
public class TheWordsASetSays
{
    [Theory]
    [InlineData(1, 3, "disc 1 of 3")]
    [InlineData(2, 3, "disc 2 of 3")]
    [InlineData(3, 3, "disc 3 of 3")]
    [InlineData(1, 1, "disc 1 of 1")]
    public void Says_which_disc_of_how_many(int disc, int ofThem, string reads)
    {
        Assert.Equal(reads, new WhichDisc.OfASet(disc, ofThem).InWords);
    }

    [Fact]
    public void Asks_for_the_blank_by_naming_the_disc_it_is_for()
    {
        // The disc it is waiting for, not the one just written. A dialog is
        // asking for the next blank at the same moment and the bar behind it
        // has to agree with the dialog.
        Assert.Equal(
            "waiting for a blank disc for disc 2 of 3",
            new WhichDisc.OfASet(2, 3).WaitingForABlank);
    }

    [Fact]
    public void Says_what_the_drive_is_doing_beside_the_disc_it_is_doing_it_to()
    {
        Assert.Equal("disc 2 of 3: writing", new WhichDisc.OfASet(2, 3).Saying("writing"));
    }

    [Fact]
    public void Says_the_same_thing_about_a_disc_whether_writing_it_or_reading_it_back()
    {
        // Writing it and reading it back say the same thing about it, which
        // they did not: the walk said "disc 1 of 2" and the check said
        // "disc 1" in the line underneath.
        WhichDisc.OfASet where = new(2, 3);

        Assert.Equal("disc 2 of 3", where.InWords);
        Assert.Equal("disc 2 of 3: writing", where.Saying("writing"));
        Assert.Equal("disc 2 of 3: hashing", where.Saying("hashing"));
    }
}
