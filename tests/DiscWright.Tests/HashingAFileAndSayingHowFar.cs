using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// What the real hasher says about how far it has read.
/// </summary>
/// <remarks>
/// Every 8 MB, and once straight after the first megabyte. On 2026-11-11 a CD
/// spinning up in the owner's drive took eleven seconds to give its first 8 MB,
/// and the check of his set sat at 0% for all of them. Real files, so these are
/// the reports the window would hear.
/// </remarks>
public sealed class HashingAFileAndSayingHowFar : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dw-hash-" + Guid.NewGuid().ToString("N")[..8]);

    public HashingAFileAndSayingHowFar() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Says_how_far_after_the_first_megabyte_and_at_the_end()
    {
        Assert.Equal([1 * Megabyte, 3 * Megabyte], ReportsFor(3 * Megabyte));
    }

    [Fact]
    public void Says_how_far_every_eight_megabytes_after_the_first()
    {
        Assert.Equal([1 * Megabyte, 9 * Megabyte, 17 * Megabyte, 20 * Megabyte], ReportsFor(20 * Megabyte));
    }

    [Fact]
    public void Says_a_file_of_one_bite_once_rather_than_twice()
    {
        Assert.Equal([1 * Megabyte], ReportsFor(1 * Megabyte));
        Assert.Equal([1000L], ReportsFor(1000));
    }

    [Fact]
    public void Says_an_empty_file_once()
    {
        Assert.Equal([0L], ReportsFor(0));
    }

    [Fact]
    public void Still_hashes_what_it_reads()
    {
        string path = AFile(3 * Megabyte);

        Assert.Equal(
            new Sha256Hasher().Sha256(path, CancellationToken.None),
            new Sha256Hasher().Sha256(path, new Said([]), CancellationToken.None));
    }

    private long[] ReportsFor(long bytes)
    {
        List<long> heard = [];
        new Sha256Hasher().Sha256(AFile(bytes), new Said(heard), CancellationToken.None);
        return [.. heard];
    }

    private string AFile(long bytes)
    {
        string path = Path.Combine(_folder, $"{bytes}.bin");
        byte[] content = new byte[bytes];
        new Random(7).NextBytes(content);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Heard on the thread that read, the way the read-back hears it.</summary>
    private sealed class Said(List<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard.Add(value);
    }
}
