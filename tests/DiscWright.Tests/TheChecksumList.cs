using DiscWright.Core;

namespace DiscWright.Tests;

public class TheChecksumList
{
    [Fact]
    public void Is_written_in_the_format_the_real_tool_reads()
    {
        string list = ChecksumList.For("HOLLOW KNIGHT", [Entry("setup.exe", "abc123", 10)]);

        // sha256sum format: the hash, a space, a star for binary mode, the
        // path. Nothing from this program is needed to read it.
        Assert.Contains("abc123 *setup.exe", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_forward_slashes_even_though_this_is_Windows()
    {
        string list = ChecksumList.For("X", [Entry(@"Data\sound\music.ogg", "aa", 1)]);

        // What sha256sum expects, and Windows accepts them in a path just as
        // happily, so there is nothing to lose.
        Assert.Contains("aa *Data/sound/music.ogg", list, StringComparison.Ordinal);

        // Asserted on the hash LINES rather than the whole file. The header's
        // PowerShell instructions contain a backslash of their own, in the
        // pattern that splits a line on " \*", and a blanket check caught that
        // instead of catching a path.
        Assert.DoesNotContain(
            list.Split('\n').Where(line => !line.StartsWith('#')),
            line => line.Contains('\\', StringComparison.Ordinal));
    }

    [Fact]
    public void Ends_every_line_with_a_newline_and_never_a_carriage_return()
    {
        string list = ChecksumList.For("X", [Entry("a.bin", "aa", 1), Entry("b.bin", "bb", 2)]);

        // The opposite of every other file this program writes, and found with
        // the real tool, which is the only way it could have been found:
        // sha256sum -c takes a carriage return as part of the filename and
        // reports every single line as a missing file.
        Assert.DoesNotContain("\r", list, StringComparison.Ordinal);
        Assert.EndsWith("\n", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_how_many_files_there_are_so_a_truncated_list_can_be_caught()
    {
        string list = ChecksumList.For("X", [Entry("a.bin", "aa", 100), Entry("b.bin", "bb", 23)]);

        // The single way a checksum list can lie to you: one that has lost its
        // tail verifies the files it still mentions and reports every one of
        // them right. The count is here to be CHECKED, not only read.
        Assert.Contains("# 2 files, 123 bytes, SHA-256, sha256sum format.", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Tells_somebody_how_to_check_it_with_tools_they_already_have()
    {
        string list = ChecksumList.For("X", [Entry("a.bin", "aa", 1)]);

        // The ten year promise in practice: whoever finds this disc does not
        // need this program, and may not be able to get it.
        Assert.Contains("sha256sum -c checksums.sha256", list, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Puts_the_instructions_where_the_format_already_skips_them()
    {
        string list = ChecksumList.For("X", [Entry("a.bin", "aa", 1)]);

        string[] beforeTheFirstHash =
            [.. list.Split('\n').TakeWhile(line => !line.StartsWith("aa ", StringComparison.Ordinal))];

        // Every header line is a comment, so sha256sum -c reads the file
        // without being told to ignore anything.
        Assert.All(beforeTheFirstHash, line => Assert.StartsWith("#", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Lists_the_files_in_the_same_order_every_time()
    {
        string one = ChecksumList.For("X", [Entry("b.bin", "bb", 1), Entry("a.bin", "aa", 1)]);
        string two = ChecksumList.For("X", [Entry("a.bin", "aa", 1), Entry("b.bin", "bb", 1)]);

        // Two builds of the same disc produce the same list, so a difference
        // between them means something.
        Assert.Equal(one, two);
    }

    [Fact]
    public void Names_the_disc_it_belongs_to()
    {
        string list = ChecksumList.For("HOLLOW KNIGHT", [Entry("a.bin", "aa", 1)]);

        Assert.StartsWith("# DiscWright checksum list for HOLLOW KNIGHT\n", list, StringComparison.Ordinal);
    }

    private static ChecksumEntry Entry(string path, string hash, long bytes) => new(path, hash, bytes);
}
