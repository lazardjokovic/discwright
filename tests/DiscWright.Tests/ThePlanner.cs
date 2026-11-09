using DiscWright.Core;

namespace DiscWright.Tests;

public class ThePlanner
{
    // A disc with round numbers, so a test says what it means. The real tiers
    // are tested in TheMediaTable; nothing here depends on their odd sizes.
    private static readonly Medium TenBytes = new("TEST", "a ten byte disc", 10);

    [Fact]
    public void Puts_everything_on_one_disc_when_it_fits()
    {
        DiscLayout layout = Planned(Files(3, 4));

        PlannedDisc only = Assert.Single(layout.Discs);
        Assert.Equal(1, only.Number);
        Assert.Equal(7, only.Bytes);
        Assert.Equal(2, only.Files.Count);
    }

    [Fact]
    public void Fills_a_disc_exactly_without_starting_another()
    {
        DiscLayout layout = Planned(Files(6, 4));

        Assert.Single(layout.Discs);
        Assert.Equal(10, layout.Discs[0].Bytes);
    }

    [Fact]
    public void Starts_a_second_disc_for_one_byte_too_many()
    {
        DiscLayout layout = Planned(Files(6, 5));

        Assert.Equal(2, layout.Discs.Count);
        Assert.Equal(6, layout.Discs[0].Bytes);
        Assert.Equal(5, layout.Discs[1].Bytes);
    }

    [Fact]
    public void Fills_each_disc_before_starting_the_next()
    {
        DiscLayout layout = Planned(Files(4, 4, 4, 4, 4));

        // First fit, not an even spread: 4+4 then 4+4 then 4, never 4+4+4+4+4
        // smoothed into three discs of seven.
        Assert.Equal(3, layout.Discs.Count);
        Assert.Equal([8L, 8L, 4L], layout.Discs.Select(disc => disc.Bytes));
    }

    [Fact]
    public void Numbers_the_discs_from_one_with_no_gaps()
    {
        DiscLayout layout = Planned(Files(9, 9, 9, 9));

        Assert.Equal([1, 2, 3, 4], layout.Discs.Select(disc => disc.Number));
    }

    [Fact]
    public void Keeps_the_order_it_was_given()
    {
        DiscLayout layout = Planned([
            new PlanFile("second.bin", @"C:\a", 6),
            new PlanFile("first.exe", @"C:\b", 3)
        ]);

        // Reordering these would save nothing here, and the point is that it is
        // never attempted: the order on the disc is the order it was handed.
        Assert.Equal("second.bin", layout.Discs[0].Files[0].RelativePath);
        Assert.Equal("first.exe", layout.Discs[0].Files[1].RelativePath);
    }

    [Fact]
    public void Says_what_is_left_for_files_after_the_menu_takes_its_share()
    {
        DiscLayout layout = Assert.IsType<Outcome<DiscLayout>.Ok>(
            DiscPlanner.Plan(Request(Files(1), overhead: 4))).Value;

        Assert.Equal(6, layout.RoomPerDisc);
    }

    [Fact]
    public void Refuses_when_the_menu_and_artwork_leave_no_room_at_all()
    {
        Refusal refusal = Refused(Request(Files(1), overhead: 10));

        Assert.Contains("no room", refusal.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, refusal.Figures.Count);
        Assert.Equal(RemedyKind.ChangeMedium, refusal.Remedy?.Kind);
    }

    [Fact]
    public void Refuses_to_plan_nothing()
    {
        Refusal refusal = Refused(Request([]));

        Assert.Contains("nothing", refusal.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_a_file_bigger_than_a_disc_and_names_it()
    {
        Refusal refusal = Refused(Request([
            new PlanFile("small.exe", @"C:\a", 2),
            new PlanFile("setup_the_witcher_3-1.bin", @"C:\b", 11)
        ]));

        // Named, because the answer is a bigger blank and the person needs to
        // know which file decided that.
        Assert.Contains("setup_the_witcher_3-1.bin", refusal.Because, StringComparison.Ordinal);
        Assert.DoesNotContain("small.exe", refusal.Because, StringComparison.Ordinal);
        Assert.Equal(RemedyKind.ChangeMedium, refusal.Remedy?.Kind);
    }

    [Fact]
    public void Gives_a_refusal_the_numbers_as_well_as_the_sentence()
    {
        Refusal refusal = Refused(Request([new PlanFile("big.bin", @"C:\a", 11)]));

        // The window has room for the figures and not for the sentence, and
        // reading them back out of our own prose would be parsing our writing.
        Assert.Equal(2, refusal.Figures.Count);
        Assert.Contains(refusal.Figures, figure => figure.Label == "the file");
        Assert.Contains(refusal.Figures, figure => figure.Label == "a disc leaves");
    }

    [Fact]
    public void Cuts_a_file_too_big_for_one_disc_when_it_is_allowed_to()
    {
        DiscLayout layout = Planned([new PlanFile("huge.bin", @"C:\a", 25)], cut: true);

        Assert.Equal(3, layout.Discs.Count);
        Assert.Equal([10L, 10L, 5L], layout.Discs.Select(disc => disc.Bytes));
    }

    [Fact]
    public void Names_the_pieces_the_way_the_join_will_look_for_them()
    {
        DiscLayout layout = Planned([new PlanFile("huge.bin", @"C:\a", 25)], cut: true);

        // Exactly as the shipped app writes them, because a disc built by
        // either version has to be readable by the other.
        Assert.Equal(
            ["huge.bin.dwpart01", "huge.bin.dwpart02", "huge.bin.dwpart03"],
            layout.Discs.Select(disc => disc.Files[0].RelativePath));
    }

    [Fact]
    public void Pads_a_piece_number_to_the_width_of_the_count()
    {
        DiscLayout layout = Planned([new PlanFile("vast.bin", @"C:\a", 100)], cut: true);

        // Ten pieces, so two digits, and the first is 01 rather than 1: the
        // pieces have to sort in the order they go back together.
        Assert.Equal(10, layout.Discs.Count);
        Assert.Equal("vast.bin.dwpart01", layout.Discs[0].Files[0].RelativePath);
        Assert.Equal("vast.bin.dwpart10", layout.Discs[9].Files[0].RelativePath);
    }

    [Fact]
    public void Keeps_every_byte_of_a_file_it_cuts()
    {
        DiscLayout layout = Planned([new PlanFile("huge.bin", @"C:\a", 25)], cut: true);

        IEnumerable<PlannedFile> pieces = layout.Discs.SelectMany(disc => disc.Files);

        Assert.Equal(25, pieces.Sum(piece => piece.Bytes));
        Assert.Equal([0L, 10L, 20L], pieces.Select(piece => piece.Part!.Offset));
        Assert.All(pieces, piece => Assert.Equal(25, piece.Part!.WholeBytes));
        Assert.All(pieces, piece => Assert.Equal(3, piece.Part!.Count));
    }

    [Fact]
    public void Starts_the_pieces_of_a_cut_file_on_a_disc_of_their_own()
    {
        DiscLayout layout = Planned([
            new PlanFile("readme.txt", @"C:\a", 2),
            new PlanFile("huge.bin", @"C:\b", 15)
        ], cut: true);

        // readme.txt had room beside the first piece, and still does not share
        // it: the pieces run in order so disc 2 obviously holds piece 1.
        Assert.Equal(3, layout.Discs.Count);
        Assert.Equal("readme.txt", Assert.Single(layout.Discs[0].Files).RelativePath);
        Assert.Equal("huge.bin.dwpart01", Assert.Single(layout.Discs[1].Files).RelativePath);
        Assert.Equal("huge.bin.dwpart02", Assert.Single(layout.Discs[2].Files).RelativePath);
    }

    [Fact]
    public void Lets_the_files_after_a_cut_share_the_last_piece_disc()
    {
        DiscLayout layout = Planned([
            new PlanFile("huge.bin", @"C:\a", 15),
            new PlanFile("readme.txt", @"C:\b", 2)
        ], cut: true);

        // The last piece is 5 bytes on a 10 byte disc, so the 2 byte file goes
        // beside it rather than starting a third disc for itself.
        Assert.Equal(2, layout.Discs.Count);
        Assert.Equal(
            ["huge.bin.dwpart02", "readme.txt"],
            layout.Discs[1].Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void Leaves_an_ordinary_file_with_no_piece_number()
    {
        DiscLayout layout = Planned(Files(3));

        Assert.Null(Assert.Single(layout.Discs[0].Files).Part);
    }

    [Fact]
    public void Puts_the_extras_on_disc_one_the_disc_everybody_puts_in_first()
    {
        DiscLayout layout = Planned(Files(6, 4), extras: 3);

        // They rode the last disc until 2026-11-09, and disc 1 of a set had
        // no Manual and no Extras on its menu: the owner previewed his set
        // and found neither. Their room comes off disc 1 before anything is
        // laid down, so the 6 fits beside them and the 4 moves on.
        Assert.Equal(1, layout.DiscCarryingTheExtras);
        Assert.Equal(2, layout.Discs.Count);
        Assert.Equal([6L], layout.Discs[0].Files.Select(file => file.Bytes));
        Assert.Equal([4L], layout.Discs[1].Files.Select(file => file.Bytes));
    }

    [Fact]
    public void A_file_that_will_not_fit_beside_the_extras_moves_on_to_disc_two()
    {
        DiscLayout layout = Planned(Files(10), extras: 3);

        // The price the owner chose knowingly: without cutting, a disc 1
        // that would have been full now carries the extras and nothing
        // else, and the file starts disc 2.
        Assert.Equal(2, layout.Discs.Count);
        Assert.Equal(1, layout.DiscCarryingTheExtras);
        Assert.Empty(layout.Discs[0].Files);
        Assert.Equal([10L], layout.Discs[1].Files.Select(file => file.Bytes));
    }

    [Fact]
    public void A_cut_file_shares_disc_one_with_the_extras_instead_of_costing_a_disc()
    {
        DiscLayout layout = Planned(Files(15), extras: 3, cut: true);

        // The owner's own case: a Hollow Knight installer bigger than a CD,
        // with a manual and the press kit. Starting the pieces on a fresh
        // disc would leave disc 1 holding only the extras and make it a set
        // of three. The first piece takes the 7 disc 1 has left, the second
        // the 8 that remain, so it stays a set of two.
        Assert.Equal(2, layout.Discs.Count);
        Assert.Equal(1, layout.DiscCarryingTheExtras);

        PlannedFile first = Assert.Single(layout.Discs[0].Files);
        PlannedFile second = Assert.Single(layout.Discs[1].Files);

        Assert.Equal(7, first.Bytes);
        Assert.Equal(new FilePart(1, 2, 0, 15), first.Part);
        Assert.Equal(8, second.Bytes);
        Assert.Equal(new FilePart(2, 2, 7, 15), second.Part);
    }

    [Fact]
    public void Extras_that_fill_disc_one_send_a_cut_file_to_disc_two()
    {
        DiscLayout layout = Planned(Files(15), extras: 10, cut: true);

        // Nothing is left beside them, so no empty first piece is made: the
        // pieces start on disc 2 at full size.
        Assert.Equal(1, layout.DiscCarryingTheExtras);
        Assert.Empty(layout.Discs[0].Files);
        Assert.Equal([10L, 5L], layout.Discs.Skip(1).Select(disc => disc.Files.Single().Bytes));
    }

    [Fact]
    public void Says_no_disc_carries_extras_when_there_are_none()
    {
        DiscLayout layout = Planned(Files(3));

        Assert.Equal(0, layout.DiscCarryingTheExtras);
    }

    [Fact]
    public void Refuses_extras_that_will_not_fit_on_any_disc()
    {
        Refusal refusal = Refused(Request(Files(3), extras: 11));

        Assert.Contains("extras", refusal.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RemedyKind.ChangeMedium, refusal.Remedy?.Kind);
    }

    // ------------------------------------------------------------------ helpers

    private static PlanFile[] Files(params long[] sizes) =>
        [.. sizes.Select((size, at) => new PlanFile($"file{at}.bin", $@"C:\source\file{at}.bin", size))];

    private static PlanRequest Request(
        IReadOnlyList<PlanFile> files,
        long overhead = 0,
        long extras = 0,
        bool cut = false) =>
        new(files, TenBytes, overhead, extras, cut);

    private static DiscLayout Planned(IReadOnlyList<PlanFile> files, long extras = 0, bool cut = false) =>
        Assert.IsType<Outcome<DiscLayout>.Ok>(
            DiscPlanner.Plan(Request(files, extras: extras, cut: cut))).Value;

    private static Refusal Refused(PlanRequest request) =>
        Assert.IsType<Outcome<DiscLayout>.No>(DiscPlanner.Plan(request)).Refusal;
}
