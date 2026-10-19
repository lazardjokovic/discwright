using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Reading the size of disc somebody is using off the disc they have put in.
/// </summary>
/// <remarks>
/// The size dropdown said DVD5 4.7 GB with a 700 MB disc in the tray, and
/// a set that should have been two discs was built as one 735 MB disc for a
/// DVD nobody had.
/// </remarks>
public class TheDiscThatIsActuallyIn
{
    [Theory]
    [InlineData(3, 359_847, "CD")]      // a blank CD-RW
    [InlineData(2, 359_847, "CD")]      // a blank CD-R
    [InlineData(9, 2_298_496, "DVD5")]  // a blank DVD-R
    [InlineData(8, 4_173_824, "DVD9")]  // a blank DVD+R DL
    [InlineData(18, 12_219_392, "BD25")] // a blank BD-R
    [InlineData(19, 24_438_784, "BD50")] // a blank BD-RE DL
    public void Knows_which_size_a_blank_disc_is(int mediaType, long freeSectors, string expected)
    {
        Medium? found = TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", mediaType, true, freeSectors)]);

        Assert.Equal(expected, found?.Key);
    }

    [Fact]
    public void Takes_no_size_from_a_used_disc_whose_size_cannot_be_told()
    {
        // A DVD-RW nearly empty: what is LEFT on it is not how big it is, and
        // nothing in IMAPI's numbers tells a DVD5 from a DVD9. So it says
        // nothing rather than halving somebody's disc.
        // 2,295,000 sectors is 4.70 GB, which DOES fit a DVD5. The point is
        // that it must not be read that way: those are the sectors LEFT after
        // a small session, and the disc could as easily be a DVD9. An earlier
        // go at this test used a number just under the DVD5 figure, so it
        // passed whether the rule was there or not.
        Assert.Null(TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", 10, false, 2_295_000)]));

        // The same disc blank is answerable.
        Assert.Equal("DVD5", TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", 10, true, 2_298_496)])?.Key);
    }

    [Fact]
    public void A_CD_is_a_CD_whether_or_not_there_is_anything_on_it()
    {
        // A used CD-RW is the disc somebody actually has in the tray, and
        // there is only one size of CD this program aims at. Insisting on a
        // blank one left them being planned for a DVD, which is the entire
        // fault this exists to fix.
        Assert.Equal("CD", TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", 3, false, 358_000)])?.Key);
        Assert.Equal("CD", TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", 3, false, 1_000)])?.Key);
    }

    [Fact]
    public void Takes_no_size_from_an_empty_tray_or_a_pressed_film()
    {
        Assert.Null(TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", 0, false, 0)]));
        Assert.Null(TheDiscsThatAreIn.TheSizeToStartFrom([ADrive("D:", 4, false, 2_000_000)]));

        // A drive that could not be asked at all.
        Assert.Null(TheDiscsThatAreIn.TheSizeToStartFrom(
            [ADrive("D:", 3, true, 359_847) with { WhyNot = "the tray is open" }]));
    }

    [Fact]
    public void Refuses_to_guess_between_two_drives_holding_two_different_discs()
    {
        Medium? found = TheDiscsThatAreIn.TheSizeToStartFrom(
        [
            ADrive("D:", 3, true, 359_847),
            ADrive("E:", 9, true, 2_298_496)
        ]);

        // The same rule as everywhere else here: it refuses to guess between
        // drives rather than taking the first one and being quietly wrong.
        Assert.Null(found);
    }

    [Fact]
    public void Two_drives_agreeing_is_not_a_disagreement()
    {
        Medium? found = TheDiscsThatAreIn.TheSizeToStartFrom(
        [
            ADrive("D:", 3, true, 359_847),
            ADrive("E:", 2, true, 359_847)
        ]);

        Assert.Equal("CD", found?.Key);
    }

    [Fact]
    public void Says_so_when_the_disc_in_the_drive_is_not_the_size_being_aimed_at()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
            [ADrive("D:", 3, true, 359_847)], Media.Dvd5);

        Assert.Equal("The disc in D: is a CD-RW, and this is being built for DVD5 4.7 GB.", said);
    }

    [Fact]
    public void Says_nothing_when_the_disc_in_the_drive_is_the_right_size()
    {
        Assert.Null(TheDiscsThatAreIn.WhatIsWorthSaying([ADrive("D:", 3, true, 359_847)], Media.Cd));
    }

    [Fact]
    public void Says_nothing_when_there_is_no_disc_anywhere()
    {
        // Somebody who has not put a disc in yet has done nothing wrong.
        Assert.Null(TheDiscsThatAreIn.WhatIsWorthSaying([ADrive("D:", 0, false, 0)], Media.Dvd5));
        Assert.Null(TheDiscsThatAreIn.WhatIsWorthSaying([], Media.Dvd5));
    }

    [Fact]
    public void Says_nothing_when_one_of_the_drives_holds_the_right_disc()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
        [
            ADrive("D:", 9, true, 2_298_496),
            ADrive("E:", 3, true, 359_847)
        ],
            Media.Dvd5);

        // The disc that matters is in the machine. What is in the other drive
        // is not this sentence's business.
        Assert.Null(said);
    }

    [Fact]
    public void Names_the_disc_that_could_be_written_to_rather_than_a_film()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
        [
            ADrive("D:", 4, false, 2_000_000),
            ADrive("E:", 3, true, 359_847)
        ],
            Media.Dvd5);

        // A pressed film somebody left in the other drive is not the point.
        Assert.Equal("The disc in E: is a CD-RW, and this is being built for DVD5 4.7 GB.", said);
    }

    [Fact]
    public void Is_about_the_drive_that_could_take_the_burn_not_one_that_could_not()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
        [
            ADrive("D:", 9, true, 2_297_888),
            ADrive("E:", 3, false, 0)
        ],
            Media.Cd);

        // Measured on the real machine: a blank DVD-R in D: and a used CD-RW
        // in E:, aiming at a CD. Looking at both at once, the CD-RW was the
        // size being aimed at and the whole sentence went quiet, while the
        // only disc that could take the burn was the wrong one.
        Assert.Equal("The disc in D: is a DVD-R, and this is being built for CD-R 700 MB.", said);
    }

    [Fact]
    public void Says_nothing_when_the_drive_that_could_take_the_burn_holds_the_right_disc()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
        [
            ADrive("D:", 9, true, 2_297_888),
            ADrive("E:", 3, false, 0)
        ],
            Media.Dvd5);

        // The mirror of it. The used CD-RW is no more interesting the other
        // way round, and silence here is still the right answer.
        Assert.Null(said);
    }

    [Theory]
    [InlineData("D:", "The disc in D: is a DVD-R, and this is being built for CD-R 700 MB.")]
    [InlineData("E:", null)]
    public void Is_about_the_drive_somebody_chose_once_one_is_chosen(string chosen, string? expected)
    {
        // Two blank discs, either of which could take a burn. Which of them
        // this is about stops being a guess the moment somebody says.
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
        [
            ADrive("D:", 9, true, 2_297_888),
            ADrive("E:", 3, true, 359_847)
        ],
            Media.Cd,
            chosen);

        Assert.Equal(expected, said);
    }

    [Fact]
    public void Falls_back_to_every_drive_when_not_one_of_them_is_ready()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
        [
            ADrive("D:", 4, false, 0),
            ADrive("E:", 3, false, 0)
        ],
            Media.Dvd5);

        // Narrowing to the ready ones would narrow to none at all here, and
        // going quiet because nothing is ready is the opposite of useful:
        // there are two discs in the machine and neither is the one wanted.
        Assert.Equal("The disc in E: is a CD-RW, and this is being built for DVD5 4.7 GB.", said);
    }

    [Fact]
    public void Still_says_something_when_the_only_disc_is_one_nobody_can_write_to()
    {
        string? said = TheDiscsThatAreIn.WhatIsWorthSaying(
            [ADrive("D:", 17, false, 0)], Media.Dvd5);

        Assert.Equal("The disc in D: is a BD-ROM, pressed, and this is being built for DVD5 4.7 GB.", said);
    }

    // ---------------------------------------------------------- the tray

    [Fact]
    public void Says_so_when_the_drive_cannot_shut_its_own_tray()
    {
        string? said = TheDiscsThatAreIn.WhatToSayAboutTheTray(
            [ADrive("E:", 3, true, 359_847)], _ => false);

        // Short enough not to wrap, because it sits in a 340 pixel column.
        // The reason behind it is WhyTheTrayMatters, which the window hovers
        // for rather than spends room on.
        Assert.Equal("E: needs the tray pushed in", said);
    }

    [Fact]
    public void Says_nothing_when_the_drive_shuts_its_own_tray()
    {
        Assert.Null(TheDiscsThatAreIn.WhatToSayAboutTheTray(
            [ADrive("D:", 3, true, 359_847)], _ => true));
    }

    [Fact]
    public void Never_asks_about_a_drive_with_nothing_in_it()
    {
        List<string> asked = [];

        string? said = TheDiscsThatAreIn.WhatToSayAboutTheTray(
        [
            ADrive("D:", 0, false, 0),
            ADrive("E:", 3, true, 359_847)
        ],
            drive =>
            {
                asked.Add(drive.Drive);
                return true;
            });

        // The asking is done by shutting a tray that is already shut, so an
        // empty one would close for no reason anybody could see.
        Assert.Equal(["E:"], asked);
        Assert.Null(said);
    }

    [Fact]
    public void Asks_about_a_disc_that_would_have_to_be_erased_first()
    {
        List<string> asked = [];

        TheDiscsThatAreIn.WhatToSayAboutTheTray(
            [ADrive("D:", 3, false, 100_000)],
            drive =>
            {
                asked.Add(drive.Drive);
                return true;
            });

        // A used CD-RW is a disc somebody is about to burn to, once they have
        // said yes to erasing it, so its tray matters just as much.
        Assert.Equal(["D:"], asked);
    }

    [Fact]
    public void Names_both_when_neither_can_shut_itself()
    {
        string? said = TheDiscsThatAreIn.WhatToSayAboutTheTray(
        [
            ADrive("D:", 3, true, 359_847),
            ADrive("E:", 18, true, 12_219_392)
        ],
            _ => false);

        Assert.Equal("D: and E: need the tray pushed in", said);
    }

    private static Recorder ADrive(string letter, int mediaType, bool blank, long freeSectors) =>
        new("id" + letter, letter, "A Drive", mediaType, blank, freeSectors, [750]);
}
