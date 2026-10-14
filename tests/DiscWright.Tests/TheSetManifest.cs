using DiscWright.Core;

namespace DiscWright.Tests;

public class TheSetManifest
{
    [Fact]
    public void Lists_every_file_on_every_disc_of_the_set()
    {
        string file = Written();

        // The same file on every disc, so any ONE disc can tell you what the
        // whole set should be. Somebody who finds disc 2 in a drawer gets a
        // disc that knows what it belongs to.
        Assert.Contains("*setup.exe", file, StringComparison.Ordinal);
        Assert.Contains("*big.bin.dwpart01", file, StringComparison.Ordinal);
        Assert.Contains("*big.bin.dwpart02", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_every_sentence_out_of_the_parsers()
    {
        string file = Written();

        // Three things read this: the snippet inside it, the menu, and a
        // person. All three take a row to be a line that starts with a digit
        // and holds a star, so every other line has to be indented or it is
        // read as a file that does not exist.
        foreach (string line in file.Split('\n').Where(line => line.Length > 0 && char.IsDigit(line[0])))
        {
            Assert.Contains(" *", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Says_which_disc_you_are_holding()
    {
        Assert.Contains("<- the disc you are holding", Written(thisDisc: 2), StringComparison.Ordinal);

        string[] marked =
        [
            .. Written(thisDisc: 2).Split('\n').Where(line => line.Contains("<-", StringComparison.Ordinal))
        ];

        Assert.Single(marked);
        Assert.StartsWith("  2 ", marked[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Says_what_cannot_be_done_with_one_disc_alone()
    {
        string file = Written();

        // The thing somebody holding disc 2 most needs to know, before they
        // try to run an installer that is only half there.
        Assert.Contains("cannot install the game on its own", file, StringComparison.Ordinal);
        Assert.Contains("Copy the contents of every disc", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Can_be_checked_by_hand_with_nothing_installed()
    {
        string file = Written();

        Assert.Contains("Get-FileHash", file, StringComparison.Ordinal);
        Assert.Contains("Silence means every file is there", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_the_same_set_id_to_every_disc_of_one_burn()
    {
        SetEntry[] entries = Entries();

        // The same on every disc of this burn, which is the whole point: two
        // discs that agree were built together.
        Assert.Equal(SetManifest.FingerprintOf(entries), SetManifest.FingerprintOf(entries));
        Assert.Contains(SetManifest.FingerprintOf(entries), Written(thisDisc: 1), StringComparison.Ordinal);
        Assert.Contains(SetManifest.FingerprintOf(entries), Written(thisDisc: 2), StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_a_different_set_id_to_a_different_burn()
    {
        SetEntry[] one = Entries();
        SetEntry[] other = [.. Entries().Select(entry =>
            entry with { Sha256 = entry.Sha256.Replace('a', 'b') })];

        // What a failed join cannot tell you. A join that produces the wrong
        // hash says the finished file is wrong; it does not say disc 2 came
        // from a later download than disc 1.
        Assert.NotEqual(SetManifest.FingerprintOf(one), SetManifest.FingerprintOf(other));
    }

    [Fact]
    public void Does_not_care_what_order_it_was_handed_the_files_in()
    {
        SetEntry[] forwards = Entries();
        SetEntry[] backwards = [.. Entries().Reverse()];

        // Two builds of the same thing have to agree, or the id says they
        // differ when only the walk order did.
        Assert.Equal(SetManifest.FingerprintOf(forwards), SetManifest.FingerprintOf(backwards));
    }

    [Fact]
    public void Is_twelve_characters_of_hexadecimal_and_nothing_more()
    {
        string id = SetManifest.FingerprintOf(Entries());

        // Not a security measure, a label: enough that two burns of the same
        // game in the same week cannot be confused for one another.
        Assert.Equal(12, id.Length);
        Assert.All(id, character => Assert.Contains(character, "0123456789abcdef"));
    }

    [Fact]
    public void Counts_what_is_on_this_disc_and_what_is_in_the_set()
    {
        string file = Written(thisDisc: 1);

        Assert.Contains("3 files in the set", file, StringComparison.Ordinal);
        Assert.Contains("On this disc: 2 of the 3 files.", file, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static SetEntry[] Entries() =>
    [
        new(1, "setup.exe", "aaaa1111", 1000),
        new(1, "big.bin.dwpart01", "bbbb2222", 2000),
        new(2, "big.bin.dwpart02", "cccc3333", 500)
    ];

    private static string Written(int thisDisc = 1) =>
        SetManifest.For("HOLLOW KNIGHT", Entries(), thisDisc, 2, "DVD5 4.7 GB", DateTimeOffset.Parse("2026-10-14T19:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
