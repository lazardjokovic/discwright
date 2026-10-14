using System.Globalization;

using DiscWright.Core;

namespace DiscWright.Tests;

public class ByteSizes
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(817, "817 bytes")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void Reads_the_way_Explorer_writes_the_same_number(long bytes, string expected)
    {
        using CultureSwitch _ = new("en-GB");

        Assert.Equal(expected, ByteSize.Describe(bytes));
    }

    [Fact]
    public void Tells_two_discs_apart_that_one_decimal_would_merge()
    {
        using CultureSwitch _ = new("en-GB");

        // The whole reason gigabytes get two decimals. A DVD5 holds 4.37 GB and
        // is sold as 4.7 GB, and at one decimal those print as 4.4 and 4.7,
        // which reads as a rounding error rather than as two different discs.
        Assert.Equal("4.37 GB", ByteSize.Describe(Media.Dvd5.Capacity));
        Assert.Equal("7.95 GB", ByteSize.Describe(Media.Dvd9.Capacity));
        Assert.Equal("23.3 GB", ByteSize.Describe(Media.Bd25.Capacity));
    }

    [Fact]
    public void Describes_the_file_the_witcher_3_refusal_is_about()
    {
        using CultureSwitch _ = new("en-GB");

        // The sparse file in the testing kit, folder M, which exists to be
        // refused. Its refusal reads "the file 6.1 GB" against "a DVD5 holds
        // 4.37 GB", and those two strings both come from here.
        Assert.Equal("6.1 GB", ByteSize.Describe((long)(6.1 * 1024 * 1024 * 1024)));
    }

    [Fact]
    public void Follows_the_machine_it_is_running_on()
    {
        using CultureSwitch _ = new("sr-Latn-RS");

        // Serbian writes a decimal comma. This is what InvariantGlobalization
        // would have quietly taken away, which is why it is off.
        Assert.Equal("4,37 GB", ByteSize.Describe(Media.Dvd5.Capacity));
    }

    [Fact]
    public void Refuses_a_negative_size_rather_than_printing_one()
    {
        // A negative size is a mistake in the caller. Printing "-2 GB" would
        // send it onward into a refusal somebody then has to make sense of.
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSize.Describe(-1));
    }

    /// <summary>
    /// Puts the thread's culture back however the test ends, so one test cannot
    /// leave the next one running in Serbian.
    /// </summary>
    private sealed class CultureSwitch : IDisposable
    {
        private readonly CultureInfo _was;

        public CultureSwitch(string name)
        {
            _was = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _was;
    }
}
