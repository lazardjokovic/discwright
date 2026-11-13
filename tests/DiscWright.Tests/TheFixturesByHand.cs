using Microsoft.Win32.SafeHandles;
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

    [Theory]
    // folder, the dialog's row, what it suggests, whether the row is a folder, whether it starts ticked
    [InlineData("own-gog-goodies", "Lantern Keep Manual.pdf", WhatBecomesOfIt.Manual, false, true)]
    [InlineData("own-gog-goodies", "Lantern Keep Game Guide.pdf", WhatBecomesOfIt.Manual, false, true)]
    [InlineData("own-gog-goodies", "Manuals", WhatBecomesOfIt.Manual, true, true)]
    [InlineData("own-gog-goodies", "lantern_keep_manual.zip", WhatBecomesOfIt.Extra, false, true)]
    [InlineData("own-gog-goodies", "README.txt", WhatBecomesOfIt.Extra, false, true)]
    [InlineData("own-gog-goodies", "Artbook", WhatBecomesOfIt.Extra, true, true)]
    [InlineData("own-gog-goodies", "Maps", WhatBecomesOfIt.Extra, true, true)]
    [InlineData("own-gog-goodies", "patch_lantern_keep_1.4_(70100)_to_1.5_(70300).exe", WhatBecomesOfIt.AddOn, false, false)]
    [InlineData("own-gog-goodies", "patch_lantern_keep_1.5_(70300)_to_1.6_(70400).exe", WhatBecomesOfIt.AddOn, false, true)]
    [InlineData("own-one-manual", "Quiet Harbour manual.pdf", WhatBecomesOfIt.Manual, false, true)]
    [InlineData("own-one-manual", "Wallpapers", WhatBecomesOfIt.Extra, true, true)]
    [InlineData("own-manual-only", "Cinder Road Manual.pdf", WhatBecomesOfIt.Manual, false, true)]
    [InlineData("own-for-a-set", "Long Winter manual.pdf", WhatBecomesOfIt.Manual, false, true)]
    [InlineData("own-for-a-set", "Making Of", WhatBecomesOfIt.Extra, true, true)]
    [InlineData("own-three-games/North Gate", "North Gate manual.pdf", WhatBecomesOfIt.Manual, false, true)]
    [InlineData("own-three-games/North Gate", "Artbook", WhatBecomesOfIt.Extra, true, true)]
    [InlineData("own-three-games/North Gate", "patch_north_gate_1.0_(72000)_to_1.1_(72100).exe", WhatBecomesOfIt.AddOn, false, true)]
    [InlineData("own-three-games/Salt Marsh", "patch_salt_marsh_1.1_(72400)_to_1.2_(72500).exe", WhatBecomesOfIt.AddOn, false, false)]
    [InlineData("own-three-games/Ember Fall", "patch_ember_fall_1.0_(73000)_to_1.1_(73100).exe", WhatBecomesOfIt.AddOn, false, true)]
    public void Each_games_own_goodie_is_suggested_what_its_case_says(
        string folder, string row, WhatBecomesOfIt suggested, bool isAFolder, bool ticked)
    {
        if (Where is not string root)
        {
            return;
        }

        // The dialog's own reading, rows gathered exactly as it gathers them,
        // so section F's cases say what the window will really show.
        LeftBehind found = Assert.Single(Rows(root, folder, out GameFolder game), each => each.Name == row);

        Assert.Equal(suggested, found.Suggested);
        Assert.Equal(isAFolder, found.IsAFolder);
        Assert.Equal(ticked, found.WantedAgainst(game.Installer?.Name));
    }

    [Fact]
    public void The_download_with_every_goodie_asks_about_nine_rows()
    {
        if (Where is not string root)
        {
            return;
        }

        IReadOnlyList<LeftBehind> rows = Rows(root, "own-gog-goodies", out _);

        // Two manuals, a folder of manuals, a zip, a readme, two folders of
        // pictures and two patches. More would mean a folder came apart into
        // its files, which is the forty questions the gathering is for.
        Assert.Equal(9, rows.Count);

        // The artbook's own folder of sketches is inside its one row, not a row of its own.
        Assert.Equal(9, rows.Single(each => each.Name == "Artbook").Inside!.Count);
        Assert.Equal(2, rows.Single(each => each.Name == "Manuals").Inside!.Count);
    }

    [Fact]
    public void The_three_games_are_three_and_the_bare_one_asks_nothing()
    {
        if (Where is not string root)
        {
            return;
        }

        WindowsFileSystem disk = new();

        Assert.Equal(3, GamesInside.Of(disk.List(Path.Combine(root, "own-three-games"))).Count);

        // The game whose goodies are kept elsewhere has nothing beside it, so
        // no dialog comes up and the list's links are the only way in.
        Assert.Empty(Rows(root, "own-bare-game", out _));
        Assert.Equal(GameKind.GogInstaller, Read(root, "own-bare-game").Kind);
    }

    [Fact]
    public void Every_manual_is_a_pdf_a_reader_opens_and_says_which_it_is()
    {
        if (Where is not string root)
        {
            return;
        }

        // A file of zeros called manual.pdf opens as an error in every reader,
        // and that reads as a bug in the menu. Each one's cross reference has
        // to point at its objects, and its page has to name the file, so a
        // button that opens the wrong manual is seen to.
        string[] pdfs = Directory.GetFiles(root, "*.pdf", SearchOption.AllDirectories);

        Assert.True(pdfs.Length >= 13, $"Only {pdfs.Length} manuals among the fixtures.");

        foreach (string pdf in pdfs)
        {
            string text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
            string name = Path.GetFileName(pdf);

            Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
            Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
            Assert.Contains("(" + name + ")", text, StringComparison.Ordinal);

            string[] tail = text.Split('\n');
            int xref = int.Parse(tail[^3], System.Globalization.CultureInfo.InvariantCulture);

            Assert.True(text[xref..].StartsWith("xref\n", StringComparison.Ordinal), $"{name}: startxref misses the table.");

            string[] entries = text[xref..].Split('\n');
            int objects = int.Parse(entries[1].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);

            for (int number = 1; number < objects; number++)
            {
                int at = int.Parse(entries[2 + number][..10], System.Globalization.CultureInfo.InvariantCulture);

                Assert.True(
                    text[at..].StartsWith($"{number} 0 obj\n", StringComparison.Ordinal),
                    $"{name}: object {number} is not where its cross reference says.");
            }
        }
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
    [InlineData("own-for-a-set", 1100, 1400)]
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

        int shared = 0;

        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            planned += new FileInfo(file).Length;

            // A hard link costs nothing: its bytes already existed and are
            // shared with the file it was made from. gog-real is 1.8 GB of
            // them, pointing at a GOG installer this machine already had, and
            // Windows reports each one at full size because there is no way to
            // ask which half of a share is yours. Counting them makes the
            // fixtures look twenty times more expensive than they are.
            if (LinksTo(file) > 1)
            {
                shared++;
                continue;
            }

            onDisk += ReallyTakes(file);
        }

        Assert.True(planned > 10L * 1024 * 1024 * 1024, $"Only {planned} bytes of fixtures, so this proves little.");

        // The skip above must not be a way for a real copy to hide. If the
        // real game is there at all it has to be linked rather than copied,
        // which is the whole claim the script makes about it.
        string real = Path.Combine(root, "gog-real");

        if (Directory.Exists(real) && Directory.GetFiles(real).Length > 0)
        {
            Assert.All(
                Directory.GetFiles(real),
                each => Assert.True(
                    LinksTo(each) > 1,
                    Path.GetFileName(each) + " was copied rather than hard linked."));
        }

        Assert.True(
            onDisk < planned / 20,
            $"The fixtures plan as {planned / 1024 / 1024 / 1024} GB and really take "
                + $"{onDisk / 1024 / 1024} MB of it. They are meant to be holes.");
    }

    /// <summary>What a file actually occupies, holes not counted.</summary>
    /// <summary>How many names this file's bytes answer to.</summary>
    private static int LinksTo(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path);

        return GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION about)
            ? (int)about.NumberOfLinks
            : 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        // Every field a DWORD, including the three FILETIMEs as pairs. A
        // long here looks tidier and is wrong: .NET aligns it to 8 bytes and
        // pads after the attributes, while the native struct is packed to 4,
        // so NumberOfLinks lands on the wrong bytes and comes back as
        // whatever happened to be there. It read above 1 for a plain copy,
        // which made the guard below pass against the very thing it is for.
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out BY_HANDLE_FILE_INFORMATION about);
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

    /// <summary>The rows the folder dialog would ask about, gathered as it gathers them.</summary>
    private static IReadOnlyList<LeftBehind> Rows(string root, string folder, out GameFolder game)
    {
        WindowsFileSystem disk = new();

        game = Read(root, folder);

        return WhatWasLeftBehind.Gathered(WhatWasLeftBehind.In(disk.List(Path.Combine(root, folder)), game));
    }

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
