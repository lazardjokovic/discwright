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
        // A DVD5 is sold as 4.7 GB and holds 4.37 GiB. Planning against 4.7 GB
        // produces a disc that fails at 99 per cent, so this is the one figure
        // in the table worth pinning by hand.
        Assert.Equal((long)(4.37 * 1024 * 1024 * 1024), Media.Dvd5.Capacity);
        Assert.True(Media.Dvd5.Capacity < 4_700_000_000, "A DVD5 must not be planned as the 4.7 GB on the box.");
    }

    [Fact]
    public void Matches_the_capacities_the_shipped_app_plans_against()
    {
        // Carried over from Get-MediaTiers in v1, so a disc laid out by either
        // version comes out the same. If one of these ever has to change, the
        // other app has to change with it.
        Assert.Equal(Gib(0.68), Media.Cd.Capacity);
        Assert.Equal(Gib(4.37), Media.Dvd5.Capacity);
        Assert.Equal(Gib(7.95), Media.Dvd9.Capacity);
        Assert.Equal(Gib(23.3), Media.Bd25.Capacity);
        Assert.Equal(Gib(46.6), Media.Bd50.Capacity);
        Assert.Equal(Gib(93.0), Media.BdXl.Capacity);
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

    private static long Gib(double gibibytes) => (long)(gibibytes * 1024 * 1024 * 1024);
}
