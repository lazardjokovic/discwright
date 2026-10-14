using DiscWright.Core;

namespace DiscWright.Tests;

public class TheMediaTable
{
    [Fact]
    public void Offers_the_discs_smallest_first()
    {
        long[] capacities = [.. Media.All.Select(medium => medium.Capacity)];

        // The order is not decoration. A recommendation walks the list and stops
        // at the first disc that fits, so out of order it would recommend a
        // Blu-ray for a 300 MB game.
        Assert.Equal(capacities.Order(), capacities);
    }

    [Fact]
    public void Gives_every_disc_a_distinct_key()
    {
        string[] keys = [.. Media.All.Select(medium => medium.Key)];

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Plans_against_usable_capacity_rather_than_the_number_on_the_box()
    {
        // A DVD5 is sold as 4.7 GB and holds 4.37 GiB. Planning against the box
        // number produces a disc that fails at 99 per cent.
        Assert.True(Media.Dvd5.Capacity < 4_700_000_000L, "A DVD5 must not be planned as the 4.7 GB on the box.");
    }

    [Fact]
    public void Turns_the_tier_figures_into_the_byte_counts_it_plans_with()
    {
        // The left side is the specification and the right side is arithmetic.
        //
        // This test first read Assert.Equal(Gib(0.68), Media.Cd.Capacity), with
        // the same formula on both sides, which proved only that nobody had
        // changed the number: a wrong formula passed it happily. Written as
        // literals it checks the conversion as well as the figures.
        //
        // The figures themselves come from Get-MediaTiers in v1, so a disc laid
        // out by either version comes out the same. Cross-checking them against
        // that source belongs in a tool rather than here, because the v1
        // repository is not on every machine this runs on.
        Assert.Equal(730_144_440L, Media.Cd.Capacity);
        Assert.Equal(4_692_251_770L, Media.Dvd5.Capacity);
        Assert.Equal(8_536_247_500L, Media.Dvd9.Capacity);
        Assert.Equal(25_018_184_499L, Media.Bd25.Capacity);
        Assert.Equal(50_036_368_998L, Media.Bd50.Capacity);
        Assert.Equal(99_857_989_632L, Media.BdXl.Capacity);
    }

    [Fact]
    public void Finds_a_key_whatever_its_case_because_it_came_out_of_a_saved_file()
    {
        Assert.Equal(Media.Dvd9, Media.ByKey("DVD9"));
        Assert.Equal(Media.Dvd9, Media.ByKey("dvd9"));
        Assert.Equal(Media.Dvd9, Media.ByKey("Dvd9"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DVD7")]
    [InlineData("HD-DVD")]
    public void Answers_null_for_a_key_it_does_not_know_rather_than_guessing(string? key)
    {
        // Every project file in the manual testing kit was saved with an empty
        // MediaKey, so this is not a hypothetical: it is what the next eleven
        // projects opened by this app will contain. Guessing a disc size on
        // somebody's behalf is how a build fails at the end instead of refusing
        // at the start.
        Assert.Null(Media.ByKey(key));
    }
}
