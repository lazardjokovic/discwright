using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Checking a disc against the checksum list it carries, which is the only
/// way to check one when the folder it was built from is not here.
/// </summary>
public sealed class ADiscCheckedAgainstItsOwnList
{
    [Fact]
    public async Task Says_the_disc_is_the_disc_when_every_file_matches()
    {
        ADisc disc = new(
            ("autorun.inf", "aaa"),
            ("game.exe", "bbb"),
            (@"Add-ons\patch.exe", "ccc"));

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        Assert.True(check.Good);
        Assert.Equal(3, check.Matched);
        Assert.Empty(check.Wrong);
        Assert.Empty(check.Missing);
        Assert.Null(check.WhyNot);
    }

    [Fact]
    public async Task Catches_a_file_that_came_back_different()
    {
        ADisc disc = new(("autorun.inf", "aaa"), ("game.exe", "bbb"));
        disc.ActuallyReads["game.exe"] = "something else entirely";

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        Assert.False(check.Good);
        Assert.Equal("game.exe", Assert.Single(check.Wrong));
    }

    [Fact]
    public async Task Catches_a_file_the_drive_will_not_read_back()
    {
        ADisc disc = new(("autorun.inf", "aaa"), ("game.exe", "bbb"));
        disc.WillNotRead.Add("game.exe");

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        // The whole reason for reading a disc back rather than trusting the
        // write: a cyclic redundancy check is how a bad disc announces itself,
        // and one did exactly that on Lazar's first attempt at disc 1.
        Assert.False(check.Good);
        Assert.Equal("game.exe", Assert.Single(check.Unreadable));
    }

    [Fact]
    public async Task Catches_a_file_that_is_not_on_the_disc_at_all()
    {
        ADisc disc = new(("autorun.inf", "aaa"), ("game.exe", "bbb"));
        disc.LeaveOff.Add("game.exe");

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        Assert.False(check.Good);
        Assert.Equal("game.exe", Assert.Single(check.Missing));
    }

    [Fact]
    public async Task Refuses_a_list_that_lost_its_tail()
    {
        // The single way a checksum list can lie: drop half its lines and it
        // verifies everything it still mentions and reports them all right.
        // The count in its own header is there to be checked, not read.
        ADisc disc = new(("autorun.inf", "aaa"), ("game.exe", "bbb"), ("extra.dat", "ccc"))
        {
            ListSaysItHas = 9
        };

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        Assert.False(check.Good);
        Assert.Contains("says it has 9 files and lists 3", check.WhyNot!, StringComparison.Ordinal);
        Assert.Equal(0, check.Matched);
    }

    [Fact]
    public async Task Says_so_when_the_disc_carries_no_list()
    {
        ADisc disc = new(("autorun.inf", "aaa")) { HasAList = false };

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        Assert.False(check.Good);
        Assert.Contains("no checksums.sha256 on the disc", check.WhyNot!, StringComparison.Ordinal);
        Assert.False(TheDiscAgainstItsOwnList.IsOn(@"E:\", disc));
    }

    [Fact]
    public async Task Does_not_call_the_list_itself_a_stranger()
    {
        ADisc disc = new(("autorun.inf", "aaa"));

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        // It is on the disc and it does not list itself, which would make it
        // an unexpected file on every disc ever written.
        Assert.Empty(check.Unexpected);
        Assert.True(check.Good);
    }

    [Fact]
    public async Task Does_not_call_the_set_note_a_stranger_either()
    {
        ADisc disc = new(("autorun.inf", "aaa"));
        disc.AlsoOnTheDisc.Add(SetManifest.FileName);

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:" + (char)92, disc, disc, null, default);

        // DiscWright writes the set note after it writes the checksum list,
        // so the list never mentions it. Counting it as a stranger made a
        // perfectly good disc report that something was already on it, which
        // reads as a disc that was not blank. Found on a real disc: the fakes
        // had nothing on them that was not in the list.
        Assert.Empty(check.Unexpected);
        Assert.True(check.Good);
        Assert.DoesNotContain("already on the disc", check.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notices_something_nobody_put_there()
    {
        ADisc disc = new(("autorun.inf", "aaa"));
        disc.AlsoOnTheDisc.Add("leftover.tmp");

        DiscCheck check = await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, null, default);

        Assert.Equal("leftover.tmp", Assert.Single(check.Unexpected));

        // Not a failure on its own. Something extra does not make the disc
        // the wrong disc.
        Assert.True(check.Good);
    }

    [Fact]
    public async Task Counts_its_way_through_so_the_window_can_say_where_it_is()
    {
        ADisc disc = new(("a.txt", "aaa"), ("b.txt", "bbb"), ("c.txt", "ccc"));
        EverythingItSaid<CheckProgress> said = new();

        await TheDiscAgainstItsOwnList.Check(@"E:\", disc, disc, said, default);

        // Heard on the thread they were said on, so there is nothing to wait
        // for. This used to give Progress<T> a second to deliver the last one.
        Assert.Equal(3, said.Heard.Count);
        Assert.Equal(3, said.Heard[^1].Done);
        Assert.Equal(1, said.Heard[^1].HowFar);
    }

    /// <summary>A disc that carries a list, and can be made to misbehave.</summary>
    private sealed class ADisc : IFileSystem, IHasher
    {
        private readonly List<(string Path, string Contents)> _files;

        internal ADisc(params (string Path, string Contents)[] files) => _files = [.. files];

        internal Dictionary<string, string> ActuallyReads { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal List<string> WillNotRead { get; } = [];

        internal List<string> LeaveOff { get; } = [];

        internal List<string> AlsoOnTheDisc { get; } = [];

        internal bool HasAList { get; init; } = true;

        internal int? ListSaysItHas { get; init; }

        public bool FolderExists(string path) => true;

        public FolderListing List(string path) =>
            new(path,
            [
                .. _files.Where(file => !LeaveOff.Contains(file.Path))
                    .Select(file => new ListedFile(file.Path, 10)),
                .. AlsoOnTheDisc.Select(extra => new ListedFile(extra, 10)),
                new ListedFile(ChecksumList.FileName, 100)
            ]);

        public string? ReadText(string path)
        {
            if (!HasAList || !path.EndsWith(ChecksumList.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            int says = ListSaysItHas ?? _files.Count;

            System.Text.StringBuilder text = new();
            text.Append("# DiscWright checksum list for A DISC\n");
            text.Append("# ").Append(says).Append(" files, 30 bytes, SHA-256, sha256sum format.\n");
            text.Append("#\n");

            foreach ((string where, string contents) in _files)
            {
                text.Append(Fake(contents)).Append(" *").Append(where).Append('\n');
            }

            return text.ToString();
        }

        public string Sha256(string path, CancellationToken ct)
        {
            string where = path.Replace(@"E:\", string.Empty);

            if (WillNotRead.Contains(where))
            {
                throw new IOException("Data error (cyclic redundancy check).");
            }

            if (ActuallyReads.TryGetValue(where, out string? instead))
            {
                return Fake(instead);
            }

            return Fake(_files.First(file =>
                string.Equals(file.Path, where, StringComparison.OrdinalIgnoreCase)).Contents);
        }

        /// <summary>A stand-in hash: same input, same answer, no real hashing.</summary>
        private static string Fake(string contents) =>
            Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(contents)).PadRight(64, '0')[..64];
    }
}
