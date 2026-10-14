using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// The checksum step against a real staging folder, because the one thing a
/// written listing cannot tell you is whether the hashes are right.
/// </summary>
public sealed class CheckingEveryFile : IDisposable
{
    private readonly string _stage = Path.Combine(
        Path.GetTempPath(), "dwsum_" + Guid.NewGuid().ToString("N")[..8]);

    public CheckingEveryFile() => Directory.CreateDirectory(_stage);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stage, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Hashes_what_is_actually_on_the_disc()
    {
        // The known SHA-256 of the three bytes "abc", which is the most
        // checkable number in computing and needs no tool to confirm.
        Put("setup.exe", "abc");

        RecordingWriter wrote = new();
        await Step().Run(Context(wrote), CancellationToken.None);

        Assert.Contains(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *setup.exe",
            Written(wrote),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_itself_out_of_its_own_list()
    {
        Put("a.bin", "x");
        Put(ChecksumList.FileName, "an older list that happened to be here");

        RecordingWriter wrote = new();
        await Step().Run(Context(wrote), CancellationToken.None);

        // It cannot be in it. Its own hash would have to be written into it.
        Assert.DoesNotContain(" *" + ChecksumList.FileName, Written(wrote), StringComparison.Ordinal);
        Assert.Contains("# 1 files", Written(wrote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counts_and_totals_what_it_hashed()
    {
        Put("a.bin", "12345");
        Put(@"Data\b.bin", "123");

        RecordingWriter wrote = new();
        await Step().Run(Context(wrote), CancellationToken.None);

        Assert.Contains("# 2 files, 8 bytes,", Written(wrote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_when_it_is_cancelled()
    {
        Put("a.bin", "x");

        using CancellationTokenSource cancelling = new();
        cancelling.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Step().Run(Context(new RecordingWriter()), cancelling.Token));
    }

    [Fact]
    public void Has_nothing_to_do_when_no_list_was_asked_for()
    {
        BuildContext context = Context(new RecordingWriter(), checksums: false);

        Assert.False(Step().AppliesTo(context));
    }

    // ------------------------------------------------------------------ helpers

    private void Put(string relative, string contents)
    {
        string full = Path.Combine(_stage, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
    }

    private static string Written(RecordingWriter wrote) =>
        wrote.Texts.Single(text => text.Path.EndsWith(ChecksumList.FileName, StringComparison.Ordinal)).Text;

    private static WriteTheChecksumsStep Step() =>
        new(new WindowsFileSystem(), new Sha256Hasher());

    private BuildContext Context(RecordingWriter wrote, bool checksums = true)
    {
        PlannedDisc disc = new(1, [], 0);

        return new BuildContext(
            new DiscProject("HOLLOW KNIGHT", [], new DiscOptions(Checksums: checksums)),
            new DiscLayout([disc], 1_000_000, 0),
            disc,
            _stage,
            wrote);
    }
}
