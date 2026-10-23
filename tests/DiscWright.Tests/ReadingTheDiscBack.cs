using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// Comparing what came back off a disc with what was built.
/// </summary>
/// <remarks>
/// Tested with two folders rather than a disc, because the question is the
/// same one: does everything that was built appear, readable and unchanged,
/// somewhere else. A real disc adds nothing to it except minutes.
/// </remarks>
public class ReadingTheDiscBack : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dw-check-" + Guid.NewGuid().ToString("N")[..8]);

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
    public void Says_what_went_wrong_in_words_somebody_can_act_on()
    {
        // What Lazar was told after burning disc one of two, word for word:
        //
        //     This disc is not the disc: 2 could not be read off the disc.
        //
        // The reading was right. Two files on that CD-RW really had gone
        // bad, and he could not tell from this which two, or 2 of what: the
        // figure reads as a disc number sitting where a disc number would be.
        DiscCheck check = new(
            7,
            [@"setup_hollow_knight.exe.dwpart01", @"AUTORUN\background.png"],
            [],
            [],
            [],
            null);

        Assert.Equal(
            "This disc is not the disc it was meant to be: 2 files could not be read off it. "
                + @"setup_hollow_knight.exe.dwpart01, AUTORUN\background.png.",
            check.InWords);
    }

    [Fact]
    public void Names_the_first_few_and_counts_the_rest()
    {
        // A disc that has gone wrong has usually gone wrong in hundreds of
        // files at once, and this goes in a box somebody reads rather than a
        // log somebody searches.
        DiscCheck check = new(0, [], ["a", "b", "c", "d", "e"], [], [], null);

        Assert.Equal(
            "This disc is not the disc it was meant to be: 5 files did not reach it. a, b, c, and 2 more.",
            check.InWords);
    }

    [Fact]
    public void Counts_one_file_as_one_file()
    {
        DiscCheck check = new(3, [], [], [], ["one.bin"], null);

        Assert.Contains("1 file came back different", check.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_a_good_disc_is_good()
    {
        string built = Folder("built", ("setup_a_game.exe", "one"), (@"AUTORUN\menu.hta", "two"));
        string disc = Folder("disc", ("setup_a_game.exe", "one"), (@"AUTORUN\menu.hta", "two"));

        DiscCheck check = await Check(disc, built);

        Assert.True(check.Good);
        Assert.Equal(2, check.Matched);
        Assert.Contains("exactly what was built", check.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catches_a_file_that_never_reached_the_disc()
    {
        string built = Folder("built", ("one.bin", "a"), ("two.bin", "b"));
        string disc = Folder("disc", ("one.bin", "a"));

        DiscCheck check = await Check(disc, built);

        Assert.False(check.Good);
        Assert.Equal(["two.bin"], check.Missing);
        // Counted with the word on it and named, because "1" on its own
        // says nothing, not even 1 of what.
        Assert.Contains("1 file did not reach it", check.InWords, StringComparison.Ordinal);
        Assert.Contains("two.bin", check.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catches_a_file_that_came_back_different()
    {
        string built = Folder("built", ("one.bin", "what was built"));
        string disc = Folder("disc", ("one.bin", "what came back!"));

        DiscCheck check = await Check(disc, built);

        Assert.False(check.Good);
        Assert.Equal(["one.bin"], check.Wrong);
        Assert.Contains("came back different", check.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catches_a_file_of_the_wrong_size_without_reading_it()
    {
        string built = Folder("built", ("one.bin", "the whole thing"));
        string disc = Folder("disc", ("one.bin", "half"));

        DiscCheck check = await Check(disc, built);

        // A wrong size is the same answer for none of the reading time, which
        // on a full disc is minutes.
        Assert.False(check.Good);
        Assert.Equal(["one.bin"], check.Wrong);
    }

    [Fact]
    public async Task Does_not_mind_something_extra_that_was_already_there()
    {
        string built = Folder("built", ("one.bin", "a"));
        string disc = Folder("disc", ("one.bin", "a"), ("something else.txt", "not ours"));

        DiscCheck check = await Check(disc, built);

        // Something extra does not make the disc wrong. Everything that was
        // meant to be there being there does make it right.
        Assert.True(check.Good);
        Assert.Equal(["something else.txt"], check.Unexpected);
        Assert.Contains("already on the disc", check.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_so_when_the_disc_cannot_be_read_at_all()
    {
        string built = Folder("built", ("one.bin", "a"));

        DiscCheck check = await Check(Path.Combine(_root, "no such drive"), built);

        Assert.False(check.Good);
        Assert.NotNull(check.WhyNot);
    }

    [Fact]
    public async Task Says_which_file_it_is_on()
    {
        string built = Folder("built", ("one.bin", "a"), ("two.bin", "b"), ("three.bin", "c"));
        string disc = Folder("disc", ("one.bin", "a"), ("two.bin", "b"), ("three.bin", "c"));

        EverythingItSaid<CheckProgress> said = new();
        await Check(disc, built, said);

        // Hashing a full disc takes minutes, and a still window is how somebody
        // decides it has hung.
        Assert.Equal(3, said.Heard.Count);
        Assert.Equal(1, said.Heard[^1].HowFar);
    }

    // ------------------------------------------------------------------ helpers

    private static Task<DiscCheck> Check(string disc, string built, IProgress<CheckProgress>? progress = null) =>
        TheDiscAgainstWhatWasBuilt.Check(
            disc, built, new WindowsFileSystem(), new Sha256Hasher(), progress, default);

    private string Folder(string name, params (string Path, string Contents)[] files)
    {
        string folder = Path.Combine(_root, name);

        foreach ((string path, string contents) in files)
        {
            string full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
        }

        return folder;
    }
}
