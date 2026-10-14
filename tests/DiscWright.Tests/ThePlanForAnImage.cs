using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Everything decided before an image is made, which is all of the parts that
/// have ever been wrong.
/// </summary>
public class ThePlanForAnImage
{
    private const long Gib = 1024L * 1024 * 1024;

    [Theory]
    [InlineData("HOLLOW KNIGHT", "HOLLOW_KNIGHT")]
    [InlineData("The Witcher: Enhanced", "The_Witcher__Enh")]
    [InlineData("A", "A")]
    [InlineData("1234567890123456789", "1234567890123456")]
    public void Makes_a_volume_name_out_of_the_label(string label, string expected)
    {
        // Sixteen characters, letters, digits and underscores. Everything else
        // becomes an underscore, which can turn two different labels into one
        // volume name, and that is fine: this is what a drive shows in This PC,
        // not anything the disc is identified by.
        string name = ImagePlan.For(label, Gib, 1000, false).VolumeName;

        Assert.Equal(expected.Length > 16 ? expected[..16] : expected, name);
        Assert.True(name.Length <= 16);
    }

    [Fact]
    public void Sizes_the_medium_to_what_is_going_on_it()
    {
        // v1 used a fixed twelve and a half million blocks, about 25 GB, which
        // silently capped every build at a quarter of a BD-R XL.
        long blocks = ImagePlan.For("BIG", 90 * Gib, Gib, false).Blocks;

        Assert.True(blocks > 12_500_000, $"90 GB asked for only {blocks} blocks");
        Assert.True(blocks * 2048 > 90 * Gib, "the medium is smaller than what goes on it");
    }

    [Fact]
    public void Never_asks_for_a_medium_smaller_than_the_old_fixed_one()
    {
        // Nothing smaller has ever been a problem, and a tiny medium is one
        // more thing that could refuse a disc that would have fitted.
        Assert.Equal(12_500_000, ImagePlan.For("SMALL", 1000, 1000, false).Blocks);
    }

    [Fact]
    public void Does_not_ask_for_more_than_the_writer_can_count()
    {
        Assert.True(ImagePlan.For("HUGE", 100_000 * Gib, Gib, false).Blocks <= 2_147_483_000);
    }

    [Fact]
    public void Leaves_the_older_file_systems_on_when_everything_fits_them()
    {
        ImagePlan plan = ImagePlan.For("OLD", 3 * Gib, Gib, wantsLegacyFileSystems: true);

        Assert.True(plan.LegacyFileSystems);
        Assert.Null(plan.Worry);
    }

    [Fact]
    public void Takes_the_older_file_systems_off_for_a_file_they_cannot_hold()
    {
        // A GOG installer part is 2 GiB minus one byte, so a multi-part game is
        // always under it, but a single big file is not.
        ImagePlan plan = ImagePlan.For("BIG", 5 * Gib, 3 * Gib, wantsLegacyFileSystems: true);

        // The image is still made. A disc that reads everywhere except on a
        // twenty-five year old machine is worth far more than no disc.
        Assert.False(plan.LegacyFileSystems);
        Assert.NotNull(plan.Worry);
        Assert.Contains("left off", plan.Worry, StringComparison.Ordinal);
    }

    [Fact]
    public void Knows_the_ceiling_is_two_gibibytes_and_not_four()
    {
        // The format allows four and the writer stops at two. v1 carried a
        // comment claiming the opposite and a real build died on it after
        // copying 7.79 GB.
        Assert.Equal(2 * Gib, ImagePlan.BiggestFileTheOldWaysCanTake);

        Assert.True(ImagePlan.For("X", 3 * Gib, 2 * Gib, true).LegacyFileSystems);
        Assert.False(ImagePlan.For("X", 3 * Gib, (2 * Gib) + 1, true).LegacyFileSystems);
    }

    [Fact]
    public void Says_nothing_about_file_systems_nobody_asked_for()
    {
        ImagePlan plan = ImagePlan.For("BIG", 5 * Gib, 3 * Gib, wantsLegacyFileSystems: false);

        Assert.False(plan.LegacyFileSystems);
        Assert.Null(plan.Worry);
    }

    [Theory]
    [InlineData(700L * 1024 * 1024)]
    [InlineData(4L * 1024 * 1024 * 1024)]
    [InlineData(100L * 1024 * 1024 * 1024)]
    public void Reports_about_two_hundred_times_whatever_the_size(long payload)
    {
        ImagePlan plan = ImagePlan.For("X", payload, 1000, false);
        long blocks = payload / 2048;
        long reports = blocks / plan.ReportEvery;

        // A fixed interval fires a handful of times on a CD and tens of
        // thousands of times on a BD-R XL.
        Assert.InRange(reports, 100, 260);
    }

    [Fact]
    public void Still_reports_on_something_tiny()
    {
        Assert.True(ImagePlan.For("X", 2048, 1000, false).ReportEvery >= 256);
    }
}
