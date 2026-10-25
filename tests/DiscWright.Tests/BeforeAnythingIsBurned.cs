using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Everything that can be settled by asking the drive a question.
/// </summary>
/// <remarks>
/// A CD-R is written once. Every mistake caught by asking costs nothing, and
/// every mistake not caught costs a disc, so everything answerable is answered
/// before the laser comes on.
/// </remarks>
public class BeforeAnythingIsBurned
{
    [Theory]
    [InlineData(2, "CD-R", true, false)]
    [InlineData(3, "CD-RW", true, true)]
    [InlineData(1, "CD-ROM, read only", false, false)]
    [InlineData(18, "BD-R", true, false)]
    [InlineData(19, "BD-RE", true, true)]
    [InlineData(4, "DVD-ROM, read only", false, false)]
    [InlineData(17, "BD-ROM, read only", false, false)]
    public void Knows_what_the_drive_means_by_a_number(int type, string name, bool writable, bool rewritable)
    {
        // A report that says "media type 9" is a report nobody can act on.
        Assert.Equal(name, OpticalMedia.NameOf(type));
        Assert.Equal(writable, OpticalMedia.CanBeWritten(type));
        Assert.Equal(rewritable, OpticalMedia.CanBeErased(type));
    }

    [Fact]
    public void Says_something_useful_about_a_number_it_does_not_know()
    {
        Assert.Contains("99", OpticalMedia.NameOf(99), StringComparison.Ordinal);
        Assert.False(OpticalMedia.CanBeWritten(99));
    }

    [Theory]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(2, false, false)]
    [InlineData(1, false, false)]
    public void Knows_when_erasing_would_help(int type, bool blank, bool wouldHelp)
    {
        // Three different questions: can it be written at all, is it the
        // rewritable kind, and is there anything on it. Erasing a blank is a
        // minute spent to achieve nothing, and a CD-R cannot be erased however
        // many times somebody wishes it could.
        Assert.Equal(wouldHelp, OpticalMedia.ErasingWouldHelp(type, blank));
    }

    [Fact]
    public void Measures_an_image_against_a_disc_in_sectors()
    {
        // 700 MB in 2 KiB sectors, exactly.
        Assert.True(OpticalMedia.Fits(700L * 1024 * 1024, 358_400));
        Assert.False(OpticalMedia.Fits((700L * 1024 * 1024) + 1, 358_400));
    }

    [Fact]
    public void Counts_a_part_used_sector_as_a_whole_one()
    {
        Assert.Equal(1, OpticalMedia.SectorsFor(1));
        Assert.Equal(1, OpticalMedia.SectorsFor(2048));
        Assert.Equal(2, OpticalMedia.SectorsFor(2049));
    }

    // ------------------------------------------------------------- speeds

    [Fact]
    public void Reads_the_speed_in_the_drive_the_way_the_disc_is_labelled()
    {
        // The real case, measured on this machine: a High Speed CD-RW in a
        // BD-RE drive offers 750 sectors a second. v1 called that 5x, which is
        // not a speed any CD-RW is made in. It is 10x, and the real burns said
        // so: 690,814,976 bytes in 496 seconds.
        Assert.Equal(10, WriteSpeeds.AsAMultiple(750, 3));
        Assert.Equal("10x", WriteSpeeds.InWords(750, 3));
    }

    [Theory]
    [InlineData(75, 3, "1x")]
    [InlineData(1380, 9, "2x")]
    [InlineData(2195, 18, "1x")]
    [InlineData(4568, 15, "1x")]
    public void Knows_what_one_times_is_for_each_kind_of_disc(int sectors, int type, string expected)
    {
        // Getting the base wrong halves or doubles every speed in the program.
        Assert.Equal(expected, WriteSpeeds.InWords(sectors, type));
    }

    [Fact]
    public void Writes_a_cd_no_faster_than_sixteen()
    {
        // Cheap media written at full speed is the usual way to make a coaster.
        int[] offered = [4125, 3000, 1800, 1200, 750];

        int? chosen = WriteSpeeds.Choose(offered, 3);

        Assert.NotNull(chosen);
        Assert.True(WriteSpeeds.AsAMultiple(chosen.Value, 3) <= 16);
    }

    [Fact]
    public void Writes_everything_else_no_faster_than_eight()
    {
        int[] offered = [11080, 8160, 5440, 2720, 1360];

        int? chosen = WriteSpeeds.Choose(offered, 9);

        Assert.NotNull(chosen);
        Assert.True(WriteSpeeds.AsAMultiple(chosen.Value, 9) <= 8);
    }

    [Fact]
    public void Takes_the_fastest_of_the_ones_that_are_slow_enough()
    {
        int[] offered = [1200, 750, 300];

        // 1200 is 16x on a CD, which is the cap exactly.
        Assert.Equal(1200, WriteSpeeds.Choose(offered, 3));
    }

    [Fact]
    public void Takes_the_slowest_on_offer_when_every_speed_is_a_fast_one()
    {
        // Refusing to write because every speed is fast would be a strange way
        // to protect somebody.
        int[] offered = [4125, 3000];

        Assert.Equal(3000, WriteSpeeds.Choose(offered, 3));
    }

    [Fact]
    public void Has_nothing_to_choose_from_when_the_drive_offered_nothing()
    {
        Assert.Null(WriteSpeeds.Choose([], 3));
    }

    // ------------------------------------------------------- which drive

    [Fact]
    public void Writes_to_the_one_drive_that_is_ready()
    {
        Outcome<Recorder> chosen = WhichRecorder.For(
            [ADrive("D:", 3, blank: false), ADrive("E:", 2, blank: true)],
            10_000_000);

        Assert.Equal("E:", Assert.IsType<Outcome<Recorder>.Ok>(chosen).Value.Drive);
    }

    [Fact]
    public void Refuses_to_choose_between_two_ready_drives()
    {
        Outcome<Recorder> chosen = WhichRecorder.For(
            [ADrive("D:", 2, blank: true), ADrive("E:", 2, blank: true)],
            10_000_000);

        // Guessing is how a disc ends up written in the wrong drive, and the
        // one in the other drive is often somebody's only blank.
        Refusal why = Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal;
        Assert.Contains("D:", why.Because, StringComparison.Ordinal);
        Assert.Contains("E:", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void Takes_the_drive_somebody_already_chose()
    {
        Outcome<Recorder> chosen = WhichRecorder.For(
            [ADrive("D:", 2, blank: true), ADrive("E:", 2, blank: true)],
            10_000_000,
            "E:");

        Assert.Equal("E:", Assert.IsType<Outcome<Recorder>.Ok>(chosen).Value.Drive);
    }

    [Fact]
    public void Says_when_erasing_the_disc_in_there_would_help()
    {
        Outcome<Recorder> chosen = WhichRecorder.For([ADrive("E:", 3, blank: false)], 10_000_000);

        // Said, never done. Erasing somebody's disc without asking is not a
        // thing to do quietly.
        Refusal why = Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal;
        Assert.Contains("CD-RW", why.Headline, StringComparison.Ordinal);
        Assert.Contains("Nothing has been erased", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_both_discs_when_either_of_them_could_be_erased()
    {
        Outcome<Recorder> chosen = WhichRecorder.For(
            [ADrive("D:", 3, blank: false), ADrive("E:", 3, blank: false)],
            10_000_000);

        // It used to fall through to "that disc cannot be written to", which is
        // false about a rewritable one and is the wording that makes somebody
        // bin a usable disc. Which one goes is theirs to decide, so both are
        // named and nothing is erased.
        Refusal why = Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal;
        Assert.Contains("More than one", why.Headline, StringComparison.Ordinal);
        Assert.Contains("D:", why.Because, StringComparison.Ordinal);
        Assert.Contains("E:", why.Because, StringComparison.Ordinal);
        Assert.Contains("nothing has been erased", why.Because, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Says_when_what_is_in_there_can_never_be_written_to()
    {
        Outcome<Recorder> chosen = WhichRecorder.For([ADrive("E:", 1, blank: false)], 10_000_000);

        Refusal why = Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal;
        Assert.Contains("cannot be written", why.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_when_the_image_is_bigger_than_the_disc()
    {
        Outcome<Recorder> chosen = WhichRecorder.For(
            [ADrive("E:", 2, blank: true, freeSectors: 1000)],
            10_000_000);

        Refusal why = Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal;
        Assert.Contains("too small", why.Headline, StringComparison.Ordinal);
        Assert.Contains("Nothing was written", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_when_there_is_no_writer_at_all()
    {
        Outcome<Recorder> chosen = WhichRecorder.For([], 10_000_000);

        // Not a disaster: the image is made and any machine with a writer can
        // burn it.
        Refusal why = Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal;
        Assert.Contains("output folder", why.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_which_tray_to_put_a_blank_in()
    {
        Outcome<Recorder> chosen = WhichRecorder.For([ANothingIn("D:")], 10_000_000);

        Assert.Contains("D:", Assert.IsType<Outcome<Recorder>.No>(chosen).Refusal.Because, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static Recorder ADrive(string drive, int mediaType, bool blank, long freeSectors = 360_000) =>
        new($"id-{drive}", drive, "A Drive", mediaType, blank, freeSectors, [750]);

    private static Recorder ANothingIn(string drive) =>
        new($"id-{drive}", drive, "A Drive", 0, false, 0, [], "the tray is empty");
}
