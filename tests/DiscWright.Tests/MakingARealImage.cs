using System.Text;

using DiscWright.Core;
using DiscWright.Imaging;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// Making a real image, with the real Windows component, out of a real folder.
/// </summary>
/// <remarks>
/// There is no pretending here. An image is the one thing on the disc side that
/// cannot be checked by reading what we wrote, because what we wrote is a
/// request to Windows and what comes back is a file format neither of us
/// controls.
/// </remarks>
public class MakingARealImage : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dw-image-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Makes_one_file_that_is_the_whole_disc()
    {
        string staged = Staged(("setup_a_game.exe", 40_000), (@"AUTORUN\menu.hta", 2_000));
        string image = Path.Combine(_root, "A GAME.iso");

        Outcome<MadeImage> made = await New().Make(staged, image, "A GAME", false, null, default);

        MadeImage iso = Assert.IsType<Outcome<MadeImage>.Ok>(made).Value;

        Assert.True(File.Exists(iso.Path));
        Assert.Equal(new FileInfo(iso.Path).Length, iso.Bytes);

        // A disc is blocks of 2 KiB and nothing else, so an image that is not a
        // whole number of them is not an image.
        Assert.Equal(0, iso.Bytes % 2048);

        // Bigger than what went in, because the file system goes in too.
        Assert.True(iso.Bytes > 42_000, $"only {iso.Bytes} bytes");
    }

    [Fact]
    public async Task Puts_the_files_in_it_where_a_reader_can_find_them()
    {
        string staged = Staged(("a_distinctive_name.txt", 1_000));
        string image = Path.Combine(_root, "NAMES.iso");

        await New().Make(staged, image, "NAMES", false, null, default);

        byte[] bytes = await File.ReadAllBytesAsync(image);

        // Read back out of the image rather than trusted. UDF writes names as
        // sixteen bit characters, so the plain spelling of it is not in there
        // and looking for that would have passed on an empty image.
        Assert.True(
            Holds(bytes, Encoding.Unicode.GetBytes("a_distinctive_name.txt"))
                || Holds(bytes, Encoding.BigEndianUnicode.GetBytes("a_distinctive_name.txt"))
                || Holds(bytes, Encoding.ASCII.GetBytes("A_DISTINCTIVE_NAME.TXT")),
            "the name of the file is nowhere in the image");
    }

    [Fact]
    public async Task Carries_the_name_somebody_gave_the_disc()
    {
        string staged = Staged(("setup_a_game.exe", 1_000));
        string image = Path.Combine(_root, "LABELLED.iso");

        await New().Make(staged, image, "HOLLOW KNIGHT", false, null, default);

        byte[] bytes = await File.ReadAllBytesAsync(image);

        // The volume name, not the label: spaces became underscores on the way.
        Assert.True(
            Holds(bytes, Encoding.Unicode.GetBytes("HOLLOW_KNIGHT"))
                || Holds(bytes, Encoding.ASCII.GetBytes("HOLLOW_KNIGHT")),
            "the disc's name is nowhere in the image");
    }

    [Fact]
    public async Task Says_how_far_through_it_is()
    {
        string staged = Staged(("big.bin", 3_000_000));
        string image = Path.Combine(_root, "PROGRESS.iso");

        List<ImageProgress> said = [];

        await New().Make(staged, image, "PROGRESS", false, new Progress<ImageProgress>(said.Add), default);

        // Reported at all, and the last word is the whole of it, so nothing is
        // left sitting at 199 parts in 200.
        Assert.NotEmpty(said);
        Assert.Equal(1, said[^1].HowFar);
    }

    [Fact]
    public async Task Refuses_an_empty_folder_rather_than_making_an_empty_disc()
    {
        string staged = Path.Combine(_root, "nothing");
        Directory.CreateDirectory(staged);

        Outcome<MadeImage> made = await New().Make(staged, Path.Combine(_root, "x.iso"), "X", false, null, default);

        Assert.IsType<Outcome<MadeImage>.No>(made);
    }

    [Fact]
    public async Task Replaces_the_image_from_last_time()
    {
        string staged = Staged(("setup_a_game.exe", 1_000));
        string image = Path.Combine(_root, "TWICE.iso");

        await File.WriteAllTextAsync(image, "this was here before");

        Outcome<MadeImage> made = await New().Make(staged, image, "TWICE", false, null, default);

        Assert.IsType<Outcome<MadeImage>.Ok>(made);
        Assert.True(new FileInfo(image).Length > 1000);
    }

    [Fact]
    public async Task Lets_go_of_the_folder_it_read()
    {
        string staged = Staged(("setup_a_game.exe", 1_000));
        string image = Path.Combine(_root, "LOCKS.iso");

        await New().Make(staged, image, "LOCKS", false, null, default);

        // The whole reason every COM object is released by hand. In v1 this was
        // a rebuild failing with "something is still using it", blaming
        // Explorer for a lock DiscWright was holding itself.
        Directory.Delete(staged, recursive: true);
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public async Task Can_be_stopped()
    {
        string staged = Staged(("big.bin", 8_000_000));
        using CancellationTokenSource stop = new();

        Task<Outcome<MadeImage>> making = New().Make(
            staged,
            Path.Combine(_root, "STOPPED.iso"),
            "STOPPED",
            false,
            new Progress<ImageProgress>(_ => stop.Cancel()),
            stop.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => making);
    }

    // ------------------------------------------------------------------ helpers

    private static WindowsImageMaker New() => new(new WindowsFileSystem());

    private string Staged(params (string Path, int Bytes)[] files)
    {
        string staged = Path.Combine(_root, "disc");

        foreach ((string path, int bytes) in files)
        {
            string full = Path.Combine(staged, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            byte[] contents = new byte[bytes];
            Random.Shared.NextBytes(contents);
            File.WriteAllBytes(full, contents);
        }

        return staged;
    }

    private static bool Holds(byte[] haystack, byte[] needle)
    {
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            int same = 0;

            while (same < needle.Length && haystack[at + same] == needle[same])
            {
                same++;
            }

            if (same == needle.Length)
            {
                return true;
            }
        }

        return false;
    }
}
