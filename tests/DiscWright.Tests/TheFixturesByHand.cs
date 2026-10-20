using System.Runtime.InteropServices;

using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// The folders <c>tests/by-hand/Build-TheFixtures.ps1</c> makes, held against
/// the shapes the matrix says they are.
/// </summary>
/// <remarks>
/// <para>
/// A pass by hand is only worth the fixtures it starts from. "A folder of game
/// files with two executables worth running" has to really be that, or the
/// case tests nothing and the person running it has no way to tell.
/// </para>
/// <para>
/// Off unless <c>DISCWRIGHT_FIXTURES</c> points at them, because a suite that
/// needs fourteen gigabytes of scaffolding on disk fails on a fresh clone and
/// teaches everybody to ignore it. Build them, set the variable, and this says
/// whether they came out right:
/// </para>
/// <code>
/// tests\by-hand\Build-TheFixtures.ps1
/// $env:DISCWRIGHT_FIXTURES = "$env:USERPROFILE\DiscWright-Lab\fixtures"
/// dotnet test --filter TheFixturesByHand
/// </code>
/// <para>
/// It reads them with the real recognisers, so it is the program's own answer
/// rather than a second opinion written here.
/// </para>
/// </remarks>
public class TheFixturesByHand
{
    private static string? Where => Environment.GetEnvironmentVariable("DISCWRIGHT_FIXTURES");

    [Theory]
    // folder, what it must be recognised as, how many could start it
    [InlineData("gog-plain", GameKind.GogInstaller, 0)]
    [InlineData("gog-with-parts", GameKind.GogInstaller, 0)]
    [InlineData("gog-unfinished", GameKind.GogInstaller, 0)]
    [InlineData("gog-two-installers", GameKind.GogInstaller, 0)]
    [InlineData("gog-with-a-patch", GameKind.GogInstaller, 0)]
    [InlineData("gog-with-a-readme", GameKind.GogInstaller, 0)]
    [InlineData("gog-with-both", GameKind.GogInstaller, 0)]
    [InlineData("files-nothing-to-run", GameKind.FilesOnly, 0)]
    [InlineData("files-one-to-run", GameKind.FilesWithLauncher, 1)]
    [InlineData("files-one-and-noise", GameKind.FilesWithLauncher, 1)]
    [InlineData("files-two-to-run", GameKind.FilesWithLauncher, 2)]
    [InlineData("files-deep", GameKind.FilesWithLauncher, 2)]
    public void Each_one_is_the_shape_the_matrix_says_it_is(string folder, GameKind kind, int couldStart)
    {
        if (Where is not string root)
        {
            return;
        }

        GameFolder game = Read(root, folder);

        Assert.Equal(kind, game.Kind);
        Assert.Equal(couldStart, game.LauncherCandidates.Count);
    }

    [Fact]
    public void The_one_with_a_gap_in_its_parts_says_it_looks_unfinished()
    {
        if (Where is not string root)
        {
            return;
        }

        // The case that matters most in the whole matrix: it builds a clean
        // looking ISO and fails only off the burned disc.
        Assert.Contains(
            "INCOMPLETE",
            Read(root, "gog-unfinished").Warning ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_one_with_two_installers_says_which_it_took()
    {
        if (Where is not string root)
        {
            return;
        }

        GameFolder game = Read(root, "gog-two-installers");

        Assert.Contains("2 installers", game.Warning ?? string.Empty, StringComparison.Ordinal);

        // The largest, which is the base game rather than the DLC beside it.
        Assert.Contains("the_base_game", game.Installer!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plain_ones_have_nothing_to_say()
    {
        if (Where is not string root)
        {
            return;
        }

        Assert.Null(Read(root, "gog-plain").Warning);
        Assert.Null(Read(root, "gog-with-parts").Warning);
    }

    [Fact]
    public void The_one_that_is_really_a_patch_is_named_like_one()
    {
        if (Where is not string root)
        {
            return;
        }

        GameFolder game = Read(root, "gog-that-is-a-patch");

        Assert.True(
            AddOn.LooksLikeOne(game.Installer!.Name),
            $"{game.Installer.Name} is meant to look like an add-on and does not, so that case asks nothing.");
    }

    [Theory]
    [InlineData("gog-with-a-patch", 1)]
    [InlineData("gog-with-a-readme", 1)]
    [InlineData("gog-with-both", 3)]
    [InlineData("gog-plain", 0)]
    [InlineData("files-one-to-run", 0)]
    public void The_right_number_of_things_are_left_behind(string folder, int left)
    {
        if (Where is not string root)
        {
            return;
        }

        WindowsFileSystem disk = new();
        FolderListing listing = disk.List(Path.Combine(root, folder));
        GameFolder game = Read(root, folder);

        // A folder of game files leaves nothing behind whatever is in it,
        // because that recogniser takes the lot. Only a GOG download can.
        Assert.Equal(left, WhatWasLeftBehind.In(listing, game).Count);
    }

    [Fact]
    public void The_folder_of_folders_really_holds_two_games()
    {
        if (Where is not string root)
        {
            return;
        }

        WindowsFileSystem disk = new();
        FolderListing listing = disk.List(Path.Combine(root, "a-folder-of-games"));

        Assert.Equal(2, GamesInside.Of(listing).Count);

        // And two files sitting loose in it, which belong to neither game and
        // go on the disc either way.
        Assert.Equal(2, listing.AtTheTop.Count());
    }

    [Fact]
    public void An_empty_folder_is_the_only_one_that_is_refused()
    {
        if (Where is not string root)
        {
            return;
        }

        WindowsFileSystem disk = new();
        GameFolderReader reader = GameFolderReader.InTheUsualOrder(new FileVersionFacts());

        Assert.IsType<Outcome<GameFolder>.No>(
            reader.Read(disk.List(Path.Combine(root, "odd-empty-folder"))));

        // And a folder with things in it and nothing to run is NOT refused,
        // which is worth knowing before writing a case about it. The files
        // recogniser claims any folder that is not empty, so it comes back as
        // a disc of files. Refusals.NothingRecognisedIn is unreachable while
        // that recogniser goes last.
        Assert.IsType<Outcome<GameFolder>.Ok>(
            reader.Read(disk.List(Path.Combine(root, "odd-nothing-recognised"))));
    }

    [Theory]
    [InlineData("size-fits-a-cd", 400, 500)]
    [InlineData("size-one-too-big-for-a-cd", 1100, 1400)]
    [InlineData("size-three-cds", 2000, 2300)]
    [InlineData("size-over-the-iso-ceiling", 2200, 2400)]
    public void The_sizes_are_the_sizes_the_cases_need(string folder, int leastMb, int mostMb)
    {
        if (Where is not string root)
        {
            return;
        }

        long bytes = Read(root, folder).Bytes;
        long mb = bytes / 1024 / 1024;

        Assert.True(
            mb >= leastMb && mb <= mostMb,
            $"{folder} is {mb} MB and the case needs between {leastMb} and {mostMb}.");
    }

    [Fact]
    public void The_one_over_the_iso_ceiling_really_is_over_it()
    {
        if (Where is not string root)
        {
            return;
        }

        // 2 GiB exactly, measured on a real build in the v1 repository.
        Assert.True(Read(root, "size-over-the-iso-ceiling").LargestFile > FileSystems.Iso9660MaxFile);
    }

    [Fact]
    public void The_artwork_and_the_tune_are_really_what_they_are_called()
    {
        if (Where is not string root)
        {
            return;
        }

        WindowsImages pictures = new();

        foreach (string named in new[]
        {
            "icon.png", "background-760x480.png", "background-wide.png",
            "background-tall.png", "cover.png", "disc-face.png"
        })
        {
            ImageFacts facts = pictures.Read(Path.Combine(root, "artwork", named));

            Assert.True(facts.Readable, $"artwork\\{named} is not a picture, so its case proves nothing.");
        }

        // And the one that is meant not to be.
        Assert.False(pictures.Read(Path.Combine(root, "artwork", "not-a-picture.png")).Readable);

        // A tune of random bytes plays nothing, and hearing it is the whole
        // of the music cases. Real mp3 frames start with a sync word.
        string where = Path.Combine(root, "music", "tune.mp3");

        // Said rather than thrown. On a machine without ffmpeg this is the
        // one thing the builder cannot make, and a stack trace about a file
        // that is not there does not tell anybody what to do about it.
        Assert.True(
            File.Exists(where),
            "There is no tune. Build-TheFixtures.ps1 makes it with ffmpeg and said so when it could not "
                + "find it, and the music cases cannot be done on this machine until it is on the path.");

        byte[] tune = File.ReadAllBytes(where);

        Assert.True(tune.Length > 4096, "The tune is too small to be a real one.");
        Assert.True(
            (tune[0] == 0xFF && (tune[1] & 0xE0) == 0xE0) || (tune[0] == 'I' && tune[1] == 'D' && tune[2] == '3'),
            "The tune does not start like an mp3, so the music cases would hear nothing.");
    }

    [Fact]
    public void They_cost_almost_nothing_on_the_disk()
    {
        if (Where is not string root)
        {
            return;
        }

        // Fourteen gigabytes of fixtures that really take fourteen gigabytes
        // are not fixtures anybody will keep. The first version of the script
        // marked each file sparse AFTER giving it its length, by which time
        // the clusters were allocated and the flag could not free them: the
        // file read as sparse, fsutil said its valid data length was zero,
        // and the free space on the test VM went down by the full amount.
        //
        // So the claim is measured here rather than believed. Empty, then the
        // flag, then the length.
        long planned = 0;
        long onDisk = 0;

        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            planned += new FileInfo(file).Length;
            onDisk += ReallyTakes(file);
        }

        Assert.True(planned > 10L * 1024 * 1024 * 1024, $"Only {planned} bytes of fixtures, so this proves little.");

        Assert.True(
            onDisk < planned / 20,
            $"The fixtures plan as {planned / 1024 / 1024 / 1024} GB and really take "
                + $"{onDisk / 1024 / 1024} MB of it. They are meant to be holes.");
    }

    /// <summary>What a file actually occupies, holes not counted.</summary>
    private static long ReallyTakes(string path)
    {
        uint high;
        uint low = GetCompressedFileSizeW(path, out high);

        return low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0
            ? new FileInfo(path).Length
            : ((long)high << 32) + low;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string name, out uint high);

    private static GameFolder Read(string root, string folder)
    {
        WindowsFileSystem disk = new();
        GameFolderReader reader = GameFolderReader.InTheUsualOrder(new FileVersionFacts());

        Outcome<GameFolder> read = reader.Read(disk.List(Path.Combine(root, folder)));

        return read is Outcome<GameFolder>.Ok(GameFolder game)
            ? game
            : throw new InvalidOperationException(
                $"{folder} was refused: {((Outcome<GameFolder>.No)read).Refusal.Headline}");
    }
}
