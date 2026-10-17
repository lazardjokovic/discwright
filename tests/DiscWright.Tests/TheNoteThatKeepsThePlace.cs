using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// The note DiscWright leaves beside a set it is part way through.
/// </summary>
/// <remarks>
/// It is read back by this program and by whoever opens the folder, so it is
/// tested for both: that it survives a round trip, and that what it says is
/// worth reading.
/// </remarks>
public class TheNoteThatKeepsThePlace
{
    [Fact]
    public void Goes_out_and_comes_back_the_same()
    {
        TheNoteBesideTheDiscs note = new("THE WITCHER", ThreeImages, 2);

        TheNoteBesideTheDiscs read = Assert.IsType<TheNoteBesideTheDiscs>(
            TheNoteBesideTheDiscs.Read(note.AsText()));

        // Part by part rather than record against record. A record holding a
        // list compares the list by reference, so two notes saying the same
        // thing are not equal, which is why Matches exists at all.
        Assert.Equal(note.Label, read.Label);
        Assert.Equal<IReadOnlyList<NotedImage>>(note.Images, read.Images);
        Assert.Equal(2, read.Written);
        Assert.Equal(3, read.OfDiscs);
        Assert.Equal(3, read.CarryOnFrom);
    }

    [Fact]
    public void Reads_a_name_with_spaces_in_it()
    {
        TheNoteBesideTheDiscs note = new(
            "A BIG GAME", [new NotedImage("A BIG GAME D1.iso", 369_557_504)], 1);

        TheNoteBesideTheDiscs read = Assert.IsType<TheNoteBesideTheDiscs>(
            TheNoteBesideTheDiscs.Read(note.AsText()));

        // Every image this program makes has spaces in its name, and the size
        // is the only thing after it on the line.
        Assert.Equal("A BIG GAME D1.iso", read.Images[0].Name);
        Assert.Equal(369_557_504, read.Images[0].Bytes);
    }

    [Fact]
    public void Stops_counting_at_the_first_disc_that_is_not_done()
    {
        // Disc 2 missed and disc 3 written, which should not happen and is not
        // a reason to guess. What can be relied on is disc 1.
        string odd = new TheNoteBesideTheDiscs("A SET", ThreeImages, 3).AsText()
            .Replace("disc 2 of 3  done ", "disc 2 of 3  to do", StringComparison.Ordinal);

        Assert.Equal(1, Assert.IsType<TheNoteBesideTheDiscs>(TheNoteBesideTheDiscs.Read(odd)).Written);
    }

    [Fact]
    public void Passes_over_lines_it_does_not_recognise()
    {
        string reworded = new TheNoteBesideTheDiscs("A SET", ThreeImages, 2).AsText()
            + Environment.NewLine
            + "  Somebody wrote this underneath by hand, and it means nothing to anything."
            + Environment.NewLine;

        // The prose around the list is for a person and will be reworded. The
        // list is the part that has to survive.
        Assert.Equal(2, Assert.IsType<TheNoteBesideTheDiscs>(TheNoteBesideTheDiscs.Read(reworded)).Written);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A file that is about something else entirely.")]
    public void Is_nothing_when_there_is_nothing_to_read(string? text) =>
        Assert.Null(TheNoteBesideTheDiscs.Read(text));

    [Fact]
    public void Is_nothing_when_a_disc_is_missing_from_the_list()
    {
        string gappy = string.Join(
            Environment.NewLine,
            new TheNoteBesideTheDiscs("A SET", ThreeImages, 2).AsText()
                .Split('\n')
                .Where(line => !line.Contains("disc 2 of 3", StringComparison.Ordinal)));

        // Two lines for a set of three. Which disc is which is now a guess,
        // and a guess about disc order is how a set comes out shuffled.
        Assert.Null(TheNoteBesideTheDiscs.Read(gappy));
    }

    [Fact]
    public void Is_nothing_when_the_list_disagrees_about_how_many_discs_there_are()
    {
        string muddled = new TheNoteBesideTheDiscs("A SET", ThreeImages, 2).AsText()
            .Replace("disc 3 of 3", "disc 3 of 4", StringComparison.Ordinal);

        Assert.Null(TheNoteBesideTheDiscs.Read(muddled));
    }

    [Fact]
    public void Matches_the_same_build_and_nothing_else()
    {
        TheNoteBesideTheDiscs note = new("A SET", ThreeImages, 2);

        Assert.True(note.Matches("A SET", ThreeImages));

        // A rebuild: same names, different sizes.
        Assert.False(note.Matches(
            "A SET", [.. ThreeImages.Select(each => each with { Bytes = each.Bytes + 1 })]));

        // Somebody else's set in the same folder.
        Assert.False(note.Matches("ANOTHER SET", ThreeImages));

        // A set of two where there were three.
        Assert.False(note.Matches("A SET", [.. ThreeImages.Take(2)]));
    }

    [Fact]
    public void Is_not_part_way_through_when_the_set_is_finished_or_not_begun()
    {
        Assert.True(new TheNoteBesideTheDiscs("A SET", ThreeImages, 1).PartWayThrough);
        Assert.True(new TheNoteBesideTheDiscs("A SET", ThreeImages, 2).PartWayThrough);

        // Pressing burn on a finished set is how a second copy gets made.
        Assert.False(new TheNoteBesideTheDiscs("A SET", ThreeImages, 3).PartWayThrough);
        Assert.False(new TheNoteBesideTheDiscs("A SET", ThreeImages, 0).PartWayThrough);
    }

    [Fact]
    public void Says_enough_for_somebody_who_has_never_seen_this_program()
    {
        string text = new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 2).AsText();

        // The thing it is for is somebody coming back tomorrow, so it names
        // the set, says how many discs it has, and says which are done.
        Assert.Contains("THE WITCHER, a set of 3 discs.", text, StringComparison.Ordinal);
        Assert.Contains("disc 2 of 3  done", text, StringComparison.Ordinal);
        Assert.Contains("disc 3 of 3  to do", text, StringComparison.Ordinal);
        Assert.Contains("carry on from the first disc that is not done", text, StringComparison.Ordinal);
    }

    // ------------------------------------- a disc burned on its own

    [Fact]
    public void Counts_the_next_disc_when_it_is_burned_on_its_own()
    {
        // Finishing a set over two evenings is the ordinary way to make one:
        // build it, burn disc 1, come back tomorrow, open the project and
        // burn disc 2. Only walking a set through from the beginning ever
        // moved this note on, so the second evening left it saying disc 2 was
        // to do however well disc 2 read back. Lazar hit exactly that.
        TheNoteBesideTheDiscs note = new("THE WITCHER", ThreeImages, 1);

        TheNoteBesideTheDiscs moved = Assert.IsType<TheNoteBesideTheDiscs>(
            note.WithThatOneDone("THE WITCHER D2.iso"));

        Assert.Equal(2, moved.Written);
        Assert.Contains("disc 2 of 3  done", moved.AsText(), StringComparison.Ordinal);
        Assert.Contains("disc 3 of 3  to do", moved.AsText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_care_what_case_the_image_was_named_in()
    {
        Assert.NotNull(new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 1)
            .WithThatOneDone("the witcher d2.iso"));
    }

    [Fact]
    public void Will_not_jump_a_disc_that_was_never_read_back()
    {
        // Three discs, one done, and disc 3 comes out good. There is no way
        // to record that here: what is kept is how many are done counting
        // from disc 1, so saying 3 would be claiming disc 2 was read back
        // when it never was. Leaving it alone costs the offer to carry on
        // and nothing else.
        Assert.Null(new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 1)
            .WithThatOneDone("THE WITCHER D3.iso"));
    }

    [Fact]
    public void Leaves_a_disc_that_was_already_counted_alone()
    {
        // Burning disc 1 again, to replace one that was scratched. It is
        // already counted and the note does not go backwards or forwards.
        Assert.Null(new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 2)
            .WithThatOneDone("THE WITCHER D1.iso"));
    }

    [Fact]
    public void Says_nothing_about_an_image_that_is_not_in_the_set()
    {
        Assert.Null(new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 1)
            .WithThatOneDone("SOMETHING ELSE.iso"));
    }

    [Fact]
    public void Finishes_a_set_off_with_its_last_disc()
    {
        TheNoteBesideTheDiscs moved = Assert.IsType<TheNoteBesideTheDiscs>(
            new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 2)
                .WithThatOneDone("THE WITCHER D3.iso"));

        Assert.Equal(3, moved.Written);

        // And a finished set is not part way through, so nobody is asked
        // whether to carry on from disc 4 of 3.
        Assert.False(moved.PartWayThrough);
    }

    [Fact]
    public void Counts_the_very_first_disc_of_a_set_nobody_has_started()
    {
        TheNoteBesideTheDiscs moved = Assert.IsType<TheNoteBesideTheDiscs>(
            new TheNoteBesideTheDiscs("THE WITCHER", ThreeImages, 0)
                .WithThatOneDone("THE WITCHER D1.iso"));

        Assert.Equal(1, moved.Written);
    }

    private static readonly NotedImage[] ThreeImages =
    [
        new("THE WITCHER D1.iso", 4_600_000_000),
        new("THE WITCHER D2.iso", 4_600_000_000),
        new("THE WITCHER D3.iso", 1_200_000_000)
    ];
}
